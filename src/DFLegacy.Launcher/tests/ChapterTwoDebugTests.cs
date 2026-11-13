using DFLegacy.Launcher;
using System.Security.Cryptography;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

internal static class ChapterTwoDebugTests
{
    internal static void Run(string root,string client,Action<bool,string> check)
    {
        var cursor=new CursorVisibilityLease();int counter=-3;
        int Show(bool up)=>up?++counter:--counter;
        cursor.Update(true,false,Show);check(counter==0,"WPF tool reveals hidden game cursor");
        cursor.Update(true,true,Show);check(counter==0,"WPF cursor hover does not inflate count");
        cursor.Update(false,true,Show);check(counter==-3,"WPF cursor leave balances only own increments");
        cursor.Update(false,true,Show);check(counter==-3,"WPF repeated hide preserves cursor count");
        var path=Path.Combine(client,"Script.pvf");var before=SHA256.HashData(File.ReadAllBytes(path));
        var resources=new ChapterTwoDebugResources(path);
        var coin=resources.Read("stackable/coin.stk");
        var decorated=resources.DecorateNames(coin,1);
        var text=ChapterTwoDebugResources.TextEncoding.GetString(decorated);
        check(text.Contains("复活币 [ID:1]"),"inline Chinese item name gets ID");
        check(text.Contains("Coin [ID:1]"),"secondary item name gets ID");
        var equipment=resources.Read("equipment/character/common/jacket/cloth/sht_ragcloth.equ");
        var label=resources.Field(ChapterTwoDebugResources.TextEncoding.GetString(equipment),"name");
        check(ChapterTwoDebugResources.TextEncoding.GetString(resources.DecorateNames(equipment,10000)).Contains(label+" [ID:10000]"),"localized reference gets its own ID");
        check(resources.DecorateNames(decorated,1).AsSpan().SequenceEqual(decorated),"ID is not appended twice");
        check(resources.Describe("stackable/coin.stk").Description.Contains("复活"),"localized explanation is available");
        var icons=new ChapterTwoDebugIcons(client);
        var icon=icons.Get("Item/IconStackable.img",5);
        check(icon is {PixelWidth:28,PixelHeight:28},"real revive coin icon decodes");
        check(icons.Get("Item/missing.img",0) is null&&icons.Get("Item/IconStackable.img",99999) is null,"missing icons fail safely");
        check(ChapterTwoDebugIcons.Decode(new byte[8],0) is null,"truncated icon rejected");
        var (debugPath,count)=resources.BuildDebugView(Path.Combine(root,"debug"));
        var view=new ChapterTwoDebugResources(debugPath);
        Console.WriteLine($"Debug view: {count} item/shop scripts; source {resources.SourceHash}");
        check(count>1000,"full item directory has debug labels");
        check(view.Read("stackable/coin.stk").AsSpan().SequenceEqual(decorated),"generated archive reads back exact decorated name");
        check(view.Read("string.lst").AsSpan().SequenceEqual(resources.Read("string.lst")),"unrelated resource stays byte identical");
        var unchanged=true;var total=0;
        string WithoutNames(byte[] bytes)=>System.Text.RegularExpressions.Regex.Replace(Encoding.Latin1.GetString(bytes),@"(?m)(^\s*\[name2?\]\s*)(?:`[^`]*`|<\d+::[^>]+>)","$1");
        foreach(var family in new[]{"equipment","stackable"})
            foreach(var pair in ChapterTwoDebugResources.List(resources.Read(family+"/"+family+".lst")))
            {
                var source=resources.Read(family+"/"+pair.Value);var changed=view.Read(family+"/"+pair.Value);
                unchanged&=WithoutNames(source)==WithoutNames(changed);total++;
            }
        check(total==13043&&unchanged,"all 13043 item attributes and unrelated bytes remain identical");
        var shop=ChapterTwoDebugResources.TextEncoding.GetString(view.Read("etc/newcashshop.etc"));
        check(shop.Contains("[ID:1]"),"coin shop override names have underlying item ID");
        var again=resources.BuildDebugView(Path.Combine(root,"debug"));
        check(again.Path==debugPath&&again.Count==count,"validated debug cache is reusable");
        check(SHA256.HashData(File.ReadAllBytes(path)).AsSpan().SequenceEqual(before),"source archive unchanged");
        check(!new LauncherOptions().ChapterTwoDebugEnabled,"normal mode defaults to undecorated source");
    }
    internal static async Task Api(Action<bool,string> check)
    {
        // Exercise the real launch entry before any file/process side effects.
        // This test assembly does not embed the C2 native components, so valid
        // legacy credentials must reach that next prerequisite, not be rejected.
        foreach(var (account,password,valid) in new[]{("legacyqa","123",true),("legacyqa","1",true),("legacyqa","1234567890123456",true),
            ("legacyqa","",false),("legacyqa","12345678901234567",false),("legacyqa","12?3",false),("abc","123",false)})
        {
            try
            {
                await ChapterTwoClient.RunAsync("unused.exe",new LauncherOptions(),new LauncherAccountProfile{Account=account,Password=password},CancellationToken.None);
                check(false,"login respects native field bounds before launch");
            }
            catch(InvalidDataException e)
            {
                check(valid?e.Message.Contains("普通启动组件不完整"):e.Message.Contains("密码"),"login accepts legacy length but rejects empty/oversize/delimiter credentials");
            }
        }
        check(!ChapterTwoClient.IsValidCredential("123",6)&&ChapterTwoClient.IsValidCredential("123456",6),"new password policy still requires six characters");
        var handler=new Handler();using var api=new ChapterTwoDebugApi(handler);
        try{await api.Search("复活");check(false,"anonymous search rejected");}catch(InvalidOperationException){check(handler.Calls==0,"anonymous request never sent");}
        handler.Role=0;
        try{await api.Login("tester","testpass");check(false,"nonadmin rejected");}catch(InvalidOperationException){check(!api.Authenticated,"nonadmin cannot send");}
        handler.Role=1;await api.Login("tester","testpass");
        var items=await api.Search("复活币 & 1");
        check(items is [{Id:1,Name:"复活币"}],"real GM item response shape deserializes");
        check(handler.LastUri.Contains("%26"),"query content escaped");
        handler.EquipmentSearch=true;
        try
        {
            var equipment=await api.Search("光剑");
            check(equipment is [{Id:12345,Name:"测试光剑"}]&&(object?)equipment[0].StackLimit is null,"equipment with absent stack limit remains searchable");
        }
        catch(JsonException){check(false,"equipment with absent stack limit remains searchable");}
        handler.EquipmentSearch=false;
        var characters=await api.Characters();
        var receipt=await api.Send(characters[0],items[0],12,0);
        check(handler.LastBody.Contains("\"quantity\":12")&&handler.LastBody.Contains("\"itemId\":1"),"mail uses selected item and quantity");
        check(handler.LastUri.EndsWith("/api/admin/characters/11111111-1111-1111-1111-111111111111/mail")&&receipt.GetProperty("mailIds").GetArrayLength()==1,"mail targets the exact selected character and retains receipt");
        int before=handler.Calls;
        try{await api.Send(characters[0],items[0] with{ScriptKind="Equipment"},2,0);check(false,"invalid equipment quantity rejected");}catch(ArgumentException){check(handler.Calls==before,"invalid quantity does not send");}
        handler.FailMail=true;
        try{await api.Send(characters[0],items[0],1,0);check(false,"mail error surfaced");}catch(InvalidOperationException){check(handler.Calls==before+1,"failed mail is never auto retried");}
        handler.UnknownMail=true;
        try{await api.Send(characters[0],items[0],1,0);check(false,"unknown mail outcome surfaced");}
        catch(InvalidOperationException e){check(e.Message.Contains("未确认"),"server unknown result warns against duplicate resend");}
        check(handler.AllLocal,"all debug operations stay on isolated loopback");
    }
    private sealed class Handler : HttpMessageHandler
    {
        internal int Calls,Role=1;internal bool FailMail,UnknownMail,EquipmentSearch,AllLocal=true;
        internal string LastUri="",LastBody="";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;LastUri=request.RequestUri!.AbsoluteUri;AllLocal&=request.RequestUri.Host=="127.0.0.2"&&request.RequestUri.Port==48091;
            LastBody=request.Content is null?"":await request.Content.ReadAsStringAsync(token);
            string body;var status=HttpStatusCode.OK;
            if(request.RequestUri.AbsolutePath=="/api/auth/login")body=$"{{\"role\":{Role},\"token\":\"local-test-only\"}}";
            else if(request.Headers.Authorization?.Parameter!="local-test-only"){body="{\"error\":\"login required\"}";status=HttpStatusCode.Unauthorized;}
            else if(request.RequestUri.AbsolutePath=="/api/admin/items")body=EquipmentSearch
                ?"""[{"id":12345,"name":"测试光剑","attachmentKind":1,"scriptKind":"Equipment","inventoryCategory":"Equipment","typeTag":"[weapon]","attachType":"Sealing","stackLimit":null,"durability":35,"scriptPath":"equipment/character/swordman/weapon/beamsword/test.equ"}]"""
                :"""[{"id":1,"name":"复活币","attachmentKind":1,"scriptKind":"Stackable","inventoryCategory":"Consumable","stackLimit":1000,"scriptPath":"stackable/coin.stk"}]""";
            else if(request.RequestUri.AbsolutePath=="/api/admin/characters")body="""[{"id":"11111111-1111-1111-1111-111111111111","name":"测试角色","userName":"tester","online":true}]""";
            else if(UnknownMail){body="{\"status\":3,\"error\":\"write-outcome-unknown\"}";status=HttpStatusCode.ServiceUnavailable;}
            else if(FailMail){body="{\"error\":\"failed\"}";status=HttpStatusCode.BadRequest;}
            else body="{\"mailIds\":[123]}";
            return new(status){Content=new StringContent(body,Encoding.UTF8,"application/json")};
        }
    }
}
