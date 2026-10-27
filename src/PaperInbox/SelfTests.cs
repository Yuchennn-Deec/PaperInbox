using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PaperInbox {
  public static class SelfTests {
    static readonly List<object> results=new List<object>();
    static void Check(string name,bool passed) {results.Add(new{name=name,passed=passed});if(!passed)throw new Exception(name);}
    static DownloadFile Sample(string name,long size,long time) {return new DownloadFile{Path=Path.Combine(Path.GetTempPath(),name),Name=name,Size=size,LastModified=time};}
    public static int Run(string folder) {
      Directory.CreateDirectory(folder);JavaScriptSerializer json=new JavaScriptSerializer();
      try {
        AppState state=new AppState();Tracker tracker=new Tracker(state);DownloadFile old=Sample("old.pdf",100,1);
        Check("首次运行不会提示旧文件",tracker.Observe(new[]{old},0).Count==0&&state.Pending.Count==0);
        DownloadFile fresh=Sample("new.pdf",200,2);
        tracker.Observe(new[]{old,fresh},1);
        Check("未稳定的 PDF 不提示",tracker.Observe(new[]{old,fresh},500).Count==0);
        Check("稳定后的新 PDF 只入队",tracker.Observe(new[]{old,fresh},13000).Count==1&&state.Pending[0].Status=="Pending");
        Check("不重复提示同一版本",tracker.Observe(new[]{old,fresh},26000).Count==0&&state.Pending.Count==1);
        string id=state.Pending[0].Id;state.Pending[0].Status="Approved";
        DownloadFile changed=Sample("new.pdf",300,3);tracker.Observe(new[]{old,changed},30000);tracker.Observe(new[]{old,changed},43000);
        Check("文件版本变化后须重新确认",state.Pending[0].Status=="Pending"&&state.Pending[0].Id!=id);
        StateStore store=new StateStore(Path.Combine(folder,"state-test"));state.Pending[0].Status="Importing";state.Pending[0].Analysis=AiTests.Sample();store.Save(state);
        AppState restored=store.Load();
        Check("重启保留已确认任务",restored.Pending.Count==1&&restored.Pending[0].Status=="Approved");
        Tracker afterRestart=new Tracker(restored);DownloadFile offline=Sample("offline.pdf",400,4);
        afterRestart.Observe(new[]{old,changed,offline},50000);
        Check("程序关闭期间的新文件可发现",afterRestart.Observe(new[]{old,changed,offline},63000).Count==1);
        string fixture=Path.Combine(folder,"complete.pdf");File.WriteAllText(fixture,"%PDF-1.7\nTest fixture\n%%EOF\n");
        Check("检测 PDF 头与结束标记",DownloadFile.IsCompletePDF(fixture));
        string partial=Path.Combine(folder,"incomplete.pdf");File.WriteAllText(partial,"%PDF-1.7\nStill downloading");
        Check("不完整 PDF 不提示",!DownloadFile.IsCompletePDF(partial));
        BridgeConfig secret=store.GetBridgeConfig();
        Check("本机连接密钥随机且可持久化",secret.token.Length==64&&store.GetBridgeConfig().token==secret.token);
        Task.Run(()=>TestNetwork(folder)).GetAwaiter().GetResult();
        string uiState=Path.Combine(folder,"ui-state");StateStore uiStore=new StateStore(uiState);AppState ui=new AppState();
        ui.Folder=folder;ui.Initialized=true;ui.Pending.Add(new PendingFile{Id=Guid.NewGuid().ToString("N"),Name="Spin dynamics of frustrated magnets.pdf",Path=fixture,Size=2100000,LastModified=1,Status="Pending",Message="等待你确认是否识别并归类。"});
        ui.Pending.Add(new PendingFile{Id=Guid.NewGuid().ToString("N"),Name="The toroidal moment and magnetoelectric effect.pdf",Path=fixture,Size=3400000,LastModified=1,Status="Review",Analysis=AiTests.Sample(),Message="分类已准备好，等待你确认。"});
        ui.Pending.Add(new PendingFile{Id=Guid.NewGuid().ToString("N"),Name="Crystal chemistry of garnet materials.pdf",Path=fixture,Size=5800000,LastModified=1,Status="Error",Message="暂时无法连接模型，可稍后重试。"});uiStore.Save(ui);
        using(TrayContext context=new TrayContext(uiState,true)) {
          context.Form.StartPosition=System.Windows.Forms.FormStartPosition.Manual;context.Form.Location=new Point(-20000,-20000);context.Form.ShowInTaskbar=false;context.Form.Show();System.Windows.Forms.Application.DoEvents();context.Form.PerformLayout();context.Form.Render();System.Windows.Forms.Application.DoEvents();
          using(Bitmap preview=new Bitmap(context.Form.Width,context.Form.Height)){context.Form.DrawToBitmap(preview,new Rectangle(Point.Empty,preview.Size));preview.Save(Path.Combine(folder,"ui-preview.png"),ImageFormat.Png);}
          Check("界面可绘制且包含待确认文件",context.State.Pending.Count==3&&context.Form.PresentedFileCount==3&&context.Form.Controls[0].Visible);
          AiTests.Render(context.Form,Path.Combine(folder,"ui-preview.png"));
          Size normal=context.Form.Size;context.Form.Size=context.Form.MinimumSize;AiTests.Render(context.Form,Path.Combine(folder,"minimum-preview.png"));context.Form.Size=normal;
          var original=context.State.Pending.ToArray();context.State.Pending.Clear();context.Form.Render();AiTests.Render(context.Form,Path.Combine(folder,"empty-preview.png"));context.State.Pending.AddRange(original);context.Form.Render();
          using(PreferencesForm settings=new PreferencesForm(context))AiTests.Render(settings,Path.Combine(folder,"preferences-preview.png"));
          context.Form.Close();Check("关闭窗口不会自动导入",context.State.Pending[0].Status=="Pending");context.DisposeForTest();
        }
        AiTests.Run(folder,Check);
        FormulaTests.Run(folder,Check);
        TopicTests.Run(folder,Check);
        DualTests.Run(folder,Check);
        PromptTests.Run(folder,Check);
        File.WriteAllText(Path.Combine(folder,"results.json"),json.Serialize(new{success=true,tests=results}),Encoding.UTF8);return 0;
      }catch(Exception error){File.WriteAllText(Path.Combine(folder,"results.json"),json.Serialize(new{success=false,tests=results,error=error.ToString()}),Encoding.UTF8);return 1;}
    }
    static async Task TestNetwork(string folder) {
      StateStore store=new StateStore(Path.Combine(folder,"network-state"));BridgeConfig config=store.GetBridgeConfig();
      using(MockServer server=new MockServer(config.token)) {
        config.port=server.Port;StateStore.WriteAtomic(Path.Combine(store.Folder,"bridge.json"),store.Json.Serialize(config));
        using(BridgeClient client=new BridgeClient(store)) {
          PendingFile pending=new PendingFile{Id=Guid.NewGuid().ToString("N"),Path=Path.Combine(folder,"test.pdf"),Name="test.pdf",Status="Pending",Size=100,LastModified=1};
          bool rejected=false;try{await client.Submit(pending);}catch(InvalidOperationException){rejected=true;}
          Check("未确认任务不会发送到 Zotero",rejected&&server.SubmitCount==0);
          BridgeStatus status=await client.Status(new string[0]);Check("仅通过本机地址查询连接",status.ready&&status.protocolVersion==3);
          pending.Status="Approved";pending.Analysis=AiTests.Sample();SubmitResponse response=await client.Submit(pending);
          Check("确认后才提交归档任务",server.SubmitCount==1&&response.request.id==pending.Id);
          Check("连接请求携带本机认证密钥",server.AuthenticatedCount==2);
          config.token=new string('0',64);StateStore.WriteAtomic(Path.Combine(store.Folder,"bridge.json"),store.Json.Serialize(config));
          bool bad=false;try{await client.Status(new string[0]);}catch(BridgeUnavailable){bad=true;}
          Check("错误密钥被拒绝",bad&&server.SubmitCount==1);
        }
      }
    }
    sealed class MockServer : IDisposable {
      readonly TcpListener listener;readonly Thread thread;readonly string token;volatile bool stopped;
      public int Port,SubmitCount,AuthenticatedCount;
      public MockServer(string token){this.token=token;listener=new TcpListener(IPAddress.Loopback,0);listener.Start();Port=((IPEndPoint)listener.LocalEndpoint).Port;thread=new Thread(Serve){IsBackground=true};thread.Start();}
      void Serve(){while(!stopped){try{using(TcpClient socket=listener.AcceptTcpClient()){using(NetworkStream stream=socket.GetStream()){stream.ReadTimeout=4000;ReadRequest(stream);}}}catch{if(stopped)return;}}}
      void ReadRequest(NetworkStream stream) {
        List<byte> head=new List<byte>();int value;
        while((value=stream.ReadByte())>=0){head.Add((byte)value);int n=head.Count;if(n>=4&&head[n-4]==13&&head[n-3]==10&&head[n-2]==13&&head[n-1]==10)break;if(n>16384)throw new IOException();}
        string headers=Encoding.ASCII.GetString(head.ToArray());string[] lines=headers.Split(new[]{"\r\n"},StringSplitOptions.None);
        int length=0;string supplied="";
        foreach(string line in lines){if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))length=int.Parse(line.Substring(15).Trim());if(line.StartsWith("X-PaperInbox-Token:",StringComparison.OrdinalIgnoreCase))supplied=line.Substring(19).Trim();}
        byte[] body=new byte[length];int offset=0;while(offset<length){int read=stream.Read(body,offset,length-offset);if(read==0)break;offset+=read;}
        bool allowed=supplied==token;string response;
        if(!allowed)response="{\"error\":\"NOT_AUTHORIZED\"}";
        else {AuthenticatedCount++;if(lines[0].Contains("/submit")){SubmitCount++;Dictionary<string,object> request=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(Encoding.UTF8.GetString(body));response=new JavaScriptSerializer().Serialize(new{request=new{id=request["id"],status="pending",message="queued"}});}else response="{\"ready\":true,\"protocolVersion\":3,\"version\":\"test\",\"requests\":{}}";}
        byte[] payload=Encoding.UTF8.GetBytes(response);byte[] prefix=Encoding.ASCII.GetBytes("HTTP/1.1 "+(allowed?"200 OK":"403 Forbidden")+"\r\nContent-Type: application/json\r\nConnection: close\r\nContent-Length: "+payload.Length+"\r\n\r\n");stream.Write(prefix,0,prefix.Length);stream.Write(payload,0,payload.Length);
      }
      public void Dispose(){stopped=true;listener.Stop();thread.Join(1000);}
    }
  }
}
