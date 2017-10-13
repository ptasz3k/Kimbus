using System;
using System.Collections.Generic;
using System.Linq;
using Kimbus.Helpers;
using NLog;

namespace Kimbus.Slave
{
    internal static class ModbusFunctions
    {
        private static Logger _logger = LogManager.GetCurrentClassLogger();

        internal static byte[] GenerateExceptionResponse(int transId, byte unitId, int functionCode, MbExceptionCode responseCode)
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


        internal static (byte[], MbExceptionCode) Read<T>(int address, int count,
            Func<ushort, ushort, (T[], MbExceptionCode)> onRead, Func<IEnumerable<T>, IEnumerable<byte>> unpack)
        {
            if (onRead == null)
            {
                return (new byte[0], MbExceptionCode.IllegalFunction);
            }

            var maxCount = typeof(T) == typeof(bool) ? 0x07d0 : 0x007d;

            if (count < 1 || count > maxCount)
            {
                return (new byte[0], MbExceptionCode.IllegalDataValue);
            }

            try
            {
                (var values, var exceptionCode) = onRead((ushort)address, (ushort)count);

                if (exceptionCode != MbExceptionCode.Ok)
                {
                    return (new byte[0], exceptionCode);
                }

                if (values == null || values.Length == 0 || values.Length != count)
                {
                    return (new byte[0], MbExceptionCode.SlaveDeviceFailure);
                }

                var bytes = unpack(values).ToArray();

                return (bytes, MbExceptionCode.Ok);
            }
            catch (Exception ex)
            {
                _logger.Error($"Unhandled exception in user supplied function {ex.Message}" +
                    ex.InnerException != null ? $", inner exception: {ex.InnerException.Message}" : string.Empty);
                return (new byte[0], MbExceptionCode.SlaveDeviceFailure);
            }

        }

        internal static (byte[], MbExceptionCode) ReadDigitals(int address, int count,
            Func<ushort, ushort, (bool[], MbExceptionCode)> onRead)
        {
            return Read(address, count, onRead, bools => bools.Chunk(8).Select(MbHelpers.BooleansToByte));
        }

        internal static (byte[], MbExceptionCode) ReadAnalogs(int address, int count,
            Func<ushort, ushort, (ushort[], MbExceptionCode)> onRead)
        {
            return Read(address, count, onRead, ushorts => ushorts.SelectMany(us => new byte[] { (byte)(us >> 8), (byte)(us & 0xff) }));
        }


        internal static MbExceptionCode WriteCoils(int address, int count, byte[] input,
            Func<ushort, bool[], MbExceptionCode> onWrite)
        {
            if (onWrite == null || input == null || input.Length == 0)
            {
                return MbExceptionCode.IllegalFunction;
            }

            var bools = new bool[0];
            if (count == 1 && input.Length == 2)
            {
                /* write single coil - special values for true (0xff00) and false (0x0000) */
                var value = (input[0] << 8) | input[1];
                if (value != 0xff00 && value != 0x00000)
                {
                    return MbExceptionCode.IllegalDataValue;
                }

                bools = new bool[] { value == 0xff00 };
            }
            else
            {
                if ((count < 1)
                    || (count > 0x07b0) 
                    || (input.Length != count / 8 + (count % 8 != 0 ? 1 : 0)))
                {
                    return MbExceptionCode.IllegalDataValue;
                }

                bools = input.SelectMany(b => Enumerable.Range(0, 8).Select(n => (b & (1 << n)) != 0)).Take(count).ToArray();
            }

            try
            {
                return onWrite((ushort)address, bools);
            }
            catch (Exception ex)
            {
                _logger.Error($"Unhandled exception in user supplied function {ex.Message}" +
                    ex.InnerException != null ? $", inner exception: {ex.InnerException.Message}" : string.Empty);
                return MbExceptionCode.SlaveDeviceFailure;
            }
        }

        internal static MbExceptionCode WriteHoldingRegisters(int address, int count, byte[] input,
            Func<ushort, ushort[], MbExceptionCode> onWrite)
        {
            if (onWrite == null || input == null || input.Length == 0)
            {
                return MbExceptionCode.IllegalFunction;
            }

            if ((count == 0)
               || (count > 0x007b)
               || (input.Length != count * 2))
            {
                return MbExceptionCode.IllegalDataValue;
            }

            var ushorts = input.Chunk(2).Select(b => (ushort)(b.ElementAt(0) << 8 | b.ElementAt(1))).ToArray();

            try
            {
                return onWrite((ushort)address, ushorts);
            }
            catch (Exception ex)
            {
                _logger.Error($"Unhandled exception in user supplied function {ex.Message}" +
                    ex.InnerException != null ? $", inner exception: {ex.InnerException.Message}" : string.Empty);
                return MbExceptionCode.SlaveDeviceFailure;
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
