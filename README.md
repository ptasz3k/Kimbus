# Kimbus2

![.NET Core](https://github.com/ptasz3k/Kimbus/workflows/.NET%20Core/badge.svg)

Asynchronous netstandard2.0 Modbus/TCP client/server (master/slave) library.

Current status:

Function Code | Function name | Client | Server
:-------------|:--------------- |:------:|:----:
0x01          | Read Coils | Yes | Yes
0x02          | Read Discrete Inputs | Yes | Yes
0x03          | Read Holding Registers | Yes | Yes
0x04          | Read Input Registers   | Yes | Yes
0x05          | Write Single Coil      |  Yes | Yes
0x06          | Write Single Register  | Yes  | Yes
0x0f          | Write Multiple Coils   | Yes | Yes
0x10          | Write Multiple Registers | Yes | Yes
0x15          | Write File               | Yes | No
0x17          | Read/Write Multiple Registers | No | No    
0x2b 0x2e     | Read Device Identification | No | No
n/a           | Custom function            | Yes | Yes

To add custom function support for client TODO: write

To add custom function support for server simply add entry to `MbTcpSlave.UserFunctions` dictionary.

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