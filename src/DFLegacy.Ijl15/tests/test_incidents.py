"""Only creates and faults our own child fixture; never touches an existing game."""
import os, pathlib, shutil, subprocess, sys, tempfile
def main():
    exe=pathlib.Path(sys.argv[1]).resolve()
    with tempfile.TemporaryDirectory(prefix="dflegacy-native-incidents-") as temp:
        root=pathlib.Path(temp)
        copied=root/"客户端"/exe.name;copied.parent.mkdir();shutil.copyfile(exe,copied)
        for mode in ("handled","crash","previous","network","signature","signature-match","signature-mismatch","signature-wrong-image","signature-wrong-frame","signature-wrong-arguments"):
            folder=root/mode;folder.mkdir()
            result=subprocess.run([str(copied),mode,str(folder)],timeout=15,capture_output=True,env={**os.environ,"DFLEGACY_DIAGNOSTIC_PVF":"ab"*32})
            files=list(folder.glob("*.log"))
            if mode=="signature-match":
                assert result.returncode==0 and len(files)==2,(mode,result.returncode,result.stdout,files)
                assert any("socketError=10061" in p.read_text(encoding="utf-8") for p in files)
            elif mode=="handled" or mode.startswith("signature"):
                assert result.returncode==0 and not files,(mode,result.returncode,files)
            elif mode in ("crash","previous"):
                assert result.returncode!=0 and len(files)==1,(mode,result.returncode,files)
                text=files[0].read_text(encoding="utf-8")
                for expected in ("crash-","evidence=unhandled-exception-filter","exceptionCode=0xC0000005","register.eip=","module=","stack.return[","fixture-session","fixture-version+commit","architecture=x86","timezoneOffsetMinutes=","clientPvfSha256="+"ab"*32):
                    assert expected in files[0].name+text,(mode,expected,text)
            else:
                assert result.returncode==0 and len(files)==2,(result.returncode,files)
                texts=[p.read_text(encoding="utf-8") for p in files]
                assert all(p.name.startswith("networkerror-") for p in files)
                assert any("socketError=10061" in t and "occurrences=20" in t for t in texts),texts
                assert any("evidence=native-disconnect-notification" in t for t in texts),texts
            print("PASS native incident",mode)


if __name__ == "__main__":
    main()
