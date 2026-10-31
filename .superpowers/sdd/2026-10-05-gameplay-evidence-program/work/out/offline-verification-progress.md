# 离线验证进度与剩余项

## 本轮结果

[回放] `verify-static-password.py` 扫描captures全部10013/30011配对登录前缀：11个文件对、按完整C/S流SHA256去重后8对。5个无配对登录前缀的文件显式SKIP，不能算通过。VM用VM公钥，容器用容器公钥；每对报告公钥和C/S哈希。所有11对通过：NOTI1密文CRC、两个AES实现一致、CMD1密文往返、base64、Python模幂与OpenSSL裸块及PKCS1恢复逐字节一致。来源：[verify-static-password.txt](<verify-static-password.txt>)。

[回放] 三个VM登录样本的token accessor UID=18000008；容器样本UID=3。只表示各token值，不关联账号名；旧B0 VM默认uid=2的混用结论作废。46B RSA恢复内容，经服务端size-1复制实际入结构45B。字段与来源：[token-static-fields.md](<token-static-fields.md>)。

[静态] PI出站边界：0x0808ca60把原低8位CRC保留，高24位替换为hash返回值左移8。0x0808be6e在NOTI1将旧seed置0x27，取rand低字节作下次seed；非空正文调用MakeHash；空正文另外取rand低字节作hash。MakeHash(0x0808c311)=SuperFastHash(0x0808c174)四字节XOR再XOR旧seed。hash和新seed按2bit组交错；另取一个随机字节。这仅是游戏封包兼容边界，不扩大反外挂进程实现。

[回放] 交错布局65536个组合逆变换全部一致；两份90帧S流各89个非空正文hash全部匹配。第43号（零基）NOTI124正文为空，走随机hash分支，不做虚假预测，下一帧仍用其携带的新seed继续验证。来源：[verify-pi-boundary.txt](<verify-pi-boundary.txt>)。

[作废] 实验初稿把空正文也当确定性SuperFastHash，导致两份流同位置失败；静态0x0808beb6..0x0808bec4明确另调rand，现已分开计数，不删失败原因。

## 重跑

工作区根：
```powershell
$B='.superpowers/sdd/2026-10-05-gameplay-evidence-program'
python "$B/work/verify-static-password.py" > "$B/work/out/verify-static-password.txt"
python "$B/work/verify-pi-boundary.py" > "$B/work/out/verify-pi-boundary.txt"
python "$B/work/static-cipher-details.py" "$env:TEMP/dfl-creds/vm-df_game_r" 0x808ca60 0x808be6e 0x808c8b5 0x8173034 0x8173042 > "$B/work/out/pi-boundary-static.txt"
python "$B/work/static-cipher-details.py" "$env:TEMP/dfl-creds/vm-df_game_r" 0x808c311 > "$B/work/out/pi-makehash-static.txt"
python "$B/work/static-cipher-details.py" "$env:TEMP/dfl-creds/vm-df_game_r" 0x808c174 > "$B/work/out/pi-superfast-static.txt"
```

## 仍不能宣称“只剩真机”

- [未验证] 当前singleton与抓包key同运行实例的逐字节交叉；历史抓包若跨重启，不能直接与新进程比相等。
- [静态]+[回放] LOGIN尾字段已逐游标消费：400B明文消费388B，余12B全零；不同部署version/loginout/local-ip字段已列出。mouse 8B CTEA经目标ELF有界回放成功，独立Python轮函数与目标ELF逐块往返256例一致；输出不命名为密码。LOGIN限制器已静态确认：mobileAuth为true时isRestricted直接false；否则按policy map/虚表判定，缺项默认restricted。
- [未验证] C→S PI校验/序号完整重构、LOGIN token后续字段及padding/尾hash的完整游标消费，仍可静态与离线推进。
- [未验证] 其余13算法及全90帧正文语义不是CMD1通过就完成；仍需逐算法已知答案。
- [未验证] RSA非法输入/base64边界、错误模式/偏移负例强度仍可扩大；当前错误key负例仅断言明文不同，不是完整登录拒绝语义。
- [未验证] 动态安全内存探针尚未加固；此轮没有附加、没有发送任何登录帧。

本文件更新旧报告中“OpenSSL未核”和“两份样本”的历史状态，不把离线通过冒充完整登录构造或重写就绪。
