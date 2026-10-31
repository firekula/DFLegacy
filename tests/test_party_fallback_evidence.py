"""Synthetic wire negatives only; these tests never assert native acceptance."""
import struct
from pathlib import Path
import tempfile
import hashlib
import json
import unittest
from tools.acceptance.party_fallback_evidence import RelayStream, parse_relay_frame, validate_forwarding, parse_peers, decode_capture, verify_capture_manifest, inspect_phase


def synthetic_phase(root, mode='normal', bad=None):
    """Pinned synthetic bytes exercise the verifier, never native acceptance."""
    phase = root/'phase'; phase.mkdir()
    uids = {'A':18000000,'B':1}
    if bad == 'duplicate_uid': uids['A'] = 1
    pids = {'A':123,'B':124 if bad != 'duplicate_pid' else 123}
    ips = {'A':'10.0.0.1','B':'10.0.0.2'}; ports = {'A':40001,'B':40002}
    def relay(op, source, target, payload=b''):
        return struct.pack('<HHII',op,12+len(payload),source,target)+payload
    def game(op, body, kind=0):
        encrypted=bytes((((v^0xB5)>>6)|((v^0xB5)<<2))&255 for v in body)
        return struct.pack('<BBI',kind,op,6+len(body))+encrypted
    def packet(direction, proto, local, remote, data):
        return f'2026-10-28 12:00:00.001 T=1 {direction} {proto} {local} -> {remote} len={len(data)} hex={data.hex()}\n'
    # ACT1 NOTI11 mixes little-endian scalar fields with a network-order port.
    peers=b'\x02'+b''.join(struct.pack('<H4s4s',101+i,bytes(map(int,ips[r].split('.'))),
        bytes(map(int,ips[r].split('.'))))+struct.pack('>H',ports[r])+
        struct.pack('<IBI',uids[r],2,1472) for i,r in enumerate(['A','B']))
    for role, other in [('A','B'),('B','A')]:
        text=f'# DFLegacy.Ijl15 net dump: 2026-10-28 12:00:00 pid={pids[role]}\n'
        login=b'\x01\0\0\0\0'+struct.pack('<II',uids[role],9)+b'127.0.0.1'+struct.pack('<II',7005,7005)+b'\x01\0'
        for op,body,kind in [(1,login,1),(11,peers,0),(29,b'\0',0)]:
            text+=packet('RX','TCP',f'{ips[role]}:51001','192.0.2.1:7001',game(op,body,kind))
        local=f'0.0.0.0:{ports[role]}'
        remote=f'{ips[other]}:{ports[other]}' if bad != 'unrelated_udp' else '203.0.113.50:8888'
        for direction, payload in [('TX',b'opaque-peer-'+role.encode()),('RX',b'opaque-peer-'+other.encode())]:
            text+=packet(direction,'UDP',local,remote,payload)
        if mode == 'fallback':
            endpoint=f'{ips[role]}:50001'; server='192.0.2.1:7005'
            registration=packet('TX','TCP',endpoint,server,relay(0,uids[role],uids[role]))
            data=packet('TX','TCP',endpoint,server,relay(1,uids[role],uids[other],b'opaque-'+role.encode()))
            if bad == 'second_stream': data=packet('TX','TCP',f'{ips[role]}:50002',server,relay(1,uids[role],uids[other],b'opaque-'+role.encode()))
            text+=(data+registration if bad == 'before_registration' else registration+(registration if bad == 'repeat_registration' else '')+data)
            receive_endpoint=f'{ips[role]}:50002' if bad == 'unregistered_receive' else endpoint
            text+=packet('RX','TCP',receive_endpoint,server,relay(3,uids[other],uids[role],b'opaque-'+other.encode()))
        if bad == 'cut_line': text+='2026-10-28 12:00:00.002 T=1 TX TCP 10.0.0.1:50001 -> 192.0.2.1:7005 len=12'
        if bad == 'missing_hex': text+='2026-10-28 12:00:00.002 T=1 TX UDP 0.0.0.0:40001 -> 10.0.0.2:40002 len=12\n'
        if bad == 'invalid_hex': text+='2026-10-28 12:00:00.002 T=1 TX UDP 0.0.0.0:40001 -> 10.0.0.2:40002 len=12 hex=ZZ\n'
        (phase/(role+'-netdump.log')).write_text(text,encoding='utf-8')
    build=root/'build.json';build.write_text('{}',encoding='utf-8')
    clients=[dict(Role=r,Id=pids[r]) for r in ['A','B']]
    if bad == 'duplicate_role':clients.append(dict(Role='A',Id=125))
    (phase/'deployment.json').write_text(json.dumps(dict(Status='stopped',Phase='synthetic',
        Clients=clients,BuildManifest=str(build),BuildManifestSha256=hashlib.sha256(build.read_bytes()).hexdigest().upper())),encoding='utf-8')
    (phase/'state.json').write_text('{}',encoding='utf-8')
    entries=[dict(File=p.name,Sha256=hashlib.sha256(p.read_bytes()).hexdigest().upper()) for p in phase.iterdir()]
    (phase/'manifest.json').write_text(json.dumps(entries),encoding='utf-8')
    return phase,uids


class PartyFallbackEvidenceTests(unittest.TestCase):
    def test_complete_phase_positives(self):
        for mode in ['normal','fallback']:
            with self.subTest(mode=mode), tempfile.TemporaryDirectory() as directory:
                phase,uids=synthetic_phase(Path(directory),mode)
                report=inspect_phase(phase,uids,mode)
                self.assertEqual(report['wire_contract'],'PASS')
                self.assertEqual(report['native_acceptance'],'NOT_ASSESSED')

    def test_complete_phase_negatives(self):
        for mode,bad in [('normal',v) for v in ['cut_line','missing_hex','invalid_hex','unrelated_udp','duplicate_uid','duplicate_pid','duplicate_role']]+[('fallback',v) for v in ['before_registration','repeat_registration','second_stream','unregistered_receive']]:
            with self.subTest(mode=mode,bad=bad), tempfile.TemporaryDirectory() as directory:
                phase,uids=synthetic_phase(Path(directory),mode,bad)
                with self.assertRaises(ValueError):inspect_phase(phase,uids,mode)
    def test_literal_split_and_coalesced_frames(self):
        raw = bytes.fromhex('0100110044332211887766556D7400FF7A')
        stream = RelayStream()
        self.assertEqual(stream.feed(raw[:3]), [])
        self.assertEqual(stream.feed(raw[3:10]), [])
        frames = stream.feed(raw[10:] + raw)
        stream.finish()
        self.assertEqual(len(frames), 2)
        self.assertEqual((frames[0].sender, frames[0].recipient, frames[0].payload),
                         (0x11223344, 0x55667788, b'mt\x00\xffz'))

    def test_invalid_stream_never_resynchronizes(self):
        for prefix in [b'\x01\0\x01\x20', b'\x02\0\x0c\0', b'\x01\0\x0b\0']:
            with self.subTest(prefix=prefix), self.assertRaises(ValueError):
                RelayStream().feed(prefix)
        stream = RelayStream(); stream.feed(bytes.fromhex('0100110044332211'))
        with self.assertRaises(ValueError): stream.finish()

    def test_bidirectional_original_payloads_required(self):
        a = bytes.fromhex('0100110044332211887766556D7400FF7A')
        b = struct.pack('<HHII', 1, 15, 0x55667788, 0x11223344) + b'ACK'
        sent = [parse_relay_frame(a), parse_relay_frame(b)]
        received = [parse_relay_frame(b'\x03\0' + raw[2:]) for raw in [a, b]]
        self.assertEqual(sum(validate_forwarding(sent, received).values()), 2)
        for bad in [[], [received[0]], received + [received[0]]]:
            with self.subTest(bad=len(bad)), self.assertRaises(ValueError): validate_forwarding(sent, bad)
        mutated = parse_relay_frame(b'\x03\0' + a[2:-1] + b'!')
        with self.assertRaises(ValueError): validate_forwarding(sent, [mutated, received[1]])
        with self.assertRaises(ValueError): validate_forwarding([], [])

    def test_peer_uid_offsets_and_exact_count(self):
        def row(area_uid, account_uid):
            return struct.pack('<H4s4sHIBI', area_uid, b'\x7f\0\0\1', b'\x7f\0\0\1', 42000, account_uid, 2, 1472)
        body = b'\x02' + row(101, 18000000) + row(202, 1)
        self.assertEqual(parse_peers(body), [(101, 18000000), (202, 1)])
        for bad in [body[:-1], body + b'\0', b'\x03' + body[1:]]:
            with self.subTest(length=len(bad)), self.assertRaises(ValueError): parse_peers(bad)

    def test_capture_pid_and_complete_bytes_are_required(self):
        with tempfile.TemporaryDirectory() as directory:
            capture = Path(directory)/'capture.log'
            prefix = '# DFLegacy.Ijl15 net dump: 2026-10-28 12:00:00 pid=123\n'
            frame = '0100110044332211887766556D7400FF7A'
            line = '2026-10-28 12:00:00.001 T=1 TX TCP 127.0.0.1:40000 -> 127.0.0.1:7005 len=17 hex='
            registration = '2026-10-28 12:00:00.000 T=1 TX TCP 127.0.0.1:40000 -> 127.0.0.1:7005 len=12 hex=00000C004433221144332211\n'
            capture.write_text(prefix + registration + line + frame + '\n', encoding='utf-8')
            public, sent, received, udp = decode_capture(capture, 123, 0x11223344)
            self.assertEqual((len(sent), len(received)), (1, 0))
            self.assertNotIn('payload', public)
            with self.assertRaises(ValueError): decode_capture(capture, 124, 0x11223344)
            with self.assertRaises(ValueError): decode_capture(capture, 123, 0x55667788)
            for suffix in [frame[:-2], frame + '00']:
                capture.write_text(prefix + line + suffix + '\n', encoding='utf-8')
                with self.assertRaises(ValueError): decode_capture(capture, 123, 0x11223344)
            capture.write_text(prefix + 'netdump: size limit reached; further packets are not logged.\n', encoding='utf-8')
            with self.assertRaises(ValueError): decode_capture(capture, 123, 0x11223344)

    def test_manifest_pins_every_required_capture_without_extra_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory); names = ['deployment.json','state.json','A-netdump.log','B-netdump.log']
            for name in names: (root/name).write_bytes(b'synthetic fixture, never native acceptance')
            entries = [dict(File=name, Sha256=hashlib.sha256((root/name).read_bytes()).hexdigest().upper()) for name in names]
            manifest = root/'manifest.json'
            manifest.write_text(json.dumps(entries), encoding='utf-8'); verify_capture_manifest(root)
            for bad in [entries[:-1], entries + [entries[0]], [dict(File='../state.json',Sha256=entries[0]['Sha256'])] + entries[1:]]:
                manifest.write_text(json.dumps(bad), encoding='utf-8')
                with self.subTest(bad=bad), self.assertRaises(ValueError): verify_capture_manifest(root)
            manifest.write_text(json.dumps(entries), encoding='utf-8')
            (root/'A-netdump.log').write_bytes(b'changed')
            with self.assertRaises(ValueError): verify_capture_manifest(root)


if __name__ == '__main__': unittest.main()
