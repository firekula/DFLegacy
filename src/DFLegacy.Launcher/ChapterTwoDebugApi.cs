using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace DFLegacy.Launcher;

internal sealed record DebugItem(ushort Id,string Name,int AttachmentKind,string ScriptKind,string InventoryCategory,int? StackLimit,string ScriptPath)
{ public override string ToString()=>$"{Name}   [ID:{Id}]"; }
internal sealed record DebugCharacter(Guid Id,string Name,string UserName,bool Online)
{ public override string ToString()=>$"{Name} · {UserName} · {(Online?"在线":"离线")}"; }

internal sealed class ChapterTwoDebugApi : IDisposable
{
    private readonly HttpClient http;
    internal bool Authenticated { get; private set; }
    internal ChapterTwoDebugApi(HttpMessageHandler? handler=null)
    {
        http=handler is null?new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false}):new HttpClient(handler);
        http.BaseAddress=new Uri("http://127.0.0.2:48091");http.Timeout=TimeSpan.FromSeconds(12);
    }
    private async Task<JsonElement> Read(HttpResponseMessage response)
    {
        using(response)
        {
            if(response.StatusCode==System.Net.HttpStatusCode.Unauthorized)
            {Authenticated=false;http.DefaultRequestHeaders.Authorization=null;}
            var body=await response.Content.ReadFromJsonAsync<JsonElement>();
            if(!response.IsSuccessStatusCode)
                throw new InvalidOperationException(body.TryGetProperty("error",out var error)?error.GetString():$"请求失败：{(int)response.StatusCode}");
            return body;
        }
    }
    internal async Task Login(string name,string password)
    {
        Authenticated=false;http.DefaultRequestHeaders.Authorization=null;
        var result=await Read(await http.PostAsJsonAsync("/api/auth/login",new{userName=name,password}));
        if(result.GetProperty("role").GetInt32()!=1)throw new InvalidOperationException("发放物品需要管理员账号。");
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",result.GetProperty("token").GetString());
        Authenticated=true;
    }
    private void RequireLogin(){if(!Authenticated)throw new InvalidOperationException("请先登录隔离 GM 管理员账号。");}
    internal async Task<DebugCharacter[]> Characters()
    {
        RequireLogin();var value=await Read(await http.GetAsync("/api/admin/characters"));
        return value.Deserialize<DebugCharacter[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web))??[];
    }
    internal async Task<DebugItem[]> Search(string query)
    {
        RequireLogin();var value=await Read(await http.GetAsync("/api/admin/items?limit=100&query="+Uri.EscapeDataString(query.Trim())));
        return value.Deserialize<DebugItem[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web))??[];
    }
    internal async Task<JsonElement> Send(DebugCharacter character,DebugItem item,uint quantity,ushort ability)
    {
        RequireLogin();
        if(quantity==0||quantity>1000000)throw new ArgumentException("数量范围为 1–1000000。");
        if((item.AttachmentKind!=1||item.ScriptKind!="Stackable")&&quantity!=1)throw new ArgumentException("装备、装扮和宠物每次发送 1 件。");
        using var response=await http.PostAsJsonAsync($"/api/admin/characters/{character.Id}/mail",new
        {
            sender="本机调试",text=$"测试物品 {item.Id} × {quantity}",
            attachments=new[]{new{attachmentKind=item.AttachmentKind,itemId=item.Id,quantity,avatarAbilityIndex=item.AttachmentKind==2?ability:(ushort)0}}
        });
        if((int)response.StatusCode>=500||response.StatusCode==System.Net.HttpStatusCode.RequestTimeout)
            throw new InvalidOperationException("发放结果未确认，请先检查角色邮件，避免重复发放。");
        var receipt=await Read(response);
        if(!receipt.TryGetProperty("mailIds",out var ids)||ids.ValueKind!=JsonValueKind.Array||ids.GetArrayLength()==0)
            throw new InvalidOperationException("发放回执未确认，请先检查角色邮件，避免重复发放。");
        return receipt;
    }
    public void Dispose()=>http.Dispose();
}
