#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <fstream>
#include <iterator>
#include <limits>
#include <string>
#include <unordered_map>
#include "../src/client_state.h"

namespace
{
    using namespace dfl::client_state;
    int Failures = 0;
    void Check(bool condition, const char* label)
    {
        if (!condition) { ++Failures; std::fprintf(stderr, "FAIL client state: %s\n", label); }
    }
    struct Memory
    {
        std::unordered_map<std::uint32_t, unsigned char> bytes;
        bool layout = true, changeActorAtEnd = false;
        bool readWrappedAddress = false, readNullMirrorAddress = false;
        std::uint64_t utc = 123456789000ULL, tick = 1000, counter = 5;
        std::uint32_t index = 0, readCount = 0;
        static constexpr std::uint32_t Key = 0x12345678, Manager = 0x01002000;
        static constexpr std::uint32_t Actor = 0x04000000, Stage = 0x05000000, Map = 0x06000000;
        static constexpr std::uint32_t User = 0x07000000, Party = 0x08000000, Vtable = 0x03000000;
        template<class T> void Put(std::uint32_t address, T value)
        {
            const auto* data = reinterpret_cast<const unsigned char*>(&value);
            for (std::size_t i = 0; i < sizeof(value); ++i) bytes[address + static_cast<std::uint32_t>(i)] = data[i];
        }
        void U32(std::uint32_t address, std::uint32_t value) { Put(address, value); }
        void Secure(std::uint32_t address, std::uint32_t bits)
        {
            const auto slot = index++;
            U32(address, (address + slot) ^ Key); U32(address + 4, slot);
            const auto a = 0x02000000 + slot * 4, b = 0x02100000 + slot * 4;
            U32(a, bits ^ a ^ Key); U32(b, bits ^ b ^ Key);
        }
        void SecureFloat(std::uint32_t address, float value)
        {
            std::uint32_t bits = 0; std::memcpy(&bits, &value, sizeof(bits)); Secure(address, bits);
        }
        void SetScene(std::uint32_t scene) { Secure(0x01000010, scene); }
        void PlayerAt(std::uint32_t actor, std::uint32_t uid)
        {
            U32(actor, Vtable); U32(actor + 0xD0, 0x111); Secure(actor + 0xD4, uid);
            U32(actor + 0xE4, Map); U32(actor + 0xE0, 0);
            Secure(actor + 0x1168, 470); Secure(actor + 0xE38, 470);
            SecureFloat(actor + 0xE40, 1.0f); Secure(actor + 0x142C, 0);
            Put<unsigned char>(actor + 0x20A2, 0);
            for (std::uint32_t id = 0; id < MaximumSkills; ++id) U32(actor + 0x1AEC + id * 4, 0);
        }
        Memory()
        {
            U32(0xCFD458, Manager); U32(0xCFD45C, Key); U32(Manager + 4, 4096);
            U32(Manager + 0x44, 0x02000000); U32(Manager + 0x48, 0x02100000);
            U32(0xB5D6F0, 0x01000000); U32(0xB5DCE0, 0x01010000);
            SetScene(3); U32(0x01210010, Stage); U32(0xB75A14, Actor); U32(0xB55EE0, 1);
            U32(Stage + 0xB0, Map); U32(Stage + 0x50, 0x05010000); U32(Stage + 0x954, 0);
            Put<unsigned char>(Stage + 0x110, 0); U32(Map + 0x10, 0x06100000); U32(Map + 0x14, 0x06100000);
            Secure(Map + 0x10C, 0);
            U32(Vtable + 0x330, 0x652A20); U32(Vtable + 0x334, 0x652A30);
            U32(Vtable + 0x80, 0x471610);
            U32(Vtable + 0x318, 0x652A00); U32(Vtable + 0x508, 0x493DC0); U32(Vtable + 0x51C, 0x4A3510);
            PlayerAt(Actor, 1); U32(Actor + 0xE0, 0x04100000);
            U32(0x04100000, 0x04200000); U32(0x04100078, 0); Put<unsigned char>(0x0410007C, 0);
            U32(0x04200000, 0x04300000); U32(0x04300000, 0x4CAC70);
            SecureFloat(0x04200035, 120.5f); SecureFloat(0x0420003D, 240.0f); SecureFloat(0x04200045, 0.0f);
            U32(Actor + 0x1AEC + 20 * 4, 0x04400000); Secure(0x044002E0, 5);
            U32(0xB5DCB8, 0x09000000); U32(0x0900000C, 0x09001000); U32(0x09001000, 0x09002000);
            U32(0x09002000, 0x09001000); U32(0x0900200C, User);
            Put<std::uint16_t>(User + 4, 1); U32(User + 0x110, Actor);
            U32(0xB5DCC8, Party);
            for (std::uint32_t slot = 0; slot < 4; ++slot) U32(Party + 0x10 + slot * 4, slot == 0 ? User : 0);
            Put<unsigned char>(0x050101F8, 1);
            for (std::uint32_t door = 0; door < 4; ++door)
            {
                const auto rect = 0x050101FC + door * 16;
                Put<std::int32_t>(rect, 100); Put<std::int32_t>(rect + 4, 200);
                Put<std::int32_t>(rect + 8, 40); Put<std::int32_t>(rect + 12, 60);
            }
        }
        static bool Read(void* context, std::uint32_t address, void* output, std::size_t size) noexcept
        {
            auto& self = *static_cast<Memory*>(context); ++self.readCount;
            if (address == 0x00010010) self.readWrappedAddress = true;
            if (address == 0x00010004) self.readNullMirrorAddress = true;
            if (self.changeActorAtEnd && self.readCount > 200 && address == 0xB75A14) self.U32(address, Actor + 0x1000);
            auto* out = static_cast<unsigned char*>(output);
            for (std::size_t i = 0; i < size; ++i)
            {
                const auto found = self.bytes.find(address + static_cast<std::uint32_t>(i));
                if (found == self.bytes.end()) return false;
                out[i] = found->second;
            }
            return true;
        }
        Reader Make()
        {
            return Reader{this, &Read,
                [](void* p) noexcept { return static_cast<Memory*>(p)->layout; },
                [](void* p) noexcept { return static_cast<Memory*>(p)->utc; },
                [](void* p) noexcept { return static_cast<Memory*>(p)->tick; },
                [](void* p) noexcept { return static_cast<Memory*>(p)->counter++; }, 123, 999};
        }
        void MonsterAt(std::uint32_t object, std::uint32_t uid)
        {
            U32(object, 0x06300000); U32(0x06300034, 0x4073D0); U32(0x06300330, 0x652A20);
            U32(object + 0xD0, 529); Secure(object + 0xD4, uid); U32(object + 0x1A0, 1234);
            Secure(object + 0x1168, 100); Secure(object + 0x142C, 0); Secure(object + 0x1434, 7);
            Put<unsigned char>(object + 0x128, 1); U32(object + 0x42C, 9); U32(object + 0x1864, 0x1234);
            U32(object + 0xE0, 0);
        }
    };
}

int RunClientStateTests()
{
    using namespace dfl::client_state;
    Memory memory; History history; Snapshot first;
    auto reader = memory.Make();
    Check(Capture(reader, history, first) && first.valid && first.scene.known && first.scene.value == 3,
          "supported independent native scene is captured");
    Check(first.haveMe && first.me.uid.known && first.me.uid.value == 1 && first.me.hp.known && first.me.hp.value == 470,
          "same native user actor gives UID and HP");
    Check(first.me.maxHp.known && first.me.maxHp.value == 470 && first.me.position.known && first.me.position.x == 120.5f,
          "guarded max HP and protected coordinate getter replica");
    Check(first.playerCount == 1 && first.players[0].uid.value == 1 && first.room.doors[1].enabled.value,
          "players include self and right door uses native bit one");
    Check(first.skillCount == 1 && first.skills[0].id == 20 && first.skills[0].level == 5,
          "learned skill levels are captured without game getter calls");
    const auto json = ToJson(first, memory.utc + 500);
    Check(json.find("\"schema\":\"dfl-client-state/v1\"") != std::string::npos
          && json.find("\"cooldown_ms\":null,\"usable\":null") != std::string::npos
          && json.find("\"server_context\":null") != std::string::npos,
          "JSON schema and unsupported cooldown/context remain explicit");
    const auto captured = first.capturedTickMs;
    const auto newerExport = ToJson(first, memory.utc + 999999);
    Check(first.capturedTickMs == captured && newerExport.find("\"captured_tick_ms\":1000") != std::string::npos,
          "serialization cannot refresh capture time");
    Check(json.find("\"mp\":null,\"max_mp\":null") != std::string::npos,
          "unverified MP fields are explicitly null");
    Check(first.room.mapClearState.known && first.room.mapClearState.value == 0
          && json.find("\"map_clear_state\":0") != std::string::npos,
          "native zero map-clear value remains raw zero rather than door-open inference");
    Memory cleared; cleared.Secure(Memory::Map + 0x10C, 1); History clearHistory; Snapshot clearSnapshot;
    Capture(cleared.Make(), clearHistory, clearSnapshot);
    Check(clearSnapshot.room.mapClearState.known && clearSnapshot.room.mapClearState.value == 1,
          "native one map-clear value remains raw one");
    Memory badClear; const auto clearIndex = badClear.index; badClear.Secure(Memory::Map + 0x10C, 1);
    badClear.U32(0x02100000 + clearIndex * 4, 0); History badClearHistory; Snapshot badClearSnapshot;
    Capture(badClear.Make(), badClearHistory, badClearSnapshot);
    Check(!badClearSnapshot.room.mapClearState.known
          && ToJson(badClearSnapshot, 0).find("\"map_clear_state\":null") != std::string::npos,
          "damaged map-clear mirror is explicit unknown");

    memory.Secure(Memory::Actor + 0x1168, 0); memory.Secure(Memory::Actor + 0x142C, 1);
    memory.Put<unsigned char>(Memory::Actor + 0x20A2, 1); memory.U32(Memory::Actor + 0xE4, 0);
    Snapshot dead; Check(Capture(reader, history, dead) && dead.valid && dead.scene.value == 3,
          "dead map zero does not erase independent native Dungeon scene");
    Check(dead.me.dead.known && dead.me.dead.value && dead.me.detached.known && dead.me.detached.value
          && !dead.me.position.known, "dead detached retains identity and marks position unknown");

    Memory unsupported; unsupported.layout = false; History uhistory; Snapshot bad;
    Check(!Capture(unsupported.Make(), uhistory, bad) && !bad.layoutSupported && !bad.valid
          && unsupported.readCount == 0, "unsupported PE never probes hard-coded addresses");
    Memory wrapped; wrapped.U32(0xB5DCE0, 0xFFE10000); wrapped.U32(0x00010010, Memory::Stage);
    History wrappedHistory; Snapshot wrappedSnapshot;
    Check(!Capture(wrapped.Make(), wrappedHistory, wrappedSnapshot) && !wrappedSnapshot.valid && !wrapped.readWrappedAddress,
          "corrupt UI base plus stage offset fails before any wrapped-address read");
    Memory nullMirror;
    nullMirror.U32(Memory::Manager + 0x44, 0); nullMirror.U32(Memory::Manager + 0x48, 0);
    nullMirror.U32(Memory::Manager + 4, 0x8000);
    nullMirror.U32(0x01000010, (0x01000010 + 0x4001) ^ Memory::Key);
    nullMirror.U32(0x01000014, 0x4001); nullMirror.U32(0x00010004, 3 ^ 0x00010004 ^ Memory::Key);
    History nullMirrorHistory; Snapshot nullMirrorSnapshot;
    Capture(nullMirror.Make(), nullMirrorHistory, nullMirrorSnapshot);
    Check(!nullMirror.readNullMirrorAddress && !nullMirrorSnapshot.valid,
          "null secure mirror base is rejected before adding a large valid index");
    Memory town; town.SetScene(1); History thistory; Snapshot ts;
    Check(Capture(town.Make(), thistory, ts) && ts.valid && ts.scene.value == 1
          && !ts.room.present && !ts.haveMe && ts.playerCount == 0, "Town does not reuse stale Dungeon actor data");
    Memory provider; provider.U32(0x04100078, 0x04110000); provider.Put<unsigned char>(0x0410007C, 1);
    History phistory; Snapshot ps; Capture(provider.Make(), phistory, ps);
    Check(!ps.me.position.known, "unknown active coordinate provider is not guessed");
    Memory plain; plain.U32(0x04300000, 0x4736A0); plain.Put<float>(0x04200004, 22.0f);
    plain.Put<float>(0x04200008, 33.0f); plain.Put<float>(0x0420000C, 44.0f);
    History plainHistory; Snapshot plainSnapshot; Capture(plain.Make(), plainHistory, plainSnapshot);
    Check(plainSnapshot.me.position.known && plainSnapshot.me.position.z == 44.0f, "plain position getter replica");
    Memory unknownLayout; unknownLayout.U32(Memory::Vtable + 0x80, 0x471611);
    History unknownLayoutHistory; Snapshot unknownPosition; Capture(unknownLayout.Make(), unknownLayoutHistory, unknownPosition);
    Check(!unknownPosition.me.position.known, "unverified player position vtable never reads a guessed controller layout");
    Memory moved; History movedHistory; Snapshot beforeMove; Capture(moved.Make(), movedHistory, beforeMove);
    moved.U32(Memory::Stage + 0xB0, Memory::Map + 0x1000); Snapshot afterMove;
    Capture(moved.Make(), movedHistory, afterMove);
    Check(afterMove.generation > beforeMove.generation, "map identity changes advance scene generation");
    Memory corrupt; const auto nextIndex = corrupt.index;
    corrupt.Secure(Memory::Actor + 0x1168, 123); corrupt.U32(0x02100000 + nextIndex * 4, 0);
    History corruptHistory; Snapshot corrupted; Capture(corrupt.Make(), corruptHistory, corrupted);
    Check(!corrupted.me.hp.known && corrupted.errorCount != 0, "broken secure mirrors do not fabricate HP");
    Memory nan; nan.SecureFloat(0x04200035, std::numeric_limits<float>::quiet_NaN());
    History nanHistory; Snapshot nanSnapshot; Capture(nan.Make(), nanHistory, nanSnapshot);
    Check(!nanSnapshot.me.position.known && ToJson(nanSnapshot, 0).find("nan") == std::string::npos,
          "NaN coordinate is unknown and never invalid JSON");
    Memory changing; changing.changeActorAtEnd = true; History changingHistory; Snapshot changed;
    Check(!Capture(changing.Make(), changingHistory, changed) && !changed.valid,
          "actor identity transition invalidates the whole capture");
    Memory duplicate; duplicate.U32(Memory::Party + 0x14, Memory::User);
    History duplicateHistory; Snapshot ds; Capture(duplicate.Make(), duplicateHistory, ds);
    Check(ds.truncated && ds.playerCount == 1, "duplicate player UID makes complete roster unavailable");
    Memory party;
    for (std::uint32_t slot = 1; slot < 4; ++slot)
    {
        const auto memberActor = Memory::Actor + slot * 0x10000;
        const auto memberUser = Memory::User + slot * 0x1000;
        party.PlayerAt(memberActor, slot + 1); party.Put<std::uint16_t>(memberUser + 4, static_cast<std::uint16_t>(slot + 1));
        party.U32(memberUser + 0x110, memberActor); party.U32(Memory::Party + 0x10 + slot * 4, memberUser);
    }
    History partyHistory; Snapshot fullParty; Capture(party.Make(), partyHistory, fullParty);
    Check(fullParty.playerCount == 4 && !fullParty.truncated && !fullParty.room.livingCount.known,
          "four players are bounded and unknown member positions prevent assembly inference");
    Memory allSkills;
    for (std::uint32_t id = 0; id < MaximumSkills; ++id)
    {
        const auto skill = 0x04400000 + id * 0x1000;
        allSkills.U32(Memory::Actor + 0x1AEC + id * 4, skill); allSkills.Secure(skill + 0x2E0, 1);
    }
    History skillsHistory; Snapshot fullSkills; Capture(allSkills.Make(), skillsHistory, fullSkills);
    Check(fullSkills.skillCount == MaximumSkills && fullSkills.skills.back().id == 215 && !fullSkills.truncated,
          "all 216 verified skill slots remain bounded and complete");
    Memory enemies; enemies.MonsterAt(0x06200000, 0); enemies.MonsterAt(0x06210000, 1);
    enemies.MonsterAt(0x06220000, 2); enemies.MonsterAt(0x06230000, 3);
    enemies.U32(0x06100000, 0x06200000); enemies.U32(0x06100004, 0x06210000);
    enemies.U32(0x06100008, 0x06220000); enemies.U32(0x0610000C, 0x06230000);
    enemies.U32(Memory::Map + 0x14, 0x06100010);
    History enemyHistory; Snapshot es; Capture(enemies.Make(), enemyHistory, es);
    Check(es.enemyCount == 4 && es.enemies[0].uid.known && es.enemies[0].uid.value == 0
          && es.enemies[1].uid.value == 1 && es.enemies[2].uid.value == 2 && es.enemies[3].uid.value == 3
          && es.enemies[1].monsterId.value == 1234
          && es.enemies[0].authorityToken.value == 0x1234 && !es.truncated,
          "native enemy UID zero is included in complete four-monster enumeration");
    const auto enemyJson = ToJson(es, enemies.utc + 100);
    Check(es.errorCount == 0 && enemyJson.find("\"enemies\":[{\"uid\":0,") != std::string::npos
          && enemyJson.find("\"truncated\":false") != std::string::npos,
          "enemy UID zero is serialized as known zero without invalid UID or truncation");
    wchar_t fixturePath[MAX_PATH]{};
    if (GetEnvironmentVariableW(L"DFLEGACY_CLIENT_STATE_FIXTURE", fixturePath, MAX_PATH))
    {
        // Test-only cross-language fixture, always from the actual fake-reader capture.
        const auto fixtureJson = ToJson(es, enemies.utc + 100);
        const HANDLE fixture = CreateFileW(fixturePath, GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
        DWORD fixtureBytes = 0;
        const bool wrote = fixture != INVALID_HANDLE_VALUE
            && WriteFile(fixture, fixtureJson.data(), static_cast<DWORD>(fixtureJson.size()), &fixtureBytes, nullptr)
            && fixtureBytes == fixtureJson.size();
        if (fixture != INVALID_HANDLE_VALUE) CloseHandle(fixture);
        Check(wrote, "unique C++ capture serializer fixture is created without overwriting evidence");
    }
    enemies.U32(0x06100004, 0x06200000); enemies.U32(Memory::Map + 0x14, 0x06100008);
    Snapshot repeatedEnemy; Capture(enemies.Make(), enemyHistory, repeatedEnemy);
    Check(repeatedEnemy.truncated && repeatedEnemy.enemyCount == 1 && repeatedEnemy.enemies[0].uid.value == 0,
          "duplicate zero enemy UID still prevents complete clear");
    Memory highEnemy; highEnemy.MonsterAt(0x06200000, 65535); highEnemy.U32(0x06100000, 0x06200000);
    highEnemy.U32(Memory::Map + 0x14, 0x06100004); History highEnemyHistory; Snapshot highEnemySnapshot;
    Capture(highEnemy.Make(), highEnemyHistory, highEnemySnapshot);
    Check(highEnemySnapshot.enemyCount == 1 && highEnemySnapshot.enemies[0].uid.value == 65535 && !highEnemySnapshot.truncated,
          "enemy UID upper u16 boundary remains accepted");
    highEnemy.Secure(0x062000D4, 65536); Capture(highEnemy.Make(), highEnemyHistory, highEnemySnapshot);
    Check(highEnemySnapshot.truncated && highEnemySnapshot.enemyCount == 0,
          "out-of-u16 enemy UID still prevents complete enumeration");
    Memory corruptEnemy; corruptEnemy.MonsterAt(0x06200000, 0); corruptEnemy.U32(0x06100000, 0x06200000);
    corruptEnemy.U32(Memory::Map + 0x14, 0x06100004); const auto uidIndex = corruptEnemy.index;
    corruptEnemy.Secure(0x062000D4, 0);
    corruptEnemy.U32(0x02100000 + uidIndex * 4, 0); History corruptEnemyHistory; Snapshot corruptEnemySnapshot;
    Capture(corruptEnemy.Make(), corruptEnemyHistory, corruptEnemySnapshot);
    Check(corruptEnemySnapshot.truncated && corruptEnemySnapshot.enemyCount == 0,
          "broken zero enemy UID secure mirror still prevents complete enumeration");
    Memory zeroPlayer; zeroPlayer.Put<std::uint16_t>(Memory::User + 4, 0);
    zeroPlayer.Secure(Memory::Actor + 0xD4, 0); History zeroPlayerHistory; Snapshot zeroPlayerSnapshot;
    Capture(zeroPlayer.Make(), zeroPlayerHistory, zeroPlayerSnapshot);
    Check(!zeroPlayerSnapshot.valid && !zeroPlayerSnapshot.haveMe,
          "player UID zero remains invalid independently of enemy UID zero");
    enemies.U32(Memory::Map + 0x14, 0x06100000 + 257 * 4);
    Snapshot limited; Capture(enemies.Make(), enemyHistory, limited);
    Check(limited.truncated && limited.enemyCount <= MaximumEnemies, "map object traversal has a visible hard cap");

    Publication publication; Check(TryPublish(publication, first), "bounded publication succeeds");
    AcquireSRWLockExclusive(&publication.lock);
    Check(!TryPublish(publication, dead), "TryLock contention skips publication without blocking frame");
    ReleaseSRWLockExclusive(&publication.lock);
    Snapshot copied; Check(TryCopy(publication, copied) && copied.sequence == first.sequence,
          "contention does not replace prior capture with stale or partial data");

    wchar_t temporary[MAX_PATH]{}, directory[MAX_PATH]{}, settings[MAX_PATH]{}, stateFile[MAX_PATH]{};
    GetTempPathW(MAX_PATH, temporary);
    swprintf_s(directory, L"%sdfstate-tests-%lu", temporary, GetCurrentProcessId());
    CreateDirectoryW(directory, nullptr);
    swprintf_s(settings, L"%s\\settings.ini", directory);
    swprintf_s(stateFile, L"%s\\dfdebug-client-%lu.json", directory, GetCurrentProcessId());
    DeleteFileW(stateFile);
    WritePrivateProfileStringW(L"DebugOverlay", L"ExportState", L"0", settings);
    Initialize(settings, directory); CaptureFrame(); ExportLatest(); Shutdown();
    Check(GetFileAttributesW(stateFile) == INVALID_FILE_ATTRIBUTES, "new state export defaults off");
    WritePrivateProfileStringW(L"DebugOverlay", L"ExportState", L"1", settings);
    Initialize(settings, directory); CaptureFrame(); ExportLatest();
    auto contents = [&]()
    {
        std::ifstream input(stateFile, std::ios::binary);
        return std::string((std::istreambuf_iterator<char>(input)), {});
    };
    const auto originalFile = contents();
    Check(originalFile.find("\"layout_supported\":false") != std::string::npos
          && originalFile.find("\"valid\":false") != std::string::npos,
          "wrong EXE exports unsupported without touching client addresses");
    Sleep(225); ExportLatest();
    Check(contents() == originalFile, "stopped rendering plus worker IO cannot refresh an old capture");
    CaptureFrame(); ExportLatest(); const auto secondFile = contents();
    Check(secondFile != originalFile && secondFile.find("\"sequence\":2") != std::string::npos,
          "new render capture advances sequence and capture time");
    CaptureFrame(); ExportLatest();
    Check(contents() == secondFile, "samples within 200ms do not advance sequence");
    Shutdown(); Sleep(10); CaptureFrame(); ExportLatest();
    Check(contents() == secondFile, "shutdown prevents both capture and export");
    Initialize(settings, directory); CaptureFrame(); ExportLatest();
    Check(contents().find("\"sequence\":3") != std::string::npos,
          "same-process reinitialization never regresses the sample sequence");
    Shutdown();
    DeleteFileW(stateFile); DeleteFileW(settings); RemoveDirectoryW(directory);
    std::printf("Client state tests: %d failures\n", Failures);
    return Failures ? 1 : 0;
}
