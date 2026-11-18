using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Net.Http;
using DFLegacy.Launcher.Controls;

namespace DFLegacy.Launcher;

internal sealed class ChapterTwoDebugWindow : Window
{
    private readonly ChapterTwoDebugApi api;
    private readonly Task<(ChapterTwoDebugResources Resources,ChapterTwoDebugIcons Icons)> resources;
    private readonly TextBox user=new(){Width=140},search=new(){MinWidth=280},quantity=new(){Text="1",Width=85},ability=new(){Text="0",Width=65};
    private readonly PasswordBox password=new(){Width=140};
    private readonly ComboBox characters=new(){MinWidth=360};
    private readonly ListBox items=new(){MinHeight=130};
    private readonly Image icon=new(){Width=56,Height=56,Stretch=Stretch.Uniform};
    private readonly TextBlock name=new(){FontSize=18,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock description=new(){TextWrapping=TextWrapping.Wrap,LineHeight=22};
    private readonly TextBlock status=new(){TextWrapping=TextWrapping.Wrap};
    private readonly SkinButton send=new(){Content="发送到所选角色的邮件",IsEnabled=false};
    private readonly string logDirectory;
    private int searchVersion,previewVersion;
    private bool pending,disposed;
    private OwnedPanelCursor? pointer;
    private readonly CheckBox testMute=new(){Content="测试静音（仅当前游戏）",MinWidth=185};
    private readonly CheckBox teleport=new(){Content="右键小地图传送（仅同城镇）",MinWidth=205};
    private readonly CheckBox invincible=new(){Content="无敌（仅调试客户端）",MinWidth=160};
    private System.IO.MemoryMappedFiles.MemoryMappedFile? control;
    private System.IO.MemoryMappedFiles.MemoryMappedViewAccessor? controlView;
    private int debugGameId;
    private readonly TextBlock audioStatus=new(){MaxWidth=390,TextWrapping=TextWrapping.Wrap};
    internal bool TestMuted=>testMute.IsChecked==true;
    internal event Action? TestMuteChanged;
    internal ChapterTwoDebugWindow(string clientDirectory,string account,string serviceUrl="http://127.0.0.2:48091")
    {
        api=new(serviceUrl:serviceUrl);
        Title="第二章 · 物品工具";Width=740;Height=730;MinWidth=690;MinHeight=650;
        WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
        ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;
        logDirectory=Path.Combine(clientDirectory,"Logs","DFLegacy");
        resources=Task.Run(()=>(new ChapterTwoDebugResources(Path.Combine(clientDirectory,"Script.pvf")),new ChapterTwoDebugIcons(clientDirectory)));
        var root=new Grid();
        foreach(var height in new[]{GridLength.Auto,GridLength.Auto,GridLength.Auto,new GridLength(1,GridUnitType.Star),new GridLength(146),GridLength.Auto,GridLength.Auto})root.RowDefinitions.Add(new RowDefinition{Height=height});
        var surface=new Border
        {
            Background=new SolidColorBrush(Color.FromRgb(36,39,30)),
            BorderBrush=new SolidColorBrush(Color.FromRgb(98,86,56)),
            BorderThickness=new Thickness(1),
            CornerRadius=new CornerRadius(8),
            Padding=new Thickness(12,9,12,9),
            Child=root
        };
        var frame=new ClassicDialogFrame{Title=Title,Content=surface};
        Content=frame;
        var gold=(Brush)frame.FindResource("ClassicGold");
        var ink=(Brush)frame.FindResource("ClassicInk");
        var buttonStyle=(Style)frame.FindResource("ClassicButton");
        name.Foreground=gold;status.Foreground=gold;audioStatus.Foreground=ink;
        send.Style=buttonStyle;
        Add(root,new TextBlock{Text="物品搜索与邮件发放",Foreground=gold,FontSize=16,Margin=new Thickness(0,0,0,7)},0);
        var login=Row(new TextBlock{Text="GM 账号"},user,new TextBlock{Text="密码"},password);
        var loginButton=new SkinButton{Content="登录",Style=buttonStyle};login.Children.Add(loginButton);Add(root,login,1);
        loginButton.Click+=async(_,_)=>{loginButton.IsEnabled=false;try{await api.Login(user.Text.Trim(),password.Password);password.Clear();characters.ItemsSource=await api.Characters();characters.SelectedItem=((DebugCharacter[])characters.ItemsSource).FirstOrDefault(c=>c.UserName.Equals(account,StringComparison.OrdinalIgnoreCase)&&c.Online);status.Text="已登录。选择收件角色后搜索物品。";}catch(Exception e){status.Text=e.Message;}finally{loginButton.IsEnabled=true;}};
        var controls=new StackPanel();controls.Children.Add(Row(new TextBlock{Text="收件角色"},characters));
        var audioOptions=new LauncherAccountStore(Program.LauncherConfigPath).Load();
        testMute.IsChecked=audioOptions.ChapterTwoTestMuteEnabled;
        controls.Children.Add(Row(testMute,audioStatus));
        controls.Children.Add(Row(teleport,invincible));
        teleport.Click+=(_,_)=>WriteDebugFlags();
        invincible.Click+=(_,_)=>WriteDebugFlags();
        testMute.Click+=(_,_)=>
        {
            try
            {
                new LauncherAccountStore(Program.LauncherConfigPath).Update(o=>o.ChapterTwoTestMuteEnabled=TestMuted);
                TestMuteChanged?.Invoke();
            }
            catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {audioStatus.Text="静音偏好保存失败："+e.Message;}
        };
        var refresh=new SkinButton{Content="刷新角色",Style=buttonStyle};((StackPanel)controls.Children[0]).Children.Add(refresh);
        refresh.Click+=async(_,_)=>{try{var id=(characters.SelectedItem as DebugCharacter)?.Id;characters.ItemsSource=await api.Characters();characters.SelectedItem=((DebugCharacter[])characters.ItemsSource).FirstOrDefault(c=>c.Id==id);}catch(Exception e){status.Text=e.Message;}};
        var find=new SkinButton{Content="搜索名称 / ID",Style=buttonStyle};controls.Children.Add(Row(search,find));Add(root,controls,2);
        find.Click+=async(_,_)=>await Search();search.KeyDown+=async(_,e)=>{if(e.Key==System.Windows.Input.Key.Enter){e.Handled=true;await Search();}};
        items.SetResourceReference(StyleProperty,"ClassicList");
        Add(root,items,3);items.SelectionChanged+=async(_,_)=>await Preview();
        var preview=new Grid{Margin=new Thickness(0,12,0,10)};preview.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(72)});preview.ColumnDefinitions.Add(new ColumnDefinition());
        preview.Children.Add(icon);icon.VerticalAlignment=VerticalAlignment.Top;
        var detail=new StackPanel();detail.Children.Add(name);detail.Children.Add(description);
        var scroll=new ScrollViewer{Content=detail,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(scroll,1);preview.Children.Add(scroll);Add(root,preview,4);
        Add(root,Row(new TextBlock{Text="数量"},quantity,new TextBlock{Text="装扮能力索引"},ability,send),5);
        send.Click+=async(_,_)=>await Send();Add(root,status,6);
        status.Text="Ctrl+F11 显示或隐藏。本机 GM 管理员登录后可发送邮件。";
        Closing+=(_,e)=>{if(!disposed){e.Cancel=true;Hide();}};
        SourceInitialized+=(_,_)=>pointer=new OwnedPanelCursor(this);
        IsVisibleChanged+=(_,_)=>{if(IsVisible) Dispatcher.BeginInvoke(FitToMonitor,DispatcherPriority.Loaded);};
    }
    private void FitToMonitor()
    {
        if(disposed || !IsVisible || WindowWorkArea.Read(this) is not { } work) return;
        var dpi=VisualTreeHelper.GetDpi(this);
        MinWidth=Math.Min(690,work.Width/dpi.DpiScaleX);
        MinHeight=Math.Min(610,work.Height/dpi.DpiScaleY);
        Width=Math.Min(740,work.Width/dpi.DpiScaleX);
        Height=Math.Min(690,work.Height/dpi.DpiScaleY);
        WindowWorkArea.Clamp(this,work);
    }
    private static StackPanel Row(params UIElement[] children)
    {
        var row=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,3,0,6)};
        foreach(var child in children){if(child is FrameworkElement f){f.Margin=new Thickness(0,0,8,0);f.VerticalAlignment=VerticalAlignment.Center;}row.Children.Add(child);}return row;
    }
    private static void Add(Grid grid,UIElement child,int row){Grid.SetRow(child,row);grid.Children.Add(child);}
    private async Task Search()
    {
        int version=++searchVersion;status.Text="正在搜索…";
        try{var result=await api.Search(search.Text);if(version!=searchVersion||disposed)return;items.ItemsSource=result;status.Text=$"找到 {result.Length} 个结果（最多 100 个，可缩小关键词范围）。";if(result.Length>0)items.SelectedIndex=0;}
        catch(Exception e){if(version==searchVersion&&!disposed)status.Text=e.Message;}
    }
    private async Task Preview()
    {
        int version=++previewVersion;send.IsEnabled=false;icon.Source=null;name.Text="";description.Text="";
        if(items.SelectedItem is not DebugItem item)return;
        name.Text=item.ToString();
        try
        {
            var r=await resources;
            var detail=await Task.Run(()=>{lock(r.Resources){var data=r.Resources.Describe(item.ScriptPath);return(data.Description,Icon:r.Icons.Get(data.Icon,data.Frame));}});
            if(version!=previewVersion||disposed)return;
            icon.Source=detail.Icon;
            var stackInfo=item.ScriptKind=="Equipment"?"不可堆叠":item.StackLimit is >0?$"最大堆叠 {item.StackLimit}":"堆叠上限未设置";
            description.Text=$"{item.InventoryCategory} · {stackInfo}\n{(string.IsNullOrWhiteSpace(detail.Description)?"该物品没有介绍文本。":detail.Description)}";
            if(detail.Icon is null)description.Text+="\n（当前资源无可预览图标）";
        }
        catch(Exception e){if(version==previewVersion&&!disposed)description.Text="预览不可用："+e.Message;}
        if(version==previewVersion&&!disposed)send.IsEnabled=api.Authenticated&&!pending;
    }
    private async Task Send()
    {
        if(pending||items.SelectedItem is not DebugItem item||characters.SelectedItem is not DebugCharacter character){status.Text="请选择收件角色和物品。";return;}
        if(!uint.TryParse(quantity.Text,out var count)||count==0||!ushort.TryParse(ability.Text,out var option)){status.Text="请填写有效数量和装扮能力索引。";return;}
        pending=true;send.IsEnabled=false;
        try
        {
            var receipt=await api.Send(character,item,count,option);
            var record=new{time=DateTimeOffset.Now,character=character.Name,characterId=character.Id,itemId=item.Id,itemName=item.Name,quantity=count,receipt};
            status.Text=$"已向 {character.Name} 的邮件发送 {item.Name} [ID:{item.Id}] × {count}。";
            try{Directory.CreateDirectory(logDirectory);await File.AppendAllTextAsync(Path.Combine(logDirectory,"debug-item-mail.jsonl"),System.Text.Json.JsonSerializer.Serialize(record)+Environment.NewLine);}
            catch(Exception e) when(e is IOException or UnauthorizedAccessException){status.Text+=" 发放成功，但本地日志写入失败，请保留邮件回执。";}
        }
        catch(Exception e){status.Text=e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException?"结果未确认，请先检查角色邮件，避免重复发放。 "+e.Message:e.Message;}
        finally{pending=false;if(!disposed)send.IsEnabled=api.Authenticated;}
    }
    internal void Finish(){disposed=true;pointer?.Dispose();pointer=null;api.Dispose();Close();}
    internal void BindGame(int id)
    {
        debugGameId=id;
        controlView?.Dispose();control?.Dispose();controlView=null;control=null;
        if(id<=0)return;
        OpenControl();
    }

    private void OpenControl()
    {
        if(controlView is not null||debugGameId<=0)return;
        try
        {
            control=System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(
                "Local\\DFLegacy.C2.DebugControl",
                System.IO.MemoryMappedFiles.MemoryMappedFileRights.ReadWrite);
            controlView=control.CreateViewAccessor(0,8,
                System.IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite);
            WriteDebugFlags();
        }
        catch(Exception e) when(e is FileNotFoundException or IOException or UnauthorizedAccessException)
        {
            controlView?.Dispose();control?.Dispose();controlView=null;control=null;
        }
    }

    private void WriteDebugFlags()
    {
        if(controlView is null)OpenControl();
        if(controlView is null)return;
        try
        {
            controlView.Write(0,(byte)1);
            controlView.Write(1,(byte)((teleport.IsChecked==true?1:0)|(invincible.IsChecked==true?2:0)));
            controlView.Flush();
        }
        catch(IOException){}
    }

    internal void AudioStatus(string text)=>audioStatus.Text=text;
}

internal sealed class ChapterTwoDebugSession : IDisposable
{
    private readonly DispatcherTimer timer;
    private readonly ChapterTwoDebugWindow window;
    private int gameId;private bool wasDown;
    private ChapterTwoTestAudio? audio;
    private long nextAudio;
    private string lastAudioState="";
    internal ChapterTwoDebugSession(string root,string account,string serviceUrl="http://127.0.0.2:48091")
    {
        window=new(root,account,serviceUrl);window.TestMuteChanged+=()=>{nextAudio=0;UpdateAudio();};
        timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};
        timer.Tick+=(_,_)=>
        {
            if(Environment.TickCount64>=nextAudio)UpdateAudio();
            bool down=(GetAsyncKeyState(0x11)&0x8000)!=0&&(GetAsyncKeyState(0x7a)&0x8000)!=0;
            var foreground=GetForegroundWindow();GetWindowThreadProcessId(foreground,out uint process);
            if(gameId>0&&down&&!wasDown&&(process==gameId||foreground==new WindowInteropHelper(window).Handle))
            {
                if(window.IsVisible)window.Hide();else
                {
                    if(process==gameId)new WindowInteropHelper(window).Owner=foreground;
                    window.Show();window.Activate();
                }
            }
            wasDown=down;
        };timer.Start();
    }
    private void UpdateAudio()
    {
        nextAudio=Environment.TickCount64+1000;
        if(audio is null)return;
        try
        {
            var count=audio.SetMuted(window.TestMuted);
            var state=$"enabled={window.TestMuted} sessions={count}";
            if(state!=lastAudioState){Program.WriteLauncherLog($"Chapter Two test audio process={gameId} {state}.");lastAudioState=state;}
            window.AudioStatus(window.TestMuted?(count>0?"当前游戏音量为 0":"等待游戏声音初始化…"):"使用游戏声音设置");
        }
        catch(Exception e) when(e is COMException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {window.AudioStatus("声音控制暂不可用，将重试");}
    }
    internal void Bind(int id)
    {
        if(gameId==id&&audio is not null)return;
        audio?.Dispose();gameId=id;window.BindGame(id);audio=ChapterTwoTestAudio.TryCreate(id);nextAudio=0;UpdateAudio();
    }
    public void Dispose(){timer.Stop();audio?.Dispose();audio=null;window.Finish();}
    [DllImport("user32.dll")]private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(nint window,out uint process);
}

