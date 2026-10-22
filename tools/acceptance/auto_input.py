"""合成输入时间线的驱动端（B′ Task 4）。

游戏进程内的 ijl15 模块从客户端目录取 ``auto-input.json`` 执行步骤，计划完成后
把文件改名为 ``auto-input.done.json``。本模块是验收平台侧的契约实现：原子写计划、
等完成标记、等目录就绪。所有等待到点即返回 bool——调用方判定，这里绝不猜。
"""
from __future__ import annotations

import json
import os
import pathlib
import time

PlanName = "auto-input.json"
TmpName = "auto-input.json.tmp"
DoneName = "auto-input.done.json"

_PollIntervalS = 0.05


def _path(client_dir, name) -> pathlib.Path:
    return pathlib.Path(client_dir) / name


def _remove_stale(path: pathlib.Path, deadline: float) -> bool:
    """删除旧文件；Windows 上偶发句柄抖动时在 deadline 内重试。"""
    while True:
        try:
            path.unlink()
            return True
        except FileNotFoundError:
            return True
        except OSError:
            if time.monotonic() >= deadline:
                return False
            time.sleep(_PollIntervalS)


def write_plan(client_dir, steps, timeout_s: float = 30) -> pathlib.Path:
    """原子写一条计划并返回最终路径。

    旧的 auto-input.json / .tmp 先删（timeout_s 内对删除做重试，容忍瞬时空闲
    后仍被占用的文件）。载荷固定 ``{"version": 1, "steps": [...]}``，steps 逐字
    落盘；``ensure_ascii=False`` 保证 text 步骤按裸 UTF-8 写出——客户端解析器
    只认 ``\\\\`` 与 ``\\"`` 两种转义，``\\uXXXX`` 会被整单拒绝。
    """
    deadline = time.monotonic() + timeout_s
    for name in (TmpName, PlanName):
        _remove_stale(_path(client_dir, name), deadline)
    payload = {"version": 1, "steps": list(steps)}
    tmp = _path(client_dir, TmpName)
    tmp.write_text(json.dumps(payload, ensure_ascii=False), encoding="utf-8")
    final = _path(client_dir, PlanName)
    os.replace(tmp, final)
    return final


def await_done(client_dir, timeout_s: float) -> bool:
    """等 auto-input.done.json 出现（计划完成）；到点未完成返回 False。"""
    return _wait_for(lambda: _path(client_dir, DoneName).exists(), timeout_s)


def wait_ready(client_dir, timeout_s: float) -> bool:
    """等 .done 消失（上一条计划已被 ijl15 取走执行完，可以写下一条）。"""
    return _wait_for(lambda: not _path(client_dir, DoneName).exists(), timeout_s)


def _wait_for(predicate, timeout_s: float) -> bool:
    deadline = time.monotonic() + timeout_s
    while True:
        if predicate():
            return True
        if time.monotonic() >= deadline:
            return False
        time.sleep(_PollIntervalS)
