using System;
using System.Collections.Generic;
using System.Linq;
using Kimbus.Helpers;
using NLog;

namespace Kimbus.Master
{
    public class MbMaster : IMbMaster
    {
        private Logger _logger = LogManager.GetCurrentClassLogger();

        protected readonly IMbTransport _mbTransport;

        public MbMaster(IMbTransport transport)
        {
            if (transport == null) throw new ArgumentException("transport");

            _mbTransport = transport;
        }

        private static byte ToMbByte(IEnumerable<bool> bools)
        {
            if (bools == null) throw new ArgumentNullException("bools");

            var boolist = bools.ToList();

            if (boolist.Count > 8) throw new ArgumentException("Cannot convert more than 8 bools to MbByte");

            var byt = Enumerable.Range(0, boolist.Count)
              .Zip(boolist, (n, b) => (b ? 1 : 0) << n)
              .Aggregate<int, byte>(0, (acc, bit) => (byte)(acc | bit));
            return byt;
        }

        internal static List<byte> CreateReadPdu(MbFunctionCode fun, ushort address, ushort count)
        {
            if (count == 0)
            {
                throw new ArgumentException("Cannot read 0 registers");
            }

            if ((fun == MbFunctionCode.ReadCoils || fun == MbFunctionCode.ReadDiscreteInputs) && count > 2000)
            {
                throw new ArgumentException("Cannot read more than 2000 registers in one query");
            }

            if ((fun == MbFunctionCode.ReadHoldingRegisters || fun == MbFunctionCode.ReadInputRegisters) && count > 123)
            {
                throw new ArgumentException("Cannot read more than 123 registers in one query");
            }

            return new List<byte>(5)
      {
        (byte) fun,
        (byte) (address >> 8),
        (byte) (address & 0x00ff),
        (byte) (count >> 8),
        (byte) (count & 0x00ff)
      };
        }

        internal static List<byte> CreateWriteSingleCoilPdu(ushort address, bool value)
        {
            var pdu = new List<byte>(5) { (byte)MbFunctionCode.WriteSingleCoil, (byte)(address >> 8), (byte)(address & 0x00ff) };
            pdu.AddRange(value ? new List<byte>(2) { 0xff, 0x00 } : new List<byte>(2) { 0x00, 0x00 });
            return pdu;
        }

        internal static List<byte> CreateWriteSingleRegisterPdu(ushort address, ushort value)
        {
            return new List<byte>(5)
      {
        (byte) MbFunctionCode.WriteSingleRegister,
        (byte) (address >> 8),
        (byte) (address & 0x00ff),
        (byte) (value >> 8),
        (byte) (value & 0x00ff)
      };
        }

        internal static List<byte> CreateWriteMultipleCoilsPdu(ushort address, List<bool> values)
        {
            if (values == null) throw new ArgumentNullException("values");

            if (values.Count == 0)
            {
                throw new ArgumentException("Cannot write 0 coils");
            }

            if (values.Count > 0x07b0)
            {
                throw new ArgumentException("Cannot send more than 0x07b0 coils in one query");
            }

            var bytes = values.Chunk(8).Select(ToMbByte).ToList();

            var pdu = new List<byte>()
      {
        (byte) MbFunctionCode.WriteMultipleCoils,
        (byte) (address >> 8),
        (byte) (address & 0x00ff),
        (byte) (values.Count >> 8),
        (byte) (values.Count & 0x00ff),
        (byte) (bytes.Count),
      };

            pdu.AddRange(bytes);

            return pdu;
        }

        internal static List<byte> CreateWriteMultipleRegistersPdu(ushort address, List<ushort> values)
        {
            if (values == null) throw new ArgumentNullException("values");

            if (values.Count == 0)
            {
                throw new ArgumentException("Cannot write 0 holding registers");
            }

            if (values.Count > 123)
            {
                throw new ArgumentException("Cannot write more than 123 registers in one query");
            }

            var bytes = values.SelectMany(x => new List<byte>() { (byte)(x >> 8), (byte)(x & 0x00ff) }).ToList();

            var pdu = new List<byte>
      {
        (byte) MbFunctionCode.WriteMultipleRegisters,
        (byte) (address >> 8),
        (byte) (address & 0x00ff),
        (byte) (values.Count >> 8),
        (byte) (values.Count & 0x00ff),
        (byte) (bytes.Count)
      };
            pdu.AddRange(bytes);

            return pdu;
        }

        internal static List<byte> CreateWriteFilePdu(ushort fileNumber, byte recordSize, ushort recordNumber, byte[] file)
        {
            if (file == null) throw new ArgumentNullException(nameof(file));

            if (file.Length == 0) throw new ArgumentException("File cannot be empty.");

            if ((recordSize == 0) || (recordSize > 244)) throw new ArgumentException(nameof(recordSize));

            if (recordSize * recordNumber > file.Length)
                throw new ArgumentException("Record number with given record size lies after the end of the file.");

            var bytesToSkip = recordSize * recordNumber;

            var bytes =
                file.Skip(bytesToSkip)
                    .Take(recordSize)
                    .ToList();

            if (bytes.Count % 2 != 0)
            {
                bytes.Add(0);
            }

            var recordLength = bytes.Count / 2;

            var pdu = new List<byte>
            {
                (byte) MbFunctionCode.WriteFile,
                (byte) 0x00, /* request data length -> to be filled later */
                (byte) 0x06,
                (byte) (fileNumber >> 8),
                (byte) (fileNumber & 0x00ff),
                (byte) (recordNumber >> 8),
                (byte) (recordNumber & 0x00ff),
                (byte) (recordLength >> 8),
                (byte) (recordLength & 0x00ff)
            };
            pdu.AddRange(bytes);

            /* fill in request data length */
            pdu[1] = (byte)(pdu.Count - 2);

            return pdu;
        }

        private static void CheckReadResponsePduHeader(MbFunctionCode fun, int byteCountExpected, List<byte> response)
        {
            if (response == null || response.Count == 0) throw new ArgumentNullException("response");

            CheckFunction(response, fun);
            CheckModbusException(response);

            var byteCount = response.ElementAt(1);
            if (byteCountExpected != byteCount)
            {
                throw new ArgumentException(
                  String.Format("number of bytes in response ({0}) differs from expected ({1})", byteCount, byteCountExpected));
            }

            if (byteCount != response.Count - 2)
            {
                throw new ArgumentException(
                  String.Format("number of bytes in response ({0}) differs from declared ({1})", response.Count - 1, byteCount));
            }
        }

        private static void CheckModbusException(List<byte> response)
        {
            if ((response.First() & 0x80) == 0x80)
            {
                // FIXME: react to custom exceptions in more sane way
                throw new MbException((MbExceptionCode)response.ElementAt(1));
            }
        }

        private static void CheckFunction(List<byte> response, MbFunctionCode fun)
        {
            if ((response.First() & 0x7f) != (int)fun)
            {
                throw new ArgumentException(
                  String.Format("function code in response ({0}) differs from function call ({1})", response.First(), (int)fun));
            }
        }

        internal List<bool> UnwrapDiscretePdu(MbFunctionCode fun, ushort count, List<byte> response)
        {
            var byteCountExpected = (count / 8 + ((count % 8 != 0) ? 1 : 0));
            CheckReadResponsePduHeader(fun, byteCountExpected, response);

            var bools =
              response.Skip(2).SelectMany(b => Enumerable.Range(0, 8).Select(n => (b & (1 << n)) != 0)).Take(count).ToList();
            return bools;
        }

        internal List<ushort> UnwrapAnalogPdu(MbFunctionCode fun, ushort count, List<byte> response)
        {
            var byteCountExpected = count * 2;
            CheckReadResponsePduHeader(fun, byteCountExpected, response);

            var ushorts = response.Skip(2).Chunk(2).Select(xs => (ushort)((xs.ElementAt(0) << 8) | xs.ElementAt(1))).ToList();
            return ushorts;
        }

        public bool Connected { get { return _mbTransport.Connected; } }

        public Try<bool> Open()
        {
            var t = _mbTransport.Connect();
            return t;
        }

        public void Close()
        {
            _mbTransport.Close();
        }

        public Try<List<bool>> ReadCoils(byte unitId, ushort address, ushort count)
        {
            var result =
              from pdu in Try.Apply(() => CreateReadPdu(MbFunctionCode.ReadCoils, address, count))
              from snd in _mbTransport.Send(unitId, pdu)
              from response in _mbTransport.Receive()
              select UnwrapDiscretePdu(MbFunctionCode.ReadCoils, count, response);

            return result;
        }

        public Try<List<bool>> ReadDiscreteInputs(byte unitId, ushort address, ushort count)
        {
            var result =
              from pdu in Try.Apply(() => CreateReadPdu(MbFunctionCode.ReadDiscreteInputs, address, count))
              from snd in _mbTransport.Send(unitId, pdu)
              from response in _mbTransport.Receive()
              select UnwrapDiscretePdu(MbFunctionCode.ReadDiscreteInputs, count, response);

            return result;
        }

        public Try<List<ushort>> ReadHoldingRegisters(byte unitId, ushort address, ushort count)
        {
            var result =
              from pdu in Try.Apply(() => CreateReadPdu(MbFunctionCode.ReadHoldingRegisters, address, count))
              from snd in _mbTransport.Send(unitId, pdu)
              from response in _mbTransport.Receive()
              select UnwrapAnalogPdu(MbFunctionCode.ReadHoldingRegisters, count, response);

            return result;
        }

        public Try<List<ushort>> ReadInputRegisters(byte unitId, ushort address, ushort count)
        {
            var result =
              from pdu in Try.Apply(() => CreateReadPdu(MbFunctionCode.ReadInputRegisters, address, count))
              from snd in _mbTransport.Send(unitId, pdu)
              from response in _mbTransport.Receive()
              select UnwrapAnalogPdu(MbFunctionCode.ReadInputRegisters, count, response);

            return result;
        }

        public Try<bool> WriteCoils(byte unitId, ushort address, IEnumerable<bool> values)
        {
            if (values == null)
            {
                return Try.Failure<bool>(new ArgumentNullException("values"));
            }

            var vals = values.ToList();

            if (vals.Count() == 1)
            {
                return
                  from pdu in Try.Apply(() => CreateWriteSingleCoilPdu(address, vals.First()))
                  from snd in _mbTransport.Send(unitId, pdu)
                  from res in _mbTransport.Receive()
                  select CheckWriteSingleResponse(res, MbFunctionCode.WriteSingleCoil, address, (ushort)(vals.First() ? 0xff00 : 0x0000));
            }

            return
              from pdu in Try.Apply(() => CreateWriteMultipleCoilsPdu(address, vals))
              from snd in _mbTransport.Send(unitId, pdu)
              from res in _mbTransport.Receive()
              select CheckWriteMultipleResponse(res, MbFunctionCode.WriteMultipleCoils, address, vals.Count);
        }

        private bool CheckWriteMultipleResponse(List<byte> response, MbFunctionCode fun, ushort address, int p)
        {
            if (response == null) throw new ArgumentNullException("response");

            CheckFunction(response, fun);
            CheckModbusException(response);

            var responseAddr = (ushort)((response.ElementAt(1) << 8) | response.ElementAt(2));
            if (address != responseAddr)
            {
                throw new ArgumentException(
                  String.Format("address in response ({0}) differs from address in request ({1})", responseAddr, address));
            }

            var responseCount = (ushort)((response.ElementAt(3)) | response.ElementAt(4));
            if (p != responseCount)
            {
                throw new ArgumentException(
                  String.Format("value in response ({0}) differs from value in request ({1})", responseCount, p));
            }

            return true;
        }

        private bool CheckWriteSingleResponse(List<byte> response, MbFunctionCode fun, ushort address, ushort value)
        {
            if (response == null) throw new ArgumentNullException("response");

            CheckFunction(response, fun);
            CheckModbusException(response);

            var responseAddr = (ushort)((response.ElementAt(1) << 8) | response.ElementAt(2));
            if (address != responseAddr)
            {
                throw new ArgumentException(
                  String.Format("address in response ({0}) differs from address in request ({1})", responseAddr, address));
            }

            var responseVal = (ushort)((response.ElementAt(3)) << 8 | response.ElementAt(4));
            if (value != responseVal)
            {
                throw new ArgumentException(
                  String.Format("value in response ({0}) differs from value in request ({1})", responseVal, value));
            }

            return true;
        }

        private bool CheckWriteFileResponse(List<byte> response, List<byte> pdu)
        {
            if (response == null) throw new ArgumentException(nameof(response));

            CheckFunction(response, MbFunctionCode.WriteFile);
            CheckModbusException(response);

            if (response.Zip(pdu, (r, p) => r != p).Any(b => b))
            {
                throw new ArgumentException("Response differs from request.");
            }

            return true;
        }

        public Try<bool> WriteHoldingRegisters(byte unitId, ushort address, IEnumerable<ushort> values)
        {
            if (values == null)
            {
                return Try.Failure<bool>(new ArgumentNullException("values"));
            }

            var vals = values.ToList();

            if (vals.Count == 0)
            {
                return Try.Failure<bool>(new ArgumentException("Cannot write 0 holding registers"));
            }

            if (vals.Count == 1)
            {
                return
                  from pdu in Try.Apply(() => CreateWriteSingleRegisterPdu(address, vals.First()))
                  from snd in _mbTransport.Send(unitId, pdu)
                  from res in _mbTransport.Receive()
                  select CheckWriteSingleResponse(res, MbFunctionCode.WriteSingleRegister, address, vals.First());
            }

            return
              from pdu in Try.Apply(() => CreateWriteMultipleRegistersPdu(address, vals))
              from snd in _mbTransport.Send(unitId, pdu)
              from res in _mbTransport.Receive()
              select CheckWriteMultipleResponse(res, MbFunctionCode.WriteMultipleRegisters, address, vals.Count);
        }

        public Try<bool> WriteFile(byte unitId, ushort fileNumber, byte recordSize, ushort recordNumber, byte[] file)
        {
            _logger.Trace($"fileNumber={fileNumber},  recordSize={recordSize}, recordNumber={recordNumber}");

            if (file == null)
            {
                return Try.Failure<bool>(new ArgumentNullException(nameof(file)));
            }

            return
                from pdu in Try.Apply(() => CreateWriteFilePdu(fileNumber, recordSize, recordNumber, file))
                from snd in _mbTransport.Send(unitId, pdu)
                from res in _mbTransport.Receive()
                select CheckWriteFileResponse(res, pdu);
        }


        public Try<List<byte>> SendUserFunction(byte unitId, byte functionCode, byte[] data)
        {
            var pdu = new[] { functionCode }.Concat(data).ToList();

            var r =
                from snd in _mbTransport.Send(unitId, pdu)
                from response in _mbTransport.Receive()
                select UnwrapUserFunctionPdu(functionCode, response);

            return r;
        }

        private List<byte> UnwrapUserFunctionPdu(byte functionCode, List<byte> response)
        {
            if (!response.Any())
            {
                throw new ArgumentException("Empty response");
            }
            if (response[0] == functionCode)
            {
                return response.Skip(1).ToList();
            }
            throw new Exception("Modbus exception: " + string.Join(", ", response.Select(x => x.ToString("X"))));
        }

        public void Dispose()
        {
            _mbTransport.Dispose();
        }
    }
}
