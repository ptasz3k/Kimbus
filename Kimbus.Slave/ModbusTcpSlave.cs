using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Kimbus.Slave
{
    public class ModbusTcpSlave
    {
        private object _lock = new Object();

        private List<Task> _connections = new List<Task>();

        private TcpListener _listener;

        public IPAddress IpAddress { get; }

        public int Port { get; }

        public Func<ushort, ushort, (ushort[], ModbusExceptionCode)> OnReadHoldingRegister { get; set; } =
            (addr, count) => (new ushort[0], ModbusExceptionCode.IllegalFunction);

        public Func<ushort, ushort, (ushort[], ModbusExceptionCode)> OnReadInputRegister { get; set; } =
            (addr, count) => (new ushort[0], ModbusExceptionCode.IllegalFunction);

        public Func<ushort, ushort, (bool[], ModbusExceptionCode)> OnReadCoils { get; set; } =
            (addr, count) => (new bool[0], ModbusExceptionCode.IllegalFunction);

        public Func<ushort, ushort, (bool[], ModbusExceptionCode)> OnReadDiscrete { get; set; } =
            (addr, count) => (new bool[0], ModbusExceptionCode.IllegalFunction);

        public Func<ushort, ushort, ushort[], ModbusExceptionCode> OnWriteHoldingRegister { get; set; } =
            (addr, count, buf) => ModbusExceptionCode.IllegalFunction;

        public Func<ushort, ushort, bool[], ModbusExceptionCode> OnWriteCoils { get; set; } =
            (addr, count, buf) => ModbusExceptionCode.IllegalFunction;

        public ModbusTcpSlave(string ipAddress, int port)
        {
            IPAddress addr;
            if (!IPAddress.TryParse(ipAddress, out addr))
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
            var responseCode = ModbusExceptionCode.IllegalFunction;
            switch (functionCode)
            {
                case 1:
                    address = (request[8] << 8) | request[9];
                    count = (request[10] << 8) | request[11];
                   // (responseBuffer, responseCode) = ReadCoils(address, count);
                    break;
                case 2:
                    // Read Discrete Inputs
                    break;
                case 5:
                    // Write single Coil
                    break;
                case 15:
                    // Write multiple Coils
                    break;
                case 3:
                    // Read Holding Registers
                    break;
                case 4:
                    // Read Input Register
                    break;
                case 6:
                    // Write single Register
                    break;
                case 16:
                    // Write multiple Registers
                    break;
                default:
                    break;
            }

            var response = new byte[0];
            if (responseCode != ModbusExceptionCode.Ok)
            {
                response = GenerateExceptionResponse(transId, unitId, functionCode, responseCode);
            }

            return response;
        }

        private byte[] GenerateExceptionResponse(int transId, byte unitId, int functionCode, ModbusExceptionCode responseCode)
        {
            var function = (byte)(functionCode | 0x80);
            var exceptionCode = (byte)responseCode;
            var response = new byte[9];
            response[0] = (byte)(transId >> 8);
            response[1] = (byte)transId;
            response[2] = 0;
            response[3] = 0;
            response[4] = 0;
            response[5] = 3;
            response[6] = unitId;
            response[7] = function;
            response[8] = exceptionCode;

            return response;
        }
    }
}
