# vm-peek2.gdb —— gdb 批处理命令：**只读**读若干绝对地址后立即退出（不写内存、不改寄存器、不留断点）
#
# 用法（VM 上）：
#   gdb -q -batch -x /tmp/vm-peek2.gdb -p <PID> > /tmp/vm-peek2-out.txt 2>&1
#
# 纪律：
#   * 只用 `x/` 读命令；没有 `set`、没有 `call`、没有断点；
#   * 结尾 `detach` + `quit`，进程恢复运行；
#   * **334 B cipher key 与 policy 向量原样打印**（需要精确逐字节比对）；
#     宿主侧分析后只把 sha256 与前缀写进报告，不把 key 贴进产物。若你不想让 key 出现在
#     这份中间文件里，把下面标了 [KEY] 的三行注释掉即可。
set pagination off
set confirm off
set width 0
printf "PID=%d\n", $_inferior_thread_count

printf "\n== A1: G_Cipher 实例指针（静态槽位 .bss 0x941f8a4）==\n"
x/1xw 0x941f8a4

printf "\n== A2: Cipher 实例头 32 B（+0 vtable, +4/+8 vector begin/end）==\n"
x/8xw *(unsigned int*)0x941f8a4

printf "\n== A3: Cipher vector 内容（前 16 项 ICryptoGraph*）==\n"
x/16xw **(unsigned int**)0x941f8a4

printf "\n== A4: 第一个算法对象的 vtable ==\n"
x/1xw *(unsigned int*)(*(unsigned int*)(*(unsigned int*)0x941f8a4))

printf "\n== A5: needed_key_length 全局 ==\n"
x/2xw 0x941f864

printf "\n== A6: key 结构体数组 0x940be80 前 16 个 dword ==\n"
x/16xw 0x940be80

printf "\n[KEY] == A7: key 缓冲 0x940be80 前 256 B ==\n"
x/64xw 0x940be80

printf "\n[KEY] == A8: key 缓冲 0x940be80 后 256 B（[+1792]）==\n"
x/64xw 0x940be80+1792

printf "\n== B1: ServiceRestrictManager 单例对象 0x94045d4 头 16 dword ==\n"
x/16xw 0x94045d4

printf "\n== B2: 单例初始化守卫 0x94045a8 ==\n"
x/2xw 0x94045a8

printf "\n== B3: 名字表 0x93700c0 全部 35 项指针 ==\n"
x/35xw 0x93700c0

printf "\n== B4: 名字表 33 号指向的字符串（LoginChannel）==\n"
x/s *(unsigned int*)(0x93700c0+33*4)

printf "\n== B5: maps 里 093f6000 区间是否仍映射 ==\n"
info proc mappings

printf "\n== 结束：仅读取，未修改 ==\n"
detach
quit
