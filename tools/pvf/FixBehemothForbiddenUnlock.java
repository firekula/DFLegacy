import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardOpenOption;
import java.security.MessageDigest;
import java.util.Arrays;
import java.util.HashSet;
import java.util.HexFormat;
import java.util.Map;
import studio.pvf.core.PvfArchive;
import studio.pvf.core.PvfWriter;

/** Guarded ACT1 export: enable dungeon 27 after accepting/completing quest 452. */
class FixBehemothForbiddenUnlock {
    static final String SOURCE_SHA256 = "69aa1a0338408fbcc3eff381924311ba42119a2cd2db4e8ffd2fab369bb9b34b";
    static final String TARGET = "worldmap/behemoth.wdm";
    static final String DISABLED_ROW = "//27 452";

    static void require(boolean condition, String message) {
        if (!condition) throw new IllegalStateException(message);
    }

    static String sha256(byte[] bytes) throws Exception {
        return HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(bytes));
    }

    static int bodyOffset(byte[] archive) {
        require(new String(archive, 0, 15, StandardCharsets.US_ASCII).equals("DNF_SCRIPT_PACK"), "Expected ACT1 archive");
        return 27 + ByteBuffer.wrap(archive).order(ByteOrder.LITTLE_ENDIAN).getInt(15);
    }

    static byte[] stored(byte[] archive, int bodyOffset, long offset, long length) {
        int start = Math.toIntExact(bodyOffset + offset);
        return Arrays.copyOfRange(archive, start, Math.toIntExact(start + length));
    }

    public static void main(String[] args) throws Exception {
        require(args.length == 3, "Usage: FixBehemothForbiddenUnlock source.pvf new-output.pvf evidence-directory");
        Path source = Path.of(args[0]).toRealPath();
        Path output = Path.of(args[1]).toAbsolutePath().normalize();
        Path evidence = Path.of(args[2]).toRealPath();
        require(Files.isDirectory(evidence), "Evidence directory must already exist");
        require(!source.equals(output) && !Files.exists(output), "Output must be a new, separate file");
        Path proofPath = evidence.resolve("java-export-verification.json");
        require(!Files.exists(proofPath), "Evidence report already exists; refusing to overwrite");
        try (var input = PvfArchive.open(source)) {
            require(input.sha256().equals(SOURCE_SHA256), "Source SHA256 mismatch; already-fixed inputs are rejected");
            byte[] sourceArchive = Files.readAllBytes(source);
            require(sha256(sourceArchive).equals(SOURCE_SHA256), "Source changed after opening");
            byte[] original = input.readBytes(TARGET);
            String text = new String(original, StandardCharsets.ISO_8859_1);
            int row = text.indexOf(DISABLED_ROW);
            require(row >= 0 && row == text.lastIndexOf(DISABLED_ROW), "Expected exactly one disabled dungeon 27 row");
            require(row > text.indexOf("[dungeon]") && row < text.indexOf("[/dungeon]"), "Row outside dungeon list");
            require(row == 0 || text.charAt(row - 1) == '\r' || text.charAt(row - 1) == '\n', "Unexpected row prefix");
            int rowEnd = row + DISABLED_ROW.length();
            require(rowEnd == text.length() || text.charAt(rowEnd) == '\r' || text.charAt(rowEnd) == '\n', "Unexpected row suffix");
            // Equal-length uncomment preserves all subsequent offsets and ciphertext.
            byte[] changed = original.clone();
            changed[row] = ' ';
            changed[row + 1] = ' ';
            require(changed.length == original.length, "Entry length changed");
            PvfWriter.export(input, Map.of(TARGET, changed), output);
            try (var exported = PvfArchive.open(output)) {
                require(exported.fileCount() == input.fileCount(), "Entry count changed");
                var sourcePaths = new HashSet<String>();
                var outputPaths = new HashSet<String>();
                for (var entry : input.entries()) sourcePaths.add(entry.path());
                for (var entry : exported.entries()) outputPaths.add(entry.path());
                require(sourcePaths.equals(outputPaths), "Entry path set changed");
                byte[] outputArchive = Files.readAllBytes(output);
                int oldBody = bodyOffset(sourceArchive), newBody = bodyOffset(outputArchive);
                require(oldBody == newBody && sourceArchive.length == outputArchive.length, "Archive layout changed");
                int differences = 0;
                for (var entry : input.entries()) {
                    var actual = exported.entry(entry.path());
                    require(actual.offset() == entry.offset() && actual.length() == entry.length()
                            && actual.fileNumber() == entry.fileNumber(), "Entry metadata changed: " + entry.path());
                    byte[] before = input.readBytes(entry), after = exported.readBytes(actual);
                    boolean target = entry.path().equals(TARGET);
                    require(Arrays.equals(after, target ? changed : before), "Unexpected entry bytes: " + entry.path());
                    if (!Arrays.equals(before, after)) differences++;
                    if (!target) {
                        require(actual.key() == entry.key(), "Unchanged entry key changed: " + entry.path());
                        require(sha256(stored(sourceArchive, oldBody, entry.offset(), entry.paddedLength()))
                                .equals(sha256(stored(outputArchive, newBody, actual.offset(), actual.paddedLength()))),
                                "Unchanged entry ciphertext changed: " + entry.path());
                    }
                }
                require(differences == 1, "Expected exactly one changed entry");
                input.verifyUnchanged();
                require(sha256(Files.readAllBytes(source)).equals(SOURCE_SHA256), "Source archive changed");
                String proof = "{\n"
                    + "  \"sourceSha256\":\"" + input.sha256() + "\",\n"
                    + "  \"outputSha256\":\"" + exported.sha256() + "\",\n"
                    + "  \"entriesChecked\":" + input.fileCount() + ",\n"
                    + "  \"changedEntries\":[\"" + TARGET + "\"],\n"
                    + "  \"entryLength\":" + original.length + ",\n"
                    + "  \"changedByteOffsetsInEntry\":[" + row + "," + (row + 1) + "],\n"
                    + "  \"beforeRow\":\"//27 452\",\n"
                    + "  \"afterRow\":\"  27 452\",\n"
                    + "  \"unchangedEntryPlaintextAndCiphertext\":true,\n"
                    + "  \"allEntryOffsetsPreserved\":true,\n"
                    + "  \"sourceUnchanged\":true,\n"
                    + "  \"inGameVerified\":false\n}\n";
                Files.writeString(proofPath, proof, StandardCharsets.UTF_8, StandardOpenOption.CREATE_NEW);
                System.out.print(proof);
            }
        }
    }
}
