using System;

namespace Kimbus
{
    public class ModbusException : Exception
    {
        private readonly ModbusExceptionCode _mbExceptionCode;

        public ModbusExceptionCode ModbusExceptionCode
        {
            get { return _mbExceptionCode; }
        }

        public ModbusException(ModbusExceptionCode mbExceptionCode)
        {
            _mbExceptionCode = mbExceptionCode;
        }

        public override string Message => $"ModbusException type={_mbExceptionCode}";
    }
}
