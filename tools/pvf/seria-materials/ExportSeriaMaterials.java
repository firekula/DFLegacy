import java.nio.file.*;
import java.nio.charset.*;
import java.util.*;
import java.util.regex.*;
import studio.pvf.core.*;
class ExportSeriaMaterials {
 static final String EXPECTED="c2321f05953ae4484dc6d0b10db50f83c3251c4967cdd0859376d4f8acdf27f4";
 static final String SHOP="itemshop/equipmentshop7.shp";
 static final Charset GBK=Charset.forName("GBK"),BYTES=StandardCharsets.ISO_8859_1;
 static void require(boolean ok,String message){if(!ok)throw new IllegalStateException(message);}
 static String clean(String s){return s.replaceAll("(?m)//[^\\r\\n]*", "");}
 static String field(String s,String tag){var m=Pattern.compile("(?im)^\\s*\\["+Pattern.quote(tag)+"\\]\\s+(?:`([^`]*)`|(<[^>]+>)|([^\\s]+))").matcher(clean(s));return m.find()? (m.group(1)!=null?m.group(1):m.group(2)!=null?m.group(2):m.group(3)):"";}
 static List<Integer> slots(String s){var b=Pattern.compile("\\[sell item\\]([\\s\\S]*?)\\[/sell item\\]").matcher(clean(s));require(b.find(),"Missing item block");var nums=Pattern.compile("-?\\d+").matcher(b.group(1));var list=new ArrayList<Integer>();while(nums.find())list.add(Integer.parseInt(nums.group()));require(!b.find(),"Duplicate item block");return list;}
 public static void main(String[] args)throws Exception{
  require(args.length==2,"Usage source.pvf new-output.pvf");Path source=Path.of(args[0]).toRealPath(),output=Path.of(args[1]).toAbsolutePath().normalize();
  require(!source.equals(output)&&!Files.exists(output),"Output must be new and separate");
  try(var input=PvfArchive.open(source)){
   require(input.sha256().equals(EXPECTED),"Source checksum mismatch");
   Set<Integer> seen=new HashSet<>();SortedSet<Integer> priced=new TreeSet<>(),pending=new TreeSet<>();
   var items=Pattern.compile("(?m)^\\s*(\\d+)\\s+`([^`]+)`").matcher(clean(new String(input.readBytes("stackable/stackable.lst"),GBK)));
   while(items.find()){
    int id=Integer.parseInt(items.group(1));require(id>0&&id<=65535&&seen.add(id),"Unsupported or duplicate item ID "+id);
    String path=items.group(2).replace('\\','/').toLowerCase(Locale.ROOT);if(!path.startsWith("stackable/"))path="stackable/"+path;
    String s=new String(input.readBytes(path),GBK),type=field(s,"stackable type");
    if(!type.equals("[material]")&&!type.equals("[material expert job]"))continue;
    String price=field(s,"price");if(price.matches("[+]?[0-9]+")&&Long.parseLong(price)>0)priced.add(id);else pending.add(id);
   }
   require(priced.size()==201&&pending.size()==63,"Material inventory changed");
   byte[] original=input.readBytes(SHOP);String old=new String(original,BYTES);var oldSlots=slots(old);
   require(oldSlots.size()==98,"Original Seria layout changed");var additional=new TreeSet<>(priced);additional.removeAll(oldSlots);
   require(additional.size()==201,"Unexpected original material overlap");
   String newline=old.contains("\r\n")?"\r\n":"\n";
   StringBuilder block=new StringBuilder("// DFLegacy: materials with existing positive PVF prices; unpriced entries are pending."+newline);
   int n=0;for(int id:additional){block.append(id).append((++n%7==0)?newline:"\t");}
   while(n%7!=0){block.append(-1).append((++n%7==0)?newline:"\t");}
   int end=old.indexOf("[/sell item]");require(end>=0&&end==old.lastIndexOf("[/sell item]"),"Ambiguous close tag");
   String next=old.substring(0,end)+block+old.substring(end);
   var nextSlots=slots(next);require(nextSlots.subList(0,oldSlots.size()).equals(oldSlots),"Existing layout changed");
   Set<Integer> actualNew=new HashSet<>(nextSlots.subList(oldSlots.size(),nextSlots.size()));actualNew.remove(-1);require(actualNew.equals(priced),"Added IDs mismatch");
   require(Collections.disjoint(nextSlots,pending),"Pending price item would be free in store");
   require(nextSlots.size()%7==0&&nextSlots.size()==301,"Unexpected row alignment");
   byte[] changed=next.getBytes(BYTES);PvfWriter.export(input,Map.of(SHOP,changed),output);
   try(var reopened=PvfArchive.open(output)){
    require(reopened.fileCount()==input.fileCount(),"Entry count changed");int differences=0;
    for(var entry:input.entries()){
     byte[] before=input.readBytes(entry),after=reopened.readBytes(entry.path());
     require(Arrays.equals(after,entry.path().equals(SHOP)?changed:before),"Unexpected contents: "+entry.path());if(!Arrays.equals(before,after))differences++;
    }
    require(differences==1,"Expected only Seria shop to change");input.verifyUnchanged();
    String proof="{\n  \"sourceSha256\":\""+input.sha256()+"\",\n  \"outputSha256\":\""+reopened.sha256()+"\",\n  \"entriesChecked\":"+input.fileCount()+",\n  \"changedEntries\":[\""+SHOP+"\"],\n  \"materialsWithPriceAdded\":"+additional.size()+",\n  \"unpricedMaterialsExcluded\":"+pending.size()+",\n  \"originalSlotsPreserved\":"+oldSlots.size()+",\n  \"resultSlotsIncludingEmpty\":"+nextSlots.size()+",\n  \"sourceUnchanged\":true,\n  \"inGameVerified\":false\n}\n";
    Files.writeString(output.resolveSibling(output.getFileName()+".verification.json"),proof,StandardCharsets.UTF_8,StandardOpenOption.CREATE_NEW);
    System.out.print(proof);
   }
  }
 }
}
