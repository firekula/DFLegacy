"""Read native GP42 provider outcomes and source/output snapshots at return."""
from .motion_test_trace import Reader as BaseReader,MotionTraceError,require
from . import client_state as state
import struct,json,argparse
SIZE=133440
def decode_block(raw,*,pid,started,now):
    require(len(raw)==SIZE,'bad_size')
    magic,version,header,event_size,capacity,owner,created,revision,enabled,serial,drops,invalid=struct.unpack_from('<6IQ2I3Q',raw)
    require((magic,version,header,event_size,capacity)==(0x50543031,1,64,4168,32),'bad_header')
    require(owner==pid and created==started,'identity_mismatch');require(not revision%2 and enabled in (0,1),'torn_or_bad_header')
    events=[];previous_tick=previous_counter=0
    for n in range(max(1,serial-31),serial+1):
        offset=64+(n-1)%32*4168
        s,t,counter,thread,context,frame,result,source,length,captured,inner,inner_length,inner_captured,flags,reserved=struct.unpack_from('<3Q12I',raw,offset)
        require(s==n and previous_tick<=t<=now and thread>0 and context>=0x10000 and frame>=0x10000
            and flags<=3 and not reserved,'bad_event')
        if counter:require(previous_counter<=counter,'bad_counter');previous_counter=counter
        previous_tick=t;known=bool(flags&1);inner_known=bool(flags&2)
        def validate_capture(pointer,size,count,known):
            require((known and pointer>=0x10000 and 7<=size<=1048576 and pointer+size<=0xffffffff and count==min(size,2048)) or (not known and count==0),'bad_capture')
        validate_capture(source,length,captured,known);validate_capture(inner,inner_length,inner_captured,inner_known)
        source_bytes=raw[offset+72:offset+72+captured];inside=raw[offset+72+2048:offset+72+2048+inner_captured]
        if known:require(struct.unpack_from('<H',source_bytes,1)[0]==42,'bad_source_protocol')
        events.append(dict(serial=s,tick_ms=t,counter_us=counter or None,thread=thread,context_token=context,frame_token=frame,
            provider_result_raw=result,provider_succeeded=bool(result),source_buffer=source,source_length=length,source_captured=captured,
            source_known=known,source_truncated=known and captured<length,source_hex=source_bytes.hex() if known else None,
            inner_buffer=inner,inner_length=inner_length,inner_captured=inner_captured,inner_known=inner_known,
            inner_truncated=inner_known and inner_captured<inner_length,inner_hex=inside.hex() if inner_known else None))
    return dict(schema='dfl-provider-trace/v1',pid=pid,process_started_filetime=started,enabled=bool(enabled),serial=serial,
        lock_drops=drops,invalid_records=invalid,overwrites=max(0,serial-32),latest_age_ms=now-previous_tick if events else None,events=events,
        scope='Actual GP42 branch after original provider v28 returned at 78EE76. Source already passed the first native body transform; output is before copy/second body transform/CRC/dispatch. Captured bytes and provider success are not native CRC or movement completion. lock_drops/invalid counters are independent observations.')
class Reader(BaseReader):
    def __init__(self,pid,started,exe):super().__init__(pid,started,exe,symbol=b'DflProviderTrace',block_size=SIZE)
    def snapshot(self):return decode_block(self.copy_stable(),pid=self.pid,started=self.started,now=state.now_tick_ms())
def main(argv=None):
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--pid',required=True,type=int);p.add_argument('--exe',required=True);p.add_argument('--after-serial',type=int,default=0);a=p.parse_args(argv);reader=None
    try:
        reader=Reader(a.pid,state.get_process_started_filetime(a.pid),a.exe);v=reader.snapshot();v['events']=[e for e in v['events'] if e['serial']>a.after_serial]
        print(json.dumps(dict(ok=True,**v)));return 0
    except (MotionTraceError,state.SnapshotError,OSError) as e:print(json.dumps(dict(ok=False,error=str(e))));return 1
    finally:
        if reader:reader.close()
if __name__=='__main__':raise SystemExit(main())
