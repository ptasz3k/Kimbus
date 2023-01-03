using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Threading.Tasks;
using Kimbus.Helpers;

namespace Kimbus.Master
{
    public class MbRtuTransport : IMbTransport
    {
        private readonly string _portName;
        private readonly int _baudRate;
        private readonly int _dataBits;
        private readonly Parity _parity;
        private readonly StopBits _stopBits;

        private SerialPort _serialPort;

        public MbRtuTransport(string portName, int baudRate, int dataBits, Parity parity, StopBits stopBits)
        {
            _portName = portName;
            _baudRate = baudRate;
            _dataBits = dataBits;
            _parity = parity;
            _stopBits = stopBits;
        }

        public bool Connected => _serialPort?.IsOpen ?? false;

        public void Close()
        {
          _serialPort?.Close();
        }

        public Try<bool> Connect()
        {
          _serialPort = new SerialPort(_portName, _baudRate, _parity, _dataBits, _stopBits);
            async Task connect() => _serialPort.Open();
            async Task timeout() => await Task.Delay(100);
            async Task connectWithTimeout() => await Task.WhenAny(connect(), timeout());
            var t = Try.Apply(() => connectWithTimeout().Wait());
            return t;
        }

        public void Dispose()
        {
          _serialPort?.Dispose();
        }

        public Try<List<byte>> Receive(byte expectedUnitId)
        {
          if (!Connected)
          {
            return Try.Failure<List<byte>>(new IOException("Not connected"));
          }

          var buffer = new List<byte>();

          async Task receive() {
            do {
              var segment = new byte[256];
              await _serialPort.BaseStream.ReadAsync(segment, 0, segment.Length);
              buffer.AddRange(segment);
            } while (_serialPort.BytesToRead > 0);
          }

          bool rectimeout = false;

          async Task receiveWithTimeout() {
            await Task.WhenAny(receive(), Task.Delay(100));
            rectimeout = true;
          }

          var result = 
            from rec in Try.Apply(() => receiveWithTimeout().Wait())
            from res in Try.Apply(() => 
            {
              if (rectimeout)
              {
                throw new IOException("Receive timeout");
              }

              if (buffer.Count == 0)
              {
                throw new IOException("Invalid response");
              }

              (var unitId, var pdu) = MbHelpers.UnwrapRtuHeader(buffer);

              return pdu;
            })
            select res;

            return result;
        }

        public Try<bool> Send(byte unitId, List<byte> bytes)
        {
          if (!Connected)
          {
            return Try.Failure<bool>(new IOException("Not connected"));
          }

          var frame = MbHelpers.WrapRtuFrame(unitId, bytes);
          async Task send() => await _serialPort.BaseStream.WriteAsync(frame.ToArray(), 0, bytes.Count);
          var t = Try.Apply(() => send().Wait());
          return t;
        }
    }
}
