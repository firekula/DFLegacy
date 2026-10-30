# Mentor master gold — independent managed implementation review

**Decision: changes required for one Important notification-ordering issue.** No Critical findings. This is the consolidated finding list for the frozen managed Tasks 1–3 diff against `3a35188173314786b006a0a15454799a52fc166f`. Native Task 4 remains pending and is not approved by this report.

Reviewed all modified/new managed product and test files, all MentorGold fixtures, root Program registrations, and the scoped fixture attributes rule. Unrelated public decisions/assets and native changes are outside this review. Read the approved design, plan, original assessment and relevant original arithmetic/timer/notification evidence. Only this review file was written; no build, test execution, product/config/state mutation, UI/live action, commit or delegation occurred.

## Findings

### Critical

None.

### Important R1 — Real external Gold packets bypass the gate protecting the mentor increment/absolute pair

**Location:** `src/DFLegacy.Server/MentorGoldSessionRefresh.cs:24–39`, with the production callback wiring at `EntranceService.cs:6232–6254`.

The new helper holds `dungeonCurrencyGate` while consuming credit and sending NOTI76 followed by NOTI13. That serializes the owner and callbacks which actually acquire this gate. However, the real trade and private-store channel send delegates still call `SendTownPacketAsync` directly. That function (`EntranceService.cs:1131–1149`) only writes a packet; it does not participate in this currency transaction. SerializedWriteStream serializes packet writes, not canonical balance sampling plus the multi-packet mentor transaction.

Two concrete production paths remain outside the gate:

- `ItemTradeService.SendGoldRefreshAsync:933–949` reads canonical Gold, constructs an absolute NOTI14 slot0/item0 balance, and sends it through `channel.Send` (`:1287–1299`). Peer cancellation/disconnect/timeout reaches this through `CancelSessionAsyncCore` and `SendCancelToSideAsync:929`. An open/cancelled trade has no committed pending-trade-delivery marker, so the new helper's pending-trade check does not serialize this path.
- `PrivateStoreService.BuyAsync:257` queues a seller NOTI82 with `ProjectedSellerGold`, calculated by `JsonGameStore.PrivateStore.cs:111`, and its outbox drains through the same ungated channel delegate. The value is already sampled before delivery.

**Counterexample (static interleaving, not an executed test):** Master canonical/client Gold is G and a mentor credit of14 is waiting. A peer cancels an open trade. The trade service samples G for its NOTI14, then its task is delayed before delivery. The master owner consumes the credit and sends NOTI76(+14), then NOTI13(G+14). The delayed trade NOTI14(G) is then sent. Canonical Gold correctly remains G+14, but the final client HUD is G; that cancellation has no pending award left to trigger another mentor refresh. Conversely, an external absolute sampled after consumption can arrive before NOTI76 and expose the prohibited already-inclusive-absolute-then-increment sequence. The private-store seller packet supplies a second real concurrent currency sender with the same missing serialization boundary.

`CheckCurrencySerializationAsync` (`MentorGoldLifecycleSmokeTests.cs:194–235`) does not catch this: its invented competing callback explicitly waits on the semaphore supplied to the mentor helper. Neither real sender above does so. `CheckTradeDeliveryRaceAsync` covers a committed trade's pending-install marker, which is a different condition from cancellation's direct absolute refresh.

**Smallest requested fix:** Make these real external currency deliveries participate in the owner's currency ordering, including the moment their absolute balance is sampled. A narrow owner-queued currency refresh/delivery hook, or equivalent bounded guarded callback, is sufficient. Do not merely lock transmission of an already stale absolute packet, blindly gate every town send (owner operations already hold the currency gate), add Gold revision rejection, or block successful ordinary saves. Preserve private-store sale details and trade cancellation behavior.

**Required regression:** Exercise actual ItemTradeService cancellation and actual private-store seller delivery against the production owner installation path using controlled delivery barriers. Cover an absolute sampled before mentor consumption but delivered late, and a sender occurring during the NOTI76/absolute transaction. Decode the actual Gold-bearing packets in delivery order and assert the final HUD equals canonical Gold, with no inclusive absolute preceding the same mentor increment. Keep the existing successful item/skill/mail/Cera and pending-trade-install tests.

### Minor

None.

## Verified freeze and evidence

The final `freeze-manifest.json` SHA256 is `afe99057267eba038c4e5b00ad697b9889ac999c33a1ce3b7942e466395f8240`. Independently checked file bytes and SHA256: **466 source/test entries, 33 review-scope entries, 19 built managed DLL copies and 31 evidence entries all matched**. Final Server DLL is `7d44789c36e41abee01757f9ca003b9b3cc173183f4c891d18a7931d4b41dc4d`; Protocol is `5a55089d384efa9e4acec5a211ee5c551668cddb5c93ed46a698d50250e04a4e`; SmokeTests is `339a69692ae55fb1fa4eb5e75e983fd34b60207239c987115fd2e820262fad31`.

Read the sealed Server/Smoke Release logs (both 0 warnings, 0 errors), focused final log (0 failures), and final full-suite log (`All DFLegacy protocol smoke tests passed.`). These are author-run results verified against the manifest, not fresh reviewer test runs. Their passing status does not cover the real sender interleaving in R1.

Read-only provenance verification independently hashed the actual ACT1 client/PVF, original game ELF and Monitor ELF, all five fixture-manifest evidence exports, original member table, and both derived oracle files. All matched. Independently decrypted the actual PVF's `etc/goldlimitbylevel.etc` in memory and compared its original 1367 bytes to the fixture: identical, SHA256 `718960fccddac3f6205ca9e0a21686493cde8b3727e1e6d3860caac69e465e1e`. No fixture generator or product executable was run.

## Other reviewed guarantees

- Rate implementation matches the two binary64 constants, separate binary32 component stores and sum, canonical own-edge relationship level, current online direct-child count and native capacity tiers. Exact integer product/shift truncation avoids the demonstrated double-rounding boundary. The independent Fraction oracle covers native-valid rate combinations; get_rand_int's signed LCG branch and two-draw ordinary branch match the original instructions. Gold scheduling is independent of growth, includes the900 endpoint contract, and does not catch up missed periods.
- Generated nonzero-source pickup and trusted free-card requested amounts accrue only after successful first canonical receipt commits under the store gate. Source0, paid cost, replay, zero/negative-signed inputs and stale incarnations do not feed a batch. Solo pickup/card converge on those same receipt paths. Child Gold is not additionally deducted; consuming received mentor credit creates no upstream source.
- Runtime batches are bound to both attachments and canonical edge generation. Successful edge mutation invalidates them in the same gate. The implemented WasReplaced leaf probe closes the approved design I1 at the real ClaimAsync/Kick/Released boundary, including replacement while retirement waits for the gate. Ordinary retirement flushes before unregister; forced retirement discards. Existing final actor save/reload ordering remains intact.
- Award persistence appends only awarded credit/receipt, leaving actor Gold unchanged until owner consumption. Native cap includes pending dungeon/private-store/mentor credit; existing capacity checks reserve mentor credit too. Canonical `with` updates and the shared snapshot writer retain other character fields and root ledgers. Tests require real item/skill/mail/Cera saves to succeed and retain their data, rather than accepting stale-write rejection. Unknown award/consume outcomes quarantine and are not retried; restart follows the atomic recorded outcome and never rebuilds raw tax batches.
- The NOTI76 builder includes actual credited u32 Gold, zero i16 fatigue and full CP936 string32 name. Own helper order and selective Gold/receipt installation are correct in isolation; login consumes old awarded money without old popups. R1 is the missing connection between this helper and existing real external currency senders.

Resolve R1 in one bounded fix wave, rerun the affected real-sender regressions and required checks, then provide a new freeze for scoped re-review. Native master-gold source/popup/relogin acceptance and root deployment remain separate outstanding gates.
