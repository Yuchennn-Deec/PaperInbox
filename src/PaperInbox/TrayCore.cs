using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace PaperInbox {
  public sealed class DownloadFile {
    public string Path {get;set;}
    public string Name {get;set;}
    public long Size {get;set;}
    public long LastModified {get;set;}
    public string Fingerprint {get {return Size.ToString()+":"+LastModified.ToString();}}
    public static DownloadFile FromPath(string path) {
      FileInfo info=new FileInfo(path);
      return new DownloadFile {Path=info.FullName,Name=info.Name,Size=info.Length,LastModified=UnixMilliseconds(info.LastWriteTimeUtc)};
    }
    public static long UnixMilliseconds(DateTime value) {return (value.ToUniversalTime().Ticks-621355968000000000L)/10000L;}
    public static string Key(string path) {return System.IO.Path.GetFullPath(path).ToUpperInvariant();}
    public static bool IsCompletePDF(string path) {
      try {
        using(FileStream stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
          if(stream.Length<12)return false;
          byte[] head=new byte[Math.Min(1024,(int)Math.Min(stream.Length,1024))];
          stream.Read(head,0,head.Length);
          stream.Seek(Math.Max(0,stream.Length-4096),SeekOrigin.Begin);
          byte[] tail=new byte[(int)Math.Min(4096,stream.Length)];stream.Read(tail,0,tail.Length);
          return Encoding.ASCII.GetString(head).Contains("%PDF-")&&Encoding.ASCII.GetString(tail).Contains("%%EOF");
        }
      }catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
    }
  }
  public sealed class PendingFile {
    public string Id {get;set;}
    public string Path {get;set;}
    public string Name {get;set;}
    public long Size {get;set;}
    public long LastModified {get;set;}
    public string Status {get;set;}
    public string Message {get;set;}
    public bool Submitted {get;set;}
    public AnalysisResult Analysis {get;set;}
    public string Fingerprint {get {return Size.ToString()+":"+LastModified.ToString();}}
  }
  public sealed class HistoryEntry {
    public string Name {get;set;}
    public string Message {get;set;}
    public string Time {get;set;}
  }
  public sealed class AppState {
    public int Schema {get;set;}
    public bool Initialized {get;set;}
    public bool Enabled {get;set;}
    public bool Popup {get;set;}
    public bool AutoOpen {get;set;}
    public string Folder {get;set;}
    public string ZoteroPath {get;set;}
    public Dictionary<string,string> Known {get;set;}
    public List<PendingFile> Pending {get;set;}
    public List<HistoryEntry> History {get;set;}
    public List<MaterialTheme> MaterialThemes {get;set;}
    public AppState() {
      Schema=1;Enabled=true;Popup=true;AutoOpen=true;Folder=Locations.Downloads();
      ZoteroPath=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Zotero","zotero.exe");
      Known=new Dictionary<string,string>();Pending=new List<PendingFile>();History=new List<HistoryEntry>();MaterialThemes=new List<MaterialTheme>();
    }
  }
  public static class Locations {
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]
    static extern int SHGetKnownFolderPath(ref Guid rfid,uint flags,IntPtr token,out IntPtr path);
    public static string Downloads() {
      IntPtr pointer=IntPtr.Zero;
      try {
        Guid id=new Guid("374DE290-123F-4565-9164-39C4925E467B");
        if(SHGetKnownFolderPath(ref id,0,IntPtr.Zero,out pointer)==0)return Marshal.PtrToStringUni(pointer);
      }catch {}finally{if(pointer!=IntPtr.Zero)Marshal.FreeCoTaskMem(pointer);}
      return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),"Downloads");
    }
    public static string StateFolder() {return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PaperInbox");}
  }
  public sealed class StateStore {
    public string Folder {get;private set;}
    public JavaScriptSerializer Json {get;private set;}
    string lastSaved="";
    public StateStore(string folder) {Folder=folder;Directory.CreateDirectory(folder);Json=new JavaScriptSerializer{MaxJsonLength=16*1024*1024};}
    public AppState Load() {
      string path=System.IO.Path.Combine(Folder,"state.json");
      if(!File.Exists(path))return new AppState();
      try {
        AppState state=Json.Deserialize<AppState>(File.ReadAllText(path,Encoding.UTF8));
        if(state==null||state.Schema!=1||state.Known==null||state.Pending==null||state.History==null)throw new InvalidDataException("状态格式不正确");
        bool legacy=state.Pending.Any(p=>p.Analysis!=null&&p.Analysis.classificationVersion!=FormulaCatalog.Version&&(p.Status=="Review"||p.Status=="Approved"));
        if(legacy&&!File.Exists(path+".before-formula-v1"))File.Copy(path,path+".before-formula-v1");
        if(state.MaterialThemes==null)state.MaterialThemes=new List<MaterialTheme>();
        bool topicMigration=state.Pending.Any(p=>p.Analysis!=null&&(p.Status=="Review"||p.Status=="Error")&&MaterialThemes.CanUpgrade(p.Analysis));
        if(topicMigration&&!File.Exists(path+".before-flat-topics-v4"))File.Copy(path,path+".before-flat-topics-v4");
        foreach(PendingFile file in state.Pending) {
          if(file.Status=="Analyzing")file.Status="Pending";
          if(file.Status=="Importing")file.Status="Approved";
          if(file.Status=="Approved"&&file.Analysis==null){file.Status="Pending";file.Message="请先进行 API 分析并确认入库结果。";}
          if(file.Analysis!=null&&file.Analysis.classificationVersion!=FormulaCatalog.Version&&(file.Status=="Review"||file.Status=="Approved")){file.Status="Pending";file.Analysis=null;file.Message="分类已升级为化学通式，请重新分析后确认；旧结果已保存在状态备份。";}
          if((file.Status=="Review"||file.Status=="Error")&&FormulaCatalog.UpgradeTopicRouting(file.Analysis))file.Message="主题路径已去除材料层，请核对分类；无需重新调用API。";
        }
        return state;
      }catch(Exception error) {
        File.Copy(path,path+".unreadable-"+DateTime.Now.ToString("yyyyMMddHHmmss"),true);
        throw new InvalidDataException("任务记录无法读取，已保留备份。请从程序设置目录检查 state.json 后重试。",error);
      }
    }
    public void Save(AppState state) {string value=Json.Serialize(state);if(value==lastSaved)return;WriteAtomic(System.IO.Path.Combine(Folder,"state.json"),value);lastSaved=value;}
    public static void WriteAtomic(string path,string value) {
      string temporary=path+".tmp-"+Guid.NewGuid().ToString("N");
      File.WriteAllText(temporary,value,new UTF8Encoding(false));
      if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
    }
    public BridgeConfig GetBridgeConfig() {
      string path=System.IO.Path.Combine(Folder,"bridge.json");
      if(File.Exists(path)) {
        BridgeConfig saved=Json.Deserialize<BridgeConfig>(File.ReadAllText(path,Encoding.UTF8));
        if(saved!=null&&saved.token!=null&&saved.token.Length==64&&saved.port>0&&saved.port<65536)return saved;
        throw new InvalidDataException("Zotero 连接配置格式不正确。");
      }
      byte[] bytes=new byte[32];using(RandomNumberGenerator random=RandomNumberGenerator.Create())random.GetBytes(bytes);
      BridgeConfig config=new BridgeConfig{protocolVersion=1,port=23119,token=BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant()};
      WriteAtomic(path,Json.Serialize(config));return config;
    }
  }
  public sealed class Tracker {
    sealed class Observation {public string Fingerprint;public long Since;}
    readonly AppState state;
    readonly Dictionary<string,Observation> observed=new Dictionary<string,Observation>();
    public long StabilityMilliseconds=1000;
    public bool PendingChanged {get;private set;}
    public Tracker(AppState state) {this.state=state;}
    public List<PendingFile> Observe(IList<DownloadFile> files,long now) {
      List<PendingFile> added=new List<PendingFile>();
      PendingChanged=false;
      if(!state.Initialized) {
        foreach(DownloadFile file in files)state.Known[DownloadFile.Key(file.Path)]=file.Fingerprint;
        state.Initialized=true;return added;
      }
      HashSet<string> present=new HashSet<string>(files.Select(f=>DownloadFile.Key(f.Path)));
      foreach(string key in observed.Keys.Where(k=>!present.Contains(k)).ToList())observed.Remove(key);
      foreach(DownloadFile file in files) {
        string key=DownloadFile.Key(file.Path),previous;Observation item;
        if(state.Known.TryGetValue(key,out previous)&&previous==file.Fingerprint)continue;
        if(!observed.TryGetValue(key,out item)||item.Fingerprint!=file.Fingerprint) {
          observed[key]=new Observation{Fingerprint=file.Fingerprint,Since=now};continue;
        }
        if(now-item.Since<StabilityMilliseconds)continue;
        PendingFile pending=state.Pending.FirstOrDefault(p=>DownloadFile.Key(p.Path)==key);
        if(pending!=null) {
          if(pending.Fingerprint!=file.Fingerprint&&pending.Status=="Importing")continue;
          if(pending.Fingerprint!=file.Fingerprint&&pending.Status!="Importing") {
            pending.Id=Guid.NewGuid().ToString("N");pending.Size=file.Size;pending.LastModified=file.LastModified;
            pending.Status="Pending";pending.Message="文件版本已变化，请重新分析并确认。";pending.Submitted=false;pending.Analysis=null;
            PendingChanged=true;
          }
        } else {
          pending=new PendingFile{Id=Guid.NewGuid().ToString("N"),Path=file.Path,Name=file.Name,Size=file.Size,LastModified=file.LastModified,Status="Pending",Message="可先进行 API 内容分析，再核对并确认入库。"};
          state.Pending.Add(pending);added.Add(pending);PendingChanged=true;
        }
        state.Known[key]=file.Fingerprint;observed.Remove(key);
      }
      foreach(string key in state.Known.Keys.Where(k=>!present.Contains(k)).ToList())state.Known.Remove(key);
      return added;
    }
  }
}
