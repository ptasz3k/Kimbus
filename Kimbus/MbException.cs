using System;

namespace Kimbus
{
    public class MbException : Exception
    {
        private readonly MbExceptionCode _mbExceptionCode;

        public MbExceptionCode ModbusExceptionCode => _mbExceptionCode;

        public MbException(MbExceptionCode mbExceptionCode)
        {
            _mbExceptionCode = mbExceptionCode;
        }

        public override string Message => $"ModbusException type={_mbExceptionCode}";
    }
}
