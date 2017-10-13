namespace Kimbus
{
    internal enum MbFunctionCode
    {
        ReadCoils = 0x01,
        ReadDiscreteInputs = 0x02,
        ReadHoldingRegisters = 0x03,
        ReadInputRegisters = 0x04,
        WriteSingleCoil = 0x05,
        WriteSingleRegister = 0x06,
        WriteMultipleCoils = 0x0f,
        WriteMultipleRegisters = 0x10,
        WriteFile = 0x15
    }
}
