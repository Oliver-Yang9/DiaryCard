using System;
using System.Windows;
namespace DailyDiaryCard;
public sealed partial class MainWindow
{
    public Window CreateStorageSettingsDialog()=>config.IsStandalone
        ?TrackStorageDialog(new StandaloneSetupWindow(config.ConfigPath,ApplyVaultPlan,config,prefs.Theme))
        :CreateVaultSettingsDialog();
    public ModeSetupWindow CreateModeSettingsDialog()
        =>(ModeSetupWindow)TrackStorageDialog(new ModeSetupWindow(config.ConfigPath,ApplyVaultPlan,config,prefs.Theme));
    private Window TrackStorageDialog(Window dialog)
    {
        dialog.Owner=this;dialog.WindowStartupLocation=WindowStartupLocation.CenterOwner;
        settingsDialogs[dialog]=new(false,-1,-1,-1,-1);ApplyDialogAppearance(dialog);
        dialog.SourceInitialized+=(_,_)=>ApplyDialogAppearance(dialog);
        dialog.Closed+=(_,_)=>settingsDialogs.Remove(dialog);return dialog;
    }
    public static void ValidatePlanMappings(VaultPlan plan,System.Collections.Generic.Dictionary<string,string> mappings)
    {
        var probe=new CardConfig{ConfigPath=plan.Config.ConfigPath,PathMappings=new(mappings)};
        foreach(string path in new[]{plan.Config.VaultPath,plan.Config.TemplatePath,plan.Config.DiaryFolder,plan.Config.UserDataPath})
        {
            string actual=probe.ResolvePath(path,System.IO.Path.GetDirectoryName(plan.Config.ConfigPath)!);
            if(!actual.Equals(path,StringComparison.OrdinalIgnoreCase))throw new System.IO.IOException("所选地址命中了已有迁移映射。请改选实际地址后重试：\n"+actual);
        }
    }
    private void ShowLibrary()
    {
        if(!TrySave())return;
        var panel=CreateLibraryDialog();panel.Window.Show();panel.Window.Activate();
    }
    public void OpenWeeklyDocument(string path)
    {
        if(config.IsStandalone)OpenDocumentInApp(path);
        else OpenPath(path);
    }
}