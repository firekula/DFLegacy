"""Execute the actual generated host's settings router against Win32 INI I/O."""
import argparse
import re
import subprocess
from pathlib import Path

BODY = r'''
int main(int argc,char** argv) {
 if(argc!=2)return 9;
 const std::filesystem::path root=argv[1];
 for(const auto* p:{"attempt-a","attempt-b","persistent"})std::filesystem::create_directories(root/p);
 const auto saved=(root/"persistent"/"DNF_CHINA.cfg").string();
 SetEnvironmentVariableA("DFLEGACY_CH2_SETTINGS_PATH",saved.c_str());
 profileDirectory=(root/"attempt-a").string();
 const auto first=ProfilePath("DNF_CHINA.cfg");
 if(!WritePrivateProfileStringA("ID probe","SOUND VOLUME","32",first.c_str()))return 10;
 if(!WritePrivateProfileStringA("KEYBOARD","ACTION KEY","90",first.c_str()))return 11;
 WritePrivateProfileStringA(nullptr,nullptr,nullptr,first.c_str());
 profileDirectory=(root/"attempt-b").string();
 const auto second=ProfilePath("DNF_CHINA.cfg");
 int failures=0;
 if(GetPrivateProfileIntA("ID probe","SOUND VOLUME",999,second.c_str())!=32)++failures;
 if(GetPrivateProfileIntA("KEYBOARD","ACTION KEY",999,second.c_str())!=90)++failures;
 if(second!=saved)++failures;
 if(ProfilePath("ChannelScript.pvf")!=profileDirectory+"\\ChannelScript.pvf")++failures;
 if(ProfilePath("Config.ini")!="Config.ini"||!ProfilePath(nullptr).empty())++failures;
 std::printf("real profile routing: %d failures\n",failures);
 return failures;
}
'''

def main():
    p=argparse.ArgumentParser()
    for name in ('baseline','candidate','output','cmake'):p.add_argument('--'+name,required=True)
    a=p.parse_args();out=Path(a.output).resolve();out.mkdir(parents=True,exist_ok=False)
    for name in ('baseline','candidate'):
        text=Path(getattr(a,name)).read_text(encoding='utf-8-sig')
        match=re.search(r'static std::string ProfilePath\(const char\* filename\)\s*\{.*?\n\}',text,re.S)
        if not match:raise ValueError('Native settings router not found')
        (out/(name+'.cpp')).write_text('#include <windows.h>\n#include <string>\n#include <filesystem>\n#include <cstdio>\nstatic std::string profileDirectory;\n'+match[0]+BODY,encoding='utf-8')
    (out/'CMakeLists.txt').write_text('cmake_minimum_required(VERSION 3.24)\nproject(ProfileRouting LANGUAGES CXX)\nforeach(t baseline candidate)\nadd_executable(${t} ${t}.cpp)\ntarget_compile_features(${t} PRIVATE cxx_std_17)\ntarget_compile_options(${t} PRIVATE /W4 /WX)\nendforeach()\n',encoding='utf-8')
    subprocess.run([a.cmake,'-S',str(out),'-B',str(out/'build'),'-G','Visual Studio 18 2026','-A','Win32'],check=True)
    subprocess.run([a.cmake,'--build',str(out/'build'),'--config','Release'],check=True)
    red=subprocess.run([str(out/'build/Release/baseline.exe'),str(out/'red')]).returncode
    green=subprocess.run([str(out/'build/Release/candidate.exe'),str(out/'green')]).returncode
    assert red==3 and green==0,(red,green)
    print('PASS: old host loses sound/key settings across attempts; current host retains them and isolates scratch files.')

if __name__=='__main__':main()
