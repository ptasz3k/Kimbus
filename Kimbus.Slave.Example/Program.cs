using System;

namespace Kimbus.Slave.Example
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");

            var mbSlave = new ModbusTcpSlave("127.0.0.1", 502);

            mbSlave.OnWriteCoils = (start, bools) =>
            {
                Console.WriteLine("Write coils at {0}, len: {1}", start, bools.Length);

                for (var i = 0; i < bools.Length; ++i)
                {
                    Console.WriteLine("{0}: {1}", i + start, bools[i]);
                }

                return ModbusExceptionCode.Ok;
            };

            mbSlave.OnWriteHoldingRegisters = (start, hrs) =>
            {
                Console.WriteLine("Write holding registers at {0}, len: {1}", start, hrs.Length);

                for (var i = 0; i < hrs.Length; ++i)
                {
                    Console.WriteLine("{0}: {1}", i + start, hrs[i]);
                }

                return ModbusExceptionCode.Ok;

            };

            mbSlave.OnReadHoldingRegisters = (start, count) =>
            {
                var buffer = new ushort[count];

                for (var i = 0; i < count; ++i)
                {
                    buffer[i] = (ushort)(i + start);
                }

                return (buffer, ModbusExceptionCode.Ok);
            };
            mbSlave.OnReadInputRegisters = mbSlave.OnReadHoldingRegisters;


            mbSlave.OnReadCoils = (start, count) =>
            {
                var buffer = new bool[count];

                for (var i = 0; i < count; ++i)
                {
                    buffer[i] = (start + i) % 3 == 0;
                }

                return (buffer, ModbusExceptionCode.Ok);
            };
            mbSlave.OnReadDiscretes = mbSlave.OnReadCoils;


            mbSlave.Listen();

            Console.ReadLine();
        }
    }
}
