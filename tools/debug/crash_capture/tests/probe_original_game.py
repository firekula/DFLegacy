"""Own isolated original-process probe; never launches/modifies the source folder.

Usage: python probe_original_game.py ORIGINAL_DIR NATIVE_RELEASE_DIR NEW_PROBE_DIR
Only copies native files. If startup needs assets, copy them to NEW_PROBE_DIR/game
before resuming a separate explicitly reviewed run; never use junctions/hardlinks.
"""
import json, os, pathlib, shutil, subprocess, sys, uuid, time

source,build,root=map(lambda p:pathlib.Path(p).resolve(),sys.argv[1:4])
assert not root.exists(), 'probe destination must be new'
assert root.drive.upper()=='G:' and root.name.startswith('DFLegacy_DiagnosticProbe_')
game=root/'game';game.mkdir(parents=True)
for name in ('DNF.exe','BugTrap.dll','DumpReport.dll','RS.dll','fmod.dll','ijl15.dll','msvcr71.dll','msvcp71.dll','TerSafe.dll'):
    shutil.copyfile(source/name,game/name)
diagnostics=root/'diagnostics';session=diagnostics/'sessions'/uuid.uuid4().hex;(session/'events').mkdir(parents=True)
sid,cap=uuid.uuid4().hex,uuid.uuid4().hex
registration={'sessionId':sid,'capability':cap,'gamePid':0,'gameCreatedUtcTicks':0,'localSmallDumps':True}
(session/'session.json').write_text(json.dumps(registration))
env={**os.environ,'DFL_DIAGNOSTIC_SESSION_DIR':str(session),'DFL_DIAGNOSTIC_SESSION_ID':sid,'DFL_DIAGNOSTIC_CAPABILITY':cap,'DFL_DIAGNOSTIC_SMALL_DUMPS':'1','DFL_TEST_CAPTURE_ROOT':str(diagnostics),'DFL_DIAGNOSTIC_CAPTURE_HELPER':str(build/'native_capture_helper_fixture.exe'),'DFL_TEST_PROBE_LOG':str(root/'registration.log')}
host=subprocess.Popen([str(build/'original_game_host.exe'),str(game/'DNF.exe'),str(build/'original_game_probe.dll')],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,env=env)
ready=host.stdout.readline().decode().strip().split();assert ready and ready[0]=='ready',(ready,host.stderr.read().decode())
registration.update(gamePid=int(ready[1]),gameCreatedUtcTicks=int(ready[2]));(session/'session.json').write_text(json.dumps(registration))
out,err=host.communicate(b'\n',timeout=25)
(root/'host.txt').write_text(' '.join(ready)+'\n'+out.decode(errors='replace')+err.decode(errors='replace'))
time.sleep(.3)
events=[json.loads(p.read_text()) for p in (session/'events').glob('*-confirmed.json')]
result={'sourceDirectoryReadOnly':str(source),'isolatedProcess':registration['gamePid'],'createdUtcTicks':registration['gameCreatedUtcTicks'],'probeDirectory':str(root),'log':(root/'registration.log').read_text(errors='replace') if (root/'registration.log').exists() else 'probe entry was not reached','host':out.decode(errors='replace')+err.decode(errors='replace'),'confirmed':events,'dumps':[{'name':p.name,'bytes':p.stat().st_size} for p in (diagnostics/'captures').glob('*.dmp')]}
(root/'result.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))
sys.exit(0 if any(e['incident']['evidence']=='original-exception-context' for e in events) else 1)
