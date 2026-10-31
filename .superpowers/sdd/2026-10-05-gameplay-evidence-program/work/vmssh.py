#!/usr/bin/env python3
r"""vmssh.py —— 连 VM（192.168.200.131，OpenSSH **4.3p2**）的薄封装。

## 为什么不用系统 ssh

VM 上跑的是 OpenSSH **4.3p2**（2006 年，RHEL5 时代的 FIPS 构建），它只提供：
  kex    : diffie-hellman-group-exchange-sha1 / group14-sha1 / group1-sha1
  hostkey: ssh-rsa
  mac    : hmac-sha1（**没有** ETM 变体）
  pubkey : ssh-rsa（**不认识 ed25519** —— 实测日志 `unsupported public key algorithm: ssh-ed25519`）
现代 ssh 客户端默认不提供这些，所以必须显式放开。而 VM 自带的 ssh(4.3) 又不认
`-o KexAlgorithms=`（那是 5.7+ 的语法）—— 两头都不方便，所以走 paramiko 编程控制。

## 口令的处理（纪律）

* 口令**只从环境变量 `VM_PASS` 读**，不写进本文件、不写进命令行参数（命令行会进
  shell 历史/进程表）。
* 本文件不落盘任何凭据；产物里也不许出现口令。
* 用法：`$env:VM_PASS='...'; python vmssh.py "命令"`

用法:
  vmssh.py "id; hostname"                 # 跑一条命令并打印输出
  vmssh.py --raw "ls -l /root"            # 只打印 stdout（不打印 exit/stderr 装饰）
"""
from __future__ import annotations

import os
import sys
from pathlib import Path

import paramiko

HOST = "192.168.200.131"
USER = "root"
KEY = Path(os.path.expanduser("~")) / ".ssh" / "id_rsa_b0"


def connect(timeout: float = 60.0) -> paramiko.SSHClient:
    """连上去。先试公钥（若私钥无口令则可用），失败再退回口令（从 $env:VM_PASS）。

    ⚠️ **超时必须给足**：VM 是 OpenSSH 4.3，`UseDNS` 走默认值（=yes），每个**新**连接
    会先对客户端 IP 做反向 DNS 查询。实测第一条连接的 banner 要 **7.21 秒**才来，
    随后几条 0.01–0.07 秒（sshd 侧有缓存）。给 15 秒会**偶发**超时，症状是
    `Error reading SSH protocol banner` —— 看上去像"连不上"，其实是等得不够久。
    这里给 60 秒。**根治**是把 `sshd_config` 的 `UseDNS` 设为 `no` 再重启 sshd
    （本会话没做，因为改 sshd 有把唯一靶子弄坏的风险，而加超时零风险）。
    """
    cli = paramiko.SSHClient()
    cli.set_missing_host_key_policy(paramiko.AutoAddPolicy())

    kex = ["diffie-hellman-group14-sha1", "diffie-hellman-group-exchange-sha1",
           "diffie-hellman-group1-sha1"]
    macs = ["hmac-sha1"]
    keys = ["ssh-rsa"]

    common = dict(hostname=HOST, username=USER, timeout=timeout,
                  banner_timeout=timeout, auth_timeout=timeout,
                  look_for_keys=False, allow_agent=False)
    disabled = dict(disabled_algorithms={"pubkeys": ["ssh-ed25519", "rsa-sha2-256",
                                                     "rsa-sha2-512", "ecdsa-sha2-nistp256"]})

    last = None
    # 1) 尝试公钥（无口令的私钥才行；有口令会抛 PasswordRequiredException）
    if KEY.exists():
        try:
            cli.connect(pkey=paramiko.RSAKey.from_private_key_file(str(KEY)),
                        disabled_algorithms={"pubkeys": ["ssh-ed25519", "rsa-sha2-256",
                                                         "rsa-sha2-512"]},
                        **common)
            return cli
        except Exception as e:                       # noqa: BLE001
            last = e
            cli = paramiko.SSHClient()
            cli.set_missing_host_key_policy(paramiko.AutoAddPolicy())

    # 2) 口令
    pw = os.environ.get("VM_PASS")
    if not pw:
        raise SystemExit(f"公钥不可用（{type(last).__name__ if last else '未尝试'}）；"
                         f"且未设置 VM_PASS")
    # 老服务端不认 ETM 类 MAC，也不认 ed25519/rsa-sha2 —— 显式禁用
    cli.connect(password=pw,
                disabled_algorithms={"pubkeys": ["ssh-ed25519", "rsa-sha2-256", "rsa-sha2-512"],
                                     "macs": []},
                **common)
    return cli


def run(cmd: str, raw: bool = False) -> int:
    cli = connect()
    try:
        _in, out, err = cli.exec_command(cmd, timeout=60)
        so = out.read().decode("utf-8", "replace")
        se = err.read().decode("utf-8", "replace")
        rc = out.channel.recv_exit_status()
        if raw:
            sys.stdout.write(so)
        else:
            sys.stdout.write(so)
            if se.strip():
                sys.stdout.write(f"--- stderr ---\n{se}")
            sys.stdout.write(f"--- exit={rc} ---\n")
        return rc
    finally:
        cli.close()


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if a != "--raw"]
    if not args:
        raise SystemExit(__doc__)
    sys.exit(run(" ".join(args), raw="--raw" in sys.argv))
