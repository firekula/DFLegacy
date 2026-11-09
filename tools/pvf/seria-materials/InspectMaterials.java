import java.nio.file.*;
import java.nio.charset.*;
import java.util.*;
import java.util.regex.*;
import studio.pvf.core.*;
class InspectMaterials {
 static final Charset GBK=Charset.forName("GBK");
 static String clean(String s){return s.replaceAll("(?m)//[^\\r\\n]*", "");}
 static String field(String s,String tag){var m=Pattern.compile("(?im)^\\s*\\["+Pattern.quote(tag)+"\\]\\s+(?:`([^`]*)`|(<[^>]+>)|([^\\s]+))").matcher(clean(s));return m.find()? (m.group(1)!=null?m.group(1):m.group(2)!=null?m.group(2):m.group(3)):"";}
 static String q(String s){return "\""+s.replace("\\","\\\\").replace("\"","\\\"").replace("\r","\\r").replace("\n","\\n").replace("\t","\\t")+"\"";}
 public static void main(String[] args)throws Exception{
  Path out=Path.of(args[1]);
  try(var a=PvfArchive.open(Path.of(args[0]))){
   Set<String> paths=new HashSet<>();for(var e:a.entries())paths.add(e.path());
   Map<String,String> strings=new HashMap<>();
   var sl=Pattern.compile("(?m)^\\s*(\\d+)\\s+`([^`]+)`").matcher(clean(new String(a.readBytes("string.lst"),GBK)));
   while(sl.find()){
    String prefix=sl.group(1)+"::",path=sl.group(2).toLowerCase(Locale.ROOT);
    for(String line:new String(a.readBytes(path),GBK).split("\\r?\\n")){int p=line.indexOf('>');if(p>0&&!line.trim().startsWith("//"))strings.put(prefix+line.substring(0,p).trim(),line.substring(p+1).trim());}
   }
   List<String> rows=new ArrayList<>();List<String> missing=new ArrayList<>();Map<String,Integer> types=new TreeMap<>();
   var m=Pattern.compile("(?m)^\\s*(\\d+)\\s+`([^`]+)`").matcher(clean(new String(a.readBytes("stackable/stackable.lst"),GBK)));
   int total=0,positive=0,unpriced=0;
   while(m.find()){
    total++;String id=m.group(1),path=m.group(2).replace('\\','/').toLowerCase(Locale.ROOT);if(!path.startsWith("stackable/"))path="stackable/"+path;
    if(!paths.contains(path)){missing.add(id+" "+path);continue;}
    String s=new String(a.readBytes(path),GBK),type=field(s,"stackable type");types.merge(type,1,Integer::sum);
    if(!type.equals("[material]")&&!type.equals("[material expert job]"))continue;
    String name=field(s,"name");if(name.startsWith("<")&&name.endsWith(">"))name=strings.getOrDefault(name.substring(1,name.length()-1),name);
    String price=field(s,"price");boolean priced=price.matches("[+]?[0-9]+")&&Long.parseLong(price)>0;
    if(priced)positive++;else unpriced++;
    rows.add("{\"id\":"+id+",\"name\":"+q(name)+",\"path\":"+q(path)+",\"type\":"+q(type)+",\"price\":"+(price.isEmpty()?"null":price)+",\"stackLimit\":"+q(field(s,"stack limit"))+",\"rarity\":"+q(field(s,"rarity"))+",\"attachType\":"+q(field(s,"attach type"))+"}");
   }
   Files.writeString(out.resolve("materials.json"),"[\n"+String.join(",\n",rows)+"\n]",StandardCharsets.UTF_8);
   Files.writeString(out.resolve("missing-stackable-scripts.txt"),String.join("\n",missing),StandardCharsets.UTF_8);
   for(String path:List.of("itemshop/equipmentshop7.shp","itemshop/stackableshop1.shp","itemshop/itemshop.lst","npc/seria.npc"))Files.writeString(out.resolve(path.replace('/','_')+".txt"),new String(a.readBytes(path),GBK),StandardCharsets.UTF_8);
   int max=0;String maxPath="";
   for(var e:a.entries())if(e.path().startsWith("itemshop/")&&e.path().endsWith(".shp")){
    String s=clean(new String(a.readBytes(e),GBK));var block=Pattern.compile("\\[sell item\\]([\\s\\S]*?)\\[/sell item\\]").matcher(s);int count=0;
    if(block.find()){var numbers=Pattern.compile("-?\\d+").matcher(block.group(1));while(numbers.find())count++;}
    if(count>max){max=count;maxPath=e.path();}System.out.println("SHOP "+e.path()+" slots="+count);
   }
   a.verifyUnchanged();System.out.println("SHA256 "+a.sha256()+" entries="+a.fileCount()+" stackables="+total+" materials="+rows.size()+" positive="+positive+" unpriced="+unpriced+" missing="+missing.size());System.out.println("TYPES "+types);System.out.println("MAXSHOP "+maxPath+" "+max);
  }
 }
}
