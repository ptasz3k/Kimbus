using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Kimbus.Helpers;

namespace Kimbus.Slave
{
    public class MbTcpSlave
    {
        private object _lock = new Object();

        private List<Task> _connections = new List<Task>();

        private TcpListener _listener;

        public IPAddress IpAddress { get; }

        public int Port { get; }

        public Func<ushort, ushort, (ushort[], ModbusExceptionCode)> OnReadHoldingRegisters { get; set; }

        public Func<ushort, ushort, (ushort[], ModbusExceptionCode)> OnReadInputRegisters { get; set; }

        public Func<ushort, ushort, (bool[], ModbusExceptionCode)> OnReadCoils { get; set; }

        public Func<ushort, ushort, (bool[], ModbusExceptionCode)> OnReadDiscretes { get; set; }

        public Func<ushort, ushort[], ModbusExceptionCode> OnWriteHoldingRegisters { get; set; }

        public Func<ushort, bool[], ModbusExceptionCode> OnWriteCoils { get; set; }

        public MbTcpSlave(string ipAddress, int port)
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

            if (port < 0 || port > 65535)
            {
                throw new ArgumentException(nameof(port));
            }

            Port = (ushort)port;
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
                        // _logger.Error(ex.Message);
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
                    // _logger.Error(ex.Message);
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
            Console.WriteLine("Received connection request from " + clientEndPoint);

            using (var networkStream = tcpClient.GetStream())
            {
                while (tcpClient.Connected)
                {
                    var buffer = new byte[1024];
                    var byteCount = await networkStream.ReadAsync(buffer, 0, buffer.Length);
                    var response = Respond(buffer, byteCount);
                    await networkStream.WriteAsync(response, 0, response.Length);
                }
            }

            Console.WriteLine("Disconnected");
        }
        
        private byte[] Respond(byte[] request, int requestLength)
        {
            var responseCode = ModbusExceptionCode.IllegalFunction;
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

                var responseBuffer = new byte[0];
                var responseData = new byte[0];
                switch (functionCode)
                {
                    case 1:
                        if (requestLength == 12)
                        {
                            address = (request[8] << 8) | request[9];
                            count = (request[10] << 8) | request[11];
                            (responseData, responseCode) = ModbusFunctions.ReadDigitals(address, count, OnReadCoils);

                            if (responseCode == ModbusExceptionCode.Ok)
                            {
                                responseBuffer = new byte[2 + responseData.Length];
                                responseBuffer[0] = functionCode;
                                responseBuffer[1] = (byte)responseData.Length;
                                Array.Copy(responseData, 0, responseBuffer, 2, responseData.Length);
                            }
                        }
                        break;
                    case 2:
                        if (requestLength == 12)
                        {
                            address = (request[8] << 8) | request[9];
                            count = (request[10] << 8) | request[11];
                            (responseData, responseCode) = ModbusFunctions.ReadDigitals(address, count, OnReadDiscretes);

                            if(responseCode == ModbusExceptionCode.Ok)
                            {
                                responseBuffer = new byte[2 + responseData.Length];
                                responseBuffer[0] = functionCode;
                                responseBuffer[1] = (byte)responseData.Length;
                                Array.Copy(responseData, 0, responseBuffer, 2, responseData.Length);
                            }
                        }
                        break;
                    case 5:
                        if (requestLength == 12)
                        {
                            address = (request[8] << 8) | request[9];
                            var inputBuffer = request.Skip(10).Take(2).ToArray();
                            responseCode = ModbusFunctions.WriteCoils(address, 1, inputBuffer, OnWriteCoils);
                            if (responseCode == ModbusExceptionCode.Ok)
                            {
                                responseBuffer = new byte[5];
                                Array.Copy(request, 7, responseBuffer, 0, 5);
                            }
                        }
                        break;
                    case 15:
                        if (requestLength > 13)
                        {
                            address = (request[8] << 8) | request[9];
                            count = (request[10] << 8) | request[11];
                            var byteCount = request[12];
                            if (requestLength == byteCount + 13)
                            {
                                var inputBuffer = request.Skip(13).Take(byteCount).ToArray();
                                responseCode = ModbusFunctions.WriteCoils(address, count, inputBuffer, OnWriteCoils);

                                if (responseCode == ModbusExceptionCode.Ok)
                                {
                                    responseBuffer = new byte[5];
                                    Array.Copy(request, 7, responseBuffer, 0, 5);
                                }
                            }
                        }
                        break;
                    case 3:
                        if (requestLength == 12)
                        {
                            address = (request[8] << 8) | request[9];
                            count = (request[10] << 8) | request[11];
                            (responseData, responseCode) = ModbusFunctions.ReadAnalogs(address, count, OnReadHoldingRegisters);

                            if (responseCode == ModbusExceptionCode.Ok)
                            {
                                responseBuffer = new byte[2 + responseData.Length];
                                responseBuffer[0] = functionCode;
                                responseBuffer[1] = (byte)responseData.Length;
                                Array.Copy(responseData, 0, responseBuffer, 2, responseData.Length);
                            }
                        }
                        break;
                    case 4:
                        address = (request[8] << 8) | request[9];
                        count = (request[10] << 8) | request[11];
                        (responseData, responseCode) = ModbusFunctions.ReadAnalogs(address, count, OnReadInputRegisters);

                        if (responseCode == ModbusExceptionCode.Ok)
                        {
                            responseBuffer = new byte[2 + responseData.Length];
                            responseBuffer[0] = functionCode;
                            responseBuffer[1] = (byte)responseData.Length;
                            Array.Copy(responseData, 0, responseBuffer, 2, responseData.Length);
                        }
                        break;
                    case 6:
                        if (requestLength == 12)
                        {
                            address = (request[8] << 8) | request[9];
                            var inputBuffer = request.Skip(10).Take(2).ToArray();
                            responseCode = ModbusFunctions.WriteHoldingRegisters(address, 1, inputBuffer, OnWriteHoldingRegisters);

                            if (responseCode == ModbusExceptionCode.Ok)
                            {
                                responseBuffer = new byte[5];
                                Array.Copy(request, 7, responseBuffer, 0, 5);
                            }
                        }
                        break;
                    case 16:
                        if (requestLength > 13)
                        {
                            address = (request[8] << 8) | request[9];
                            count = (request[10] << 8) | request[11];
                            var byteCount = request[12];
                            if (requestLength == byteCount + 13)
                            {
                                var inputBuffer = request.Skip(13).Take(byteCount).ToArray();
                                responseCode = ModbusFunctions.WriteHoldingRegisters(address, count, inputBuffer, OnWriteHoldingRegisters);

                                if (responseCode == ModbusExceptionCode.Ok)
                                {
                                    responseBuffer = new byte[5];
                                    Array.Copy(request, 7, responseBuffer, 0, 5);
                                }
                            }
                        }
                        break;
                }

                if (responseCode != ModbusExceptionCode.Ok)
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
