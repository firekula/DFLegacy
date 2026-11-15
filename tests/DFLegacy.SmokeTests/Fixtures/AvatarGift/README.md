# Ordinary ACT1 avatar fixtures

The three equipment files are the unchanged CP936 entry bytes extracted from the original `Script.pvf`. `provenance.json` records the archive SHA256, original paths and each entry SHA256. The equipment list and cash-shop file select only those three original items and their nine original 7-day, 30-day and permanent rows. They retain the original option ordering; the 44-option mage coat is not synthesized.

`AvatarGiftFixture` copies these files to an isolated temporary script directory. Its explicitly synthetic additions cover malformed/missing/empty options, commented tags, the base-price107 truncation boundary, gift-disallowance, stickers99 and a physical item. They are not claimed as original PVF entries.
