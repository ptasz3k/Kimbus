using System;
using System.Collections.Generic;
using System.Linq;
using Kimbus.Helpers;
using NLog;

namespace Kimbus.Master
{
    public class MbMaster : IMbMaster
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        protected readonly IMbTransport _mbTransport;

        public MbMaster(IMbTransport transport)
        {
            if (transport == null)
            {
                throw new ArgumentNullException(nameof(transport));
            }

            _mbTransport = transport;
        }

        internal static List<byte> CreateReadPdu(MbFunctionCode fun, ushort address, ushort count)
        {
            if (count == 0)
            {
                throw new ArgumentOutOfRangeException("Cannot read 0 registers", nameof(count));
            }

            if ((fun == MbFunctionCode.ReadCoils || fun == MbFunctionCode.ReadDiscreteInputs) && count > 1968)
            {
                throw new ArgumentOutOfRangeException("Cannot read more than 1968 registers in one query", nameof(count));
            }

            if ((fun == MbFunctionCode.ReadHoldingRegisters || fun == MbFunctionCode.ReadInputRegisters) && count > 123)
            {
                throw new ArgumentOutOfRangeException("Cannot read more than 123 registers in one query", nameof(count));
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
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (values.Count == 0)
            {
                throw new ArgumentOutOfRangeException("Cannot write 0 coils", nameof(values));
            }

            if (values.Count > 1968)
            {
                throw new ArgumentOutOfRangeException("Cannot send more than 1968 coils in one query", nameof(values));
            }

            var bytes = values.Chunk(8).Select(MbHelpers.BooleansToByte).ToList();

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
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (values.Count == 0)
            {
                throw new ArgumentOutOfRangeException("Cannot write 0 holding registers", nameof(values));
            }

            if (values.Count > 123)
            {
                throw new ArgumentOutOfRangeException("Cannot write more than 123 registers in one query", nameof(values));
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
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            if (file.Length == 0)
            {
                throw new ArgumentException("File cannot be empty", nameof(file));
            }

            if ((recordSize == 0) || (recordSize > 244))
            {
                throw new ArgumentOutOfRangeException(nameof(recordSize));
            }

            if (recordSize * recordNumber > file.Length)
            {
                throw new ArgumentException("Record number with given record size lies after the end of the file", nameof(file));
            }

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
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }

            if (response.Count == 0)
            {
                throw new ArgumentException("Won't send empty response", nameof(response));
            }

            CheckFunction(response, fun);
            CheckModbusException(response);

            var byteCount = response[1];
            if (byteCountExpected != byteCount)
            {
                throw new ArgumentException(
                  $"number of bytes in response ({byteCount}) differs from expected ({byteCountExpected})", nameof(response));
            }

            if (byteCount != response.Count - 2)
            {
                throw new ArgumentException(
                  $"number of bytes in response ({response.Count - 1}) differs from declared ({byteCount})", nameof(response));
            }
        }

        private static void CheckModbusException(List<byte> response)
        {
            if ((response[0] & 0x80) == 0x80)
            {
                // FIXME: react to custom exceptions in more sane way
                throw new MbException((MbExceptionCode)response[1]);
            }
        }

        private static void CheckFunction(List<byte> response, MbFunctionCode fun)
        {
            if ((response[0] & 0x7f) != (int)fun)
            {
                throw new ArgumentException(
                  $"function code in response ({response[0]}) differs from function call ({(int)fun})", nameof(response));
            }
        }

        internal List<bool> UnwrapDiscretePdu(MbFunctionCode fun, ushort count, List<byte> response)
        {
            var byteCountExpected = ((count / 8) + ((count % 8 != 0) ? 1 : 0));
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
              from response in _mbTransport.Receive(unitId)
              select UnwrapDiscretePdu(MbFunctionCode.ReadCoils, count, response);

            return result;
        }

        public Try<List<bool>> ReadDiscreteInputs(byte unitId, ushort address, ushort count)
        {
            var result =
              from pdu in Try.Apply(() => CreateReadPdu(MbFunctionCode.ReadDiscreteInputs, address, count))
              from snd in _mbTransport.Send(unitId, pdu)
              from response in _mbTransport.Receive(unitId)
              select UnwrapDiscretePdu(MbFunctionCode.ReadDiscreteInputs, count, response);

            return result;
        }

        public Try<List<ushort>> ReadHoldingRegisters(byte unitId, ushort address, ushort count)
        {
            var result =
              from pdu in Try.Apply(() => CreateReadPdu(MbFunctionCode.ReadHoldingRegisters, address, count))
              from snd in _mbTransport.Send(unitId, pdu)
              from response in _mbTransport.Receive(unitId)
              select UnwrapAnalogPdu(MbFunctionCode.ReadHoldingRegisters, count, response);

            return result;
        }

        public Try<List<ushort>> ReadInputRegisters(byte unitId, ushort address, ushort count)
        {
            var result =
              from pdu in Try.Apply(() => CreateReadPdu(MbFunctionCode.ReadInputRegisters, address, count))
              from snd in _mbTransport.Send(unitId, pdu)
              from response in _mbTransport.Receive(unitId)
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

            if (vals.Count == 1)
            {
                return
                  from pdu in Try.Apply(() => CreateWriteSingleCoilPdu(address, vals[0]))
                  from snd in _mbTransport.Send(unitId, pdu)
                  from res in _mbTransport.Receive(unitId)
                  select CheckWriteSingleResponse(res, MbFunctionCode.WriteSingleCoil, address, (ushort)(vals[0] ? 0xff00 : 0x0000));
            }

            return
              from pdu in Try.Apply(() => CreateWriteMultipleCoilsPdu(address, vals))
              from snd in _mbTransport.Send(unitId, pdu)
              from res in _mbTransport.Receive(unitId)
              select CheckWriteMultipleResponse(res, MbFunctionCode.WriteMultipleCoils, address, vals.Count);
        }

        private bool CheckWriteMultipleResponse(List<byte> response, MbFunctionCode fun, ushort address, int p)
        {
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }

            CheckFunction(response, fun);
            CheckModbusException(response);

            var responseAddr = (ushort)((response.ElementAt(1) << 8) | response.ElementAt(2));
            if (address != responseAddr)
            {
                throw new ArgumentException(
                  $"address in response ({responseAddr}) differs from address in request ({address})", nameof(address));
            }

            var responseCount = (ushort)((response.ElementAt(3)) | response.ElementAt(4));
            if (p != responseCount)
            {
                throw new ArgumentException(
                  $"value in response ({responseCount}) differs from value in request ({p})", nameof(response));
            }

            return true;
        }

        private bool CheckWriteSingleResponse(List<byte> response, MbFunctionCode fun, ushort address, ushort value)
        {
            if (response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }

            CheckFunction(response, fun);
            CheckModbusException(response);

            var responseAddr = (ushort)((response[1] << 8) | response[2]);
            if (address != responseAddr)
            {
                throw new ArgumentException(
                  $"address in response ({responseAddr}) differs from address in request ({address})", nameof(address));
            }

            var responseVal = (ushort)((response[3]) << 8 | response[4]);
            if (value != responseVal)
            {
                throw new ArgumentException(
                  $"value in response ({responseVal}) differs from value in request ({value})", nameof(value));
            }

            return true;
        }

        private bool CheckWriteFileResponse(List<byte> response, List<byte> pdu)
        {
            if (response == null)
            {
                throw new ArgumentException(nameof(response));
            }

            CheckFunction(response, MbFunctionCode.WriteFile);
            CheckModbusException(response);

            if (response.Zip(pdu, (r, p) => r != p).Any(b => b))
            {
                throw new ArgumentException("Response differs from request", nameof(response));
            }

            return true;
        }

        public Try<bool> WriteHoldingRegisters(byte UnitId, ushort address, IEnumerable<ushort> values)
        {
            if (values == null)
            {
                return Try.Failure<bool>(new ArgumentNullException(nameof(values)));
            }

            var vals = values.ToList();

            if (vals.Count == 0)
            {
                return Try.Failure<bool>(new ArgumentOutOfRangeException("Cannot write 0 holding registers", nameof(values)));
            }

            if (vals.Count == 1)
            {
                return
                  from pdu in Try.Apply(() => CreateWriteSingleRegisterPdu(address, vals[0]))
                  from snd in _mbTransport.Send(UnitId, pdu)
                  from res in _mbTransport.Receive(UnitId)
                  select CheckWriteSingleResponse(res, MbFunctionCode.WriteSingleRegister, address, vals[0]);
            }

            return
              from pdu in Try.Apply(() => CreateWriteMultipleRegistersPdu(address, vals))
              from snd in _mbTransport.Send(UnitId, pdu)
              from res in _mbTransport.Receive(UnitId)
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
                from res in _mbTransport.Receive(unitId)
                select CheckWriteFileResponse(res, pdu);
        }


        public Try<List<byte>> SendUserFunction(byte unitId, byte functionCode, byte[] data)
        {
            var pdu = new[] { functionCode }.Concat(data).ToList();

            var r =
                from snd in _mbTransport.Send(unitId, pdu)
                from response in _mbTransport.Receive(unitId)
                select UnwrapUserFunctionPdu(functionCode, response);

            return r;
        }

        private List<byte> UnwrapUserFunctionPdu(byte functionCode, List<byte> response)
        {
            if (response.Count == 0)
            {
                throw new ArgumentException("Empty response", nameof(response));
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
