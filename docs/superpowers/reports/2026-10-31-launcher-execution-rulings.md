# 登录器执行裁定记录

以下32项按原执行顺序保留，包括复核时重申的边界。它们是已接受的实现裁定，延期Minor为0。详细验收见同目录2026-10-31-sharp-scaling-acceptance.md。

## Rulings I made

### 1. Ruling: unsupported native encodings and UTF8BOM-first-header files are rejected for display editing, preserving their bytes

Ruling: unsupported native encodings and UTF8BOM-first-header files are rejected for display editing, preserving their bytes — actual Windows profile APIs ignore them — user must re-encode such a file instead of receiving a false success. This follows the spec's native-readable-encoding requirement; exact limits are documented rather than guessed.

代价与边界：原生读取器不支持的文件需先转换编码；原字节不被改写。

### 2. Ruling: configuration replacement uses a same-user named mutex, original-byte checks and atomic rename, but cannot promise cross-process compare-and-swap against an uncooperative writer after the last check

Ruling: configuration replacement uses a same-user named mutex, original-byte checks and atomic rename, but cannot promise cross-process compare-and-swap against an uncooperative writer after the last check — standard filesystem replacement has that narrow window — cost is a possible lost external edit in that window; stale/locked/cooperating edits fail safely. Keep this limit in acceptance documentation, consistent with existing NPK export limits.

代价与边界：不合作进程在最后检查到原子替换之间写入，仍可能丢失该次外部修改。

### 3. Ruling: presenter renders to caller-owned input/output surfaces; device hook context owns the output swap chain

Ruling: presenter renders to caller-owned input/output surfaces; device hook context owns the output swap chain — keeps GPU code shared with real readback tests without test-only production hooks — internal API differs from plan wording, with unchanged D3D8 algorithm/output contract.

代价与边界：内部调用必须遵守表面和输出链的所有权；已用真实GPU及生命周期测试验证。

### 4. Ruling: split reusable factory/device hook engine into d3d8_hooks.{h,cpp} with immutable policy and input-pump callback, leaving the fixed-image IAT adapter in d3d8_scaler.cpp

Ruling: split reusable factory/device hook engine into d3d8_hooks.{h,cpp} with immutable policy and input-pump callback, leaving the fixed-image IAT adapter in d3d8_scaler.cpp — actual factory path is shared by production/GPU/fake-COM fixtures without linking synthetic input or fixed-image patching into test executables — additional internal module, no new DLL or distribution dependency.

代价与边界：多一个内部模块需要维护；分发文件和DLL数量不增加。

### 5. Ruling: preserve contiguous, readable, executable private vtable extension entries after public97/device and16/factory, bounded512; stop before guard/unreadable/non-code pointer

Ruling: preserve contiguous, readable, executable private vtable extension entries after public97/device and16/factory, bounded512; stop before guard/unreadable/non-code pointer — actual Windows runtime calls private slot133 from Clear, so a97-only clone is unsafe despite the public interface length — costs a bounded inspection of implementation-private entries and requires actual-runtime acceptance; fixed104 is also insufficient. No shared original call pointers or new proxy runtime.

代价与边界：依赖已验证运行时的私有虚表布局，不保证任意驱动布局。

### 6. Ruling: replace historical fixed Direct3DCreate8 IAT address with bounded PE named-import lookup within the already validated DNF image

Ruling: replace historical fixed Direct3DCreate8 IAT address with bounded PE named-import lookup within the already validated DNF image — observed compatible unfolded client moved imports, making the old hook inert — unnamed/ordinal-only imports are rejected rather than guessed, and such clients would need explicit adapter support; original instruction/image gates remain.

代价与边界：仅序号或无名导入的客户端需要专用适配。

### 7. Ruling: extend enhanced-mode input handling to convert window-client mouse messages into the fixed640x480 frame, including wheel hit testing

Ruling: extend enhanced-mode input handling to convert window-client mouse messages into the fixed640x480 frame, including wheel hit testing — actual800 control runs proved the old window-scaling patch already placed the software cursor/hit tests at1.25 times physical mouse coordinates, so retaining it would fail the approved click-alignment acceptance — potential cost is interaction with unknown client-side input hooks; constrain to registered enhanced windowed devices, preserve screen-coordinate wheel messages/buttons/capture, and keep legacy untouched. Synthetic input calls the native logical-coordinate function directly and is not double-transformed. Update spec's earlier assumption of existing correct input mapping with this measured exception.

代价与边界：与未知输入hook组合仍有限制；兼容模式保留旧行为。

### 8. Ruling: detect logical/physical client-size mismatch and mark dpi-virtualized original-present fallback

Ruling: detect logical/physical client-size mismatch and mark dpi-virtualized original-present fallback — avoids falsely claiming an exact final output when DWM will rescale a DPI-unaware legacy window — enhancement is unavailable under such virtualization until the window's DPI environment is corrected; no OS/compatibility settings were changed. Policy input800x600→physical1000x750 RED API then GREEN; actual local normal-DPI GPU/client paths pass. Other monitor scale settings were not modified/tested.

代价与边界：DPI虚拟化环境下增强效果不可用；不改系统显示设置。

### 9. Ruling: scope original vtable identity around original factory/device COM calls while keeping per-object clones outside those calls

Ruling: scope original vtable identity around original factory/device COM calls while keeping per-object clones outside those calls — observed existing WIN7RTM AppHelp wrappers resolve their originals by object-vtable identity, so unchanged clone identity crashes CreateDevice/Reset — relies on this client's serialized D3D use; concurrent calls while an original scope is active may bypass hooks and are not a supported/verified mode. Actual normal launcher, native runtime and AppHelp paths all exercised; this adds no global vtable modification.

代价与边界：依赖2008客户端串行D3D调用，并发调用可能绕过hook。

### 10. Ruling: execute the single fresh whole-branch review after Task4 local implementation/tests and before remote publication, rather than after Task4's remote completion

Ruling: execute the single fresh whole-branch review after Task4 local implementation/tests and before remote publication, rather than after Task4's remote completion — the spec requires review before main push, while Task4 itself contains publication — cost is that later CI/publication artifacts are verified by explicit gates rather than this pre-publication reviewer. Reviewer sharp_scaling_final_review dispatched read-only against aba40c4..40e0c91 with all plan Review Focus items verbatim and ledger rulings.

代价与边界：后续远程产物由精确发布门禁校验，源代码复核本身不认证产物。

### 11. Final: Ruling: uncooperative last-check/rename writer

Final: Ruling: uncooperative last-check/rename writer — reaffirm the documented mutex+byte-check boundary, not an absolute CAS guarantee — possible lost edit only in that narrow noncooperative window.

代价与边界：再次确认非合作写入的狭窄竞争窗口，不承诺绝对跨进程CAS。

### 12. Final: Ruling: concurrent D3D calls during original identity scopes

Final: Ruling: concurrent D3D calls during original identity scopes — retain serialized2008-client scope — concurrent callers can bypass hooks and have no general thread-safety certification.

代价与边界：再次确认不提供通用并发D3D线程安全认证。

### 13. Final: Ruling: arbitrary driver/private tables and malicious/unrelated PE images

Final: Ruling: arbitrary driver/private tables and malicious/unrelated PE images — retain validated-image instruction gates and bounded private-tail inspection — other layouts are not universal compatibility coverage.

代价与边界：其他私有表布局和无关/恶意映像不属于已验证兼容范围。

### 14. Final: Ruling: unnamed/ordinal-only imports

Final: Ruling: unnamed/ordinal-only imports — retain explicit unsupported adapter rather than guessing — enhancement unavailable for those import layouts.

代价与边界：再次确认无名/序号导入不猜测；该类客户端没有增强适配。

### 15. Final: Ruling: fullscreen/unsupported sizes/DPI virtualization

Final: Ruling: fullscreen/unsupported sizes/DPI virtualization — keep visible original-frame fallback — those environments do not receive enhanced scaling.

代价与边界：全屏、不支持尺寸或DPI虚拟化使用可见原呈现回退。

### 16. Final: Ruling: legacy mouse alignment

Final: Ruling: legacy mouse alignment — preserve old behavior as a compatibility option, enhanced modes correct it — legacy scaled windows retain the old offset.

代价与边界：旧版兼容模式的已知缩放鼠标偏移保留，增强模式修正。

### 17. Final: Ruling: active BeginStateBlock recording

Final: Ruling: active BeginStateBlock recording — do not claim coverage from the distinct active BeginScene test; no concrete defect inferred — unusual state-block-recording clients remain unverified.

代价与边界：活跃BeginStateBlock录制情形未验证，不能由BeginScene测试代替。

### 18. Final: Ruling: combat performance/GPU execution time/alternate monitor scales

Final: Ruling: combat performance/GPU execution time/alternate monitor scales — report only menu/startup CPU sampling and actual tested DPI — no combat/GPU-time/other-scale guarantee.

代价与边界：性能数据限于菜单/启动CPU样本，不保证战斗、GPU耗时及其他DPI。

### 19. Final: Ruling: pre-existing post-write IAT failure and connection-store transactions

Final: Ruling: pre-existing post-write IAT failure and connection-store transactions — keep base behavior except fix new display authority guard — prior helper/store limitations remain, not newly certified.

代价与边界：原有IAT写后失败、连接存储事务边界没有额外认证。

### 20. Final: Ruling: remote publication/download/final install

Final: Ruling: remote publication/download/final install — keep Task4 active and perform those gates after the one fix pass — local success alone is not final completion.

代价与边界：本地通过不能代替最终远程下载和安装验证。

### 21. Final: Ruling: review visual independence

Final: Ruling: review visual independence — supplied actual UI evidence plus local tests are the executor's verification, reviewer did not repeat UI — visual acceptance has one operator rather than an independent second UI run.

代价与边界：视觉证据只有一名操作员，代码复核没有重复实机UI操作。

### 22. Final: Ruling: display saves require a persisted readable launcher.json and hold its read-sharing lease through Config.ini replacement

Final: Ruling: display saves require a persisted readable launcher.json and hold its read-sharing lease through Config.ini replacement — establishes actual saved target and prevents it changing while writing the other file — a first-time configuration must save connections first, and simultaneous connection saves can briefly fail with a sharing error rather than target another installation.

代价与边界：首次需保存可读连接配置；同时保存连接可能短暂遇到共享冲突。

### 23. Ruling: preserve old rejected untracked styles until Task2 replaces ConfigWindow, while adding shared ClassicStyles separately

Ruling: preserve old rejected untracked styles until Task2 replaces ConfigWindow, while adding shared ClassicStyles separately — keeps Task1 independently buildable — one temporary unused style file removed when its consumers switch.

代价与边界：历史过渡中的未使用样式已在切换消费者后移除，无遗留分发依赖。

### 24. Ruling: rasterizer falls back to WPF text if Win32 GDI allocation fails, retaining semantic text instead of crashing; monochrome certification applies to successful GDI path

Ruling: rasterizer falls back to WPF text if Win32 GDI allocation fails, retaining semantic text instead of crashing; monochrome certification applies to successful GDI path — visible text may become antialiased in a low-resource GDI failure environment.

代价与边界：GDI资源不足时文字回退可能抗锯齿，仍保留可读语义文本。

### 25. Ruling: retain native CenterOwner for modal placement instead of forcing coordinates from primary-screen bounds

Ruling: retain native CenterOwner for modal placement instead of forcing coordinates from primary-screen bounds — avoids breaking multi-monitor owner placement; crest means a small vertical offset from static composition.

代价与边界：保留原生居中，窗体装饰相对静态合成图存在小位置差异。

### 26. Ruling: small-window acceptance uses an actual test-owned offscreen360px WPF settings window and real scroll metrics, without changing user desktop/DPI settings

Ruling: small-window acceptance uses an actual test-owned offscreen360px WPF settings window and real scroll metrics, without changing user desktop/DPI settings — alternate-monitor/DPI visual combinations remain unmeasured.

代价与边界：验证真实离屏窗口和滚动度量，未改变或实测不同物理显示器DPI。

### 27. Ruling: UI-only gate reruns complete Launcher and managed smoke plus new full CI; previously reviewed native/GPU source is unchanged

Ruling: UI-only gate reruns complete Launcher and managed smoke plus new full CI; previously reviewed native/GPU source is unchanged — native local17/GPU evidence remains from27eb, new CI covers rebuilt headless native16.

代价与边界：对未改的原生源复用已通过的GPU证据，新CI仍重新构建并测无交互原生项目。

### 28. Final: Ruling: reviewer set aside prior native/GPU implementation

Final: Ruling: reviewer set aside prior native/GPU implementation — retain completed independent scaling review and unchanged native source, rather than buying duplicate review — native compatibility/performance boundaries from that review still apply.

代价与边界：不重复已完成的独立原生复核，原兼容性边界继续适用。

### 29. Final: Ruling: reviewer set aside release/install/main/runner acceptance

Final: Ruling: reviewer set aside release/install/main/runner acceptance — keep Task3 and original Task4 active until actual exact-source remote gates complete — pre-publication source review alone cannot certify release artifacts.

代价与边界：发布、安装、main保留和Runner恢复必须另由实际门禁完成。

### 30. Final: Ruling: keep CenterOwner but replace primary-only sizing with actual monitor queries and compact uniform layout scaling below480x360

Final: Ruling: keep CenterOwner but replace primary-only sizing with actual monitor queries and compact uniform layout scaling below480x360 — makes actions reachable on small logical work areas — extremely small work areas scale pixel labels and may reduce legibility; physical alternate-DPI monitor switching is not directly certified.

代价与边界：极小工作区会统一缩小布局，文字可能较小；物理跨DPI切换未实测。

### 31. Ruling: fix the cross-thread resize fixture rather than changing the production window's native sizing behavior or retrying/skipping the gate

Ruling: fix the cross-thread resize fixture rather than changing the production window's native sizing behavior or retrying/skipping the gate — actual nonclient sizing runs on its window thread and the fixture now models that transaction — temporary subclass exists only in the test EXE; this does not certify arbitrary foreign-thread programmatic resizing as a product API.

代价与边界：临时窗口过程仅在测试EXE存在；不把任意外部线程调整尺寸当作产品API承诺。

### 32. Ruling: after this test-only CI integration change, rerun full local native/GPU17 and full new CI, retaining already-green Launcher/protocol results for identical product sources

Ruling: after this test-only CI integration change, rerun full local native/GPU17 and full new CI, retaining already-green Launcher/protocol results for identical product sources — avoids redundant local managed runs while keeping all remote gates — new CI must succeed before publication and installation.

代价与边界：产品源码相同的本地托管测试不重复，新的完整CI仍必须全部成功。

## Deferred minors

无。复核中三个Minor类别涉及用户明确要求或错误连接反馈，已提升为本轮必须处理项并完成回归。
