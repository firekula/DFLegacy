import copy
import importlib.util
import struct
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('c2control', 'tools/debug/chapter_two_test_control.py')
control = importlib.util.module_from_spec(spec)
spec.loader.exec_module(control)


def snapshot(pid=101, uid=1):
    return dict(testOnly=True, controlVersion=2, scene=3, pid=pid, uid=uid,
                processStarted='123456', nonce='9988', actor=10000+pid,
                map=20000+pid, stage=30000+pid, snapshot='4', tick='1000',
                requestId='2', enabled=True, supported=True, valid=True,
                targets=[dict(uid=0, hp=50)] if uid == 2 else [],
                position=dict(x=100, y=200, z=0),
                players=[dict(uid=i, dead=False, hp=4000,
                              position=dict(x=100, y=200, z=0)) for i in range(1, 5)],
                enemies=[dict(uid=0, hp=50, dead=False, local=uid == 2)])


class TestChapterTwoControl(unittest.TestCase):
    def test_publishing_retries_only_the_same_atomic_file_replacement(self):
        publish=self.api('publish_request')
        sharing=PermissionError('native request reader briefly owns the file');sharing.winerror=5
        source=control.Path('private.pending');destination=control.Path('request.bin')
        with patch.object(control.Path,'replace',side_effect=[sharing,destination]) as rename,patch.object(control.time,'sleep'):
            publish(source,destination)
            self.assertEqual(rename.call_count,2)
            self.assertTrue(all(call.args==(destination,) for call in rename.call_args_list))
        with patch.object(control.Path,'replace',side_effect=FileNotFoundError('missing pending file')) as rename:
            with self.assertRaises(FileNotFoundError):publish(source,destination)
            self.assertEqual(rename.call_count,1)

    def test_explicit_card_column_uses_native_ready_observation(self):
        s=snapshot()
        s['cards']=dict(available=True,count=4,slot=0,freeSelected=4294967295,
            paidSelected=4294967295,freePending=4294967295,paidPending=4294967295,
            columns=[dict(freeOwner=4294967295,paidOwner=4294967295,
                          freeVisible=True,paidVisible=True) for _ in range(4)])
        for operation in (4,5):
            payload=control.encode_request(s,operation,3,0,0,1001,20)
            self.assertEqual(struct.unpack('<IIIIQQQQQIIIIIIii',payload)[3],operation)
            for changes in ({'available':False},{'freePending':3,'paidPending':3}):
                bad=copy.deepcopy(s);bad['cards'].update(changes)
                with self.assertRaises(RuntimeError):control.encode_request(bad,operation,3,0,0,1001,20)
            for owner in (0,2):
                bad=copy.deepcopy(s)
                bad['cards']['columns'][3]['freeOwner' if operation==4 else 'paidOwner']=owner
                with self.assertRaises(RuntimeError):control.encode_request(bad,operation,3,0,0,1001,20)

    def api(self, name):
        self.assertTrue(callable(getattr(control, name, None)), f'missing semantic {name}')
        return getattr(control, name)

    def test_version_two_request_preserves_self_and_coordinates(self):
        payload = self.api('encode_request')(snapshot(), 2, 1, 700, 240, 1001, 20)
        self.assertEqual(len(payload), 88)
        fields = struct.unpack('<IIIIQQQQQIIIIIIii', payload)
        self.assertEqual(fields[:4], (0x43543244, 2, 101, 2))
        self.assertEqual(fields[-4:], (1, 0, 700, 240))

    def test_remote_actor_position_is_refused(self):
        encode = self.api('encode_request')
        with self.assertRaisesRegex(RuntimeError, 'self'):
            encode(snapshot(), 2, 2, 700, 240, 1001, 20)

    def test_kill_zero_requires_current_local_ownership(self):
        encode = self.api('encode_request')
        self.assertEqual(len(encode(snapshot(102, 2), 1, 0, 0, 0, 1001, 20)), 88)
        with self.assertRaisesRegex(RuntimeError, 'local'):
            encode(snapshot(), 1, 0, 0, 0, 1001, 20)

    def test_coordinate_bounds_and_unrelated_kill_fields(self):
        encode = self.api('encode_request')
        for x, y in [(-32769, 0), (32768, 0), (0, -32769), (0, 32768)]:
            with self.assertRaises(RuntimeError):
                encode(snapshot(), 2, 1, x, y, 1001, 20)
        with self.assertRaises(RuntimeError):
            encode(snapshot(102, 2), 1, 0, 1, 0, 1001, 20)

    def test_old_snapshot_and_expired_context_are_refused(self):
        encode = self.api('encode_request')
        for changes, now in [({'controlVersion': 1}, 1001), ({'valid': False}, 1001), ({}, 1751)]:
            s = snapshot(); s.update(changes)
            with self.assertRaises(RuntimeError):
                encode(s, 2, 1, 700, 240, now, 20)

    def test_four_observers_must_see_the_requested_position(self):
        outcome = self.api('observe_outcome')
        before = {100+i: snapshot(100+i, i) for i in range(1, 5)}
        after = copy.deepcopy(before)
        for s in after.values():
            s['snapshot'] = '5'; s['tick'] = '1010'
            s['players'][0]['position'] = dict(x=700, y=240, z=0)
        self.assertTrue(outcome(before, after, 2, 1, 700, 240)['complete'])
        after[104]['players'][0]['position']['x'] = 100
        self.assertFalse(outcome(before, after, 2, 1, 700, 240)['complete'])

    def test_room_or_process_change_cannot_confirm_position(self):
        outcome = self.api('observe_outcome')
        before = {101: snapshot()}; after = copy.deepcopy(before)
        s = after[101]; s.update(snapshot='5', tick='1010')
        s['players'][0]['position'] = dict(x=700, y=240, z=0)
        for field in ['map', 'actor', 'stage', 'pid', 'nonce', 'processStarted']:
            bad = copy.deepcopy(after)
            bad[101][field] = str(int(bad[101][field])+1)
            self.assertFalse(outcome(before, bad, 2, 1, 700, 240)['complete'], field)

    def test_stale_observation_is_not_position_confirmation(self):
        outcome = self.api('observe_outcome')
        before = {101: snapshot()}; after = copy.deepcopy(before)
        after[101]['players'][0]['position'] = dict(x=700, y=240, z=0)
        self.assertFalse(outcome(before, after, 2, 1, 700, 240)['complete'])

    def test_death_requires_every_observer_and_same_context(self):
        outcome = self.api('observe_outcome')
        before = {100+i: snapshot(100+i, i) for i in range(1, 5)}
        after = copy.deepcopy(before)
        for s in after.values():
            s.update(snapshot='5', tick='1010'); s['enemies'] = []
        self.assertTrue(outcome(before, after, 1, 0, 0, 0)['complete'])
        after[104]['enemies'] = [dict(uid=0, hp=50, dead=False, local=False)]
        self.assertFalse(outcome(before, after, 1, 0, 0, 0)['complete'])

    def test_kill_selects_actual_owner_and_partial_call_is_not_success(self):
        action = self.api('run_action')
        states = {100+i: snapshot(100+i, i) for i in range(1, 5)}
        with patch.object(control, 'read', side_effect=lambda pid: states[pid]), \
             patch.object(control.K, 'GetTickCount64', return_value=1001), \
             patch.object(control, 'submit', return_value=dict(applied=1, reason='native_context_exception')) as submit:
            result = action(101, 'kill', target=0, peers=[102, 103, 104])
        self.assertEqual(submit.call_args.args[0], 102)
        self.assertEqual(result['status'], 'partial')
        self.assertFalse(result['synchronization']['complete'])

    def test_unknown_acknowledgment_is_never_retried(self):
        action = self.api('run_action')
        with patch.object(control, 'read', return_value=snapshot()), \
             patch.object(control.K, 'GetTickCount64', return_value=1001), \
             patch.object(control, 'submit', side_effect=RuntimeError('Outcome unknown')) as submit:
            with self.assertRaisesRegex(RuntimeError, 'unknown'):
                action(101, 'teleport', x=700, y=240)
        self.assertEqual(submit.call_count, 1)

    def test_town_walk_uses_its_own_scene_and_self_target(self):
        encode = self.api('encode_request'); s = snapshot(); s['scene'] = 1
        self.assertEqual(struct.unpack('<IIIIQQQQQIIIIIIii', encode(s,3,1,474,340,1001,20))[3], 3)
        with self.assertRaisesRegex(RuntimeError, 'scene'):
            encode(s,2,1,474,340,1001,20)
        with self.assertRaisesRegex(RuntimeError, 'scene'):
            encode(snapshot(),3,1,474,340,1001,20)

    def test_town_navigation_start_does_not_claim_peer_position(self):
        action = self.api('run_action'); s = snapshot(); s['scene'] = 1
        with patch.object(control, 'read', return_value=s), \
             patch.object(control.K, 'GetTickCount64', return_value=1001), \
             patch.object(control, 'submit', return_value=dict(applied=1,reason='applied_native_town_walk')):
            result = action(101,'walk',x=474,y=340)
        self.assertEqual(result['status'], 'native_navigation_started')
        self.assertFalse(result['synchronization']['complete'])

    def test_stale_peer_is_refused_before_any_action(self):
        action = self.api('run_action')
        states = {100+i: snapshot(100+i, i) for i in range(1, 5)}
        states[104]['tick'] = '200'
        with patch.object(control, 'read', side_effect=lambda pid: states[pid]), \
             patch.object(control.K, 'GetTickCount64', return_value=1001), \
             patch.object(control, 'submit') as submit:
            with self.assertRaisesRegex(RuntimeError, 'Expired'):
                action(101, 'teleport', x=700, y=240, peers=[102,103,104])
        submit.assert_not_called()

    def test_pre_action_observation_cannot_confirm_an_applied_action(self):
        action = self.api('run_action'); before = snapshot(); after = snapshot()
        after.update(snapshot='5', tick='1010')
        after['players'][0]['position'] = dict(x=700,y=240,z=0)
        applied = False
        def send(*args):
            nonlocal applied
            applied = True
            return dict(applied=1,reason='applied_native_position',tick='1100')
        with patch.object(control, 'read', side_effect=lambda pid: after if applied else before), \
             patch.object(control.K, 'GetTickCount64', return_value=1200), \
             patch.object(control, 'submit', side_effect=send):
            result = action(101, 'teleport', x=700,y=240,timeout=.001)
        self.assertEqual(result['status'], 'partial')

    def test_peer_failure_after_mutation_retains_partial_outcome(self):
        action = self.api('run_action')
        with patch.object(control, 'read', side_effect=[snapshot(), RuntimeError('observer closed')]), \
             patch.object(control.K, 'GetTickCount64', return_value=1001), \
             patch.object(control, 'submit', return_value=dict(applied=1,reason='applied_native_position')) as submit:
            result = action(101, 'teleport', x=700,y=240)
        self.assertEqual(result['status'], 'partial')
        self.assertEqual(result['applied'], 1)
        self.assertIn('observer closed', result['error'])
        self.assertEqual(submit.call_count, 1)


if __name__ == '__main__':
    unittest.main()
