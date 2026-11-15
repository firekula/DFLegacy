# Creature gift fixtures

Source: original `G:/Game/DNF_ACT1/DF/Script.pvf`, SHA256
`288d58aace8ed4724a0a94aaa4cd0957b1d52c633771132ede2f96363c1b506f`.

The30 `.equ` files are unchanged CP936 entry bytes: all15 current ordinary
sealed eggs and their15 actual hatch outputs. `manifest.json` records each
original virtual path, item, byte length, original-entry SHA256 and fixture
SHA256. The scoped repository `-text` rule protects these original bytes.
No UTF-8 recon export is used as a runtime equipment script.

`equipment/equipment.lst` contains only those30 equipment mappings;
`etc/newcashshop.etc` contains all20 original CREATURE rows in source order.
These two files are scope-derived CP936 text, not original whole PVF entries.
Their manifest records the original source-entry hash and the derived
LF-normalized hash; checkout CRLF is harmless for these derived rows.

`CreatureGiftFixture.cs` combines this scope with the existing AvatarGift
fixture and explicitly generated ASCII exclusion/boundary definitions. The
synthetic food, rename, direct-effect, package, booster and lottery definitions
are marked in that helper and do not replace any of the30 original entries.
Individual rejection tests mutate only temporary copies.

An independent Chinese-name assertion checks that original63007 decodes as
`宠物蛋 (恰克斯)`, in addition to the byte/hash checks.
