# ACT1 warehouse Upgrade restoration validation

## Evidence and scope

The original EXE matches SHA2566b4850beaf67ec61adb04ae17f119f16278dd05e97b8ffca871a9bdb547de0d5. Read-only PID67004 samples show grade0, CashShop state0/busy0, product100063/item50 present with passing restrictions, and storage Upgrade event716 shown1/enabled0. Original and live code824C48 is C6812101000000. Native layout unconditionally disables this button in the grade<7 path. This restores the user's requested entry, changing a stock disabled policy rather than repairing a missing server opcode.

The only intended runtime byte change is824C4E:00->01. Complete native context guards cover122bytes, including the layout entry/vtable, max-grade branch, original full instruction, event/state gate and shop busy gate. Tests use original bytes and require only that immediate to differ; corrupted contexts and unknown immediate values reject without writes, retries are idempotent and write failures attempt rollback.

The native cash-shop confirmation, item/price selection, CMD67, server balance/persistence and maximum-grade hiding remain the original paths. Local PVF catalog independently confirms3290products and all seven warehouse grades; first product100063 is item50/quantity1/cash60 and capacity24.

## Verification status

- Original literals compared against EXE:122bytes match.
- Focused tests RED then GREEN; independent review found no must-fix.
- Full isolated native build/tests passed:46/46,74.12s. Candidate DFClient.dll SHA2566d2167f3944fbfd1754b33cad2c015db7e822d9fcbcb0e083108990966194018.
- The user launched the installed preview and confirmed that clicking Upgrade opened the purchase confirmation dialog. Acceptance stopped at the confirmation dialog.

Detailed local evidence is stored under D:/DFLegacyCI/validation/warehouse-upgrade-native-20261103-*.json and warehouse-upgrade-native-diagnosis-20261103.md. These are observations, not claims that an actual purchase has occurred.
