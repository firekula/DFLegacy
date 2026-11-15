using System.Reflection;
using System.Media;
using System.Diagnostics;
using DFLegacy.Launcher;

internal static class ChapterTwoTestAudioTests
{
    internal static int? Dispatch(string[] args)
    {
        if(args is not ["--test-audio-child",var signal])return null;
        using var wave=SilentWave();using var sound=new SoundPlayer(wave);sound.PlayLooping();
        File.WriteAllText(signal,"ready");
        var until=Environment.TickCount64+30000;
        while(!File.Exists(signal+".stop")&&Environment.TickCount64<until)Thread.Sleep(30);
        sound.Stop();return 0;
    }
    private static MemoryStream SilentWave()
    {
        var wave=new MemoryStream();
        using(var w=new BinaryWriter(wave,System.Text.Encoding.ASCII,true))
        {w.Write("RIFF"u8);w.Write(44136);w.Write("WAVEfmt "u8);w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(22050);w.Write(44100);w.Write((short)2);w.Write((short)16);w.Write("data"u8);w.Write(44100);w.Write(new byte[44100]);}
        wave.Position=0;return wave;
    }
    internal static void Run(Action<bool,string> check)
    {
        var type=typeof(LauncherOptions).Assembly.GetType("DFLegacy.Launcher.ChapterTwoTestAudio");
        check(type is not null,"debug sessions can mute only their bound game's audio");
        if(type is null)return;
        using var wave=new MemoryStream();
        using(var w=new BinaryWriter(wave,System.Text.Encoding.ASCII,true))
        {
            w.Write("RIFF"u8);w.Write(44136);w.Write("WAVEfmt "u8);w.Write(16);w.Write((short)1);w.Write((short)1);
            w.Write(22050);w.Write(44100);w.Write((short)2);w.Write((short)16);w.Write("data"u8);w.Write(44100);w.Write(new byte[44100]);
        }
        wave.Position=0;using var sound=new SoundPlayer(wave);sound.PlayLooping();
        var read=type.GetMethod("ReadVolumes",BindingFlags.Static|BindingFlags.NonPublic)!;
        float[] Read()=>(float[])read.Invoke(null,[Environment.ProcessId])!;
        try
        {
            for(var i=0;i<40&&Read().Length==0;i++)Thread.Sleep(50);
            var before=Read();check(before.Length>0,"silent wave creates a real process audio session for validation");
            if(before.Length==0)return;
            var instance=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,[Environment.ProcessId],null)!;
            using((IDisposable)instance)
            {
                var set=type.GetMethod("SetMuted",BindingFlags.Instance|BindingFlags.NonPublic)!;
                var changed=(int)set.Invoke(instance,[true])!;
                check(changed>0&&Read().All(x=>x==0),"test mode sets bound process sessions to zero");
                set.Invoke(instance,[false]);
                check(Read().SequenceEqual(before),"turning off test mute restores previous process volume");
                set.Invoke(instance,[true]);
            }
            check(Read().SequenceEqual(before),"ending debug session restores its audio changes");
        }
        finally {sound.Stop();}
        var signal=Path.Combine(Path.GetTempPath(),"dflegacy-audio-"+Guid.NewGuid().ToString("N"));
        var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};
        if(Path.GetFileNameWithoutExtension(start.FileName)=="dotnet")start.ArgumentList.Add(typeof(ChapterTwoTestAudioTests).Assembly.Location);
        start.ArgumentList.Add("--test-audio-child");start.ArgumentList.Add(signal);
        using var child=Process.Start(start)!;
        try
        {
            for(int i=0;i<100&&!File.Exists(signal);i++)Thread.Sleep(50);
            var owner=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,[child.Id],null)!;
            using((IDisposable)owner)
            {
                var set=type.GetMethod("SetMuted",BindingFlags.Instance|BindingFlags.NonPublic)!;
                int count=0;for(int i=0;i<60&&count==0;i++){count=(int)set.Invoke(owner,[true])!;if(count==0)Thread.Sleep(50);}
                check(count>0,"bound child has a real zero-volume audio session");
                File.WriteAllText(signal+".stop","");check(child.WaitForExit(5000),"audio fixture exits before restoration");
                check((int)set.Invoke(owner,[false])!>0,"ending the game restores its retained audio session after process exit");
                var create=type.GetMethod("TryCreate",BindingFlags.Static|BindingFlags.NonPublic);
                check(create is not null&&create.Invoke(null,[child.Id]) is null,"an ended startup attempt cannot abort the next game attempt's output observer");
            }
        }
        finally
        {
            if(!child.HasExited){child.Kill();child.WaitForExit();}
            File.Delete(signal);File.Delete(signal+".stop");
        }
    }
}
