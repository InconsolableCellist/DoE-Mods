# StayPutVR tests

The OSC layer — the encoder, the socket, and finding the app over OSC Query — is the only part
of this mod that can be checked without putting a headset on, so it is checked. Every suite
compiles the mod's real source; there is no second copy of anything in here.

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

Then `MdnsPacket` and `Discovery`: the question's bytes against the DNS layout; the app's real
answer (below) parsed with its compression pointers followed, the SRV port and A record read out,
another instance name or service type refused, every truncation of it refused without throwing;
and the discovery thread against a fake app on loopback that answers the way the real one does —
found within a few seconds, the `Port` fallback until then, dropped after silence, picked up again
on a new port, a move noticed, another OSC app's answer ignored, a dead target silent. Each state
change is checked to log exactly once.

`Stubs.cs` is the minimum host the Osc/ files need outside the game: a logger, the session log,
and `UnityEngine.Time.unscaledTime`. Nothing under test is reimplemented there.

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

## The app's mDNS answer, from its own library

`MdnsAnswerDump.cpp` goes the other way: it calls the mdns library the app vendors
(`thirdparty/mdns/mdns.h`, `mdns_query_answer_unicast`) with the records
`common/OSCQueryServer.cpp` answers with, captures the bytes off a loopback socket, and prints
them as the `AppAnswer` fixture in `Tests.cs`. It also pushes the mod's question through the
library's listen path and reports what the app's callback would see. Same build line, same
tree:

```
g++ -std=c++17 -I/mnt/c/git/StayPutVR/thirdparty -o mdns_answer_dump MdnsAnswerDump.cpp
./mdns_answer_dump
```

If the app ever changes what it answers with, re-run this and re-copy `AppAnswer`.
