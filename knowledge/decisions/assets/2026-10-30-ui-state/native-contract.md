# ACT1 UI / scene2 read-only contract — 2026-10-30

Scope: follow-on U05 research only. All new output is this directory. No product/tests edits, builds, process/memory/UI operations, or writes to any earlier evidence directory. Original EXE SHA256 `6b4850beaf67ec61adb04ae17f119f16278dd05e97b8ffca871a9bdb547de0d5` (`G:/Game/DNF_ACT1/Loot4_A/DNF.exe`). Every proposed observation is a bounded direct-memory replica; no original methods are called.

## Implementation recommendation

1. Accept **v2 scene2 snapshots** in the consumer as an observation-only scene with empty/null town/combat payload. Keep town/navigation/party actions requiring scene1, and dungeon controls requiring scene3. Do not merely expand a numeric whitelist while leaving the v2 `else` town-validation branch intact.
2. Extend the existing selector record with source-exact raw transition/phase fields and explicit selection-index meaning. Keep selector `visible` unknown. Its current +4 `active_raw` is an initialization flag, not visibility.
3. Add a bounded list of **registered native dialog windows**, including raw class and root visibility/input flags. Validate active registration, owner, root backlink, getter identities, and pending-close exclusion. Optionally prove attachment under the correct native UI layer. Do not label this a global input blocker or a complete arbitrary-UI inventory.

## Current producer/consumer mismatch and guards

`client_state.cpp Capture` reads scene via secure main+10, calls UiState, then scene1 -> TownState, scene3 -> dungeon capture, every other scene -> `valid=true` with empty world payload. Python `client_state.py` rejects scene2 in both `_validate` and `get_known_field` through `(1,3)`. Additionally its v2 block is `if scene==3 ... else: require town+me`, so scene2 needs its own branch even after broadening the scene whitelist. Existing tests intentionally list scene2 as rejected; these must be replaced/extended narrowly, not silently removed.

Proposed scene2 invariant: schema v2, valid/layout true, not truncated, `town=null`, `room=null`, `me=null`, `players=[]`, `enemies=[]`, `skills=[]`, and all skill capabilities false. UI scalar fields may remain known. Never expose stale town actors or combat actors during selector transition. v1 scene2 can stay unsupported until a deliberate compatibility policy exists.

`client_navigation.py` requires v2 scene1 and bound town/me; native client_navigation ValidateRequest and runtime live checks also require scene1. `client_party.cpp ReadLiveContext` likewise requires scene1 plus native Town identity and selection_mode_raw0. Dungeon/test-control paths explicitly require scene3. Preserve these action guards when allowing scene2 reading; accepting observations is not permission to broaden actions.

Current early scene2 success lacks the final scene/global/secure-key consistency recheck used by scene3. Before marking a new scene2 capture valid, repeat main global, secure scene, selector pointer/table, secure manager/key, and relevant vector headers/phase values. Scene change or pointer replacement during capture must invalidate or clear the affected UI record. Existing generation increments on scene transitions, but scene2 selector identity currently is not part of history; consider binding any new selector token/stage token used by future actions rather than authorizing from sequence-only data.

## Selector module identity and exact fields

`selector=U(main+20)`; original vtable **99A5B8**, +4 target **8A2A20** (update), +8 target **8A0EB0** (render). Constructor432510 sets this table; destructor432620 restores it. Do not call virtuals: update/render both mutate global input/UI/transition state.

| Source | Field recommendation | Proof / semantics |
|---|---|---|
| byte selector+04 | initialized_raw (or preserve existing active_raw with explicit meaning) | ctor432532=0; init8A4690 tests at8A46AE and sets1 at8A46BB, creating UI resources; destructor8A0E10 clears it at8A0E26. Object/resource existence does not mean rendered in current scene. |
| byte selector+100 | transition_pending_raw | 8A1299 writes1 after dungeon-entry notification; zero in reset8A1103/8A1C4D, cleared after switching scene3 at8A2E49. Validate byte0/1. |
| DWORD selector+1A0 | phase_raw | reset0; 8A0FC4 sets1; update8A2CE8 moves1->2; 8A2DFF moves2->3 after fade timer; render8A4627 moves3->4; update8A2E3E switches scene3 when phase4. Keep raw0..4, do not invent complete network phase names. |
| secure int selector+144 | selection_input_suppression_raw (optional) | 8A0F90 sets1; update8A2AAF reads and skips main selection-input handling when nonzero. This is a selector input gate, not all-app keyboard/mouse blocking. |
| pointer selector+68 | attached_stage_token (optional) | 8A0EF0 stores new stage and attaches stage+48 to selector+10 root. 8A1290 reads stage from U(U(main+24)+10) and passes it to8A0EF0. Null outside attachment. Keep in UI transition record; do not populate combat room from it. |
| int selector+200 | selected_index_raw | index into selector record vector; default-1. 8A130D/8A13BF select it. Existing JSON `selected` is this index, not dungeon ID. |
| int selector+204 | hover_index_raw | last hover index, default-1, update8A2AD8/8A2B07. |
| selector+1F4/+1F8/+1FC | records begin/end/cap | stride10hex=16 bytes; validate bounded count and overflow before index use. |
| record+0 | dungeon_id | native8A1372 passes it to4683F0 dungeon metadata lookup; 8A2C39 passes it to entry routine8A1DD0. |
| record+4 | difficulty_raw | 8A13F8 stores difficulty; native up/down difficulty controls at8A2C9B/8A2CD0. Special argument4 to setter means preserve current value, so don't misread setter arguments as stored difficulty. |
| record+8 | maximum_difficulty_raw (optional) | 8A14AC reads it and compares current+4 to enable increment button at8A14B7. Keep raw until runtime/PVF validates naming/range. |

Bounded selection projection: read vector headers, cap at e.g.64 records, verify `(end-begin)%16==0`, selected in[0,count), then return selected record's id/difficulty. Selected=-1 -> selected dungeon/difficulty null, not zero. Invalid index -> null/error, never select first row as observer; native entry code may fall back to first row, but that is activation behavior rather than snapshot selection. Repeat headers/index at end. Exposing the entire vector is optional; minimal selected record is sufficient.

**Scene2 is not always pure selection:** existing `party_combat_detail.cpp PureSelection` explicitly checks scene2 and selector+100==0. The original `8A1290` sets +100=1, attaches the dungeon stage, and sends it to game UI B5DCE0 while scene is still2. Therefore do not reset dungeon lifecycle solely because scene2 is visible, and do not infer no stage or no transition from scene2.

## Bounded native dialog inventory

UI manager `im=U(B5DCE8)`. Active bank has **0x71 slots, class0..0x70**, vector headers at **im+D8+class*10hex**: begin+0/end+4/cap+8; pointer stride4. Source799450 loops exactly0x71 slots, checking nonempty vectors. Original show799EF0 inserts into this bank, removes from pool, and attaches the window root to a native layer. **Pool bank im+7E8+class*10hex must not be reported active.** Pending deletion queue at **im+EF8/EFC/F00**; close7BDC50 calls7A1670 to append there. The window can still be in active bank until processing; exclude any pending-close token.

For each active candidate:

- `U(window+4)==class`, `U(window+10)==im` (both DWORD).
- vtable+2C==**7BF640**, bytes `8B4108C3`: pure root getter -> `U(window+8)`.
- vtable+30==**7BF650**, bytes `8B4104C3`: pure type getter -> `U(window+4)`.
- `root=U(window+8)` nonnull, root vtable **A7E09C** (`nengine::CNWindow` RTTI). Constructor7BDB30 creates it through7BF7C0 and sets `U(root+44)=window`; verify backlink.
- byte root+10 is native **shown_raw**; byte root+11 is native **input_enabled_raw**. Both must be0/1. 8F6880 initializes them1; render8F6A80 only descends into shown children (child+10), hit traversal8F6C30 immediately rejects any node with +10==0 or +11==0.
- rectangle root+14/+18/+1C/+20 is x/y/width/height; can expose only if bounded positive dimensions, if useful. It is not evidence of on-screen unclipped visibility.
- repeat manager/pointers/headers/root/backlink/flags before publishing. Bad/cyclic/oversized vectors -> explicit incomplete/truncated state, not an empty list.

Suggested cap:113 fixed headers, <=64 total active window rows and <=64 pending-close entries. Per-class rows cannot exceed remaining total cap. Deduplicate tokens, reject mismatched ownership. Existing snapshot caps must state this scope. A missing manager -> dialog list unknown, never known empty.

### Attachment and visibility limits

Show799EF0 chooses a root layer by class: class<=0F -> `U(im+C8)`, 10..17 -> `U(im+CC)`, >=18 -> `U(im+D0)`. It calls8F6B50(layer,root) to attach; removal799D20 calls8F6920 on same layer. A bounded pure membership replica can traverse layer's doubly linked list: sentinel=U(layer+64), count=U(layer+68), nodes next+0/prev+4/value(root)+8; compare root pointer, cap64/128, verify links/count and reread header. Source8F68E0 is pure membership scan. This supports an `attached` field without invoking methods.

Raw shown1 does **not** guarantee rendered pixels: parent layer visibility, clipping, occlusion, current render route and deferred removal matter. Safest first schema: `dialogs[].shown_raw` and `input_enabled_raw`, optionally `attached`, instead of broad `visible=true`. If a field is called `visible`, define it narrowly as active+not-closing+attached+shown (and ancestor shown), and retain limitations. Existing selector visible:null should remain.

**No verified universal input_blocked getter.** 799450 is normally pure, but it only returns any active vector nonempty; it ignores root flags and pending-close queue. Its four direct callers (7AA1A5/7AA1EC/8527B3/8981BC) suppress specific UI keyboard actions, not all game input. `selection_mode_raw=im+FC4` is a separate UI targeting mode (many values0..13hex), not modal visibility. Do not derive all-app input_blocked from either. A reliable false native hit eligibility is available when verified root shown or enabled is0; true hit participation still is not a guarantee to consume all input. Keep any global block field null until routing proof is scoped.

### Useful fixed native type identities

These are exact constructor-set class IDs plus RTTI, not inferred from captions. Useful allowlist if generic vtable-base fingerprint is considered too broad:

| class | vtable | native class | constructor evidence |
|---|---|---|---|
| 23hex | A7E254 | CNRDPopupMessageWindow | 7BFEB2 push23 ->7BFEE1 base7BDB30 ->7BFEEB derived table |
| 25hex | A7E2F4 | CNRDResponsePeerWindow | existing verified party prompt;7C0141 base constructor |
| 30hex | A7E434 | CNRDWalkoutConfirmWindow | 7C0612 push30 ->7C0641 base ->7C064B table |
| 59hex | A7EA5C | CNRDCommonConfirmWindow | 7C18C7 push59 ->7C1540 ->7C1595 base,7C18D1 derived table |
| 6Ahex | A7F4C4 | CNRDGotoTownConfirmWindow | 7C4132 push6A ->7C4161 base ->7C416B table |

Do not assume class1/2 mean generic OK/Yes-No: class1 vtableA7FB2C is PartyRecommendUserWindow. CNRDYesNoConfirmWindow (A7E044) is a base used by multiple concrete class IDs. Raw ID plus layout proof is more accurate than guessed friendly labels.

## Suggested tests and four-client acceptance

Consumer tests: v2 scene2 roundtrip with UI known and all world fields empty; wait scene2 succeeds; v2 scene2 with stale town/me/room/skills is rejected; skill caps true rejected; v1 scene2 stays policy-explicit; scene0/4 remain rejected unless deliberately separately supported. Town navigation and party/dungeon actions must still reject scene2 at both consumer and native guard.

Native pure fixtures: selector initialized1 while scene1 must not imply selector visible; +100 transition1 while scene2 with attached stage; selected index maps raw dungeon ID/difficulty; -1 ->null; out-of-range/corrupt vector ->unknown; manager/window/root replacement invalidates. Dialog active versus pool, pending-close exclusion, root hidden/disabled, class mismatch, backlink mismatch, duplicate rows, oversized/cyclic graph, and unknown vtable must be covered. Never turn unreadable/incomplete inventory into empty/false.

Current four test roles can support a reversible acceptance sequence without new game mutation features: use one already-returned-to-town client to enter a native dungeon selector, collect fresh scene2 UI-only snapshots, hover/select native rows/difficulty and match index/projected ID, then Esc back to town. Retain untouched peers as source-of-truth scene1/3 checks. For a generic dialog, open native return-to-town/walkout confirmation on a dungeon client, record class6A/30 if actually observed, then **cancel**; do not assert which class will appear until actual snapshot+image agrees. Check closed/deferred prompt disappears from active result. Existing party invite prompt can exercise class25 if an invitation is independently authorized; no automatic peer messaging is part of this research.

Observe selector transition via normal existing debug capture during one user-authorized dungeon entry: scene2 may coexist briefly with +100=1, phase2/3/4 and nonnull attached stage before scene3. If sampling misses this short window, report unobserved rather than forcing state. No raw input fields, function calls, packets, actor state edits, U11 skillbar fixes, or ground-bug work are needed.

## Evidence files

`selector-window-base.txt`, `selector-phase-modal.txt`, `final-anchors.txt`, `fc4-refs.txt`, `modal-refs.txt`, `phase-refs.txt`, `offsets.txt`, `manifest.json`; fresh tool static.py only reads original executable. `fixtures.json` lists proposed tests; JSON validation is not an implementation test run.
