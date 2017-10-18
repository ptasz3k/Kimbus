using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Kimbus.Helpers;

namespace Kimbus.Master
{
    public class MbTcpTransport : IMbTransport
    {
        private readonly SocketAsyncEventArgs _eventArgs;
        private readonly SocketAwaitable _awaitable;
        private readonly Socket _socket;
        private readonly int _timeout;

        private readonly string _ip;
        private readonly ushort _port;
        private ushort _transactionId;

        private void NextTransaction()
        {
            _transactionId = (ushort)(_transactionId == ushort.MaxValue ? 0 : _transactionId + 1);
        }

        private static byte LittleNibble(ushort val)
        {
            return (byte)(val & 0x00ff);
        }

        private static byte BigNibble(ushort val)
        {
            return (byte)(val >> 8);
        }

        public MbTcpTransport(string ip, ushort port, int timeout = 10000)
        {
            _eventArgs = new SocketAsyncEventArgs();
            _awaitable = new SocketAwaitable(this._eventArgs);
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
            Func<Task> connect = async () => await _socket.ConnectAsync(_awaitable, _ip, _port);
            Func<Task> connectWithTimeout = async () => await Task.WhenAny(connect(), Task.Delay(_timeout));
            var t = Try.Apply(() => connectWithTimeout().Wait());
            if (!_socket.Connected && t.IsSuccess)
            {
                // timeout
                _socket.Close();
                t = Try.Failure<bool>(new SocketException((int)SocketError.TimedOut));
            }

            return t;
        }

        public Try<List<byte>> Receive()
        {
            if (!_socket.Connected)
                return Try.Failure<List<byte>>(new SocketException((int)SocketError.NotConnected));

            var buffer = new List<byte>();

            Func<Task> receive = async () =>
            {
                do
                {
                    await _socket.ReceiveAsync(_awaitable);
                    buffer.AddRange(_eventArgs.Buffer.Take(_eventArgs.BytesTransferred));
                } while (_socket.Available != 0);
            };

            bool rectimeout = false;
            Func<Task> receiveWithTimeout =
              async () => await Task.WhenAny(receive(), Task.Delay(_timeout).ContinueWith(
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

                  // TODO: check if transId and unitId are consistent with request,
                  //       throw exception if not
                  return pdu;

              })
              select res;

            // TODO: Timeouts and other bad stuff...
            // TODO: should we check transactionId?

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
                /* at this point we assume, that there can't be anything to read,
                * so socket received FINACK some time ago
                * http://stackoverflow.com/questions/23665917/is-it-possible-to-detect-if-a-socket-is-in-the-close-wait-state/ */
                _socket.Close();
                return Try.Failure<bool>(new SocketException((int)SocketError.NotConnected));
            }

            Func<Task> send = async () => await _socket.SendAsync(_awaitable);

            var result =
              from pdu in Try.Apply(() => MbHelpers.PrependMbapHeader(unitId, _transactionId, adu))
              from set in Try.Apply(() => _eventArgs.SetBuffer(pdu.ToArray(), 0, pdu.Count))
              from snd in Try.Apply(() => send().Wait())
              select _eventArgs.BytesTransferred > 0;

            // TODO: check for exceptions and so on....

            return result;
        }

        public void Close()
        {
            _socket.Dispose();
            _eventArgs.Dispose();
        }

        public void Dispose()
        {
            _socket.Dispose();
            _eventArgs.Dispose();
        }
    }
}
