"""Real faults in test-owned children. Production paths, no debugger, no network."""
import hashlib, ctypes, json, os, pathlib, shutil, subprocess, sys, time, uuid, tempfile, socket, struct, argparse
from ctypes import wintypes

def control_own_capture_helper(game,helper,pause=False):
    class ProcessEntry(ctypes.Structure):
        _fields_=[('size',wintypes.DWORD),('usage',wintypes.DWORD),('pid',wintypes.DWORD),('heap',ctypes.c_size_t),('module',wintypes.DWORD),('threads',wintypes.DWORD),('parent',wintypes.DWORD),('priority',wintypes.LONG),('flags',wintypes.DWORD),('exe',wintypes.WCHAR*260)]
    kernel=ctypes.WinDLL('kernel32',use_last_error=True)
    kernel.CreateToolhelp32Snapshot.restype=wintypes.HANDLE;kernel.OpenProcess.restype=wintypes.HANDLE
    kernel.CloseHandle.argtypes=[wintypes.HANDLE];kernel.TerminateProcess.argtypes=[wintypes.HANDLE,wintypes.UINT];kernel.WaitForSingleObject.argtypes=[wintypes.HANDLE,wintypes.DWORD]
    kernel.QueryFullProcessImageNameW.argtypes=[wintypes.HANDLE,wintypes.DWORD,wintypes.LPWSTR,ctypes.POINTER(wintypes.DWORD)]
    kernel.Process32FirstW.argtypes=[wintypes.HANDLE,ctypes.POINTER(ProcessEntry)];kernel.Process32NextW.argtypes=kernel.Process32FirstW.argtypes
    snapshot=kernel.CreateToolhelp32Snapshot(2,0);entry=ProcessEntry();entry.size=ctypes.sizeof(entry);found=False;paused=[]
    def resume():
        for owned in paused:kernel.ResumeThread(owned);kernel.CloseHandle(owned)
        paused.clear()
    try:
        more=kernel.Process32FirstW(snapshot,ctypes.byref(entry))
        while more:
            if entry.parent==game.pid and entry.exe==helper.name:
                handle=kernel.OpenProcess(0x100000|0x1000|1,False,entry.pid);path=ctypes.create_unicode_buffer(32768);length=wintypes.DWORD(32768)
                try:
                    assert handle and kernel.QueryFullProcessImageNameW(handle,0,path,ctypes.byref(length)) and pathlib.Path(path.value).resolve()==helper
                    assert game.poll() is None
                    if not pause:
                        assert kernel.TerminateProcess(handle,125);kernel.WaitForSingleObject(handle,2000)
                    else:
                        class ThreadEntry(ctypes.Structure):
                            _fields_=[('size',wintypes.DWORD),('usage',wintypes.DWORD),('tid',wintypes.DWORD),('owner',wintypes.DWORD),('priority',wintypes.LONG),('delta',wintypes.LONG),('flags',wintypes.DWORD)]
                        kernel.OpenThread.restype=wintypes.HANDLE;kernel.SuspendThread.argtypes=[wintypes.HANDLE];kernel.ResumeThread.argtypes=[wintypes.HANDLE]
                        kernel.Thread32First.argtypes=[wintypes.HANDLE,ctypes.POINTER(ThreadEntry)];kernel.Thread32Next.argtypes=kernel.Thread32First.argtypes
                        threads=kernel.CreateToolhelp32Snapshot(4,0);thread=ThreadEntry();thread.size=ctypes.sizeof(thread)
                        try:
                            more_threads=kernel.Thread32First(threads,ctypes.byref(thread))
                            while more_threads:
                                if thread.owner==entry.pid:
                                    owned=kernel.OpenThread(2,False,thread.tid);assert owned
                                    if kernel.SuspendThread(owned)==-1:kernel.CloseHandle(owned);raise AssertionError('cannot suspend owned collector thread')
                                    paused.append(owned)
                                more_threads=kernel.Thread32Next(threads,ctypes.byref(thread))
                        finally:kernel.CloseHandle(threads)
                        assert paused,'collector thread must exist'
                    found=True
                finally:
                    if handle:kernel.CloseHandle(handle)
                break
            more=kernel.Process32NextW(snapshot,ctypes.byref(entry))
    except BaseException:
        resume();raise
    finally:kernel.CloseHandle(snapshot)
    if not found or game.poll() is not None:resume()
    assert found and game.poll() is None,'stopping own native helper must leave game alive'
    return resume

def stop_own_capture_helper(game,helper):control_own_capture_helper(game,helper)

def wait_for(predicate, seconds=8):
    end=time.monotonic()+seconds
    while time.monotonic()<end:
        value=predicate()
        if value:return value
        time.sleep(.05)
    return None

def verify_exception_dump(path,code):
    data=path.read_bytes();assert data[:4]==b'MDMP','expected actual Windows minidump'
    streams,directory=struct.unpack_from('<II',data,8)
    locations=[struct.unpack_from('<III',data,directory+i*12) for i in range(streams)]
    exception=[entry for entry in locations if entry[0]==6];assert len(exception)==1,'dump must contain original exception stream'
    offset=exception[0][2];assert struct.unpack_from('<I',data,offset+8)[0]==int(code,16)
    address=struct.unpack_from('<Q',data,offset+24)[0]
    size,context=struct.unpack_from('<II',data,offset+160);assert size>=188
    assert struct.unpack_from('<I',data,context+184)[0]==address,'saved EIP must match original exception instruction'

def main():
    parser=argparse.ArgumentParser();parser.add_argument('exe');parser.add_argument('helper');parser.add_argument('original',nargs='?');parser.add_argument('--modes',nargs='+');args=parser.parse_args()
    exe=pathlib.Path(args.exe).resolve()
    helper=pathlib.Path(args.helper).resolve()
    temporary=tempfile.TemporaryDirectory(prefix='dflegacy-capture-fixtures-');root=pathlib.Path(temporary.name)
    failures=[];modes=['crash','overflow','recover','previous-exit','previous-block','block-recover','handled','normal','abnormal-exit','later','dialog','no-dump','dump-timeout','large-dump','wrong-capability','wrong-creation','wrong-pid','early-bind','helper-exit','linked-events','helper-dump-exit']
    modes+=['delayed-recovery-twice','skip-provisional','signature-match','network-normal','module-crash']
    if args.original:
        source=pathlib.Path(args.original);copies=root/'original-modules';copies.mkdir()
        for name in ('BugTrap.dll','DumpReport.dll','MSVCR71.dll'):shutil.copyfile(source/name,copies/name)
        changed=root/'changed-modules';changed.mkdir();shutil.copyfile(source/'BugTrap.dll',changed/'BugTrap.dll')
        with (changed/'BugTrap.dll').open('ab') as file:file.write(b'fixture hash mismatch')
        modes+=['original-bug','original-dump','original-bug-privacy','original-bug-badhash','original-bug-badsig']
    else:print('SKIP original module fixture: supply read-only original directory as third argument')
    if args.modes:modes=[mode for mode in modes if mode in args.modes]
    for mode in modes:
        folder=root/'sessions'/uuid.uuid4().hex; (folder/'events').mkdir(parents=True)
        (folder/'rich').mkdir();sid=uuid.uuid4().hex;cap=uuid.uuid4().hex
        registration={'sessionId':sid,'capability':cap,'gamePid':0,'gameCreatedUtcTicks':0,'localSmallDumps':True}
        (folder/'session.json').write_text(json.dumps(registration))
        env={**os.environ,'DFL_TEST_CAPTURE_ROOT':str(root),'DFL_DIAGNOSTIC_SESSION_DIR':str(folder),'DFL_DIAGNOSTIC_SESSION_ID':sid,'DFL_DIAGNOSTIC_CAPABILITY':cap,'DFL_DIAGNOSTIC_CAPTURE_HELPER':str(helper),'DFL_DIAGNOSTIC_SMALL_DUMPS':'1'}
        if mode=='module-crash':env['DFL_TEST_FAULT_MODULE']=str(exe.parent.parent/'fault-module'/exe.parent.name/'DFLegacy.Client.dll')
        if mode.startswith('original-'):env['DFL_TEST_ORIGINAL_DIR']=str(copies)
        if mode=='original-bug-badhash':env['DFL_TEST_ORIGINAL_DIR']=str(changed)
        if mode=='no-dump':env['DFL_DIAGNOSTIC_SMALL_DUMPS']='0'
        if mode in ('dump-timeout','helper-dump-exit'):env['DFL_TEST_STALL_DUMP']='1'
        if mode=='wrong-capability':env['DFL_DIAGNOSTIC_CAPABILITY']=uuid.uuid4().hex
        if mode=='linked-events':
            outside=root/'outside';outside.mkdir(exist_ok=True);(outside/'sentinel').write_text('untouched');(folder/'events').rmdir()
            result=subprocess.run(['cmd.exe','/c','mklink','/J',str(folder/'events'),str(outside)],capture_output=True);assert result.returncode==0,result.stderr
        process=None;receiver=None;resume=None
        if mode=='original-bug-privacy':
            receiver=socket.socket();receiver.bind(('127.0.0.1',0));receiver.listen(4);receiver.settimeout(.3);env['DFL_TEST_RECEIVER_PORT']=str(receiver.getsockname()[1])
        try:
            fixturemode='crash' if mode in ('no-dump','dump-timeout','wrong-capability','wrong-creation','wrong-pid','early-bind','linked-events','helper-dump-exit') else 'normal' if mode=='helper-exit' else mode
            process=subprocess.Popen([str(exe),fixturemode,str(folder/'rich')],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,env=env,cwd=folder/'rich')
            ready=process.stdout.readline().decode().strip().split();assert ready and ready[0]=='ready',(ready,process.poll(),process.stderr.read().decode(errors='replace'))
            registration.update(gamePid=int(ready[1]),gameCreatedUtcTicks=int(ready[2]))
            if mode=='wrong-creation':registration['gameCreatedUtcTicks']+=1
            if mode=='wrong-pid':registration['gamePid']=os.getpid()
            if mode!='early-bind':
                (folder/'session.tmp').write_text(json.dumps(registration));os.replace(folder/'session.tmp',folder/'session.json')
            if mode=='helper-exit':stop_own_capture_helper(process,helper)
            started=time.monotonic();process.stdin.write(b'\n');process.stdin.flush()
            if mode in ('delayed-recovery-twice','skip-provisional'):
                assert process.stdout.readline().decode().strip()=='recovered'
                cancelled=wait_for(lambda:list((folder/'events').glob('*-cancelled.json')));assert cancelled,'first real fault must be cancelled'
                assert not list((folder/'events').glob('*-confirmed.json'))
                if mode=='skip-provisional':resume=control_own_capture_helper(process,helper,pause=True)
                process.stdin.write(b'\n');process.stdin.flush();process.wait(timeout=8)
                if resume:resume();resume=None
            if mode=='helper-dump-exit':
                assert wait_for(lambda:list((root/'captures').glob('*.part'))),'need actual in-flight dump reservation'
                stop_own_capture_helper(process,helper)
            if mode=='early-bind':
                time.sleep(.5);assert not list((folder/'events').glob('*.json'))
                (folder/'session.tmp').write_text(json.dumps(registration));os.replace(folder/'session.tmp',folder/'session.json')
            if mode=='block-recover':
                time.sleep(5.5);assert process.poll() is None and not list((folder/'events').glob('*-confirmed.json')),'blocked recovery/window must never promote on timeout'
            if mode=='previous-block':
                assert wait_for(lambda:list((folder/'events').glob('*-provisional.json'))), 'blocked old handler lost original context'
                assert not list((folder/'events').glob('*-confirmed.json')), 'timeout is not confirmation'
            else:
                process.wait(timeout=12)
                if mode in ('delayed-recovery-twice','skip-provisional'):
                    records=wait_for(lambda:list((folder/'events').glob('*-confirmed.json')));assert records,'second real fault must remain capturable after recovery'
                    assert len(records)==1;record=json.loads(records[0].read_text());incident=record['incident']
                    assert records[0].name==incident['eventId']+'-confirmed.json','new sequence filename and incident ID must agree'
                    assert incident['eventId']!=json.loads(cancelled[0].read_text())['incident']['eventId']
                    assert incident['exceptionCode']=='0xC000001D','second fault must retain its own original context'
                elif mode in ('crash','module-crash','overflow','previous-exit','no-dump','dump-timeout','large-dump','early-bind') or (mode.startswith('original-') and '-bad' not in mode):
                    records=wait_for(lambda:list((folder/'events').glob('*-confirmed.json')))
                    assert records, f'{mode}: original exception never confirmed'
                    record=json.loads(records[0].read_text());incident=record['incident']
                    assert record['producerPid']==process.pid and record['producerCreatedUtcTicks']==int(ready[2])
                    assert incident['exceptionCode']==('0xC00000FD' if mode=='overflow' else '0xC0000005'),incident
                    assert incident['evidence']==('original-exception-context' if mode.startswith('original-') else 'confirmed-unhandled-exception'),incident
                    assert '\\' not in incident['module'] and incident['stack'],incident
                    if mode in ('crash','module-crash'):
                        faultfile=pathlib.Path(env['DFL_TEST_FAULT_MODULE']) if mode=='module-crash' else exe
                        assert incident['module']==faultfile.name and incident['moduleHash']==hashlib.sha256(faultfile.read_bytes()).hexdigest(),('actual fault module SHA256 must be present',incident)
                    if mode in ('no-dump','dump-timeout','large-dump'):
                        assert not list((root/'captures').glob(incident['eventId']+'*')),'disabled/over-limit/timed-out dump must leave no partial'
                    else:
                        dumps=wait_for(lambda:list((root/'captures').glob(incident['eventId']+'*.dmp')))
                        assert dumps and dumps[0].stat().st_size<=16*1024*1024,'small local dump missing or oversized'
                        verify_exception_dump(dumps[0],incident['exceptionCode'])
                    assert time.monotonic()-started<8,'fault handoff exceeded bound'
                elif mode in ('recover','block-recover'):
                    assert process.returncode==0
                    assert wait_for(lambda:list((folder/'events').glob('*-cancelled.json'))),'recovery was not cancelled'
                    assert not list((folder/'events').glob('*-confirmed.json'))
                elif mode=='signature-match':
                    records=wait_for(lambda:(lambda found:found if len(found)>=2 else None)(list((folder/'events').glob('*-confirmed.json'))))
                    assert records and len(records)==2,('validated actual native hooks must hand off two structured network events',[(p.name,p.read_text()) for p in (folder/'events').glob('*.json')],list((folder/'rich').glob('*.log')))
                    assert all(json.loads(p.read_text())['incident']['kind']=='networkerror' for p in records)
                elif mode=='dialog':
                    records=wait_for(lambda:list((folder/'events').glob('*-confirmed.json')))
                    assert records and all(json.loads(p.read_text())['incident']['evidence']=='owned-crash-window' for p in records),'owned dialog needs window-only evidence'
                else:
                    time.sleep(.3)
                    assert not list((folder/'events').glob('*-confirmed.json')),'handled/unknown exit is not native crash evidence'
            assert len(list((root/'captures').glob('*.dmp')))+len(list((root/'captures').glob('*.part')))<=2,'global count includes in-flight and partial dumps'
            if mode=='helper-dump-exit':assert not list((root/'captures').glob('*.part')),'helper death must not strand partial dump after worker deadline'
            if receiver:
                ordinary,_=receiver.accept();assert ordinary.recv(100)==b'ordinary-game-socket';ordinary.close()
                try:
                    leaked,_=receiver.accept();leaked.close();assert False,'legacy BugTrap reached receiver'
                except TimeoutError:pass
            print('PASS native capture',mode)
        except Exception as error:
            failures.append(mode);print('FAIL native capture',mode,str(error))
        finally:
            if resume:resume()
            if process and process.poll() is None:process.kill();process.wait()
            if receiver:receiver.close()
            if mode=='linked-events':
                assert list(p.name for p in outside.iterdir())==['sentinel'];os.rmdir(folder/'events')
            time.sleep(.3)
            shutil.rmtree(folder,ignore_errors=True)
    children=[]
    try:
        for _ in range(2 if not args.modes or 'concurrent' in args.modes else 0):
            folder=root/'sessions'/uuid.uuid4().hex;(folder/'events').mkdir(parents=True);(folder/'rich').mkdir();sid,cap=uuid.uuid4().hex,uuid.uuid4().hex
            registration={'sessionId':sid,'capability':cap,'gamePid':0,'gameCreatedUtcTicks':0,'localSmallDumps':True};(folder/'session.json').write_text(json.dumps(registration))
            env={**os.environ,'DFL_TEST_CAPTURE_ROOT':str(root),'DFL_DIAGNOSTIC_SESSION_DIR':str(folder),'DFL_DIAGNOSTIC_SESSION_ID':sid,'DFL_DIAGNOSTIC_CAPABILITY':cap,'DFL_DIAGNOSTIC_CAPTURE_HELPER':str(helper)}
            child=subprocess.Popen([str(exe),'crash',str(folder/'rich')],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,env=env);children.append((child,folder))
            ready=child.stdout.readline().decode().split();assert ready[0]=='ready';registration.update(gamePid=int(ready[1]),gameCreatedUtcTicks=int(ready[2]));(folder/'session.tmp').write_text(json.dumps(registration));os.replace(folder/'session.tmp',folder/'session.json')
        for child,_ in children:child.stdin.write(b'\n');child.stdin.flush()
        until=time.monotonic()+10
        while any(child.poll() is None for child,_ in children):
            assert time.monotonic()<until,'concurrent own fixtures must finish'
            assert len(list((root/'captures').glob('*.dmp')))+len(list((root/'captures').glob('*.part')))<=2,'concurrent reservations exceeded global count'
            time.sleep(.01)
        ids=[]
        for child,folder in children:
            records=wait_for(lambda:list((folder/'events').glob('*-confirmed.json')));assert len(records)==1;record=json.loads(records[0].read_text());assert record['producerPid']==child.pid;ids.append(record['incident']['eventId'])
        if children:assert len(set(ids))==2;print('PASS native capture concurrent ownership and global dump budget')
    finally:
        for child,_ in children:
            if child.poll() is None:child.kill();child.wait()
    deadline=time.monotonic()+7
    while True:
        try:temporary.cleanup();break
        except PermissionError:
            if time.monotonic()>deadline:raise
            time.sleep(.1)
    assert not failures, failures

if __name__=='__main__':main()
