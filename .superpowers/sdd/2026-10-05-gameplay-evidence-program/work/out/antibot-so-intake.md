# 用户提供SO：只读接入与候选ABI核对

来源目录：[game](<G:/Work/Server/dnf/build/dnf_data/home/template/neople/game/>)。所有者明确确认：该目录及 `libantisvrimport.so` 是从对应官方 Docker 容器提取的官方版本。源文件未修改、未执行加载；本轮只读字节与ELF元数据。

## [静态] 身份

[libantisvrimport.so](<G:/Work/Server/dnf/build/dnf_data/home/template/neople/game/libantisvrimport.so>)：31,904,293 B；ELF32/i386；SHA256 `8f6263dae6fcd1eb19baece6f7c9408d2161abd1a05bd492d398d5cb7783d2a7`。导出GetAntiBotInterfaceAll，符号值0x88980，size44。地址是SO内ELF虚拟偏移，不是VM运行地址。

依赖仅按DT_NEEDED记录：librt.so.1、libstdc++.so.6、libm.so.6、libgcc_s.so.1、libpthread.so.0、libc.so.6。不能据此排除进一步dlopen。

## [静态] 接口对应

目标df_game_r在AntibotSend内通过虚表+0x34判定下行列表、+0x28请求加密（static-cipher-details.txt，0x086480df与0x08648267）。

候选SO的AntibotInterface虚表必须经过ELF relocation解析：原文件表项为0，不能直接当作空函数。address point=符号起点+8：

- +0x28 → RecvBufferForEncrypt，0x89900，size549。
- +0x2c → RecvBufferForDecrypt，0x89b30，size538。
- +0x34 → IsCmdInDownList。

这是调用者槽位与候选库重定位的ABI对应，不证明VM抓包时期加载的库哈希相同，更不证明已解出NOTI561。

## 复跑

```powershell
$B='.superpowers/sdd/2026-10-05-gameplay-evidence-program'
$SO='G:/Work/Server/dnf/build/dnf_data/home/template/neople/game/libantisvrimport.so'
python "$B/work/survey-antibot-so.py" $SO > "$B/work/out/antibot-so-survey.txt"
Get-FileHash $SO -Algorithm SHA256
```

原始结果：[antibot-so-survey.txt](<antibot-so-survey.txt>)，含工厂/入口完整精确边界反汇编和虚表重定位。下一步沿RecvBufferForEncrypt的PIC/PLT调用解析下行封装；不能拿最近符号标call。

## [作废] 材料缺失边界

之前“缺少SO，所以NOTI561当前无法继续离线”的限制已被用户新资产解除；不是所有离线工作已经走到头。只解除材料缺失，不解除部署同一性及动态状态限制。不分析gunnersvr/secagent/zergsvr进程本体。
