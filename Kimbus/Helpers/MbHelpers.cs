using System;
using System.Collections.Generic;
using System.Linq;

namespace Kimbus.Helpers
{
    static class MbHelpers
    {

        internal static List<byte> PrependMbapHeader(byte unitId, ushort transactionId, List<byte> pdu)
        {
            var header = new List<byte>()
            {
                (byte)(transactionId >> 8),
                (byte)(transactionId & 0xff),
                0x0,
                0x0,
                (byte)((pdu.Count + 1) >> 8),
                (byte)((pdu.Count + 1) & 0xff),
                unitId
            };

            header.AddRange(pdu);
            return header;
        }

        private static ushort CalculateCrc(List<byte> data)
        {
            var crc = 0xffff;
            foreach (var b in data)
            {
                crc ^= b;
                for (var i = 0; i < 8; i++)
                {
                    if ((crc & 0x0001) != 0)
                    {
                        crc >>= 1;
                        crc ^= 0xa001;
                    }
                    else
                    {
                        crc >>= 1;
                    }
                }
            }

            return (ushort)crc;
        }

        internal static (byte unitId, List<byte> data) UnwrapRtuHeader(List<byte> frame)
        {
          if (frame == null || frame.Count < 4)
          {
            throw new ArgumentException("Invalid frame");
          }

          var messageCrc = frame.Skip(frame.Count - 2).Take(2).ToArray();
          var calculatedCrc = CalculateCrc(frame.Take(frame.Count - 2).ToList());

          if (calculatedCrc != (messageCrc[0] << 8 | messageCrc[1]))
          {
            throw new ArgumentException("Invalid CRC");
          }

          var unitId = frame[0];
          var data = frame.Skip(1).Take(frame.Count - 3).ToList();
          return (unitId, data);
        }

        internal static (ushort transId, byte unitId, List<byte> pdu) UnwrapMbapHeader(List<byte> adu)
        {
            if (adu == null || adu.Count < 7)
            {
                throw new ArgumentException("Cannot get MBAP header from adu, it is shorter than 7 bytes", nameof(adu));
            }

            var protoId = (ushort)(adu[2] << 8 | adu[3]);
            if (protoId != 0)
            {
                throw new ArgumentException("Protocol identifier in MBAP header does not meet Modbus specification", nameof(adu));
            }

            var length = (ushort)(adu[4] << 8 | adu[5]);
            if (length != adu.Count - 6)
            {
                throw new ArgumentException("Length specified in MBAP header is not consistent with received buffer", nameof(adu));
            }

            var transId = (ushort)(adu[0] << 8 | adu[1]);
            var unitId = adu[6];

            return (transId, unitId, adu.Skip(7).ToList());
        }

        internal static byte BooleansToByte(IEnumerable<bool> bools)
        {
            if (bools == null)
            {
                throw new ArgumentNullException(nameof(bools));
            }

            var boolist = bools.ToList();

            if (boolist.Count > 8)
            {
                throw new ArgumentException("Cannot convert more than 8 bools to byte", nameof(bools));
            }

            return Enumerable.Range(0, boolist.Count)
              .Zip(boolist, (n, b) => (b ? 1 : 0) << n)
              .Aggregate<int, byte>(0, (acc, bit) => (byte)(acc | bit));
        }
    }
}
