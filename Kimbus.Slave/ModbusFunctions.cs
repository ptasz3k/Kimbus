using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kimbus.Slave.Helpers;

namespace Kimbus.Slave
{
    internal static class ModbusFunctions
    {
        internal static byte[] GenerateExceptionResponse(int transId, byte unitId, int functionCode, ModbusExceptionCode responseCode)
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

        private static byte BooleansToByte(IEnumerable<bool> bools)
        {
            if (bools == null) throw new ArgumentNullException("bools");

            var boolist = bools.ToList();

            if (boolist.Count > 8) throw new ArgumentException("Cannot convert more than 8 bools to MbByte");

            return Enumerable.Range(0, boolist.Count)
              .Zip(boolist, (n, b) => (b ? 1 : 0) << n)
              .Aggregate<int, byte>(0, (acc, bit) => (byte)(acc | bit));
        }

        internal static (byte[], ModbusExceptionCode) Read<T>(int address, int count,
            Func<ushort, ushort, (T[], ModbusExceptionCode)> onRead, Func<IEnumerable<T>, IEnumerable<byte>> unpack)
        {
            if (onRead == null)
            {
                return (new byte[0], ModbusExceptionCode.IllegalFunction);
            }

            var maxCount = typeof(T) == typeof(bool) ? 0x07d0 : 0x007d;

            if (count < 1 || count > maxCount)
            {
                return (new byte[0], ModbusExceptionCode.IllegalDataValue);
            }

            try
            {
                (var values, var exceptionCode) = onRead((ushort)address, (ushort)count);

                if (exceptionCode != ModbusExceptionCode.Ok)
                {
                    return (new byte[0], exceptionCode);
                }

                if (values == null || values.Length == 0 || values.Length != count)
                {
                    return (new byte[0], ModbusExceptionCode.SlaveDeviceFailure);
                }

                var bytes = unpack(values).ToArray();

                return (bytes, ModbusExceptionCode.Ok);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unhandled exception in user supplied function " + ex.Message);
                return (new byte[0], ModbusExceptionCode.SlaveDeviceFailure);
            }

        }

        internal static (byte[], ModbusExceptionCode) ReadDigitals(int address, int count,
            Func<ushort, ushort, (bool[], ModbusExceptionCode)> onRead)
        {
            return Read(address, count, onRead, bools => bools.Chunk(8).Select(BooleansToByte));
        }

        internal static (byte[], ModbusExceptionCode) ReadAnalogs(int address, int count,
            Func<ushort, ushort, (ushort[], ModbusExceptionCode)> onRead)
        {
            return Read(address, count, onRead, ushorts => ushorts.SelectMany(us => new byte[] { (byte)(us >> 8), (byte)(us & 0xff) }));
        }


        internal static ModbusExceptionCode WriteCoils(int address, int count, byte[] input,
            Func<ushort, bool[], ModbusExceptionCode> onWrite)
        {
            if (onWrite == null || input == null || input.Length == 0)
            {
                return ModbusExceptionCode.IllegalFunction;
            }

            var bools = new bool[0];
            if (count == 1 && input.Length == 2)
            {
                /* write single coil - special values for true (0xff00) and false (0x0000) */
                var value = (input[0] << 8) | input[1];
                if (value != 0xff00 && value != 0x00000)
                {
                    return ModbusExceptionCode.IllegalDataValue;
                }

                bools = new bool[] { value == 0xff00 };
            }
            else
            {
                if ((count < 1)
                    || (count > 0x07b0) 
                    || (input.Length != count / 8 + (count % 8 != 0 ? 1 : 0)))
                {
                    return ModbusExceptionCode.IllegalDataValue;
                }

                bools = input.SelectMany(b => Enumerable.Range(0, 8).Select(n => (b & (1 << n)) != 0)).Take(count).ToArray();
            }

            try
            {
                return onWrite((ushort)address, bools);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unhandled exception in user supplied function " + ex.Message);
                return ModbusExceptionCode.SlaveDeviceFailure;
            }
        }

        internal static ModbusExceptionCode WriteHoldingRegisters(int address, int count, byte[] input,
            Func<ushort, ushort[], ModbusExceptionCode> onWrite)
        {
            if (onWrite == null || input == null || input.Length == 0)
            {
                return ModbusExceptionCode.IllegalFunction;
            }

            if ((count == 0)
               || (count > 0x007b)
               || (input.Length != count * 2))
            {
                return ModbusExceptionCode.IllegalDataValue;
            }

            var ushorts = input.Chunk(2).Select(b => (ushort)(b.ElementAt(0) << 8 | b.ElementAt(1))).ToArray();

            try
            {
                return onWrite((ushort)address, ushorts);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Unhandled exception in user supplied function " + ex.Message);
                return ModbusExceptionCode.SlaveDeviceFailure;
            }
        }

        /// <summary>
        /// Prepend modbus response with MBAP header
        /// </summary>
        /// <param name="transId"></param>
        /// <param name="unitId"></param>
        /// <param name="functionCode"></param>
        /// <param name="responseBuffer"></param>
        /// <returns></returns>
        internal static byte[] GenerateResponse(int transId, byte unitId, byte functionCode, byte[] responseBuffer)
        {
            var response = new byte[7 + responseBuffer.Length];
            response[0] = (byte)(transId >> 8);
            response[1] = (byte)(transId & 0xff);
            response[2] = 0;
            response[3] = 0;
            response[4] = (byte)((responseBuffer.Length + 1) >> 8);
            response[5] = (byte)((responseBuffer.Length + 1) & 0xff);
            response[6] = unitId;
            Array.Copy(responseBuffer, 0, response, 7, responseBuffer.Length);

            return response;
        }
    }
}
