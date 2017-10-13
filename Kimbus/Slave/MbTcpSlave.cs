using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NLog;

namespace Kimbus.Slave
{
    public class MbTcpSlave
    {
        private Logger _logger = LogManager.GetCurrentClassLogger();

        private object _lock = new Object();

        private List<Task> _connections = new List<Task>();

        private TcpListener _listener;

        public IPAddress IpAddress { get; }

        public int Port { get; }

        public Func<ushort, ushort, (ushort[], MbExceptionCode)> OnReadHoldingRegisters { get; set; }

        public Func<ushort, ushort, (ushort[], MbExceptionCode)> OnReadInputRegisters { get; set; }

        public Func<ushort, ushort, (bool[], MbExceptionCode)> OnReadCoils { get; set; }

        public Func<ushort, ushort, (bool[], MbExceptionCode)> OnReadDiscretes { get; set; }

        public Func<ushort, ushort[], MbExceptionCode> OnWriteHoldingRegisters { get; set; }

        public Func<ushort, bool[], MbExceptionCode> OnWriteCoils { get; set; }

        public int Timeout { get; }

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
            _logger.Info($"Received connection request from {clientEndPoint}");

            using (var networkStream = tcpClient.GetStream())
            {
                while (true)
                {
                    var buffer = new byte[1024];
                    var timeout = false;
                    var cts = new CancellationTokenSource();
                    var byteCount = (await Task.WhenAny(
                        networkStream.ReadAsync(buffer, 0, buffer.Length).ContinueWith(t => { cts.Cancel(); return t.Result; }),
                        Task.Delay(Timeout, cts.Token).ContinueWith(t => { timeout = !t.IsCanceled; return 0; }))
                        ).Result;
                    if (byteCount == 0)
                    {
                        if (timeout)
                        {
                            _logger.Info($"{clientEndPoint} was quiet for {Timeout / 1000} seconds, disconnecting");
                            break;
                        }

                        if (tcpClient.Client.Poll(1, SelectMode.SelectRead) && !networkStream.DataAvailable)
                        {
                            _logger.Info($"{clientEndPoint} disconnected");
                            break;
                        }

                        _logger.Error("Bytecount after stream read was 0, but it wasn't timeout nor client disconnect. INVESTIGATE.");
                        continue;
                    }

                    var response = Respond(buffer, byteCount);
                    await networkStream.WriteAsync(response, 0, response.Length);
                }
            }
            
            tcpClient.Close();
        }
        
        private byte[] Respond(byte[] request, int requestLength)
        {
            var responseCode = MbExceptionCode.IllegalFunction;
            var response = new byte[0];

            if (requestLength > 8)
            {
                // process MBAP header
                var transId = (request[0] << 8) | request[1];
                var protoId = (request[2] << 8) | request[3];
                var length = (request[4] << 8) | request[5];
                var unitId = request[6];

                if (protoId != 0)
                {
                    return new byte[0];
                }

                if (length + 6 != requestLength)
                {
                    return new byte[0];
                }

                var functionCode = request[7];
                var address = 65536;
                var count = 0;

                /* FIXME: rationalize response creation code */
                var responseBuffer = new byte[0];
                var responseData = new byte[0];
                switch ((MbFunctionCode)functionCode)
                {
                    case MbFunctionCode.ReadCoils:
                        if (requestLength == 12)
                        {
                            address = (request[8] << 8) | request[9];
                            count = (request[10] << 8) | request[11];
                            (responseData, responseCode) = ModbusFunctions.ReadDigitals(address, count, OnReadCoils);

                            if (responseCode == MbExceptionCode.Ok)
                            {
                                responseBuffer = new byte[2 + responseData.Length];
                                responseBuffer[0] = functionCode;
                                responseBuffer[1] = (byte)responseData.Length;
                                Array.Copy(responseData, 0, responseBuffer, 2, responseData.Length);
                            }
                        }
                        break;
                    case MbFunctionCode.ReadDiscreteInputs:
                        if (requestLength == 12)
                        {
                            address = (request[8] << 8) | request[9];
                            count = (request[10] << 8) | request[11];
                            (responseData, responseCode) = ModbusFunctions.ReadDigitals(address, count, OnReadDiscretes);

                            if(responseCode == MbExceptionCode.Ok)
                            {
                                responseBuffer = new byte[2 + responseData.Length];
                                responseBuffer[0] = functionCode;
                                responseBuffer[1] = (byte)responseData.Length;
                                Array.Copy(responseData, 0, responseBuffer, 2, responseData.Length);
                            }
                        }
                        break;
                    case MbFunctionCode.WriteSingleCoil:
                        if (requestLength == 12)
                        {
                            address = (request[8] << 8) | request[9];
                            var inputBuffer = request.Skip(10).Take(2).ToArray();
                            responseCode = ModbusFunctions.WriteCoils(address, 1, inputBuffer, OnWriteCoils);
                            if (responseCode == MbExceptionCode.Ok)
                            {
                                responseBuffer = new byte[5];
                                Array.Copy(request, 7, responseBuffer, 0, 5);
                            }
                        }
                        break;
                    case MbFunctionCode.WriteMultipleCoils:
                        if (requestLength > 13)
                        {
                            address = (request[8] << 8) | request[9];
                            count = (request[10] << 8) | request[11];
                            var byteCount = request[12];
                            if (requestLength == byteCount + 13)
                            {
                                var inputBuffer = request.Skip(13).Take(byteCount).ToArray();
                                responseCode = ModbusFunctions.WriteCoils(address, count, inputBuffer, OnWriteCoils);

                                if (responseCode == MbExceptionCode.Ok)
                                {
                                    responseBuffer = new byte[5];
                                    Array.Copy(request, 7, responseBuffer, 0, 5);
                                }
                            }
                        }
                        break;
                    case MbFunctionCode.ReadHoldingRegisters:
                        if (requestLength == 12)
                        {
                            address = (request[8] << 8) | request[9];
                            count = (request[10] << 8) | request[11];
                            (responseData, responseCode) = ModbusFunctions.ReadAnalogs(address, count, OnReadHoldingRegisters);

                            if (responseCode == MbExceptionCode.Ok)
                            {
                                responseBuffer = new byte[2 + responseData.Length];
                                responseBuffer[0] = functionCode;
                                responseBuffer[1] = (byte)responseData.Length;
                                Array.Copy(responseData, 0, responseBuffer, 2, responseData.Length);
                            }
                        }
                        break;
                    case MbFunctionCode.ReadInputRegisters:
                        address = (request[8] << 8) | request[9];
                        count = (request[10] << 8) | request[11];
                        (responseData, responseCode) = ModbusFunctions.ReadAnalogs(address, count, OnReadInputRegisters);

                        if (responseCode == MbExceptionCode.Ok)
                        {
                            responseBuffer = new byte[2 + responseData.Length];
                            responseBuffer[0] = functionCode;
                            responseBuffer[1] = (byte)responseData.Length;
                            Array.Copy(responseData, 0, responseBuffer, 2, responseData.Length);
                        }
                        break;
                    case MbFunctionCode.WriteSingleRegister:
                        if (requestLength == 12)
                        {
                            address = (request[8] << 8) | request[9];
                            var inputBuffer = request.Skip(10).Take(2).ToArray();
                            responseCode = ModbusFunctions.WriteHoldingRegisters(address, 1, inputBuffer, OnWriteHoldingRegisters);

                            if (responseCode == MbExceptionCode.Ok)
                            {
                                responseBuffer = new byte[5];
                                Array.Copy(request, 7, responseBuffer, 0, 5);
                            }
                        }
                        break;
                    case MbFunctionCode.WriteMultipleRegisters:
                        if (requestLength > 13)
                        {
                            address = (request[8] << 8) | request[9];
                            count = (request[10] << 8) | request[11];
                            var byteCount = request[12];
                            if (requestLength == byteCount + 13)
                            {
                                var inputBuffer = request.Skip(13).Take(byteCount).ToArray();
                                responseCode = ModbusFunctions.WriteHoldingRegisters(address, count, inputBuffer, OnWriteHoldingRegisters);

                                if (responseCode == MbExceptionCode.Ok)
                                {
                                    responseBuffer = new byte[5];
                                    Array.Copy(request, 7, responseBuffer, 0, 5);
                                }
                            }
                        }
                        break;
                }

                if (responseCode != MbExceptionCode.Ok)
                {
                    response = ModbusFunctions.GenerateExceptionResponse(transId, unitId, functionCode, responseCode);
                }
                else if (responseBuffer.Length != 0)
                {
                    response = ModbusFunctions.GenerateResponse(transId, unitId, functionCode, responseBuffer);
                }
            }

            return response;
        }

        
    }
}
