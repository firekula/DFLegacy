using System.Windows;
using System.Windows.Controls;
using DFLegacy.Launcher.Updates;
using DFLegacy.Launcher.Views;

namespace DFLegacy.Launcher;

public partial class MainWindow
{
    private void AddChapterTransitionMenu()
    {
        try
        {
            if(ChapterTwoClient.IsBundled&&ChapterUpdatePolicy.IsManaged(AppContext.BaseDirectory))
            {
                var check=new MenuItem{Header="检查更新"};check.Click+=CheckUpdates_Click;
                MoreServicesButton.ContextMenu.Items.Add(check);
            }
            if(ChapterTransitionOffer.Read(AppContext.BaseDirectory) is not {} offer)return;
            var item=new MenuItem{Header=offer.Enrollment?"准备章节更新…":offer.Rollback?"回退第一章…":"升级第二章…"};
            item.Click+=(_,_)=>
            {
                updateCoordinator?.Dispose();updateCoordinator=null;
                new ChapterTransitionWindow(AppContext.BaseDirectory,offer){Owner=this}.ShowDialog();
            };
            MoreServicesButton.ContextMenu.Items.Add(item);
        }
        catch(Exception ex){Program.ReportError("chapter-transition-menu",ex);}
    }
}
