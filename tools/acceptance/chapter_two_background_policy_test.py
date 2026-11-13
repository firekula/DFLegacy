"""Execute original Chapter Two policy instructions in Unicorn, without a client.

The installed mapped image is emitted by background_policy_tests.cpp. With only
--image this intentionally exercises the unmodified policies as a RED baseline.
No Windows API is called: only Sleep, process-priority and shell boundaries are
stubbed and recorded; instruction execution and object writes remain native x86.
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path

import pefile
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EBX, UC_X86_REG_ECX
from unicorn.x86_const import UC_X86_REG_EDI, UC_X86_REG_ESI, UC_X86_REG_ESP, UC_X86_REG_EIP

BASE = 0x400000
SIZE = 0xE57000
OBJECT = 0x21000000
STACK = 0x20008000
STUB = 0x22000000
SHA256 = "9bc32efadd05a2fbf407c85c73ac66d221c5577e7a2234c981c3bba0cb9a8bdb"


def execute(mapped, entry, end, registers, stack_args=()):
    machine = Uc(UC_ARCH_X86, UC_MODE_32)
    for address, size in [(0, 0x1000), (BASE, SIZE), (STACK - 0x8000, 0x10000),
                          (OBJECT, 0x1000), (STUB, 0x1000)]:
        machine.mem_map(address, size)
    machine.mem_write(BASE, mapped)
    calls = []
    apis = {}
    # API boundaries only: return value, argument count, stdcall or cdecl.
    for i, (iat, name, count, result) in enumerate([
        (0xAEA2CC, "Sleep", 1, 0),
        (0xAEA2E4, "GetCurrentProcess", 0, 0xFFFFFFFF),
        (0xAEA22C, "SetPriorityClass", 2, 1),
        (0xAEA3C8, "FindWindowA", 2, 0xBEEF),
        (0xAEA3C4, "SendMessageA", 4, 0),
    ]):
        target = STUB + i * 0x10
        machine.mem_write(iat, struct.pack("<I", target))
        apis[target] = (name, count, result, True)
    apis[0xA5FE66] = ("atexit", 1, 0, False)
    stop = STUB + 0xF0
    machine.mem_write(STACK, struct.pack("<" + "I" * (1 + len(stack_args)), stop, *stack_args))
    machine.mem_write(OBJECT + 0x0C, struct.pack("<I", 15))
    machine.reg_write(UC_X86_REG_ESP, STACK)
    for register, value in registers.items():
        machine.reg_write(register, value)

    def hook(uc, address, _size, _context):
        if address == end or address == stop:
            uc.emu_stop()
        elif address in apis:
            name, count, result, stdcall = apis[address]
            sp = uc.reg_read(UC_X86_REG_ESP)
            values = struct.unpack("<" + "I" * (count + 1), uc.mem_read(sp, 4 * (count + 1)))
            calls.append({"name": name, "args": list(values[1:])})
            uc.reg_write(UC_X86_REG_EAX, result)
            uc.reg_write(UC_X86_REG_ESP, sp + 4 + (count * 4 if stdcall else 0))
            uc.reg_write(UC_X86_REG_EIP, values[0])

    machine.hook_add(UC_HOOK_CODE, hook)
    machine.emu_start(entry, 0, count=2000)
    if machine.reg_read(UC_X86_REG_EIP) not in (end, stop):
        raise AssertionError("native block did not reach its verified continuation")
    return calls, bytes(machine.mem_read(OBJECT, 0x100))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--image", required=True, type=Path)
    parser.add_argument("--mapped", type=Path)
    parser.add_argument("--policy", choices=["all", "background", "startup", "original"], default="all")
    parser.add_argument("--write-original", type=Path)
    parser.add_argument("--result", required=True, type=Path)
    args = parser.parse_args()
    raw = args.image.read_bytes()
    assert hashlib.sha256(raw).hexdigest() == SHA256, "unexpected original image"
    pe = pefile.PE(data=raw)
    original = pe.get_memory_mapped_image().ljust(SIZE, b"\0")
    assert pe.OPTIONAL_HEADER.ImageBase == BASE and len(original) == SIZE
    if args.write_original:
        args.write_original.write_bytes(original)
    mapped = args.mapped.read_bytes() if args.mapped else original
    assert len(mapped) == SIZE
    results = []
    background = args.policy in ("all", "background")
    startup = args.policy in ("all", "startup")

    def check(name, passed, **evidence):
        results.append({"name": name, "passed": bool(passed), **evidence})

    for flag in [0, 1, 2]:
        data = bytearray(mapped)
        # This block only reads the window's flag; initialize through the mapped
        # object in execute by selecting EDI to point into a scratch global.
        scratch = 0xE50000
        struct.pack_into("<I", data, scratch + 0x20 - BASE, flag)
        struct.pack_into("<I", data, scratch + 0x0C - BASE, 15)
        calls, _ = execute(bytes(data), 0xA3CEE8, 0xA3CEF9, {UC_X86_REG_EDI: scratch})
        expected = [] if background or flag else [{"name": "Sleep", "args": [15]}]
        check(f"frame flag {flag} has expected sleep policy", calls == expected, calls=calls)

    for wparam in [0, 1, 2, 0x10000, 0x10001]:
        calls, obj = execute(mapped, 0xA3D386, 0xA3D54C,
                             {UC_X86_REG_ESI: OBJECT, UC_X86_REG_EBX: wparam})
        actual_flag = struct.unpack_from("<I", obj, 0x20)[0]
        duration = struct.unpack_from("<I", obj, 0x0C)[0]
        priorities = [c["args"] for c in calls if c["name"] == "SetPriorityClass"]
        check(f"activate {wparam:#x} preserves flag", actual_flag == (wparam & 0xFFFF), flag=actual_flag)
        normal = background or (wparam & 0xFFFF) != 0
        check(f"activate {wparam:#x} has expected priority and wait",
              priorities == [[0xFFFFFFFF, 0x20 if normal else 0x4000]] and duration == (0 if normal else 15),
              calls=calls, duration=duration)

    calls, _ = execute(mapped, 0xAE0F80, 0, {})
    expected = [] if startup else [
        {"name": "FindWindowA", "args": [0xAEB86C, 0]},
        {"name": "SendMessageA", "args": [0xBEEF, 0x111, 0x19F, 0]},
        {"name": "atexit", "args": [0xAE6D90]},
    ]
    check("static startup has expected shell and exit-registration policy", calls == expected, calls=calls)
    calls, obj = execute(mapped, 0x40D410, 0, {UC_X86_REG_ECX: OBJECT}, (1,))
    expected = [] if startup else [[0xBEEF, 0x111, 0x19F, 0]]
    check("direct constructor has expected shell policy",
          [c["args"] for c in calls if c["name"] == "SendMessageA"] == expected, calls=calls)
    check("direct constructor retains event type", struct.unpack_from("<I", obj, 4)[0] == 1)
    report = {"original_sha256": SHA256, "mapped_sha256": hashlib.sha256(mapped).hexdigest(), "policy": args.policy,
              "checks": len(results), "failures": sum(not r["passed"] for r in results),
              "results": results}
    args.result.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Chapter Two original-x86 policy checks: {report['checks']}, failures: {report['failures']}")
    for result in results:
        if not result["passed"]:
            print("FAIL:", result["name"])
    return bool(report["failures"])


if __name__ == "__main__":
    raise SystemExit(main())
