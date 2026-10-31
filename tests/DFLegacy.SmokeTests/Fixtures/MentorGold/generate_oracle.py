"""Independent exact-rational oracle for the native Monitor float stores.

Run from the repository root. No server assemblies are loaded. The final binary32
rounding uses integer ties-to-even, not the policy implementation or host float.
"""
from pathlib import Path
from fractions import Fraction
import hashlib, json, struct

ROOT = Path(__file__).resolve().parent
RECON = ROOT.parents[3] / '.recon/overnight-20261028/mentor-economics'

def sha(data): return hashlib.sha256(data).hexdigest()

def binary(bits, fraction_bits, bias):
    exponent = (bits >> fraction_bits) & ((1 << (11 if fraction_bits == 52 else 8)) - 1)
    significand = (bits & ((1 << fraction_bits) - 1)) | (1 << fraction_bits)
    shift = exponent - bias - fraction_bits
    return Fraction(significand << max(shift, 0), 1 << max(-shift, 0))

def f32(value):
    if not value: return 0
    exponent = value.numerator.bit_length() - value.denominator.bit_length()
    if value < Fraction(2) ** exponent: exponent -= 1
    scaled = value / (Fraction(2) ** (exponent - 23))
    integer, remainder = divmod(scaled.numerator, scaled.denominator)
    if 2 * remainder > scaled.denominator or (2 * remainder == scaled.denominator and integer & 1): integer += 1
    if integer == 1 << 24: integer >>= 1; exponent += 1
    return ((exponent + 127) << 23) | (integer - (1 << 23))

def award(basis, bits):
    value = basis * binary(bits, 23, 127)
    return value.numerator // value.denominator

def signed(value): return ((value + (1 << 31)) % (1 << 32)) - (1 << 31)
def truncdiv(value): return (abs(value) // 65536) * (-1 if value < 0 else 1)
def jitter(seed):
    a = signed(seed * 1103515245 + 12345)
    b = signed(a * 1103515245 + 12345)
    c = signed(b * 1103515245 + 12345)
    result = (((truncdiv(a) & 2047) << 10) ^ (truncdiv(b) & 1023)) << 10
    result ^= truncdiv(c) & 1023
    return result if result <= 300 else result % 300

def extract_pvf():
    source = Path('G:/Game/DNF_ACT1/DF/Script.pvf')
    raw = source.read_bytes()
    def dec(data, key):
        words = struct.unpack('<' + 'I' * (len(data) // 4), data)
        return struct.pack('<' + 'I' * len(words), *(((w ^ key) >> 6 | ((w ^ key) << 26) & 0xffffffff) for w in words))
    assert raw[:15] == b'DNF_SCRIPT_PACK'
    length, key, count = struct.unpack_from('<III', raw, 15)
    tree = dec(raw[27:27 + length], key); position = 0
    for _ in range(count):
        _, offset, size, key, name_size = struct.unpack_from('<IIIII', tree, position); position += 20
        name = tree[position:position + name_size].decode('cp949').rstrip('\0').replace('\\', '/').lower(); position += name_size
        if name != 'etc/goldlimitbylevel.etc': continue
        start = 27 + length + offset
        entry = dec(raw[start:start + ((size + 3) & ~3)], key)[:size]
        target = ROOT / name; target.parent.mkdir(parents=True, exist_ok=True); target.write_bytes(entry)
        return {'sourcePath': str(source), 'pvfSha256': sha(raw), 'path': name, 'length': size,
                'entrySha256': sha(entry), 'fixtureSha256': sha(entry), 'encoding': 'CP936 (original bytes)'}
    raise AssertionError('native gold limit entry missing')

def main():
    one = binary(0x3f847ae147ae147b, 52, 1023)
    five = binary(0x3fa999999999999a, 52, 1023)
    rows = []; boundaries = []
    for level in range(1, 11):
        for capacity in (5, 8, 10):
            for count in range(1, capacity + 1):
                a = f32(level * one); b = f32(Fraction(count, capacity) * five)
                rate = f32(binary(a, 23, 127) + binary(b, 23, 127))
                rows.append({'level': level, 'count': count, 'capacity': capacity,
                             'component1Bits': f'{a:08X}', 'component2Bits': f'{b:08X}', 'rateBits': f'{rate:08X}',
                             'award1000': award(1000, rate), 'awardMax': award(0xffffffff, rate)})
                fraction = binary(rate, 23, 127)
                # Solve exact product fractions immediately below an integer;
                # these expose double rounding before a truncating conversion.
                if fraction.numerator & 1:
                    inv = pow(fraction.numerator, -1, fraction.denominator)
                    basis = (-inv) % fraction.denominator
                    basis += ((0xffffffff - basis) // fraction.denominator) * fraction.denominator
                    naive = int(basis * float(fraction))
                    if naive != award(basis, rate) and len(boundaries) < 24:
                        for near in (basis - 1, basis, basis + 1):
                            boundaries.append({'basis': near, 'rateBits': f'{rate:08X}', 'award': award(near, rate)})
    oracle = {'rates': rows, 'integerBoundaries': boundaries,
              'alternateJitter': [{'seed': seed, 'jitter': jitter(seed)} for seed in range(301)]}
    (ROOT / 'math-oracle.json').write_text(json.dumps(oracle, indent=2) + '\n', encoding='utf8')
    member = (RECON / 'member_cnt_config.tbl').read_bytes(); (ROOT / 'member_cnt_config.tbl').write_bytes(member)
    provenance = json.loads((RECON / 'manifest.json').read_text(encoding='utf8'))
    manifest = {'scope': 'Original table and CP936 PVF entry; independently derived exact-rational math oracle',
                'pvfEntry': extract_pvf(), 'memberTable': {'path': 'member_cnt_config.tbl', 'length': len(member), 'sha256': sha(member)},
                'inputs': provenance['inputs'],
                'evidence': [{'path': name, 'sha256': sha((RECON / name).read_bytes())} for name in
                             ('monitor-economics.txt', 'monitor-constants.txt', 'official-lifecycle.txt', 'client-tax-helpers.txt', 'client-tax-case.asm')],
                'derived': [{'path': name, 'sha256': sha((ROOT / name).read_bytes().replace(b'\r\n', b'\n'))} for name in ('math-oracle.json', 'generate_oracle.py')],
                'method': 'Monitor 08082E30..08082E62 float32 stores; binary64 constants 7b14ae47e17a843f/9a9999999999a93f. Fraction arithmetic with integer ties-to-even, exact product floor. Timer 086B1BD2..086B1C77 signed32 LCG, signed truncating division, masks, XOR, unsigned comparison.'}
    (ROOT / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf8')
    print(json.dumps({'rateRows': len(rows), 'boundaryRows': len(boundaries), 'timerRows': 301, 'pvfEntry': manifest['pvfEntry']}))

if __name__ == '__main__': main()
