// Produces the exact bytes the StayPutVR app sends when it answers this mod's discovery query,
// by calling the same vendored mdns library the app calls (thirdparty/mdns/mdns.h,
// mdns_query_answer_unicast) with the same records common/OSCQueryServer.cpp builds: a PTR for
// _osc._udp.local. pointing at StayPutVR._osc._udp.local., an SRV with the receive port, and an
// A record for the host. The library is send-only, so the answer goes over loopback to a second
// socket and is read back from there. The output is pasted into Tests.cs as the parser fixture;
// re-run this and re-copy if the app's OSCQueryServer.cpp ever changes what it answers with.
//
//   g++ -std=c++17 -I/mnt/c/git/StayPutVR/thirdparty -o mdns_answer_dump MdnsAnswerDump.cpp
//   ./mdns_answer_dump
//
// It also pushes this mod's query bytes (pasted from the C# suite's output, id 0x1234) through
// the library's listen path and reports what the app's callback would see, which is the test
// that the query is one the app recognises rather than merely well-formed DNS.
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>
#include <unistd.h>
#include <arpa/inet.h>
#include "mdns/mdns.h"

static int failures = 0;

static void Dump(const char* label, const unsigned char* p, size_t n) {
    printf("%s (%zu bytes):\n", label, n);
    for (size_t i = 0; i < n; i++) printf("%s0x%02X%s", i % 16 == 0 ? "    " : "", p[i], i + 1 == n ? "\n" : (i % 16 == 15 ? ",\n" : ", "));
}

// The query the mod sends: id 0x1234, one PTR question for _osc._udp.local. with the QU bit.
static const unsigned char kModQuery[] = {
    0x12, 0x34, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    0x04, '_', 'o', 's', 'c', 0x04, '_', 'u', 'd', 'p', 0x05, 'l', 'o', 'c', 'a', 'l', 0x00,
    0x00, 0x0C, 0x80, 0x01,
};

struct Seen { std::string name; int entry = -1; int rtype = -1; int rclass = -1; };

static int ListenCallback(int, const struct sockaddr*, size_t, mdns_entry_type_t entry, uint16_t,
                          uint16_t rtype, uint16_t rclass, uint32_t, const void* data, size_t size,
                          size_t name_offset, size_t, size_t, size_t, void* user) {
    auto* seen = static_cast<Seen*>(user);
    char buf[256] = {};
    mdns_string_t name = mdns_string_extract(data, size, &name_offset, buf, sizeof(buf));
    seen->name.assign(name.str, name.length);
    seen->entry = entry; seen->rtype = rtype; seen->rclass = rclass;
    return 0;
}

static int OpenLoopback(uint16_t* port) {
    int s = socket(AF_INET, SOCK_DGRAM, 0);
    sockaddr_in a{}; a.sin_family = AF_INET; a.sin_addr.s_addr = htonl(INADDR_LOOPBACK); a.sin_port = 0;
    if (bind(s, (sockaddr*)&a, sizeof(a)) < 0) { perror("bind"); exit(1); }
    socklen_t len = sizeof(a); getsockname(s, (sockaddr*)&a, &len); *port = ntohs(a.sin_port);
    return s;
}

int main() {
    // ---- 1. the answer, exactly as OSCQueryServer.cpp's AnswerQuery builds it -------------
    uint16_t asker_port = 0; int asker = OpenLoopback(&asker_port);
    uint16_t app_port = 0;   int app = OpenLoopback(&app_port);

    const std::string service_name = "StayPutVR";
    const std::string hostname = "DESKTOP-TEST";
    const int osc_port = 51234;
    std::string osc_service = service_name + "._osc._udp.local.";
    std::string host = hostname + ".local.";
    sockaddr_in local_addr{}; local_addr.sin_family = AF_INET; inet_pton(AF_INET, "192.168.1.20", &local_addr.sin_addr);

    mdns_record_t answer = {};
    answer.name = {MDNS_STRING_CONST("_osc._udp.local.")};
    answer.type = MDNS_RECORDTYPE_PTR;
    answer.data.ptr.name = {osc_service.c_str(), osc_service.size()};
    answer.rclass = 0; answer.ttl = 120;

    mdns_record_t additional[2] = {};
    additional[0].name = {osc_service.c_str(), osc_service.size()};
    additional[0].type = MDNS_RECORDTYPE_SRV;
    additional[0].data.srv.name = {host.c_str(), host.size()};
    additional[0].data.srv.port = (uint16_t)osc_port;
    additional[0].rclass = 0; additional[0].ttl = 120;
    additional[1].name = {host.c_str(), host.size()};
    additional[1].type = MDNS_RECORDTYPE_A;
    additional[1].data.a.addr = local_addr;
    additional[1].rclass = 0; additional[1].ttl = 120;

    sockaddr_in to{}; to.sin_family = AF_INET; to.sin_addr.s_addr = htonl(INADDR_LOOPBACK); to.sin_port = htons(asker_port);
    alignas(4) char sendbuf[2048];
    static const char kQuestion[] = "_osc._udp.local.";
    int rc = mdns_query_answer_unicast(app, &to, sizeof(to), sendbuf, sizeof(sendbuf), 0x1234,
                                       MDNS_RECORDTYPE_PTR, kQuestion, sizeof(kQuestion) - 1,
                                       answer, nullptr, 0, additional, 2);
    if (rc != 0) { printf("FAIL mdns_query_answer_unicast returned %d\n", rc); return 1; }

    unsigned char got[2048]; ssize_t n = recv(asker, got, sizeof(got), 0);
    if (n <= 0) { printf("FAIL nothing arrived at the asker\n"); return 1; }
    Dump("ANSWER StayPutVR._osc._udp.local. -> DESKTOP-TEST.local.:51234 A 192.168.1.20, id 0x1234", got, (size_t)n);

    // ---- 2. the mod's query through the library's listen path ---------------------------
    to.sin_port = htons(app_port);
    if (sendto(asker, kModQuery, sizeof(kModQuery), 0, (sockaddr*)&to, sizeof(to)) < 0) { perror("sendto"); return 1; }
    Seen seen; char buffer[2048];
    size_t records = mdns_socket_listen(app, buffer, sizeof(buffer), ListenCallback, &seen);
    bool ok = records == 1 && seen.entry == MDNS_ENTRYTYPE_QUESTION && seen.rtype == MDNS_RECORDTYPE_PTR &&
              seen.name == "_osc._udp.local." && (seen.rclass & 0x7FFF) == MDNS_CLASS_IN;
    if (!ok) failures++;
    printf("%s the mod's query reaches the app's callback as a PTR question for '%s' (records=%zu entry=%d rtype=%d rclass=0x%X)\n",
           ok ? "PASS" : "FAIL", seen.name.c_str(), records, seen.entry, seen.rtype, seen.rclass);

    close(asker); close(app);
    printf(failures ? "%d FAILURE(S)\n" : "ALL PASS\n", failures);
    return failures ? 1 : 0;
}
