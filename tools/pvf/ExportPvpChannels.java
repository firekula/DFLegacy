import java.nio.charset.Charset;
import java.nio.charset.StandardCharsets;
import java.nio.file.Path;
import java.util.Arrays;
import java.util.LinkedHashMap;
import java.util.regex.Pattern;
import studio.pvf.core.PvfArchive;
import studio.pvf.core.PvfWriter;

/** Guarded ACT1 export: preserve every entry except the two channel tables. */
class ExportPvpChannels {
    static final Charset BYTES = StandardCharsets.ISO_8859_1;
    static final String SOURCE = "09e5eefdee0777378a967704eb14c175383eef89ca0f4e27e00dd3409af728cb";
    static final String[] NAMES = {"初级决斗场", "中级决斗场", "高级决斗场", "自由决斗场"};
    static void require(boolean condition, String message) {
        if (!condition) throw new IllegalStateException(message);
    }
    static String name(int index) {
        return new String(NAMES[index].getBytes(Charset.forName("GBK")), BYTES);
    }
    static String channels(String text) {
        var blocks = Pattern.compile("\\[server\\]([\\s\\S]*?)\\[/server\\]").matcher(text);
        var result = new StringBuilder(); int count = 0;
        while (blocks.find()) {
            String body = blocks.group(1);
            var number = Pattern.compile("\\d+").matcher(body); require(number.find(), "Missing server group");
            int group = Integer.parseInt(number.group());
            if (group < 1 || group > 9) continue;
            var rows = Pattern.compile("(?m)^\\s*(5\\d)\\s+<[^>]+>[^\\r\\n]*(?:\\r?\\n|$)").matcher(body);
            var replacement = new StringBuilder(); int removed = 0;
            while (rows.find()) { rows.appendReplacement(replacement, ""); removed++; }
            rows.appendTail(replacement);
            require(removed == (group == 8 ? 4 : 5), "Unexpected old PvP row count in group " + group);
            String newline = body.contains("\r\n") ? "\r\n" : "\n";
            int[] ids = {51,52,53,55};
            for (int i = 0; i < 4; i++) replacement.append("\t").append(ids[i]).append(" `").append(name(i))
                .append("` ").append(i == 3 ? 5 : 4).append(" `[none]` 0 0 0 0 0 0 0 0 0 0 0").append(newline);
            blocks.appendReplacement(result, java.util.regex.Matcher.quoteReplacement("[server]" + replacement + "[/server]")); count++;
        }
        blocks.appendTail(result); require(count == 9, "Expected nine public server groups"); return result.toString();
    }
    static String ranks(String text) {
        var block = Pattern.compile("\\[PVP CHANNEL INFO\\][\\s\\S]*?\\[/PVP CHANNEL INFO\\]").matcher(text);
        require(block.find(), "Missing PvP rank table");
        var value = new StringBuilder("[PVP CHANNEL INFO]\r\n");
        String[] ranges = {"0 9 0", "10 19 1", "20 30 2", "0 30 4"};
        for (int group = 1; group <= 9; group++) {
            value.append(group).append(" 4\r\n");
            for (int i = 0; i < 4; i++) value.append('`').append(name(i)).append("` ").append(ranges[i]).append("\r\n");
            value.append("\r\n");
        }
        value.append("[/PVP CHANNEL INFO]");
        String result = block.replaceFirst(java.util.regex.Matcher.quoteReplacement(value.toString()));
        require(!block.find(), "Multiple rank tables"); return result;
    }
    public static void main(String[] args) throws Exception {
        require(args.length == 2, "Usage: ExportPvpChannels source.pvf output.pvf");
        Path source = Path.of(args[0]).toRealPath(), output = Path.of(args[1]).toAbsolutePath().normalize();
        require(!source.equals(output) && !java.nio.file.Files.exists(output), "Output must be a new, separate file");
        try (var input = PvfArchive.open(source)) {
            require(input.sha256().equalsIgnoreCase(SOURCE), "Unexpected source SHA256");
            var changes = new LinkedHashMap<String, byte[]>();
            changes.put("etc/channel_info.etc", channels(new String(input.readBytes("etc/channel_info.etc"), BYTES)).getBytes(BYTES));
            changes.put("etc/pvpchannellist.etc", ranks(new String(input.readBytes("etc/pvpchannellist.etc"), BYTES)).getBytes(BYTES));
            PvfWriter.export(input, changes, output);
            try (var exported = PvfArchive.open(output)) {
                require(exported.fileCount() == input.fileCount(), "Entry count changed"); int count = 0;
                for (var entry : input.entries()) {
                    byte[] before = input.readBytes(entry), after = exported.readBytes(entry.path());
                    require(Arrays.equals(after, changes.getOrDefault(entry.path(), before)), "Unexpected entry change " + entry.path());
                    if (!Arrays.equals(before, after)) { count++; System.out.println("CHANGED " + entry.path()); }
                }
                require(count == 2, "Expected exactly two changed entries");
                System.out.println("PASS all " + input.fileCount() + " entries; exactly two channel tables changed");
                System.out.println("OUTPUT_SHA256 " + exported.sha256());
            }
            input.verifyUnchanged(); System.out.println("SOURCE_SHA256 " + input.sha256());
        }
    }
}
