# Kimbus2

![.NET Core](https://github.com/ptasz3k/Kimbus/workflows/.NET%20Core/badge.svg)

Asynchronous netstandard2.0 Modbus/TCP client/server (master/slave) library

Sample client code:

```csharp
using (var mbMaster = new Master.MbMaster(new Master.MbTcpTransport("127.0.0.1", 502)))
{
    mbMaster.Open();
    var result = mbMaster.ReadCoils(0, 999, 1200);
    if (result.IsSuccess)
    {
        /* process result.Success */
    }
    else
    {
        /* check result.Failure */
    }
}
```

Sample server code:

```csharp
var mbSlave = new MbTcpSlave("*", 502)
{
    OnWriteHoldingRegisters = (_, start, hrs) =>
    {
        Console.WriteLine("Write holding registers at {0}, len: {1}", start, hrs.Length);

        for (var i = 0; i < hrs.Length; ++i)
        {
            Console.WriteLine("{0}: {1}", i + start, hrs[i]);
        }

        return MbExceptionCode.Ok;
    },
    OnReadHoldingRegisters = (_, start, count) =>
    {
        var buffer = new ushort[count];

        for (var i = 0; i < count; ++i)
        {
            buffer[i] = (ushort)(i + start);
        }

        return (buffer, MbExceptionCode.Ok);
    }
};
mbSlave.Listen();
```