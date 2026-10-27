using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PaperInbox {
  public static class FormulaTests {
    static AnalysisResult Paper(string family,string compound,string evidence){return new AnalysisResult{isAcademic=true,title="Test paper",confidence=.95,tags=new string[0],materials=new[]{new MaterialAssignment{family=family,compound=compound,evidence=evidence}}};}
    static void Pump(Task task,int milliseconds){Stopwatch elapsed=Stopwatch.StartNew();while(!task.IsCompleted&&elapsed.ElapsedMilliseconds<milliseconds){Application.DoEvents();Thread.Sleep(10);}if(!task.IsCompleted)throw new TimeoutException("Test UI pump timed out");task.GetAwaiter().GetResult();}
    public static void Run(string folder,Action<string,bool> check) {
      check("保留原通式并补充简单氧化物",FormulaCatalog.Data.families.Length==40&&FormulaCatalog.Data.existingPaths.Length>90);
      string excerpt="We investigate magnetic ordering in CaMnGe2O6 using neutron diffraction.";
      AnalysisResult a=Paper("pyroxene","CaMnGe2O6",excerpt);FormulaCatalog.Classify(a,excerpt);a.Validate();
      check("正文证据映射辉石通式与已有材料子类",a.collections.Contains("按化学通式/AMX2O6（辉石）/CaMnGe2O6")&&a.tags.Contains("化学通式:AMX2O6"));
      a=Paper("bismuthate","Na3Ni2BiO6","The honeycomb Na3Ni2BiO6 compound shows a magnetization plateau.");FormulaCatalog.Classify(a,a.materials[0].evidence);
      check("铋酸盐不会误归入碲酸盐",a.collections.Single().Contains("A3M2BiO6")&&!a.collections.Single().Contains("A2M2TeO6"));
      a=Paper("pyroxene","CaMnGe2O6",excerpt);FormulaCatalog.Classify(a,"This document never mentions that compound.");
      check("模型虚构正文证据时进入待核对",a.collections.SequenceEqual(new[]{FormulaCatalog.Pending})&&!a.tags.Any(t=>t.StartsWith("化学通式:")));
      a=Paper("pyroxene","CaMnGe2O6","We investigate magnetic ordering with neutron diffraction.");FormulaCatalog.Classify(a,a.materials[0].evidence);
      check("证据不含具体化学式时不猜填材料",a.collections.Contains(FormulaCatalog.Pending));
      a=Paper("not-in-catalog","CuO","The compound CuO was prepared and characterized.");FormulaCatalog.Classify(a,a.materials[0].evidence);
      check("目录外材料CuO可正确归入MO",a.collections.Single()=="按化学通式/MO（二元氧化物）/CuO");
      a=Paper("pyroxene","CaMnGe2O6",excerpt);a.confidence=.4;FormulaCatalog.Classify(a,excerpt);
      check("低置信度不会自动进入材料子类",a.collections.SequenceEqual(new[]{FormulaCatalog.Pending}));
      a=Paper("pyroxene","CaMnGe2O6",excerpt);a.isAcademic=false;FormulaCatalog.Classify(a,excerpt);
      check("非学术PDF不会被自动当作材料论文",a.collections.SequenceEqual(new[]{FormulaCatalog.Pending}));
      a=Paper("pyroxene","CaMnGe2O6",excerpt);a.materials[0].collectionPath="按化学通式/AMX2O6（辉石）/现有的复合材料分类";FormulaCatalog.Classify(a,excerpt,new[]{a.materials[0].collectionPath});
      check("允许复用Zotero现有具体材料分类",a.collections.Single().EndsWith("现有的复合材料分类"));
      a=Paper("pyroxene","CaMnGe2O6",excerpt);a.materials=new[]{a.materials[0],new MaterialAssignment{family="garnet",compound="Mn3Al2Ge3O12",evidence="We also study Mn3Al2Ge3O12 and its low temperature order."}};FormulaCatalog.Classify(a,excerpt+" "+a.materials[1].evidence);
      check("跨体系研究可以进入多个通式",a.collections.Length==2&&a.tags.Count(t=>t.StartsWith("化学通式:"))==2);
      check("化学式支持下标但保留元素大小写",FormulaCatalog.Normalize("CaMnGe₂O₆")=="CaMnGe2O6"&&FormulaCatalog.Normalize("Co")!=FormulaCatalog.Normalize("CO"));
      AppState versions=new AppState{Initialized=true};Tracker tracker=new Tracker(versions);DownloadFile original=new DownloadFile{Path=Path.Combine(folder,"versions.pdf"),Name="versions.pdf",Size=100,LastModified=1};tracker.Observe(new[]{original},0);tracker.Observe(new[]{original},1001);versions.Pending[0].Status="Importing";
      DownloadFile revised=new DownloadFile{Path=original.Path,Name=original.Name,Size=200,LastModified=2};tracker.Observe(new[]{revised},1500);tracker.Observe(new[]{revised},2501);versions.Pending[0].Status="Error";tracker.Observe(new[]{revised},2600);
      check("导入期间文件变化在任务结束后仍需重新确认",versions.Pending[0].Status=="Pending"&&versions.Pending[0].Size==200&&tracker.PendingChanged);

      string download=Path.Combine(folder,"downloads-fast");Directory.CreateDirectory(download);string fixture=Path.Combine(download,"new.pdf");
      File.WriteAllText(fixture,"%PDF-1.7\nComplete PDF fixture for detection\n%%EOF\n");
      using(FileStream writer=new FileStream(fixture,FileMode.Open,FileAccess.Write,FileShare.ReadWrite))check("文件尚被写入时即使有EOF也不提醒",!DownloadFile.IsCompletePDF(fixture));
      File.WriteAllText(fixture+".part","downloading");using(DownloadMonitor scan=new DownloadMonitor(download,false))check("Firefox临时伴随文件存在时不提示",scan.Scan().Count==0);File.Delete(fixture+".part");
      File.WriteAllText(fixture+".crdownload","downloading");using(DownloadMonitor scan=new DownloadMonitor(download,false))check("Chromium临时伴随文件存在时不提示",scan.Scan().Count==0);File.Delete(fixture+".crdownload");
      using(DownloadMonitor scan=new DownloadMonitor(download,false))check("无系统通知时仍能通过轮询检测完整文件",scan.Scan().Count==1&&scan.Due(DownloadFile.UnixMilliseconds(DateTime.UtcNow)+501));
      using(DownloadMonitor scan=new DownloadMonitor(download)) {
        Thread.Sleep(4150);scan.Scan();bool idle=!scan.Due(DownloadFile.UnixMilliseconds(DateTime.UtcNow)+501);
        string browserTemporary=Path.Combine(download,"renamed.crdownload"),completed=Path.Combine(download,"renamed.pdf");File.Copy(fixture,browserTemporary);File.Move(browserTemporary,completed);
        Stopwatch signal=Stopwatch.StartNew();while(!scan.Due(DownloadFile.UnixMilliseconds(DateTime.UtcNow)+501)&&signal.ElapsedMilliseconds<1500)Thread.Sleep(20);
        check("真实Windows文件通知唤醒扫描并识别重命名下载",scan.Watching&&idle&&scan.Due(DownloadFile.UnixMilliseconds(DateTime.UtcNow)+501)&&scan.Scan().Any(f=>f.Name=="renamed.pdf"));
        File.Delete(completed);
      }

      string testState=Path.Combine(folder,"independent-monitor");StateStore store=new StateStore(testState);AppState state=new AppState{Initialized=true,Folder=download,Popup=false};store.Save(state);
      using(FakeAiServer slow=new FakeAiServer("openai"){DelayMilliseconds=4500}) {
        BridgeConfig config=store.GetBridgeConfig();config.port=slow.Port;StateStore.WriteAtomic(Path.Combine(testState,"bridge.json"),store.Json.Serialize(config));
        using(TrayContext context=new TrayContext(testState,true)) {
          Stopwatch elapsed=Stopwatch.StartNew();Task bridgeAndScan=context.Cycle();
          while(context.State.Pending.Count==0&&elapsed.ElapsedMilliseconds<3200){Pump(context.ScanDownloads(),1000);Application.DoEvents();Thread.Sleep(50);}
          long latency=elapsed.ElapsedMilliseconds;
          check("Zotero请求阻塞时下载仍在3.2秒内入队",context.State.Pending.Count==1&&latency<3200&&!bridgeAndScan.IsCompleted);
          File.WriteAllText(Path.Combine(folder,"latency.json"),store.Json.Serialize(new{downloadDetectionMilliseconds=latency,bridgeStillPending=!bridgeAndScan.IsCompleted,stabilityMilliseconds=new Tracker(new AppState()).StabilityMilliseconds}));
          Pump(bridgeAndScan,5500);context.DisposeForTest();
        }
      }
    }
  }
}
