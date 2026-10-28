"""Decode stopped ACT1 netdumps independently. Wire evidence never asserts native acceptance.

No game authentication payloads or opaque MT bytes are exported. Stream parsing
starts at byte zero, rejects truncated captures, and never searches for a new header.
"""
from __future__ import annotations
import argparse
from collections import Counter
from dataclasses import dataclass
import hashlib
import json
from pathlib import Path
import re
import socket
import struct


@dataclass(frozen=True)
class RelayFrame:
    opcode: int
    sender: int
    recipient: int
    payload: bytes

    @property
    def fingerprint(self):
        return hashlib.sha256(struct.pack('<HII', len(self.payload) + 12, self.sender, self.recipient) + self.payload).hexdigest()


def parse_relay_frame(raw):
    if len(raw) < 12: raise ValueError('Short native relay frame')
    opcode, length, sender, recipient = struct.unpack_from('<HHII', raw)
    if opcode not in (0, 1, 3) or not 12 <= length <= 8192 or length != len(raw):
        raise ValueError('Invalid native relay header or declared length')
    return RelayFrame(opcode, sender, recipient, raw[12:])


class RelayStream:
    def __init__(self): self.pending = bytearray()

    def feed(self, chunk):
        self.pending.extend(chunk); result = []
        while len(self.pending) >= 4:
            opcode, length = struct.unpack_from('<HH', self.pending)
            if opcode not in (0, 1, 3) or not 12 <= length <= 8192: raise ValueError('Invalid relay stream prefix')
            if len(self.pending) < length: break
            result.append(parse_relay_frame(bytes(self.pending[:length])))
            del self.pending[:length]
        return result

    def finish(self):
        if self.pending: raise ValueError('Incomplete relay stream; no resynchronization allowed')


def validate_forwarding(sent, received):
    inputs = Counter(f.fingerprint for f in sent if f.opcode in (0, 1) and f.payload)
    outputs = Counter(f.fingerprint for f in received)
    if any(f.opcode != 3 or not f.payload for f in received) or outputs - inputs:
        raise ValueError('Delivery has no exact original sender/header/payload match')
    directions = Counter((f.sender, f.recipient) for f in received)
    if not directions or any(sender == recipient or directions[recipient, sender] == 0 for sender, recipient in directions):
        raise ValueError('Both original native transport directions are required')
    return {f'{sender}->{recipient}': count for (sender, recipient), count in sorted(directions.items())}


def parse_peers(body):
    if not body or len(body) != 1 + body[0] * 21: raise ValueError('Invalid NOTI11 record count/length')
    return [(struct.unpack_from('<H', body, 1 + i * 21)[0], struct.unpack_from('<I', body, 13 + i * 21)[0])
            for i in range(body[0])]


LINE = re.compile(r'^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d+) T=\d+ (RX|TX) (TCP|UDP) (\S+) -> (\S+) len=(\d+)(.*?) hex=([\da-fA-F]*)$')
CONNECT = re.compile(r'^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d+ T=\d+ TX TCP-CONNECT \S+ -> \S+ len=(\d+) hex=([\da-fA-F]*)$')


def sha(path): return hashlib.sha256(Path(path).read_bytes()).hexdigest().upper()


def decode_capture(path, pid, own_uid):
    raw = Path(path).read_bytes()
    if len(raw) > 256 * 1024 * 1024: raise ValueError('Capture exceeds bounded evidence size')
    heading = re.search(rb'^# DFLegacy\.Ijl15 net dump: [\d :\-]+ pid=(\d+)', raw)
    if heading is None or int(heading[1]) != pid: raise ValueError('Capture PID does not match stopped journal')
    if b'netdump: size limit reached' in raw: raise ValueError('Capture size cap reached')
    if raw and not raw.endswith(b'\n'): raise ValueError('Incomplete final capture record')
    streams = {}; registered_streams = set(); receiving_streams = set(); game_buffers = {}; sent = []; received = []
    udp = {'TX': [], 'RX': [], 'self_endpoints': set()}
    login_uids = []; peer_tables = []; map_starts = 0; registrations = 0
    for number, line in enumerate(raw.decode('utf-8', errors='strict').splitlines(), 1):
        match = LINE.match(line)
        if not match:
            if not line or line.startswith('# '): continue
            connect = CONNECT.fullmatch(line)
            if connect and int(connect[1]) == len(bytes.fromhex(connect[2])): continue
            raise ValueError(f'Unrecognized or incomplete capture record on line{number}')
        at, direction, transport, local, remote, count, flags, hex_payload = match.groups()
        data = bytes.fromhex(hex_payload); port = int(remote.rsplit(':', 1)[1])
        if len(data) != int(count) or 'truncated' in flags: raise ValueError(f'Truncated capture chunk on line{number}')
        if port == 7005:
            if transport == 'TCP':
                stream = streams.setdefault((direction, local, remote), RelayStream()); frames = stream.feed(data)
            else: frames = [parse_relay_frame(data)]
            for frame in frames:
                if direction == 'TX':
                    if frame.sender != own_uid or frame.opcode not in ((0, 1) if transport == 'TCP' else (0,)):
                        raise ValueError('Native outgoing relay identity/direction mismatch')
                    if transport == 'TCP' and frame.opcode == 0:
                        if frame.payload or frame.sender != frame.recipient: raise ValueError('Invalid native TCP registration')
                        if (local, remote) in registered_streams: raise ValueError('Repeated registration or ambiguous reused TCP tuple')
                        registered_streams.add((local, remote))
                        registrations += 1
                    else:
                        if transport == 'TCP' and (local, remote) not in registered_streams:
                            raise ValueError('Native TCP data precedes registration on its own connection')
                        if frame.payload: sent.append(frame)
                else:
                    if transport != 'TCP' or frame.opcode != 3 or frame.recipient != own_uid:
                        raise ValueError('Native incoming relay identity/direction mismatch')
                    receiving_streams.add((local, remote))
                    received.append(frame)
        elif transport == 'UDP' and len(data) >= 9:
            local_ip, local_port = local.rsplit(':',1); remote_ip, remote_port = remote.rsplit(':',1)
            udp[direction].append((local_ip,int(local_port),remote_ip,int(remote_port),hashlib.sha256(data).hexdigest()))
        elif transport == 'TCP' and direction == 'RX' and port == 7001:
            buffer = game_buffers.setdefault((local, remote), bytearray()); buffer.extend(data)
            while len(buffer) >= 6:
                kind, op, length = struct.unpack_from('<BBI', buffer)
                if kind not in (0, 1) or not 6 <= length <= 4 * 1024 * 1024: raise ValueError('Invalid game RX stream prefix')
                if len(buffer) < length: break
                encrypted = bytes(buffer[6:length]); del buffer[:length]
                body = bytes((((v << 6) | (v >> 2)) & 255) ^ 0xB5 for v in encrypted)
                if kind == 1 and op == 1 and body[:1] == b'\x01':
                    if len(body) < 23: raise ValueError('Short ACT1 login tail')
                    uid, count = struct.unpack_from('<II', body, 5)
                    if uid != own_uid or count > 15 or len(body) != 23 + count: raise ValueError('ACT1 login identity/tail mismatch')
                    login_uids.append(uid)
                elif kind == 0 and op == 11:
                    table=parse_peers(body);peer_tables.append(table)
                    for i,(_, account) in enumerate(table):
                        if account == own_uid:
                            offset=1+i*21; peer_port=struct.unpack_from('>H',body,offset+10)[0]
                            for ip_offset in [offset+2,offset+6]:
                                udp['self_endpoints'].add((socket.inet_ntoa(body[ip_offset:ip_offset+4]),peer_port))
                elif kind == 0 and op == 29: map_starts += 1
    for stream in streams.values(): stream.finish()
    if receiving_streams - registered_streams: raise ValueError('Delivery arrived on an unregistered native TCP connection')
    if any(game_buffers.values()): raise ValueError('Incomplete game RX stream at capture close')
    public = dict(pid=pid, capture_sha256=sha(path), bytes=len(raw), login_account_uids=login_uids,
                  peer_tables=peer_tables, map_starts=map_starts, registrations=registrations,
                  sent_relay_frames=len(sent), received_relay_frames=len(received))
    return public, sent, received, udp


def verify_capture_manifest(directory):
    directory = Path(directory)
    manifest_path = directory/'manifest.json'; entries = json.loads(manifest_path.read_text(encoding='utf-8-sig'))
    names = [entry['File'] for entry in entries]
    actual = {path.name for path in directory.iterdir() if path.is_file() and path.name != 'manifest.json'}
    if len(names) != len(set(names)) or set(names) != actual or not {'deployment.json','state.json','A-netdump.log','B-netdump.log'} <= actual:
        raise ValueError('Frozen capture inventory is incomplete, duplicated or changed')
    if any(path.is_symlink() or path.is_dir() for path in directory.iterdir()): raise ValueError('Only original flat capture files are accepted')
    for entry in entries:
        name = entry['File']
        if name in ('.','..') or '/' in name or '\\' in name or sha(directory/name) != entry['Sha256']: raise ValueError('Frozen capture file mismatch')
    return manifest_path


def inspect_phase(directory, account_uids, mode):
    directory = Path(directory); manifest_path = verify_capture_manifest(directory)
    journal = json.loads((directory/'deployment.json').read_text(encoding='utf-8-sig'))
    if journal['Status'] != 'stopped': raise ValueError('Only stopped capture journals are accepted')
    if set(account_uids) != {'A','B'} or any(type(uid) is not int or not 1 <= uid <= 0xFFFFFFFF for uid in account_uids.values()) or len(set(account_uids.values())) != 2:
        raise ValueError('Two distinct nonzero uint32 account identities are required')
    clients=journal['Clients']
    if len(clients) != 2 or Counter(c['Role'] for c in clients) != Counter({'A':1,'B':1}) or any(type(c['Id']) is not int or c['Id'] <= 0 for c in clients) or len({c['Id'] for c in clients}) != 2:
        raise ValueError('Exactly one distinct recorded native process is required for each role')
    if sha(journal['BuildManifest']) != journal['BuildManifestSha256']: raise ValueError('Frozen build manifest mismatch')
    decoded = {}; all_sent = []; all_received = []; udp = {}
    for role, uid in account_uids.items():
        client = next(c for c in journal['Clients'] if c['Role'] == role)
        public, sent, received, udp[role] = decode_capture(directory/(role+'-netdump.log'), client['Id'], uid)
        if not public['login_account_uids'] or public['map_starts'] == 0: raise ValueError('Native authenticated dungeon entry is missing')
        if not any({account for _, account in table} == set(account_uids.values()) for table in public['peer_tables']):
            raise ValueError('Distinct actual party account identities are missing from native NOTI11')
        decoded[role] = public; all_sent.extend(sent); all_received.extend(received)
    matched_udp = {}
    for a,b in [('A','B'),('B','A')]:
        def attributed(records, own, other, receive):
            counts=Counter();ports={port for _,port in own}
            for local_ip,local_port,remote_ip,remote_port,digest in records:
                if local_port not in ports or (remote_ip,remote_port) not in other: continue
                if local_ip != '0.0.0.0' and (local_ip,local_port) not in own: continue
                key=(remote_port,local_port,digest) if receive else (local_port,remote_port,digest)
                counts[key]+=1
            return counts
        tx=attributed(udp[a]['TX'],udp[a]['self_endpoints'],udp[b]['self_endpoints'],False)
        rx=attributed(udp[b]['RX'],udp[b]['self_endpoints'],udp[a]['self_endpoints'],True)
        matched_udp[f'{a}->{b}']=sum((tx & rx).values())
    forwarding = validate_forwarding(all_sent, all_received) if mode == 'fallback' else {}
    if mode == 'normal' and not all(matched_udp.values()): raise ValueError('Bidirectional original peer UDP evidence is missing')
    if mode == 'fallback' and not all(v['registrations'] for v in decoded.values()): raise ValueError('Both original relay registrations are required')
    return dict(schema='native-party-relay-wire-evidence/v1', mode=mode, phase=journal['Phase'],
                decoder_sha256=sha(__file__),
                frozen_capture_manifest_sha256=sha(manifest_path), build_manifest_sha256=journal['BuildManifestSha256'],
                captures=decoded, matched_peer_udp_datagrams=matched_udp, matched_relay_deliveries=forwarding,
                wire_contract='PASS', native_acceptance='NOT_ASSESSED',
                scope='Header identity and exact opaque bytes; UDP attribution uses counterpart self-reported IPv4 NAT addresses and captured socket ports, including wildcard binds. No temporal/loss-free, MT application/CRC, screenshot, world-state, NAT or Internet acceptance claim.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--phase-dir', type=Path, required=True); parser.add_argument('--mode', choices=['normal','fallback'], required=True)
    parser.add_argument('--a-uid', type=int, required=True); parser.add_argument('--b-uid', type=int, required=True)
    parser.add_argument('--out', type=Path, required=True); args = parser.parse_args()
    result = inspect_phase(args.phase_dir, {'A':args.a_uid, 'B':args.b_uid}, args.mode)
    with args.out.open('x', encoding='utf-8') as stream: json.dump(result, stream, ensure_ascii=False, indent=2)
    print(json.dumps({k:result[k] for k in ['mode','wire_contract','matched_peer_udp_datagrams','matched_relay_deliveries','native_acceptance']}))


if __name__ == '__main__': main()
