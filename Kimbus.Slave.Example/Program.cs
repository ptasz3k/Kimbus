using System;

namespace Kimbus.Slave.Example
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");

            var mbSlave = new ModbusTcpSlave("127.0.0.1", 502);
            mbSlave.Listen();

            Console.ReadLine();
        }
    }
}
