# 原生 auto-input 的 drag 契约

新增 `drag` 用于可靠表达“背包源位置按住，移到地面目标，再在目标放开”。`click` 仍在同一位置按下/放开，不能充当拖拽。输入继续经现有 `NativeMouseInput@0x008FCAA0`；没有增加 SendInput、mouse_event、SetCursorPos 或其他外部注入。

```json
{"version":1,"steps":[
  {"t":200,"op":"drag","x":420,"y":300,"x2":240,"y2":380,"dur":240}
]}
```

`t` 是相对计划起点的毫秒数。drag 必须明确提供 `x/y/x2/y2`；`dur` 可省略，默认80ms，不能为负。目标字段仅属于 drag，key/click/rclick/move/text 带 x2/y2 会被拒绝；drag 的 vk/chars 也会被拒绝。其他已有操作的字段兼容性不改变。字段重复、非整数、超出已有 int 数值范围、缺目标等仍事务性拒绝，不修改解析调用方的缓冲区。

正常时间线为：t 时 source move+left down；t+dur/2 时 target move；t+dur 时 target left up。持有期间的原生 WM_MOUSEMOVE 携带 MK_LBUTTON，target up 的该 bit 清除。

由于 Pump 在一个渲染帧中可能被调用多次，drag 阶段之间还有至少16ms的输入采样间隔，且 move/up 不在同一次 Advance 内合并。这是输入采样约束，不是对渲染帧率的证明。名义时点过密、dur=0，或首次 Pump 已经迟于整个时间线时，完成会相应延后。例如迟到至1000ms才发 down，最早1016ms target move、1032ms target up；重复同一 tick 的调用不会瞬发三阶段。GetTickCount 回绕按无符号差值处理。

多步鼠标计划按名义 t 排序，同 t 时按 steps 数组顺序。drag 开始前会等待较早的鼠标步骤完成，并距其最后 move/up 至少16ms；drag 尚未完成、以及 target up 后不足16ms时，较晚的 move/click/rclick/drag 都等待。这样即使首次 Pump 已经晚于多步的名义结束时间，也不会把下一步 down 合进当前 drag，或在同一次 Pump 改写刚刚放开的位置。键盘/text 可同时运行。该顺序约束只用于涉及 drag 的前后步骤，普通 click→click 的既有迟到语义保持不变。

成功 rearm、无效 ArmPlan、Restore，以及读取到畸形/不可读替代计划，都会先取消旧时间线，在当前原生鼠标坐标释放已合成的左/右按钮；Restore 在关闭原生桥之前释放。之后旧时间线不能再补发边沿。仅打开返回 ERROR_FILE_NOT_FOUND/ERROR_PATH_NOT_FOUND 时保留现有计划；独占锁、拒绝访问等其他打开失败也取消并释放。ParsePlan 本身保持纯解析：拒绝解析不会主动中断旧计划，旧计划仍能正常完成和放开。

边界：本模块仍只有一个原生鼠标指针；调用方应按顺序安排鼠标手势，不把同一按钮的多个持有步骤重叠。实际背包拖地、数量确认窗和服务器 CMD50 行为仍由真实双客户端验收确认；原生单元通过不等同于新实机 GREEN。本任务不修改外部验收驱动。

## 离线验证

- 基线3b2c399的原生 Release/full compatibility通过。
- 新测试先记录17项预期失败，覆盖缺 drag 与原有生命周期未释放按钮；实现后 `--auto-input-drag-only` 为0 failures。
- 新增明确覆盖名义时点/目标放开/MK_LBUTTON、迟到与零时长、同tick多次Pump、tick回绕、rearm/无效typed输入/Restore/畸形文件释放，以及纯解析拒绝不干扰旧计划。
- 独立审查后的回归：真实独占文件打开失败先2fail，再GREEN；drag→click/rclick/drag/move迟到顺序先20fail，再GREEN；click/rclick/move→drag迟到顺序先10fail，再GREEN，包含up同Pump、+15ms等待、+16ms继续及键盘/text并行。
- 最终 Win32 Release Rebuild 使用 MSBuild 与既有 `/W4 /WX`，0警告、0错误；完整测试输出 `ijl15 compatibility tests passed`。测试在自身产物目录执行，使用其自己的 Config.ini 和 DLL。
- 本机日志位于独立 worktree `.tmp/native-input-drag-validation/`；产物仅在该 worktree 的 artifacts 中，未部署到客户端、未启动真实游戏/服务。
