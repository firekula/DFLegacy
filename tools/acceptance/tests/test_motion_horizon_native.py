"""Replay original ACT1 reconciliation/integration, with actual installed repair bytes.

The native packet has already been decoded at 4C9919. This isolates arithmetic;
it does not emulate a live map, actor collision, authority, or network delivery.
"""
import os
import struct
import unittest
from tools.acceptance.motion_replay_fixture import load_motion_image, run_motion_case


class MotionHorizonNativeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        executable=os.environ.get('DFLEGACY_ACT1_CLIENT_EXE')
        fixture=os.environ.get('DFLEGACY_MOTION_HORIZON_PATCH')
        cls.image=load_motion_image(executable=executable,patch=fixture)

    def run_native(self,fixed,*,accels=(0,0,0),durations=(18836,-1,-1),
                   current=(159.87469482421875,226.,0.),target=(244.00990295410156,226.,0.),
                   terminal=(0.,0.,0.),integrate_ms=0,full=False):
        return run_motion_case(self.image,fixed,accels=accels,durations=durations,
                               current=current,target=target,terminal=terminal,
                               integrate_ms=integrate_ms,full=full,
                               recorder=getattr(self,'recorder',None))

    def test_captured_zero_acceleration_stale_duration_must_converge(self):
        old,old_end,_=self.run_native(False,integrate_ms=500)
        repaired,end,_=self.run_native(True,integrate_ms=500)
        self.assertEqual(struct.unpack_from('<f',old,16)[0],0.)
        self.assertEqual(struct.unpack_from('<i',old,40)[0],18836)
        self.assertEqual(old,old_end,'original zero velocity/acceleration is a fixed point')
        self.assertGreater(struct.unpack_from('<f',repaired,16)[0],100.)
        self.assertEqual(struct.unpack_from('<i',repaired,40)[0],500)
        self.assertAlmostEqual(struct.unpack_from('<f',end,4)[0],244.00990295410156,delta=.1)

    def test_other_axes_active_acceleration_and_native_default_remain_identical(self):
        cases=[dict(accels=(a,0,0),durations=(t,-1,-1)) for a in (-1500,-100,100) for t in (-1,0,100,18836)]
        cases.extend(dict(durations=(t,-1,-1)) for t in (-1,0))
        cases.append(dict(current=(244.00990295410156,226.,0.)))
        for case in cases:
            with self.subTest(case=case):self.assertEqual(self.run_native(False,**case),self.run_native(True,**case))
        old=self.run_native(False,accels=(0,100,-1500),durations=(18836,1000,-1),target=(244.,250.,40.))[0]
        repaired=self.run_native(True,accels=(0,100,-1500),durations=(18836,1000,-1),target=(244.,250.,40.))[0]
        for offset in (8,12,20,24,32,36,44,48):self.assertEqual(old[offset:offset+4],repaired[offset:offset+4])

    def test_zero_acceleration_small_positive_duration_and_terminal_velocity(self):
        for duration in (1,100,500,18836):
            with self.subTest(duration=duration):
                state,_,_=self.run_native(True,durations=(duration,-1,-1),terminal=(50.,0.,0.))
                self.assertEqual(struct.unpack_from('<i',state,40)[0],500)
                expected=self.run_native(False,durations=(0,-1,-1),terminal=(50.,0.,0.))[0]
                self.assertEqual(state,expected)

    def test_native_force_publication_still_copies_the_complete_target(self):
        old=self.run_native(False,full=True)
        repaired=self.run_native(True,full=True)
        self.assertEqual(old,repaired)
        self.assertEqual(repaired[0],repaired[2])


if __name__=='__main__':unittest.main()
