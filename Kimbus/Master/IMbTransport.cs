using System;
using System.Collections.Generic;
using Kimbus.Helpers;

namespace Kimbus.Master
{
    public interface IMbTransport : IDisposable
    {
        bool Connected { get; }
        Try<bool> Connect();
        Try<bool> Send(byte unitId, List<byte> bytes);
        Try<List<byte>> Receive(byte expectedUnitId);
        void Close();
    }
}
