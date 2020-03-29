using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Kimbus.Helpers;
using NLog;

namespace Kimbus.Master
{
    public class MbTcpTransport : IMbTransport
    {
        private readonly Socket _socket;
        private readonly int _timeout;

        private readonly string _ip;
        private readonly ushort _port;
        private ushort _transactionId;

        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private void NextTransaction()
        {
            _transactionId = (ushort)(_transactionId == ushort.MaxValue ? 0 : _transactionId + 1);
        }

        public MbTcpTransport(string ip, ushort port, int timeout = 10000)
        {
            _socket = new Socket(IPAddress.Parse(ip).AddressFamily, SocketType.Stream, ProtocolType.Tcp) { SendTimeout = 1000, ReceiveTimeout = 1000 };
            _socket.NoDelay = true;
            _ip = ip;
            _port = port;
            _transactionId = 0;
            _timeout = timeout;
        }

        public bool Connected { get { return _socket.Connected; } }

        public Try<bool> Connect()
        {
            var endpoint = new IPEndPoint(IPAddress.Parse(_ip), _port);
            async Task connect() => await _socket.ConnectAsync(endpoint);
            async Task connectWithTimeout() => await Task.WhenAny(connect(), Task.Delay(_timeout));
            var t = Try.Apply(() => connectWithTimeout().Wait());
            if (!_socket.Connected && t.IsSuccess)
            {
                // timeout
                _socket.Close();
                t = Try.Failure<bool>(new SocketException((int)SocketError.TimedOut));
            }

            return t;
        }

        public Try<List<byte>> Receive(byte expectedUnitId)
        {
            if (!_socket.Connected)
                return Try.Failure<List<byte>>(new SocketException((int)SocketError.NotConnected));

            var buffer = new List<byte>();

            async Task receive()
            {
                do
                {
                    var segment = new ArraySegment<byte>(new byte[512]);
                    var received = await _socket.ReceiveAsync(segment, SocketFlags.None);
                    buffer.AddRange(segment.Array.Take(received));
                } while (_socket.Available != 0);
            }

            bool rectimeout = false;
            async Task receiveWithTimeout() => await Task.WhenAny(receive(), Task.Delay(_timeout).ContinueWith(
                t => rectimeout = true));

            var result =
              from rec in Try.Apply(() => receiveWithTimeout().Wait())
              from res in Try.Apply(() =>
              {
                  if (rectimeout)
                  {
                      _socket.Close();
                      throw new SocketException((int)SocketError.TimedOut);
                  }

                  if (buffer.Count == 0)
                  {
                      _socket.Close();
                      throw new SocketException((int)SocketError.NotConnected);
                  }

                  (var transId, var unitId, var pdu) = MbHelpers.UnwrapMbapHeader(buffer);

                  if (transId != _transactionId)
                  {
                      var message = $"Sent transaction id {_transactionId}, received {transId}. Dropping response.";
                      _logger.Debug(message);
                      throw new Exception(message);
                  }

                  if (unitId != expectedUnitId)
                  {
                      var message = $"Sent unit id {expectedUnitId}, received {unitId}. Dropping response.";
                      _logger.Debug(message);
                      throw new Exception(message);
                  }

                  return pdu;
              })
              select res;

            NextTransaction();

            return result;
        }

        public Try<bool> Send(byte unitId, List<byte> adu)
        {
            if (!_socket.Connected)
            {
                return Try.Failure<bool>(new SocketException((int)SocketError.NotConnected));
            }

            if (_socket.Poll(0, SelectMode.SelectRead))
            {
                /* at this point we assume that there can't be anything to read,
                * so socket received FINACK some time ago
                * http://stackoverflow.com/questions/23665917/is-it-possible-to-detect-if-a-socket-is-in-the-close-wait-state/ */
                _socket.Close();
                return Try.Failure<bool>(new SocketException((int)SocketError.NotConnected));
            }

            async Task<int> send(ArraySegment<byte> segment) => await _socket.SendAsync(segment, SocketFlags.None);

            var result =
              from pdu in Try.Apply(() => new ArraySegment<byte>(MbHelpers.PrependMbapHeader(unitId, _transactionId, adu).ToArray()))
              from snd in Try.Apply(() => { var sent = send(pdu); sent.Wait(); return sent.Result; })
              select snd == pdu.Count;

            // TODO: check for exceptions and so on....

            return result;
        }

        public void Close()
        {
            _socket.Dispose();
        }

        public void Dispose()
        {
            _socket.Dispose();
        }
    }
}
