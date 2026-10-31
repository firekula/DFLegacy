# ACT1 skill usability static contract — 2026-10-30

Read-only analysis of `G:/Game/DNF_ACT1/Loot4_A/DNF.exe`; SHA256 `6b4850beaf67ec61adb04ae17f119f16278dd05e97b8ffca871a9bdb547de0d5`, preferred image base 0x400000. No process access, native calls, builds, product edits, or writes to previous evidence directories. Reproduce with `static.py dis VA:hexSize`, `refs`, `table`, or `offsets`.

## Main result

There is no proven single total, side-effect-free, full execution-readiness getter. The exact original icon predicate `bool __thiscall 0x4F4F50(Skill* this, Actor* actor)` covers cached context enablement, effective MP cost, and local item cost. It does **not** check cooldown, death, raw actor mode +154, or the per-skill use limit. It must not alone be published as actual usability.

The actual execution/commit function `bool __thiscall 0x49C600(Actor*, int skillId, bool showError, bool consumeMP, bool consumeItems)` (actor vtable +5F8 on generic/Slayer tables) checks learned level, per-skill limits, cooldown, MP, items, and then **commits**: MP setter, item removal, notifications/packets, related calls. Even disabling its boolean arguments does not make it a safe getter. Actor v51C `0x4A3510(actor,id)` lazily allocates/copies missing skills. Never call either for observation.

All proposed observation below is bounded direct memory reads through the existing checked secure decoder. No native getter calls. Even apparently pure native secure getters call anti-tamper reporting `0x901470` on invalid data; malformed state must become unknown instead.

## Cached action/context bit

Skill secure int **+2EC** is the original per-frame context enable bit. `0x4F3730(skill,bool)` writes it and command entries +28. Generic actor v518 `0x494100` first obtains the skill array through actor v5D8 `0x493780`, then calls `0x4FBCD0(array,false)` to clear every registered skill's +2EC. Each class v518 updater then selectively enables current legal skills according to actor action and class state. This supports using the cache instead of inventing airborne/weapon/action rules.

The real command-selection path `0x4FC0B0` rejects secure +2EC ==0 at 0x4FC175; it then evaluates command history through `0x4F35B0`. Skill secure **+2F4** is a separate command-lock toggle checked by 4F35B0 and set by UI callers of 4F3810; icon predicate ignores it. Do not confuse command locking with all-route usability (a hotbar route can differ).

For **C, Job0/GrowType0/Slayer**, class vtable is `0xA038DC`, v518=`0x633340`, v5F8=`0x49C600`. Updater first calls generic clear, then requires actor byte **+20A3 !=0**. Its action switch uses secure actor **+1434**. Action **0 or 11** reaches the ordinary group: id5 is enabled at 0x63339D..6333AD; id46 is enabled at 0x6334ED..6334FD. Other branches enable other specific skills. This is original native action eligibility, not a universal action-name inference. Read +2EC after the engine's current updater, with the normal game-thread snapshot consistency policy.

Slayer real execution **id5 (鬼斩 / hardattack.skl, ↑+Z; not bare Z)**: 0x647BE4 requires local-authority identity (`0x473620`, actor v138 result equals nonnull global B75A14) and raw actor **+154 !=2**. 0x647D50..647D77 permits actions 0/11/8 into input matching; 0x647D8C calls 4FC0B0, which also requires the cached bit. 0x647DA6 invokes v5F8 with id5 and all booleans 1. After success it constructs action 0x10. There is no additional target or weapon-type gate on this id5 branch. Input matching/history is a trigger, not a requirement that the user already be holding the activation key when querying readiness. This branch remains static evidence only; it is not the MP6/bare-Z live anchor.

## Full effective MP cost without native calls

Native `int __thiscall 0x4F4D30(Skill*, Actor*, int level=-1)`:

1. Read learned level secure skill+2E0 when level==-1.
2. Secure-int cost vector begin/end **skill+184/+188**, stride8. Select `min(level,count)-1` (valid positive levels); empty or nonpositive selection gives base cost0. Bound count and addresses, reject invalid pointers/counts rather than reproducing native unchecked access.
3. Multiply by per-skill actor factor from `0x4F74F0(actor, secure(skill+08))`; store result as float32.
4. Read secure skill **+208**. Mode1 multiplies by actor weapon factor index0; mode2 index2; any other mode skips weapon factor. The multiplication result is stored float32. This is the factor product, before multiplying the integer base cost.
5. Multiply base int cost by that float32 factor using x87, then convert via CRT 0x91B708 (x87 float-to-int with truncation correction). Base values and factors1 are exact within normal bounded integer ranges. General fractional products close to an integer boundary need the project's explicit conservative precision policy; do not claim measured runtime x87 control/precision. Nonfinite/negative/out-of-range factors or cost -> unknown.
6. Apply equipment modifiers through 0x4F3BA0 with arguments `(actor, cost, 0, 3, skillId)`. This scans **23** equipment slots and both primary and enabled attached-equipment modifier lists. See safe zero-modifier fast path below. Nonempty unsupported modifier branches must produce unknown, never assume zero.

### Per-skill factor hash map

Header `m=actor+1E74`: **m+08 head/sentinel**, **m+0C node count**, **m+14 bucket begin**, **m+18 bucket end**, **m+20 mask**, **m+24 active bucket count**. Node is doubly linked: +0 next, +4 prev, +8 integer skill ID, +0C secure float value. `4D5C80` is lookup, not insertion.

Native lookup: `idx=(id XOR DEADBEEF)&mask`; if idx>=activeBuckets then `idx=idx-1-(mask>>1)`. Read first at bucketBegin+idx*4 and last at bucketBegin+idx*4+4. Traverse next while node.key<id and node!=last; last or key>id returns header.head, exact match returns node. 4F74F0 returns float1 when result==head, else decodes secure float at node+0C. A bounded replica can cap nodes at64 and verify idx/array range.

**Empty fast path:** node count actor+1E80==0, nonnull head actor+1E7C, and head.next==head and head.prev==head -> factor1. Clear source 4A60E0 calls 4A4A80 with `this=m+4`; it sets these self-links and m+0C=0. If reproducing hash lookup rather than the logical empty shortcut, also validate buckets/indices.

### Weapon factor

Actor v54C=**498D90**. Weapon pointer **actor+13E8**; null returns1. Otherwise actor v4DC=494410 calls weapon vDC; verified **weapon.vDC=4A7B50** returns weapon+CC. Decode weapon type secure int at **weapon+D0**. Actor vector begin/end **+2040/+2044**, stride **58** bytes. If weapon type is in vector range, decode secure float at `vectorBegin + type*58 + 38 + factorIndex*8`; otherwise original returns1. Index0 for MP mode1, index2 for MP mode2. Validate the getter identities as layout fingerprints without calling them.

### Safe zero-equipment-modifier fast path

4F3BA0 equipment getter actor v368=492A60 reads each raw `actor+13C4+4*i`, i=0..22. When this pointer equals `actor+1FCC+4*i`, original treats it as null. Otherwise nonnull equipment requires vE0=**4A7B60**, which returns embedded equipment data at item+F8. The original modifier vector is data+298/+29C, hence **equipment+390/+394**, stride **24 hex (36 decimal)**. The fast path requires begin==end, so no primary modifiers. Also require **equipment+6A0==0** (v118=4A7BE0), so no attached equipment data; otherwise unknown. This deliberately supports ordinary base equipment, while not interpreting arbitrary modifier rows. Nonempty attached data could be inactive, but conservative unknown is valid.

## Use limits and item costs

Actual-use gate before cooldown: `0x4A40A0(skill,actor,-1)` selects secure-int level entry from **skill+1CC/+1D0**, stride8; empty vector gives limit0. **Unlike MP costs, the nonempty limit vector has NO level clamp.** At 4A410C..4A4118 native directly reads `begin+(level-1)*8`; a safe replica must require `1<=level<=count`, and return unknown otherwise. A shared min(level,count) selector for MP and limits changes source semantics and can grant a false true. If nonzero limit, native `0x4A4B10(actor,id)` looks up use count in actor hash map **+1E4C**; count>=limit rejects. This gate is absent from icon4F4F50. **True fast path must require empty limit vector or decoded selected limit0.** Other limits may be unknown until a bounded use-counter replica is implemented.

Item cost vector **skill+1BC/+1C0**, stride **18 hex**. Row secure id at+0, secure first cost+8, secure last cost+10. Interpolation is `first + truncTowardZero((last-first)*(learned-1)/(secure(skill+20)-1))`. The original assumes valid denominator; replica rejects invalid denominator/overflow. For local actor, `45D9A0` scans inventory **B5DCEC**, capacity secure+10 and pointer-array+18, using item v18 descriptor+4. Verified item v18=4619A0 returns item+8, so item ID raw item+C. Count v34=461AB0 decodes secure item+A0. Insufficient/missing item rejects; zero cost skips lookup. **Initial supported true scope may require empty item vector**, leaving nonempty vector unknown, which covers base no-item skills. Inventory search itself normally reads only, but uses secure failure handlers; no native calls are needed.

## Candidate bounded acceptance for C id46 (上挑 / bare Z)

For the common native manual-precast readiness scope, emit true only with verified local layout/authority, learned positive id46, alive knownfalse-dead, actor154!=2, actor20A3!=0, action0/11, secure skill2EC!=0, finite cooldown ready (or equivalent properly handled native predicate), use limit0, empty itemcost vector, complete known effective MPcost with zero equipment modifiers, and MP>=cost. This scope does not guarantee UI key acceptance, target resolution, or a hit. **Actor+154 is a DWORD comparison**, encoded 83 BE 54 01 00 00 02 at both 49C635 and 647BF3. Raw 0x102 must not be mistaken for blocked raw2 by reading only its low byte. Publish source semantics and supported scope. All missing/corrupt parts -> null. A proved blocker (dead, DWORD mode154==2, context bit0, native CDblocked, knownMPshortage, knownlimit exhaustion) can return false despite other unknowns. A positive cooldown_ms alone does not describe resources or action availability.

Corrected live/key anchor from root's original-PVF cross-check: `skill/swordmanskill.lst` maps **id46=upperslash.skl (上挑)**, command SKILL/bare Z, base MP6 and CD2000ms (`auto cooltime apply=1`); **id5=hardattack.skl (鬼斩)** uses UP+SKILL/↑+Z, base MP24 and CD7000ms. The prior Z MP6 observation belongs to id46, not id5. C's bare-Z attempt while watching id5 therefore cannot prove id5 cooldown. Root also observed D bare-Z id4 cooldown1611ms; that is a separate positive live cooldown anchor. Verify actual live cost vectors/factors before granting true. Suggested C id46 cases: idle ready true; same snapshot lowMP false; native bare Z consumes MP and yields cooldown false; wait returns idle ready; jump/blocked action gives context false; existing native death gives false. Do not use input-history state as a readiness blocker. Keep other unsupported branches null until proved.

## Minimum meaningful fixtures

See fixtures.json. Include context-blocked while cooldown0, low-MP while cooldown0, dead while cooldown0, mode154==2, use-limit nonempty unknown, resource vector unknown, fractional equipment factor, attached equipment unknown, and learned levels above table count use final table entry. Include frame-consistency cases so stale context cannot grant a false true. Runtime precision near truncation boundary should return unknown unless exactly proven, never rounded-to-nearest.

## Evidence anchors

`c-skill5-execution.txt`, `slayer-queries.txt`, `mp-cost-factors.txt`, `4f47-50.txt`, `v5f8-xrefs.txt`, `mp-offsets.txt`; `manifest.json` pins source ranges. Static scripts only read the executable; no runtime getters were invoked.
