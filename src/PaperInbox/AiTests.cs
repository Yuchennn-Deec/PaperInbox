using System;
using System.Collections;
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
using System.Windows.Forms;

namespace PaperInbox {
  public static class AiTests {
    public static AnalysisResult Sample(){return new AnalysisResult{isAcademic=true,itemType="journalArticle",title="The toroidal moment in condensed-matter physics and its relation to the magnetoelectric effect",doi="10.1088/0953-8984/20/43/434203",year="2008",authors=new[]{"N A Spaldin","M Fiebig","M Mostovoy"},collections=new[]{"按主题/磁电耦合"},routingVersion=FormulaCatalog.RoutingVersion,classificationMode="topic",classificationVersion=FormulaCatalog.Version,materials=new[]{new MaterialAssignment{family="background",compound="跨体系磁性综述与理论背景",evidence="toroidal moment"}},tags=new[]{"主题:磁电耦合"},summary="讨论凝聚态物理中的环形矩及磁电效应。",reason="正文研究主题为环形矩和磁电效应，属于理论综述。",confidence=0.95,sourceHash=new string('a',64),provider="本机模拟服务",model="演示模型",textCharacters=24000};}
    public static void Run(string folder,Action<string,bool> check) {
      StateStore store=new StateStore(Path.Combine(folder,"api-settings-test"));AiSettingsStore settings=new AiSettingsStore(store);AiConfiguration config=new AiConfiguration();
      config.Profile("openai").EncryptedKey=AiSettingsStore.Encrypt("test-key-openai");config.Profile("deepseek").EncryptedKey=AiSettingsStore.Encrypt("test-key-deepseek");settings.Save(config);
      string saved=File.ReadAllText(Path.Combine(store.Folder,"api-settings.json"));check("密钥使用 Windows 当前用户加密保存",!saved.Contains("test-key")&&AiSettingsStore.Decrypt(settings.Load().Profile("openai").EncryptedKey)=="test-key-openai");
      check("服务商切换分别保留密钥",AiSettingsStore.Decrypt(settings.Load().Profile("deepseek").EncryptedKey)=="test-key-deepseek");
      check("七个服务商预设和自定义接口可选择",ProviderPreset.All.Length==8&&ProviderPreset.All.Take(7).All(p=>AiClient.Endpoint(new ApiProfile{Endpoint=p.Endpoint,Model="test-model"}).Scheme=="https"));
      check("模型由用户填写，不猜测当前可用模型",ProviderPreset.All.All(p=>string.IsNullOrEmpty(config.Profile(p.Id).Model)));
      bool refused=false;try{AiClient.Endpoint(new ApiProfile{Endpoint="http://example.com/chat"});}catch(InvalidDataException){refused=true;}check("远程接口拒绝明文 HTTP",refused);
      refused=false;try{AiClient.Endpoint(new ApiProfile{Endpoint="https://example.com/chat?key=secret"});}catch(InvalidDataException){refused=true;}check("接口 URL 不携带密钥查询参数",refused);
      check("本机自定义模型接口允许 HTTP",AiClient.Endpoint(new ApiProfile{Endpoint="http://127.0.0.1:1234/v1/chat/completions"}).IsLoopback);
      AnalysisResult low=Sample();low.confidence=0.4;low.collections=new[]{"我的研究/磁电效应"};low.Validate();check("手动选择的路径不会被低置信度覆写",low.collections.SequenceEqual(new[]{"我的研究/磁电效应"}));
      AnalysisResult bad=Sample();bad.collections=new[]{"../bad"};refused=false;try{bad.Validate();}catch(InvalidDataException){refused=true;}check("拒绝非法分类路径",refused);
      AppState state=new AppState();state.Pending.Add(new PendingFile{Id="review",Status="Review",Analysis=Sample()});store.Save(state);check("重启保留待确认的 API 分类结果",store.Load().Pending[0].Status=="Review"&&store.Load().Pending[0].Analysis.title==Sample().title);
      state.Pending[0].Status="Approved";state.Pending[0].Analysis=null;store.Save(state);check("旧版未带 API 结果的队列需重新确认",store.Load().Pending[0].Status=="Pending");
      state.Pending[0].Status="Analyzing";store.Save(state);check("重启不会自动重复付费 API 分析",store.Load().Pending[0].Status=="Pending");
      Task.Run(()=>Network(folder,check)).GetAwaiter().GetResult();
      using(ApiSettingsForm dialog=new ApiSettingsForm(store)){Render(dialog,Path.Combine(folder,"api-settings-preview.png"));check("API 设置界面提供掩码密钥输入",Controls(dialog).OfType<TextBox>().Any(t=>t.UseSystemPasswordChar));}
      using(ReviewForm dialog=new ReviewForm(Sample())){Render(dialog,Path.Combine(folder,"review-preview.png"));dialog.Close();check("关闭预览不表示确认入库",dialog.DialogResult!=DialogResult.OK);}
    }
    public static void Render(Form form,string path){form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.ShowInTaskbar=false;form.Show();Application.DoEvents();form.PerformLayout();using(Bitmap bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(path,ImageFormat.Png);}}
    static IEnumerable<Control> Controls(Control parent){foreach(Control c in parent.Controls){yield return c;foreach(Control d in Controls(c))yield return d;}}
    static async Task Network(string folder,Action<string,bool> check) {
      foreach(string protocol in new[]{"openai","anthropic","gemini"})using(FakeAiServer server=new FakeAiServer(protocol)) {
        ApiProfile profile=server.Profile();ExtractResponse extracted=new ExtractResponse{text=new string('文',4000),sourceHash=new string('b',64),collections=new[]{"石榴石体系","综述与理论背景"}};
        AnalysisResult result=await new AiClient().Analyze(profile,"mock-"+protocol,extracted);
        check(protocol+" 请求与响应协议通过",server.Count==1&&server.Authenticated&&server.LastBody.Contains("test-model")==(protocol!="gemini")&&result.doi==Sample().doi);
        check(protocol+" 只提交限定文本且保留来源指纹",result.textCharacters==2000&&result.sourceHash==extracted.sourceHash&&server.LastBody.Contains("综述与理论背景"));
        server.Invalid=true;bool rejected=false;try{await new AiClient().Analyze(profile,"mock",extracted);}catch(InvalidDataException){rejected=true;}check(protocol+" 非法模型输出不入库",rejected);
        server.Invalid=false;server.Code=401;rejected=false;try{await new AiClient().Analyze(profile,"mock",extracted);}catch(InvalidDataException error){rejected=error.Message.Contains("401")&&!error.Message.Contains("test-secret");}check(protocol+" API 错误不显示密钥或响应正文",rejected);
        int before=server.Count;rejected=false;try{await new AiClient().Analyze(profile,"mock",new ExtractResponse{text="",sourceHash=""});}catch(InvalidDataException){rejected=true;}check(protocol+" 无文本扫描件不调用 API",rejected&&server.Count==before);
      }
      StateStore store=new StateStore(Path.Combine(folder,"review-gate"));using(BridgeClient bridge=new BridgeClient(store)) {
        bool refused=false;try{await bridge.Submit(new PendingFile{Status="Review",Analysis=Sample()});}catch(InvalidOperationException){refused=true;}check("API 分析完成仍必须确认入库",refused);
        refused=false;try{await bridge.Submit(new PendingFile{Status="Approved"});}catch(InvalidOperationException){refused=true;}check("未提供确认分类结果时拒绝提交",refused);
      }
    }
  }
  public sealed class FakeAiServer : IDisposable {
    readonly TcpListener listener;readonly Thread thread;readonly string protocol;volatile bool stopped;
    public int Port,Count,Code=200,DelayMilliseconds;public bool Authenticated,Invalid;public string LastBody="";public AnalysisResult ResultOverride;
    public FakeAiServer(string protocol){this.protocol=protocol;listener=new TcpListener(IPAddress.Loopback,0);listener.Start();Port=((IPEndPoint)listener.LocalEndpoint).Port;thread=new Thread(Serve){IsBackground=true};thread.Start();}
    public ApiProfile Profile(){return new ApiProfile{Protocol=protocol,Endpoint="http://127.0.0.1:"+Port.ToString()+"/test",Model="test-model",EncryptedKey=AiSettingsStore.Encrypt("test-secret"),MaxCharacters=2000,MaxPages=12};}
    void Serve(){while(!stopped){try{using(TcpClient socket=listener.AcceptTcpClient())using(NetworkStream stream=socket.GetStream()){stream.ReadTimeout=5000;Reply(stream);}}catch{if(stopped)return;}}}
    void Reply(NetworkStream stream) {
      List<byte> head=new List<byte>();int b;
      while((b=stream.ReadByte())>=0){head.Add((byte)b);int n=head.Count;if(n>=4&&head[n-4]==13&&head[n-3]==10&&head[n-2]==13&&head[n-1]==10)break;if(n>16384)throw new IOException();}
      string headers=Encoding.ASCII.GetString(head.ToArray());int length=0;foreach(string line in headers.Split(new[]{"\r\n"},StringSplitOptions.None))if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))length=int.Parse(line.Substring(15).Trim());
      byte[] body=new byte[length];int offset=0;while(offset<length){int n=stream.Read(body,offset,length-offset);if(n==0)break;offset+=n;}LastBody=Encoding.UTF8.GetString(body);Count++;if(DelayMilliseconds>0)Thread.Sleep(DelayMilliseconds);
      string expected=protocol=="anthropic"?"x-api-key: test-secret":protocol=="gemini"?"x-goog-api-key: test-secret":"Authorization: Bearer test-secret";Authenticated=headers.IndexOf(expected,StringComparison.OrdinalIgnoreCase)>=0;
      JavaScriptSerializer json=new JavaScriptSerializer();string text=Invalid?"not valid JSON":json.Serialize(ResultOverride??AiTests.Sample());object response=protocol=="anthropic"?(object)new{content=new[]{new{type="text",text=text}}}:protocol=="gemini"?(object)new{candidates=new[]{new{content=new{parts=new[]{new{text=text}}}}}}:new{choices=new[]{new{message=new{content=text}}}};
      byte[] payload=Encoding.UTF8.GetBytes(Code==200?json.Serialize(response):"test-secret must never be shown");byte[] prefix=Encoding.ASCII.GetBytes("HTTP/1.1 "+Code.ToString()+" "+(Code==200?"OK":"Error")+"\r\nContent-Type: application/json\r\nConnection: close\r\nContent-Length: "+payload.Length.ToString()+"\r\n\r\n");stream.Write(prefix,0,prefix.Length);stream.Write(payload,0,payload.Length);
    }
    public void Dispose(){stopped=true;listener.Stop();thread.Join(1000);}
  }
}
