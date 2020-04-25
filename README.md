# Kimbus

![.NET Core](https://github.com/ptasz3k/Kimbus/workflows/.NET%20Core/badge.svg)

Asynchronous .NET Standard 2.0 Modbus/TCP client/server (master/slave) library used for over 6 years in production in [Wayy](https://www.wayy.pl/) projects.

## Current status

Function Code | Function name                 | Client | Server
:-------------|:----------------------------- |:------:|:----:
0x01          | Read Coils                    | Yes    | Yes
0x02          | Read Discrete Inputs          | Yes    | Yes
0x03          | Read Holding Registers        | Yes    | Yes
0x04          | Read Input Registers          | Yes    | Yes
0x05          | Write Single Coil             | Yes    | Yes
0x06          | Write Single Register         | Yes    | Yes
0x0f          | Write Multiple Coils          | Yes    | Yes
0x10          | Write Multiple Registers      | Yes    | Yes
0x15          | Write File                    | Yes    | No
0x17          | Read/Write Multiple Registers | No     | No    
0x2b 0x2e     | Read Device Identification    | No     | No
any           | Custom function               | Yes    | Yes

## Sample code

Check `Kimbus.Slave.Example` project for both client and server sample code.

### Sample client code

Include `Kimbus.Master` namespace and then:

```csharp
using (var mbMaster = new MbMaster(new MbTcpTransport("127.0.0.1", 502)))
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

### Sample server code

Include `Kimbus.Slave` namespace and then:

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

## Custom function support

To add custom function support for client create your own `CustomMbMaster` class deriving from `MbMaster`.

To add custom function support for server add entry to the `MbTcpSlave.UserFunctions` dictionary.

## Custom transport support for master

It should be fairly easy to implement custom transport (eg. synchronous TCP or RTU) for master/client class by creating new transport implementing `IMbTransport` interface.
