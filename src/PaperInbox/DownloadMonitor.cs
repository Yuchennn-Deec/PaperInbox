using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace PaperInbox {
  // Worker-thread snapshot reader. No queue, WinForms, or API work runs in a watcher callback.
  public sealed class DownloadMonitor : IDisposable {
    readonly string folder;
    readonly FileSystemWatcher watcher;
    readonly Dictionary<string,string> complete=new Dictionary<string,string>();
    long fastUntil;
    public long LastScan {get;private set;}
    public bool Watching {get;private set;}
    public DownloadMonitor(string folder,bool watch=true) {
      this.folder=folder;Signal();
      if(!watch)return;
      try {
        watcher=new FileSystemWatcher(folder,"*"){IncludeSubdirectories=false,NotifyFilter=NotifyFilters.FileName|NotifyFilters.Size|NotifyFilters.LastWrite};
        watcher.Created+=(s,e)=>Signal();watcher.Changed+=(s,e)=>Signal();watcher.Deleted+=(s,e)=>Signal();watcher.Renamed+=(s,e)=>Signal();
        watcher.Error+=(s,e)=>{Watching=false;Signal();};watcher.EnableRaisingEvents=true;Watching=true;
      }catch(IOException){}catch(ArgumentException){}catch(UnauthorizedAccessException){}
    }
    public void Signal(){Interlocked.Exchange(ref fastUntil,DownloadFile.UnixMilliseconds(DateTime.UtcNow)+4000);}
    public bool Due(long now){return LastScan==0||now-LastScan>=(now<=Interlocked.Read(ref fastUntil)||!Watching?500:10000);}
    public List<DownloadFile> Scan() {
      LastScan=DownloadFile.UnixMilliseconds(DateTime.UtcNow);
      List<DownloadFile> files=new List<DownloadFile>();HashSet<string> present=new HashSet<string>();
      foreach(string path in Directory.EnumerateFiles(folder,"*.pdf",SearchOption.TopDirectoryOnly)) {
        try {
          DownloadFile file=DownloadFile.FromPath(path);string key=DownloadFile.Key(path);present.Add(key);
          if(file.Size<=0||HasTemporarySibling(path)){complete.Remove(key);continue;}
          string known;
          if(!(complete.TryGetValue(key,out known)&&known==file.Fingerprint)) {
            if(!DownloadFile.IsCompletePDF(path)){complete.Remove(key);continue;}
            // Reject a file that changed while the PDF header/tail was being checked.
            if(DownloadFile.FromPath(path).Fingerprint!=file.Fingerprint)continue;
            complete[key]=file.Fingerprint;
          }
          files.Add(file);
        }catch(IOException){}catch(UnauthorizedAccessException){}
      }
      foreach(string key in complete.Keys.Where(k=>!present.Contains(k)).ToList())complete.Remove(key);
      return files;
    }
    public static bool HasTemporarySibling(string path){return new[]{".crdownload",".part",".download",".partial",".tmp"}.Any(s=>File.Exists(path+s));}
    public void Dispose(){if(watcher!=null)watcher.Dispose();Watching=false;}
  }
}
