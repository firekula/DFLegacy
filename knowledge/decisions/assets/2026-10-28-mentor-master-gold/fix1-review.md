# Mentor master gold — scoped R1 fix review

**Decision: APPROVE. Important R1 is ADDRESSED.** No remaining Critical, Important or Minor findings in this fix scope. This closes the managed review's sole finding; native Task 4 and root publication remain separate outstanding gates.

Reviewed `fix1-evidence.md`, the complete `fix1-scoped.diff`, all four changed/new files, actual trade/store senders and their production callback/safe-point/retirement wiring. This is a scoped re-review of R1 and the added during-delivery sale case, not a new feature sweep. Only this report was written. No product/config/state edit, build, test execution, UI/live operation, commit or delegation occurred.

## R1 closure

`EntranceService` captures an `OwnerCurrencyDeliveryQueue` for each attachment and supplies its adapters to the actual ItemTradeService and PrivateStoreService channel callbacks. The adapters recognize the real single-entry absolute Gold NOTI14 shape and successful seller NOTI82 shape. They copy the payload, enqueue without awaiting the owner's currency gate, and wake the actor. Other packet delivery remains direct, including cancellation notification and sale failure. This avoids a service-to-owner-gate wait while the owner is awaiting that service.

The normal and idle owner safe points call the queue drain after private-store, dungeon and mentor installation. Drain acquires the same currency gate as the mentor NOTI76/absolute pair. It freezes the current notice batch **before** installing pending trade/store credits and sampling canonical Gold, and overwrites only the queued packet's Gold bytes. Thus a precomputed old trade/sale balance cannot overwrite the mentor result later, and a notice arriving during the mentor pair cannot expose an already-inclusive absolute before that pair's increment. Mentor credit is not consumed by the external queue; its own receipt/NOTI76 installation remains authoritative.

The extra sale-during-send case is correctly bounded. Once the first batch has been sampled, a newly arriving sale notice remains queued for a later pass, where its new credit is consumed before another balance is sampled. It cannot inherit the earlier batch's balance. Sampling may include already committed credits whose notices arrive later; subsequent notices are absolute canonical synchronizations and do not increment money again. Sale details outside bytes1–4 remain intact; trade refresh changes only its Gold field at bytes7–10.

All retirement paths use the existing retirement helper, now retiring and detaching the captured queue. A replacement gets a different queue; old delegates retain the retired object and cannot enqueue into the new actor. The queue's retired check suppresses pending Gold delivery. The owner-only drain and retirement sequencing preserve the original final-save/lease-release flow and avoid adding a new economic lock or stale-save rejection. Selective actor installation remains limited to Gold/private-store credit fields.

## Regression evidence

The new tests use actual ItemTradeService request/accept/cancellation and actual PrivateStoreService create/display/enter/purchase, connected to the same typed queue adapters and owner drain used by production. They exercise both late pre-sampled absolute delivery and a sender firing while NOTI76 is paused. The recorded initial four RED failures therefore cover the original production sender gap rather than an invented callback that voluntarily takes the semaphore.

The additional real second purchase while the first seller packet is paused has its own recorded RED, then GREEN after freezing the batch. Tests decode the real 76/13/14/82 Gold offsets and require final HUD equality with canonical Gold, as well as cancellation delivery and byte-identical non-Gold sale details. Original mentor/store/lifecycle/stale-action tests remain registered.

Read sealed logs: final focused suite reports0 failures; final full suite reports `All DFLegacy protocol smoke tests passed.`; Server and Smoke Release builds each report0 warnings and0 errors. These are author-run results checked against the freeze, not fresh reviewer executions. No native master-gold result is claimed.

## Freeze verification

Independently verified `freeze-fix1-manifest.json` SHA256 `af51f6edda236eac0c575c2f0a02cfd07d8357efe08f86890b9aa7b4f9058bd6`: **468 source/test entries,35 full-scope entries,4 fix-scope entries,19 managed DLL copies and44 evidence entries all matched byte sizes and SHA256**.

Compared source inventories with the initial freeze: only `EntranceService.cs` and `MentorMasterGoldSmokeTests.cs` changed; only `OwnerCurrencyDeliveryQueue.cs` and `MentorGoldExternalDeliverySmokeTests.cs` were added; none removed. The original managed implementation's other guarantees and policy/receipt/source code therefore remain unchanged. Root Program registrations and attributes were not changed in this wave.

Final Server DLL: `a0ee99650a49011b9527e06bb8e1c19e6a7e5d284cc51d8f9865597c9e133134`. Final SmokeTests DLL: `417ba2c62ac6e7e71d8c1d990475a41f33365628f4284c27076414d0b2c0320d`. Protocol remains `5a55089d384efa9e4acec5a211ee5c551668cddb5c93ed46a698d50250e04a4e`.
