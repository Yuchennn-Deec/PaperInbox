using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PaperInbox {
  public sealed class TrayContext : ApplicationContext {
    public readonly StateStore Store;
    public readonly AppState State;
    public readonly InboxForm Form;
    Tracker tracker;
    readonly BridgeClient client;
    readonly NotifyIcon tray;
    readonly System.Windows.Forms.Timer timer;
    DownloadMonitor monitor;
    bool running, exiting, analyzing, scanning;
    long nextBridgeCheck;
    public bool IsAnalyzing {get{return analyzing;}}
    public string ConnectionText="正在检查 Zotero 连接…";
    public string MonitorText="";
    public TrayContext(string stateDir,bool testMode) {
      Store=new StateStore(stateDir);State=Store.Load();Store.GetBridgeConfig();
      tracker=new Tracker(State);client=new BridgeClient(Store);monitor=new DownloadMonitor(State.Folder,!testMode);
      Form=new InboxForm(this);
      if(!testMode) {
        tray=new NotifyIcon();tray.Icon=System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        tray.Text="论文收件箱";tray.Visible=true;
        ContextMenuStrip menu=new ContextMenuStrip();
        menu.Items.Add("打开待处理列表",null,(s,e)=>ShowWindow());
        menu.Items.Add("暂停 / 恢复下载检测",null,(s,e)=>{State.Enabled=!State.Enabled;Store.Save(State);Refresh();});
        menu.Items.Add("退出程序",null,(s,e)=>Quit());
        tray.ContextMenuStrip=menu;tray.DoubleClick+=(s,e)=>ShowWindow();tray.BalloonTipClicked+=(s,e)=>ShowWindow();
        timer=new System.Windows.Forms.Timer();timer.Interval=500;timer.Tick+=async(s,e)=>await Cycle();timer.Start();
        Form.Shown+=async(s,e)=>await Cycle();
        ShowWindow();
      }
      Refresh();
    }
    public void ShowWindow() {if(Form.IsDisposed)return;Form.Render();Form.Show();if(Form.WindowState==FormWindowState.Minimized)Form.WindowState=FormWindowState.Normal;Form.Activate();}
    public void Refresh() {
      if(Form.Visible)Form.Render();
      if(tray!=null)tray.Text="论文收件箱 · 待处理 "+State.Pending.Count.ToString()+(State.Enabled?"":" · 已暂停检测");
    }
    public void Approve(IList<string> ids) {
      foreach(PendingFile file in State.Pending.Where(p=>ids.Contains(p.Id))) {
        if(file.Analysis!=null&&(file.Status=="Review"||file.Status=="Error")) {
          if(file.Status=="Error")file.Id=Guid.NewGuid().ToString("N");
          MaterialThemes.Remember(State,file.Analysis);file.Status="Approved";file.Submitted=false;file.Message="分类已确认，等待 Zotero 入库并打开 PDF。";
        }
      }
      Store.Save(State);Refresh();StartZoteroIfNeeded();nextBridgeCheck=0;Cycle().ContinueWith(t=>{});
    }
    public async Task Analyze(IList<string> ids) {
      if(analyzing)return;
      ApiProfile profile;string provider;
      try {AiConfiguration config=new AiSettingsStore(Store).Load();profile=config.Current;provider=config.Selected;AiClient.ValidateProfile(profile);}
      catch(Exception error){MessageBox.Show(Form,error.Message,"请配置 API");using(ApiSettingsForm settings=new ApiSettingsForm(Store))settings.ShowDialog(Form);return;}
      analyzing=true;StartZoteroIfNeeded();Refresh();
      try {
        foreach(PendingFile file in State.Pending.Where(p=>ids.Contains(p.Id)&&(p.Status=="Pending"||p.Status=="Error"||p.Status=="Review")).ToList()) {
          if(exiting)break;string originalId=file.Id,originalFingerprint=file.Fingerprint;
          file.Status="Analyzing";file.Analysis=null;file.Message="正在提取 PDF 文字；尚未入库。";Store.Save(State);Refresh();
          try {
            bool connected=false;
            for(int attempt=0;attempt<20;attempt++) {
              try {BridgeStatus status=await client.Status(new string[0]);if(!BridgeClient.SupportsTopicRouting(status))throw new InvalidDataException("请安装 0.5.0 连接插件后再使用 API 分析。");connected=status.ready;if(connected)break;}
              catch(BridgeUnavailable){if(attempt==19)throw;}
              if(!connected)await Task.Delay(1500);
              if(exiting)return;
            }
            if(!connected)throw new BridgeUnavailable("Zotero 未就绪，请启动后重试。");
            ExtractResponse extracted=await client.Extract(file,profile.MaxPages,profile.MaxCharacters);
            file.Message="正在调用 "+provider+" 分析内容；尚未入库。";Refresh();
            AnalysisResult result=await new AiClient().Analyze(profile,provider,extracted);
            if(file.Id!=originalId||file.Fingerprint!=originalFingerprint)throw new InvalidDataException("文件在分析期间发生变化，请重新分析。");
            file.Analysis=result;file.Status="Review";file.Message="API 分析完成，请查看结果并确认是否放入 Zotero。";
          }catch(Exception error){if(file.Id==originalId){file.Status="Error";file.Message=error.Message;}}
          Store.Save(State);if(!exiting)Refresh();
        }
      }finally{analyzing=false;if(!exiting){Store.Save(State);Refresh();}}
    }
    public void Review(IList<string> ids) {
      PendingFile file=State.Pending.FirstOrDefault(p=>ids.Contains(p.Id)&&p.Analysis!=null&&(p.Status=="Review"||p.Status=="Error"));if(file==null)return;
      AnalysisResult copy=Store.Json.Deserialize<AnalysisResult>(Store.Json.Serialize(file.Analysis));
      using(ReviewForm dialog=new ReviewForm(copy,State.MaterialThemes,async()=>{CollectionCatalog catalog=await client.Collections();return catalog.collections??new string[0];}))if(dialog.ShowDialog(Form)==DialogResult.OK){file.Analysis=dialog.Result;Approve(new[]{file.Id});}
    }
    void StartZoteroIfNeeded() {
      if(Process.GetProcessesByName("zotero").Length>0)return;
      if(!File.Exists(State.ZoteroPath)) {ConnectionText="找不到 Zotero，请在设置中选择 Zotero 程序。";Refresh();return;}
      try {Process.Start(new ProcessStartInfo(State.ZoteroPath){UseShellExecute=true});}
      catch(Exception error){ConnectionText="无法启动 Zotero："+error.Message;Refresh();}
    }
    public void Ignore(IList<string> ids) {
      foreach(PendingFile file in State.Pending.Where(p=>ids.Contains(p.Id)&&(p.Status=="Pending"||p.Status=="Error"||p.Status=="Review")).ToList()) {
        History(file.Name,"已忽略，原文件保留。");State.Pending.Remove(file);
      }
      Store.Save(State);Refresh();
    }
    public void History(string name,string message) {
      State.History.Insert(0,new HistoryEntry{Name=name,Message=message,Time=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")});
      if(State.History.Count>100)State.History.RemoveRange(100,State.History.Count-100);
    }
    public Task Cycle() {return Task.WhenAll(ScanDownloads(),PollBridge());}
    public async Task ScanDownloads() {
      if(exiting||scanning)return;
      if(!State.Enabled){if(MonitorText!="下载检测已暂停；已确认的任务仍会继续。"){MonitorText="下载检测已暂停；已确认的任务仍会继续。";Refresh();}return;}
      long now=DownloadFile.UnixMilliseconds(DateTime.UtcNow);DownloadMonitor current=monitor;
      if(!current.Due(now))return;
      scanning=true;
      try {
        // Disk IO and PDF readiness checks must not block the UI or wait for Zotero.
        List<DownloadFile> files=await Task.Run(()=>current.Scan());
        if(exiting||!object.ReferenceEquals(current,monitor))return;
        List<PendingFile> added=tracker.Observe(files,DownloadFile.UnixMilliseconds(DateTime.UtcNow));
        Store.Save(State);string message="正在监测："+State.Folder;
        bool changed=MonitorText!=message||tracker.PendingChanged;MonitorText=message;
        if(added.Count>0) {
          if(State.Popup)ShowWindow();
          if(tray!=null){tray.BalloonTipTitle="发现新 PDF，要识别并分类吗？";tray.BalloonTipText="新增 "+added.Count.ToString()+" 个 PDF。确认前不会导入 Zotero。";tray.ShowBalloonTip(5000);}
        }
        if(changed)Refresh();
      }catch(Exception error){if(!exiting){MonitorText="下载目录暂不可读："+error.Message;Refresh();}}
      finally{scanning=false;}
    }
    async Task PollBridge() {
      long now=DownloadFile.UnixMilliseconds(DateTime.UtcNow);
      if(exiting||running||now<nextBridgeCheck)return;running=true;
      List<PendingFile> approved=State.Pending.Where(p=>p.Status=="Approved"||p.Status=="Importing").ToList();
      nextBridgeCheck=now+(approved.Count>0?500:8000);string previousConnection=ConnectionText;
      try {
        BridgeStatus status=await client.Status(approved.Select(p=>p.Id).ToArray());
        if(exiting)return;
        if(!BridgeClient.SupportsTopicRouting(status))throw new BridgeUnavailable("请安装 0.5.0 连接插件，以按自定义Prompt归类并打开文章。");
        ConnectionText="Zotero 已连接 · Prompt分类与历史选择 · 连接插件 "+status.version;
        foreach(PendingFile file in approved) {
          ServerJob job=null;if(status.requests!=null)status.requests.TryGetValue(file.Id,out job);
          if(job==null) {
            try {SubmitResponse response=await client.Submit(file,State.AutoOpen);job=response.request;file.Submitted=true;}
            catch(InvalidDataException error){file.Status="Error";file.Message=error.Message;continue;}
          }
          if(job==null)continue;
          if(job.status=="done"){History(file.Name,job.message);State.Pending.Remove(file);}
          else if(job.status=="error"){file.Status="Error";file.Message=job.message;file.Submitted=false;}
          else{file.Status="Importing";file.Message="Zotero 正在按确认的分类入库并打开 PDF…";}
        }
        Store.Save(State);
      }catch(BridgeUnavailable error){ConnectionText=error.Message;}
      catch(Exception error){ConnectionText="处理遇到问题，任务已保留："+error.Message;}
      finally{running=false;if(!exiting&&(ConnectionText!=previousConnection||approved.Count>0))Refresh();}
    }
    public void SetFolder(string folder) {
      if(!Directory.Exists(folder))throw new DirectoryNotFoundException("目录不存在。");
      monitor.Dispose();State.Folder=Path.GetFullPath(folder);State.Initialized=false;State.Known.Clear();tracker=new Tracker(State);monitor=new DownloadMonitor(State.Folder);Store.Save(State);Refresh();
    }
    public void Quit() {
      if(exiting)return;exiting=true;Store.Save(State);
      if(timer!=null)timer.Stop();monitor.Dispose();if(tray!=null){tray.Visible=false;tray.Dispose();}
      Form.AllowClose=true;Form.Close();client.Dispose();ExitThread();
    }
    public void DisposeForTest() {exiting=true;monitor.Dispose();Form.AllowClose=true;Form.Dispose();client.Dispose();}
  }
  internal static class Program {
    [STAThread]
    public static int Main(string[] args) {
      Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
      SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
      if(args.Length==2&&args[0]=="--self-test")return SelfTests.Run(args[1]);
      bool first;
      using(Mutex mutex=new Mutex(true,"Local\\PaperInboxTray-"+Environment.UserName,out first)) {
        if(!first){MessageBox.Show("论文收件箱已在运行，请从系统托盘打开。","论文收件箱");return 0;}
        try {using(TrayContext context=new TrayContext(Locations.StateFolder(),false))Application.Run(context);}
        catch(Exception error){MessageBox.Show("论文收件箱无法启动："+error.Message,"论文收件箱",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
      }
      return 0;
    }
  }
}
