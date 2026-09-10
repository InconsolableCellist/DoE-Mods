# StayPutVR tests

The OSC layer is the only part of this mod that can be checked without putting a headset on,
so it is checked. Both suites compile the mod's real source — there is no second copy of the
encoder or the sender in here.

## The encoder and the socket

```
cd src/StayPutVR/tests
dotnet run
```

Run it from Windows rather than WSL: the Windows SDK cannot open a `\\wsl.localhost` source
path, so a WSL `dotnet` will fail on the `..\Osc\*.cs` includes.

`Tests.cs` covers `OscPacket` against the OSC 1.0 layout — null-terminated, four-byte-padded
address and type-tag strings, including the case where the address is already a multiple of
four and still needs a whole extra word; big-endian arguments; every packet a multiple of four
bytes for address lengths 1 to 40; the address validator rejecting the OSC pattern characters
and non-ASCII. Then it exercises the real `OscSender` over loopback: a trigger and its release
arriving byte-identical, each value type, retargeting when the port changes, four sends to a
dead port not poisoning the socket (the reason it is left unconnected), and every bad target
being refused rather than thrown.

`Stubs.cs` is the minimum host `OscSender.cs` needs outside the game: a logger, the session
log, and `UnityEngine.Time.unscaledTime`. Nothing under test is reimplemented there.

## Against StayPutVR's own parser

`SpvrParseTest.cpp` feeds the encoder's exact bytes through oscpp — the header-only parser
StayPutVR vendors — with StayPutVR's tag switch from `common/OSCManager.cpp` replayed around
it, and asserts which cases reach the shock decision. This is the test that says the datagram
would actually fire the device, rather than merely being well-formed OSC.

It needs the StayPutVR tree for the header, and builds anywhere with a C++17 compiler:

```
g++ -std=c++17 -I/mnt/c/git/StayPutVR/thirdparty -o spvr_parse_test SpvrParseTest.cpp
./spvr_parse_test
```

The byte arrays in it are pasted from the C# suite's own output. If the encoder ever changes,
run `dotnet run` first and re-copy them.
