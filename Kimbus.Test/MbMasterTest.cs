using System;
using Xunit;
using FakeItEasy;
using Kimbus.Master;
using System.Collections.Generic;

namespace Kimbus.Test
{
    public class MbMasterTest
    {
        [Fact]
        public void cannot_read_more_than_123_holding_registers()
        {
            var transport = A.Fake<IMbTransport>();
            var mbmaster = new MbMaster(transport);
            var r = mbmaster.ReadHoldingRegisters(0, 1, 124);

            Assert.True(r.IsFailure);
            Assert.IsType<ArgumentOutOfRangeException>(r.Failure);
        }

        [Fact]
        public void cannot_read_0_holding_registers()
        {
            var transport = A.Fake<IMbTransport>();
            var mbmaster = new MbMaster(transport);
            var r = mbmaster.ReadHoldingRegisters(0, 1, 0);

            Assert.True(r.IsFailure);
            Assert.IsType<ArgumentOutOfRangeException>(r.Failure);
        }

        [Fact]
        public void cannot_read_more_than_123_input_registers()
        {
            var transport = A.Fake<IMbTransport>();
            var mbmaster = new MbMaster(transport);
            var r = mbmaster.ReadInputRegisters(0, 1, 124);

            Assert.True(r.IsFailure);
            Assert.IsType<ArgumentOutOfRangeException>(r.Failure);
        }

        [Fact]
        public void cannot_read_0_input_registers()
        {
            var transport = A.Fake<IMbTransport>();
            var mbmaster = new MbMaster(transport);
            var r = mbmaster.ReadInputRegisters(0, 1, 0);

            Assert.True(r.IsFailure);
            Assert.IsType<ArgumentOutOfRangeException>(r.Failure);
        }

        [Fact]
        public void cannot_read_more_than_2000_coils()
        {
            var transport = A.Fake<IMbTransport>();
            var mbmaster = new MbMaster(transport);
            var r = mbmaster.ReadCoils(0, 1, 2001);

            Assert.True(r.IsFailure);
            Assert.IsType<ArgumentOutOfRangeException>(r.Failure);
        }

        [Fact]
        public void cannot_read_0_coils()
        {
            var transport = A.Fake<IMbTransport>();
            var mbmaster = new MbMaster(transport);
            var r = mbmaster.ReadCoils(0, 1, 0);

            Assert.True(r.IsFailure);
            Assert.IsType<ArgumentOutOfRangeException>(r.Failure);
        }

        [Fact]
        public void cannot_read_more_than_2000_discrete_inputs()
        {
            var transport = A.Fake<IMbTransport>();
            var mbmaster = new MbMaster(transport);
            var r = mbmaster.ReadDiscreteInputs(0, 1, 2001);

            Assert.True(r.IsFailure);
            Assert.IsType<ArgumentOutOfRangeException>(r.Failure);
        }

        [Fact]
        public void cannot_read_0_discrete_inputs()
        {
            var transport = A.Fake<IMbTransport>();
            var mbmaster = new MbMaster(transport);
            var r = mbmaster.ReadDiscreteInputs(0, 1, 0);

            Assert.True(r.IsFailure);
            Assert.IsType<ArgumentOutOfRangeException>(r.Failure);
        }

        [Fact]
        public void cannot_write_more_than_123_holding_registers()
        {
            var transport = A.Fake<IMbTransport>();
            var mbmaster = new MbMaster(transport);
            var values = new ushort[124];
            var r = mbmaster.WriteHoldingRegisters(0, 1, values);
            Assert.True(r.IsFailure);
            Assert.IsType<ArgumentOutOfRangeException>(r.Failure);
        }

        [Fact]
        public void cannot_write_0_holding_registers()
        {
            var transport = A.Fake<IMbTransport>();
            var mbmaster = new MbMaster(transport);
            var values = new List<ushort>();
            var r = mbmaster.WriteHoldingRegisters(0, 1, values);

            Assert.True(r.IsFailure);
            Assert.IsType<ArgumentOutOfRangeException>(r.Failure);
        }

        [Fact]
        public void cannot_write_null_holding_registers()
        {
            var transport = A.Fake<IMbTransport>();
            var mbmaster = new MbMaster(transport);
            List<ushort> values = null;
            var r = mbmaster.WriteHoldingRegisters(0, 1, values);

            Assert.True(r.IsFailure);
            Assert.IsType<ArgumentNullException>(r.Failure);
        }

        [Fact]
        public void cannot_write_more_than_0x7b0_coils()
        {
            var transport = A.Fake<IMbTransport>();
            var mbmaster = new MbMaster(transport);
            var values = new bool[0x7b1];
            var r = mbmaster.WriteCoils(0, 1, values);
            Assert.True(r.IsFailure);
            Assert.IsType<ArgumentOutOfRangeException>(r.Failure);
        }

        [Fact]
        public void cannot_write_0_coils()
        {
            var transport = A.Fake<IMbTransport>();
            var mbmaster = new MbMaster(transport);
            var values = new List<bool>();
            var r = mbmaster.WriteCoils(0, 1, values);

            Assert.True(r.IsFailure);
            Assert.IsType<ArgumentOutOfRangeException>(r.Failure);
        }

        [Fact]
        public void cannot_write_null_coils()
        {
            var transport = A.Fake<IMbTransport>();
            var mbmaster = new MbMaster(transport);
            List<bool> values = null;
            var r = mbmaster.WriteCoils(0, 1, values);

            Assert.True(r.IsFailure);
            Assert.IsType<ArgumentNullException>(r.Failure);
        }

        [Fact]
        public void cannot_write_null_file()
        {
            var transport = A.Fake<IMbTransport>();
            var mbmaster = new MbMaster(transport);
            byte[] file = null;
            var r = mbmaster.WriteFile(0, 1, 244, 0, file);

            Assert.True(r.IsFailure);
            Assert.IsType<ArgumentNullException>(r.Failure);
        }

        [Fact]
        public void cannot_write_empty_file()
        {
            var transport = A.Fake<IMbTransport>();
            var mbmaster = new MbMaster(transport);
            byte[] file = new byte[0];
            var r = mbmaster.WriteFile(0, 1, 244, 0, file);

            Assert.True(r.IsFailure);
            Assert.IsType<ArgumentException>(r.Failure);
        }
    }
}
