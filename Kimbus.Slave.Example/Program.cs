using System;
using System.Threading;

namespace Kimbus.Slave.Example
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");

            var mbSlave = new MbTcpSlave("*", 502)
            {
                OnWriteCoils = (_, start, bools) =>
                {
                    Console.WriteLine("Write coils at {0}, len: {1}", start, bools.Length);

                    for (var i = 0; i < bools.Length; ++i)
                    {
                        Console.WriteLine("{0}: {1}", i + start, bools[i]);
                    }

                    return MbExceptionCode.Ok;
                },

                OnWriteHoldingRegisters = (_, start, hrs) =>
                {
                    Console.WriteLine("Write holding registers at {0}, len: {1}", start, hrs.Length);

                    for (var i = 0; i < hrs.Length; ++i)
                    {
                        Console.WriteLine("{0}: {1}", i + start, hrs[i]);
                    }

                    return MbExceptionCode.Ok;

                },

                OnReadHoldingRegisters = (_, start, count) =>
                {
                    var buffer = new ushort[count];

                    for (var i = 0; i < count; ++i)
                    {
                        buffer[i] = (ushort)(i + start);
                    }

                    return (buffer, MbExceptionCode.Ok);
                }
            };
            mbSlave.OnReadInputRegisters = mbSlave.OnReadHoldingRegisters;


            mbSlave.OnReadCoils = (_, start, count) =>
            {
                var buffer = new bool[count];

                for (var i = 0; i < count; ++i)
                {
                    buffer[i] = (start + i) % 3 == 0;
                }

                return (buffer, MbExceptionCode.Ok);
            };
            mbSlave.OnReadDiscretes = mbSlave.OnReadCoils;


            mbSlave.Listen();

            var errors = 0;
            var tx = 0;
            while (true)
            {
                using (var mbMaster = new Master.MbMaster(new Master.MbTcpTransport("127.0.0.1", 502)))
                {
                    mbMaster.Open();
                    while (true)
                    {
                        tx++;
                        var result = mbMaster.ReadCoils(0, 999, 1200);
                        if (!result.IsSuccess)
                        {
                            errors++;
                            if (!mbMaster.Connected)
                            {
                                break;
                            }
                        }
                        else
                        {
                            ;
                        }
                        Console.WriteLine($"Tx: {tx}, errors: {errors}.");
                        Thread.Sleep(10);
                        if (tx % 1000 == 0)
                        {
                            Console.WriteLine("Waiting 2.5 minutes for timeout...");
                            Thread.Sleep(150000);
                        }
                        else if (tx % 100 == 0)
                        {
                            mbMaster.Close();
                            break;
                        }
                    }
                }
            }

            Console.ReadLine();
        }
    }
}
