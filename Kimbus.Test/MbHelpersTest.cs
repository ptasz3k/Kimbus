using Xunit;
using Kimbus.Helpers;
using System.Collections.Generic;

namespace Kimbus.Test
{
    public class MbHelpersTest
    {
        [Theory]
        [MemberData(nameof(CrcCheckData))]
        public void crc_is_calculated_correctly(List<byte> frame, ushort crc)
        {
            var mb = MbHelpers.CalculateCrc(frame);
            Assert.Equal(crc, mb);
        }

        public static IEnumerable<object[]> CrcCheckData()
        {
            yield return new object[] { new List<byte> { 0xF7, 0x03, 0x02, 0x64, 0x00, 0x08 }, 0xfd10 };
            yield return new object[] { new List<byte> { 0x01, 0x03, 0x00, 0x00, 0x00, 0x02 }, 0x0bc4 };
            yield return new object[] { new List<byte> { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0xff, 0xfe }, 0x4973 };
        }
    }
}
