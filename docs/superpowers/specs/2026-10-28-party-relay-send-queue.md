# ACT1 TCP relay send-queue concurrency repair

The user authorized completing UDP-failure TCP fallback, official/ACT1 comparison,
tests and original dual-client screenshots, followed by confirmed remaining work.
The existing request covers this necessary native compatibility repair; no new
deployment topology or gameplay behavior is selected here.

## Evidence and failed acceptance

Managed source `20d996f` and native `30AD` completed a normal UDP dungeon and a
controlled peer-UDP-sink dungeon, including boss settlement and channel reentry.
However, frozen `closed-fallback-v2` fails strict stream decoding. The server
also rejected three real malformed TCP headers and the client reconnected.
This phase is FAILED for final TCP acceptance. Preserve the complete package,
raw captures, photographs, logs, and failed harness attempts unchanged.

Private original-byte exports identify ACT1 `916B20` as output queue append and
`917250` as count/head/send/consume. `9174A0` appends into a mutable shared buffer;
`9174F0` shifts and changes its count. Neither operation nor the whole flush
transaction has a lock. The same relay TCP tuple is sent from the game and
network threads. Competing flushes can observe the same head; append/consume can
also mutate bytes referenced by an in-flight send and its post-send diagnostic.
The static finding is a concrete race candidate, not yet a native acceptance
claim. A deterministic interleaving test must reproduce it before implementation.

Official relay `df_relay_r` handles partial TCP sends and preserves complete
frames. Server resynchronization or discarding arbitrary bytes would violate
that contract. Keep the reviewed managed framing and authority rules unchanged.

## Selected repair

The initial low-level append/flush-only proposal was rejected by independent
review: reset, reconnect and destruction can replace/free that queue. A later
owner-entry registry proposal was also rejected: callers read a secondary owner
vtable before reaching the entry hook. No such incomplete proposal was installed.

Use one recursive mutex around the complete relay virtual dispatch, beginning
BEFORE the first owner pointer/vtable access. Original core accesses at `9072DC`,
`90769C`, `90790E`, `90858E` are the three send producers and the network tick.
Independent original-PE instruction scanning finds these four reads and the
`907DE6` setter as the only non-stack `+410h` accesses in `905000..909500`.
Replace these complete blocks with verified x86 stubs that pass the stable core,
original frame address/length, and defer current-owner lookup to the locked helper.
An unbound core returns zero without dereferencing an old owner.

The same mutex covers `907DE0` binding, `78D060` manager stop, and `906070`,
`905FC0`, `906290` direct configuration/close/reconnect. `78D060` deletes the
relay and clears manager and core references before unlocking. Its tail
`905560 -> 907DE0` only changes a pointer; misleading FLIRT names are not thread
Join evidence. Direct root operations belong to the original main-thread lifetime;
network producers first enter through the protected dispatch blocks. Destruction
and producer safety claims are limited to this verified call chain.

Preserve original enqueue, send, partial-send, would-block, consume and return
behavior. The gate also spans successful-send logging and buffer use. Native
synchronous callbacks can reenter the mutex. Unrelated socket paths have no patch.
No MT payload, peer-ready state, NAT type, controller, HP or coordinate is invented.

Installation must verify the actual original instruction bytes and both primary
and actual secondary send/tick owner tables,
reject unknown contexts, roll back a partial nine-site patch, retain executable
trampolines after uncertain rollback, and be idempotent. Do not patch the EXE.
Activate all wrappers' common gate only after the complete nine-site group
installs successfully. If a write and its rollback both fail, retained executable
stubs keep every existing jump valid, all wrappers retain the original ungated
policy and installation reports failure; never enable half of the gate group.
Build into a new native artifact directory; preserve the old 30AD/C79/743 files.

## Acceptance

Tests cover a deliberately paused flush competing with another flush and append,
exact byte conservation/order, reentrancy, independent-gate liveness, original
return values, known-code guards, patch encoding and partial rollback. Stop and
replacement tests pause a producer before pointer lookup; resumed send and tick
must use only the current binding or see null. Actual executable x86 stub tests
pass a deliberately invalid owner/core sentinel through to the helper and verify
stack balance, callee-preserved registers, all three frame offsets, u16 length,
and original return values without a pre-helper owner dereference. Record
behavioral RED with a compilable pass-through adapter, then GREEN with the gate.
Run full native compatibility tests and strict Release builds. Independent review
must pass before using the native module in new private client phases.

Repeat normal peer UDP and controlled sink TCP with a new frozen package and
native DLL identity. Require strict complete capture decoding (including each
RX connection's own TX registration), no rejected relay stream headers,
both-direction original bytes, actual combat/clear/return and channel reentry,
plus original screenshots and loaded-module/PID bindings. Native 30AD remains a
historical baseline, not the new delivery hash. Original native backups, saves,
five configuration bytes and old frozen package inventories remain protected;
only the four explicitly deployed native copies may adopt the accepted new hash.

Monster-position diagnosis remains separate and follows successful TCP closure.
The live purchase/revive unknown-error case remains recorded for subsequent work.
