// Feeds the exact bytes this mod's encoder produces through oscpp — the same header-only
// parser StayPutVR uses — and replays StayPutVR's own tag switch from
// common/OSCManager.cpp ProcessOSCMessage(). If this prints FIRE for the shock path, the
// datagram would reach shock_callback_ in the real application.
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>
#include "oscpp/server.hpp"

static const char* kShockPath = "/avatar/parameters/Shock";

struct Case { const char* name; std::vector<unsigned char> bytes; bool expect_fire; };

static int failures = 0;

// StayPutVR's ProcessOSCMessage, reduced to the shock-path decision.
static void Replay(const Case& c) {
    bool fired = false;
    std::string address;
    std::string detail;
    try {
        OSCPP::Server::Packet packet(c.bytes.data(), c.bytes.size());
        if (packet.isBundle()) { detail = "parsed as a bundle"; }
        else if (packet.isMessage()) {
            OSCPP::Server::Message message(packet);
            address = message.address();
            OSCPP::Server::ArgStream args = message.args();
            if (args.atEnd()) { detail = "no arguments"; }
            else {
                bool value_bool = false;
                char tag = args.tag();
                if (tag == 'f') { float f = args.float32(); value_bool = f > 0.5f; detail = "float " + std::to_string(f); }
                else if (tag == 'i') { int n = args.int32(); value_bool = n != 0; detail = "int " + std::to_string(n); }
                else if (tag == 'T' || tag == 'F') { value_bool = (tag == 'T'); detail = std::string("bool tag ") + tag; }
                else { detail = std::string("unsupported tag ") + tag; }
                if (address == kShockPath && value_bool) fired = true;
            }
        } else { detail = "neither a message nor a bundle"; }
    } catch (const std::exception& e) {
        detail = std::string("threw ") + e.what();
    }

    bool ok = (fired == c.expect_fire);
    if (!ok) failures++;
    printf("%s %-34s address=\"%s\" %s -> %s\n", ok ? "PASS" : "FAIL", c.name,
           address.c_str(), detail.c_str(), fired ? "FIRE" : "no fire");
}

int main() {
    // Hex captured from the C# encoder's own test run.
    Case cases[] = {
        { "bool true on the shock path",
          {0x2F,0x61,0x76,0x61,0x74,0x61,0x72,0x2F,0x70,0x61,0x72,0x61,0x6D,0x65,0x74,0x65,
           0x72,0x73,0x2F,0x53,0x68,0x6F,0x63,0x6B,0x00,0x00,0x00,0x00,0x2C,0x54,0x00,0x00}, true },
        { "bool false (the release)",
          {0x2F,0x61,0x76,0x61,0x74,0x61,0x72,0x2F,0x70,0x61,0x72,0x61,0x6D,0x65,0x74,0x65,
           0x72,0x73,0x2F,0x53,0x68,0x6F,0x63,0x6B,0x00,0x00,0x00,0x00,0x2C,0x46,0x00,0x00}, false },
        { "int 1",
          {0x2F,0x61,0x76,0x61,0x74,0x61,0x72,0x2F,0x70,0x61,0x72,0x61,0x6D,0x65,0x74,0x65,
           0x72,0x73,0x2F,0x53,0x68,0x6F,0x63,0x6B,0x00,0x00,0x00,0x00,0x2C,0x69,0x00,0x00,
           0x00,0x00,0x00,0x01}, true },
        { "float 1.0",
          {0x2F,0x61,0x76,0x61,0x74,0x61,0x72,0x2F,0x70,0x61,0x72,0x61,0x6D,0x65,0x74,0x65,
           0x72,0x73,0x2F,0x53,0x68,0x6F,0x63,0x6B,0x00,0x00,0x00,0x00,0x2C,0x66,0x00,0x00,
           0x3F,0x80,0x00,0x00}, true },
        { "bool true on a bite path",
          {0x2F,0x53,0x50,0x56,0x52,0x5F,0x42,0x69,0x74,0x65,0x00,0x00,0x2C,0x54,0x00,0x00}, false },
    };

    for (const auto& c : cases) Replay(c);
    printf(failures == 0 ? "\nALL PASS\n" : "\n%d FAILURE(S)\n", failures);
    return failures == 0 ? 0 : 1;
}
