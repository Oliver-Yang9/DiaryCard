using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Win32;
namespace DailyDiaryCard;
public sealed class MemoryStartupStore:IStartupStore
{
    public string? Value;
    public bool FailWrites;
    public string? Read()=>Value;
    public void Write(string command){if(FailWrites)throw new UnauthorizedAccessException("测试模拟：没有写入权限");Value=command;}
    public void Delete(){if(FailWrites)throw new UnauthorizedAccessException("测试模拟：没有删除权限");Value=null;}
}
public static class StartupTests
{
    public static int Run(string root)
    {
        Directory.CreateDirectory(root);var results=new List<object>();int failed=0;string exe=Environment.ProcessPath!;
        string config=Path.Combine(root,"中文 空格","diary-card.json");Directory.CreateDirectory(Path.GetDirectoryName(config)!);File.WriteAllText(config,"{}");
        void Check(bool value){if(!value)throw new Exception("断言失败");}
        void Test(string name,Action action){try{action();results.Add(new{name,passed=true});}catch(Exception e){failed++;results.Add(new{name,passed=false,error=e.ToString()});}}
        var live=new WindowsStartupStore();string? before=live.Read();
        Test("默认没有启动项，读取设置不创建启动项",()=>{var store=new MemoryStartupStore();Check(!StartupService.EnabledHere(exe,config,store)&&store.Value==null);});
        Test("中文空格路径启动命令正确引用程序与配置",()=>{string command=StartupService.Command(exe,config);Check(command=="\""+Path.GetFullPath(exe)+"\" --config \""+Path.GetFullPath(config)+"\"");});
        Test("主动开启后可以读取并关闭自己的启动项",()=>{var store=new MemoryStartupStore();StartupService.SetEnabled(exe,config,true,store);Check(StartupService.EnabledHere(exe,config,store));StartupService.SetEnabled(exe,config,false,store);Check(store.Value==null);});
        Test("关闭当前版本不会删除另一个版本的启动项",()=>{var store=new MemoryStartupStore{Value="another-version --config other.json"};StartupService.SetEnabled(exe,config,false,store);Check(store.Value=="another-version --config other.json");});
        Test("明确开启当前版本时替换旧位置，保持单一入口",()=>{var store=new MemoryStartupStore{Value="another-version"};StartupService.SetEnabled(exe,config,true,store);Check(store.Value==StartupService.Command(exe,config));});
        Test("缺少程序或首次配置时拒绝登记",()=>{var store=new MemoryStartupStore();bool denied=false;try{StartupService.SetEnabled(Path.Combine(root,"missing.exe"),config,true,store);}catch(IOException){denied=true;}Check(denied&&store.Value==null);denied=false;try{StartupService.SetEnabled(exe,Path.Combine(root,"missing.json"),true,store);}catch(IOException){denied=true;}Check(denied&&store.Value==null);});
        Test("写入失败不伪报成功，不改变旧设置",()=>{var store=new MemoryStartupStore{Value="old",FailWrites=true};bool denied=false;try{StartupService.SetEnabled(exe,config,true,store);}catch(UnauthorizedAccessException){denied=true;}Check(denied&&store.Value=="old");});
        Test("真实注册表接口在隔离测试键完成开启与关闭",()=>{
            string path=@"Software\DiaryCard\Tests\"+Guid.NewGuid().ToString("N");
            if(!path.StartsWith(@"Software\DiaryCard\Tests\",StringComparison.Ordinal))throw new IOException("测试地址校验失败");
            try{var store=new WindowsStartupStore(path);StartupService.SetEnabled(exe,config,true,store);Check(StartupService.EnabledHere(exe,config,store));StartupService.SetEnabled(exe,config,false,store);Check(store.Read()==null);}
            finally{Registry.CurrentUser.DeleteSubKeyTree(path,false);}
        });
        Test("测试结束后真实Windows开机启动入口保持原样",()=>Check(live.Read()==before));
        DiarySession.AtomicText(Path.Combine(root,"startup-result.json"),JsonSerializer.Serialize(new{passed=results.Count-failed,failed,productionStartupUnchanged=live.Read()==before,results},CardConfig.Json));return failed==0?0:1;
    }
}