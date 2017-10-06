using System;
using System.Collections.Generic;
using Kimbus.Helpers;

namespace Kimbus.Master
{
    public interface IMbMaster : IDisposable
    {
        bool Connected { get; }
        Try<bool> Open();
        void Close();
        Try<List<bool>> ReadCoils(byte slaveId, ushort address, ushort count);
        Try<List<bool>> ReadDiscreteInputs(byte slaveId, ushort address, ushort count);
        Try<List<ushort>> ReadHoldingRegisters(byte slaveId, ushort address, ushort count);
        Try<List<ushort>> ReadInputRegisters(byte slaveId, ushort address, ushort count);
        Try<bool> WriteCoils(byte slaveId, ushort address, IEnumerable<bool> values);
        Try<bool> WriteHoldingRegisters(byte slaveId, ushort address, IEnumerable<ushort> values);
        Try<List<byte>> SendUserFunction(byte slaveId, byte functionCode, byte[] data);
    }
}