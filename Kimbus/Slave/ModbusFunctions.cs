using System;
using System.Collections.Generic;
using System.Linq;
using Kimbus.Helpers;
using NLog;

namespace Kimbus.Slave
{
    internal static class ModbusFunctions
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        internal static (byte[], MbExceptionCode) Read<T>(byte unitId, ushort address, ushort count,
            Func<byte, ushort, ushort, (T[], MbExceptionCode)> onRead, Func<IEnumerable<T>, IEnumerable<byte>> unpack)
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
                (var values, var exceptionCode) = onRead(unitId, address, count);

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

        internal static (byte[], MbExceptionCode) ReadDigitals(byte unitId, ushort address, ushort count,
            Func<byte, ushort, ushort, (bool[], MbExceptionCode)> onRead)
        {
            return Read(unitId, address, count, onRead, bools => bools.Chunk(8).Select(MbHelpers.BooleansToByte));
        }

        internal static (byte[], MbExceptionCode) ReadAnalogs(byte unitId, ushort address, ushort count,
            Func<byte, ushort, ushort, (ushort[], MbExceptionCode)> onRead)
        {
            return Read(unitId, address, count, onRead, ushorts => ushorts.SelectMany(us => new byte[] { (byte)(us >> 8), (byte)(us & 0xff) }));
        }


        internal static MbExceptionCode WriteCoils(byte unitId, ushort address, ushort count, byte[] input,
            Func<byte, ushort, bool[], MbExceptionCode> onWrite)
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
                return onWrite(unitId, address, bools);
            }
            catch (Exception ex)
            {
                _logger.Error($"Unhandled exception in user supplied function {ex.Message}" +
                    ex.InnerException != null ? $", inner exception: {ex.InnerException.Message}" : string.Empty);
                return MbExceptionCode.SlaveDeviceFailure;
            }
        }

        internal static MbExceptionCode WriteHoldingRegisters(byte unitId, ushort address, ushort count, byte[] input,
            Func<byte, ushort, ushort[], MbExceptionCode> onWrite)
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
                return onWrite(unitId, address, ushorts);
            }
            catch (Exception ex)
            {
                _logger.Error($"Unhandled exception in user supplied function {ex.Message}" +
                    ex.InnerException != null ? $", inner exception: {ex.InnerException.Message}" : string.Empty);
                return MbExceptionCode.SlaveDeviceFailure;
            }
        }
    }
}
