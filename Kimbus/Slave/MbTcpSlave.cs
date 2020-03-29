using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Kimbus.Helpers;
using NLog;

namespace Kimbus.Slave
{
    public class MbTcpSlave
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly object _lock = new Object();

        private readonly List<Task> _connections = new List<Task>();

        private TcpListener _listener;

        /* IP address of server */
        public IPAddress IpAddress { get; }

        /* Listening port */
        public int Port { get; }

        /* Time after which server closes inactive connection */
        public int Timeout { get; }

        /// <summary>
        /// (unitId, address, count) => (return_values, status)
        /// Callback function for read holding register request.
        /// </summary>
        public Func<byte, ushort, ushort, (ushort[], MbExceptionCode)> OnReadHoldingRegisters { get; set; }

        /// <summary>
        /// (unitId, address, count) => (return_values, status)
        /// Callback function for read input register request.
        /// </summary>
        public Func<byte, ushort, ushort, (ushort[], MbExceptionCode)> OnReadInputRegisters { get; set; }

        /// <summary>
        /// (unitId, address, count) => (return_values, status)
        /// Callback function for read coils request.
        /// </summary>
        public Func<byte, ushort, ushort, (bool[], MbExceptionCode)> OnReadCoils { get; set; }

        /// <summary>
        /// (unitId, address, count) => (return_values, status)
        /// Callback function for read discretes request.
        /// </summary>
        public Func<byte, ushort, ushort, (bool[], MbExceptionCode)> OnReadDiscretes { get; set; }

        /// <summary>
        /// (unitId, address, values_to_store) => status
        /// Callback function for write holding registers request.
        /// </summary>
        public Func<byte, ushort, ushort[], MbExceptionCode> OnWriteHoldingRegisters { get; set; }

        /// <summary>
        /// (unitId, address, values_to_store) => status
        /// Callback function for write coils request.
        /// </summary>
        public Func<byte, ushort, bool[], MbExceptionCode> OnWriteCoils { get; set; }

        /// <summary>
        /// functionCode: (unitId, pdu) => (return_values, status)
        /// Dictionary of user handled modbus functions.
        /// </summary>
        public Dictionary<byte, Func<byte, byte[], (byte[], MbExceptionCode)>> UserFunctions { get; } 
            = new Dictionary<byte, Func<byte, byte[], (byte[], MbExceptionCode)>>();

        private static List<byte> GenerateExceptionResponse(int transId, byte unitId, int functionCode, MbExceptionCode responseCode)
        {
            var response = new List<byte>
            {
                (byte)(transId >> 8),
                (byte)(transId & 0xff),
                0,
                0,
                0,
                3,
                unitId,
                (byte)(functionCode | 0x80),
                (byte)responseCode
            };

            return response;
        }

        public MbTcpSlave(string ipAddress, int port = 502, int timeout = 120000)
        {
            IPAddress addr;

            if (ipAddress == "*")
            {
                addr = IPAddress.Any;
            }
            else if (!IPAddress.TryParse(ipAddress, out addr))
            {
                throw new ArgumentException(nameof(ipAddress));
            }

            IpAddress = addr;

            if (port < 1 || port > 65535)
            {
                throw new ArgumentOutOfRangeException(nameof(port));
            }

            Port = (ushort)port;

            if (timeout <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            Timeout = timeout;

        }

        /// <summary>
        /// Start server
        /// </summary>
        /// <returns></returns>
        public Task Listen()
        {
            return Task.Run(async () =>
            {
                _listener = new TcpListener(IpAddress, Port);
                _listener.Start();

                while (true)
                {
                    try
                    {
                        var tcpClient = await _listener.AcceptTcpClientAsync();
                        var t = StartHandleConnection(tcpClient);
                        if (t.IsFaulted)
                        {
                            t.Wait();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Exception catched: {ex.Message}" + ex.InnerException != null ? $", inner exception: {ex.InnerException.Message}" : string.Empty);
                    }
                }
            });
        }

        private async Task StartHandleConnection(TcpClient tcpClient)
        {
            var connectionTask = HandleConnection(tcpClient);

            lock (_lock)
            {
                _connections.Add(connectionTask);
            }

            try
            {
                await connectionTask;
            }
            catch (Exception ex)
            {
                _logger.Error($"Exception catched: {ex.Message}" + ex.InnerException != null ? $", inner exception: {ex.InnerException.Message}" : string.Empty);
            }
            finally
            {
                lock (_lock)
                {
                    _connections.Remove(connectionTask);
                }
            }
        }

        private async Task HandleConnection(TcpClient tcpClient)
        {
            await Task.Yield();

            string clientEndPoint = tcpClient.Client.RemoteEndPoint.ToString();
            _logger.Debug($"Received connection request from {clientEndPoint}");

            using (var networkStream = tcpClient.GetStream())
            {
                while (true)
                {
                    var buffer = new byte[1024];
                    var timeout = false;
                    var byteCount = 0;

                    using (var cts = new CancellationTokenSource())
                    {
                        byteCount = (await Task.WhenAny(
                            networkStream.ReadAsync(buffer, 0, buffer.Length).ContinueWith(t => t.Result),
                            Task.Delay(Timeout, cts.Token).ContinueWith(t => { timeout = !t.IsCanceled; return 0; }))
                            ).Result;

                        if (!timeout)
                        {
                            cts.Cancel();
                        }
                    }

                    if (byteCount == 0)
                    {
                        if (timeout)
                        {
                            _logger.Debug($"{clientEndPoint} was quiet for {Timeout / 1000} seconds, disconnecting");
                            break;
                        }

                        if (tcpClient.Client.Poll(1, SelectMode.SelectRead) && !networkStream.DataAvailable)
                        {
                            _logger.Debug($"{clientEndPoint} disconnected");
                            break;
                        }

                        _logger.Error("Bytecount after stream read was 0, but it wasn't timeout nor client disconnect. INVESTIGATE.");
                        continue;
                    }

                    var response = Respond(buffer.Take(byteCount).ToList());
                    await networkStream.WriteAsync(response, 0, response.Length);
                }
            }

            tcpClient.Close();
        }

        private byte[] Respond(List<byte> request)
        {

            if (request.Count > 8)
            {
                ushort transId = 0;
                byte unitId = 0;
                var pdu = new List<byte>();
                var responseCode = MbExceptionCode.IllegalFunction;

                try
                {
                    (transId, unitId, pdu) = MbHelpers.UnwrapMbapHeader(request);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error while processing MBAP header: {ex.Message}");
                }

                var functionCode = pdu[0];
                ushort address = 65535;
                ushort count = 0;

                /* FIXME: rationalize response creation code */
                var responsePdu = new List<byte>();
                var responseData = new byte[0];
                switch ((MbFunctionCode)functionCode)
                {
                    case MbFunctionCode.ReadCoils:
                        if (pdu.Count == 5)
                        {
                            address = (ushort)((pdu[1] << 8) | pdu[2]);
                            count = (ushort)((pdu[3] << 8) | pdu[4]);
                            (responseData, responseCode) = ModbusFunctions.ReadDigitals(unitId, address, count, OnReadCoils);

                            if (responseCode == MbExceptionCode.Ok)
                            {
                                responsePdu.Add(functionCode);
                                responsePdu.Add((byte)responseData.Length);
                                responsePdu.AddRange(responseData);
                            }
                        }
                        break;
                    case MbFunctionCode.ReadDiscreteInputs:
                        if (pdu.Count == 5)
                        {
                            address = (ushort)((pdu[1] << 8) | pdu[2]);
                            count = (ushort)((pdu[3] << 8) | pdu[4]);
                            (responseData, responseCode) = ModbusFunctions.ReadDigitals(unitId, address, count, OnReadDiscretes);

                            if (responseCode == MbExceptionCode.Ok)
                            {
                                responsePdu.Add(functionCode);
                                responsePdu.Add((byte)responseData.Length);
                                responsePdu.AddRange(responseData);
                            }
                        }
                        break;
                    case MbFunctionCode.WriteSingleCoil:
                        if (pdu.Count == 5)
                        {
                            address = (ushort)((pdu[1] << 8) | pdu[2]);
                            responseCode = ModbusFunctions.WriteCoils(unitId, address, 1, pdu.Skip(3).ToArray(), OnWriteCoils);
                            if (responseCode == MbExceptionCode.Ok)
                            {
                                responsePdu = pdu;
                            }
                        }
                        break;
                    case MbFunctionCode.WriteMultipleCoils:
                        if (pdu.Count > 6)
                        {
                            address = (ushort)((pdu[1] << 8) | pdu[2]);
                            count = (ushort)((pdu[3] << 8) | pdu[4]);
                            var byteCount = pdu[5];
                            if (pdu.Count == byteCount + 6)
                            {
                                var inputBuffer = pdu.Skip(6).Take(byteCount).ToArray();
                                responseCode = ModbusFunctions.WriteCoils(unitId, address, count, inputBuffer, OnWriteCoils);

                                if (responseCode == MbExceptionCode.Ok)
                                {
                                    responsePdu = pdu.GetRange(0, 5);
                                }
                            }
                        }
                        break;
                    case MbFunctionCode.ReadHoldingRegisters:
                        if (pdu.Count == 5)
                        {
                            address = (ushort)((pdu[1] << 8) | pdu[2]);
                            count = (ushort)((pdu[3] << 8) | pdu[4]);
                            (responseData, responseCode) = ModbusFunctions.ReadAnalogs(unitId, address, count, OnReadHoldingRegisters);

                            if (responseCode == MbExceptionCode.Ok)
                            {
                                responsePdu.Add(functionCode);
                                responsePdu.Add((byte)responseData.Length);
                                responsePdu.AddRange(responseData);
                            }
                        }
                        break;
                    case MbFunctionCode.ReadInputRegisters:
                        if (pdu.Count == 5)
                        {
                            address = (ushort)((pdu[1] << 8) | pdu[2]);
                            count = (ushort)((pdu[3] << 8) | pdu[4]);
                            (responseData, responseCode) = ModbusFunctions.ReadAnalogs(unitId, address, count, OnReadInputRegisters);

                            if (responseCode == MbExceptionCode.Ok)
                            {
                                responsePdu.Add(functionCode);
                                responsePdu.Add((byte)responseData.Length);
                                responsePdu.AddRange(responseData);
                            }
                        }
                        break;
                    case MbFunctionCode.WriteSingleRegister:
                        if (pdu.Count == 5)
                        {
                            address = (ushort)((pdu[1] << 8) | pdu[2]);
                            var inputBuffer = pdu.Skip(3).ToArray();
                            responseCode = ModbusFunctions.WriteHoldingRegisters(unitId, address, 1, inputBuffer, OnWriteHoldingRegisters);

                            if (responseCode == MbExceptionCode.Ok)
                            {
                                responsePdu = pdu;
                            }
                        }
                        break;
                    case MbFunctionCode.WriteMultipleRegisters:
                        if (pdu.Count > 6)
                        {
                            address = (ushort)((pdu[1] << 8) | pdu[2]);
                            count = (ushort)((pdu[3] << 8) | pdu[4]);
                            var byteCount = pdu[5];
                            if (pdu.Count == byteCount + 6)
                            {
                                var inputBuffer = pdu.Skip(6).ToArray();
                                responseCode = ModbusFunctions.WriteHoldingRegisters(unitId, address, count, inputBuffer, OnWriteHoldingRegisters);

                                if (responseCode == MbExceptionCode.Ok)
                                {
                                    responsePdu = pdu.GetRange(0, 5);
                                }
                            }
                        }
                        break;
                    default:
                        if (UserFunctions.ContainsKey(functionCode))
                        {
                            byte[] userResponse = new byte[0];
                            try
                            {
                                _logger.Debug($"Calling user function number {functionCode}");
                                (userResponse, responseCode) = UserFunctions[functionCode](unitId, pdu.ToArray());
                                responsePdu = userResponse.ToList();
                            }
                            catch (Exception ex)
                            {
                                _logger.Debug($"Exception catched while calling user function {functionCode}: {ex.Message}");
                                responseCode = MbExceptionCode.SlaveDeviceFailure;
                            }
                        }
                        break;
                }

                var response = responseCode != MbExceptionCode.Ok
                    ? GenerateExceptionResponse(transId, unitId, functionCode, responseCode)
                    : MbHelpers.PrependMbapHeader(unitId, transId, responsePdu);

                return response.ToArray();
            }

            return new byte[0];
        }
    }
}
