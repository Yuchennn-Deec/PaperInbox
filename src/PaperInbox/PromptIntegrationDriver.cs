using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using PaperInbox;

public static class PromptIntegrationDriver {
  static readonly List<object> checks=new List<object>();static JavaScriptSerializer json=new JavaScriptSerializer();
  static void Check(string name,bool value){checks.Add(new{name=name,passed=value});if(!value)throw new Exception(name);}
  public static int Main(string[] args){try{Run(args[0]).GetAwaiter().GetResult();File.WriteAllText(Path.Combine(args[0],"driver-results.json"),json.Serialize(new{success=true,checks=checks}));return 0;}catch(Exception error){File.WriteAllText(Path.Combine(args[0],"driver-results.json"),json.Serialize(new{success=false,checks=checks,error=error.ToString()}));return 1;}}
  static async Task Run(string root) {
    StateStore store=new StateStore(Path.Combine(root,"shared"));using(BridgeClient client=new BridgeClient(store)) {
      BridgeStatus ready=await client.Status(new string[0]);Check("现有0.5.0客户端实际连接0.5.1插件",ready.ready&&ready.protocolVersion==3&&ready.version=="0.5.1");
      DownloadFile info=DownloadFile.FromPath(Path.Combine(root,"files","approved.pdf"));PendingFile file=new PendingFile{Id=Guid.NewGuid().ToString("N"),Path=info.Path,Name=info.Name,Size=info.Size,LastModified=info.LastModified,Status="Pending"};
      ExtractResponse text=await client.Extract(file,12,24000);string leaf="按化学通式/MO（二元氧化物）/CuO";
      CollectionCatalog history=await client.Collections();Check("只读历史目录接口返回已有CuO材料路径",history.collections.Contains(leaf)&&history.collections.Contains("按主题/磁关联"));
      Check("从真实CuO论文提取正文且不入库",text.text.Length>1000&&text.sourceHash.Length==64);
      Check("读取既有材料嵌套分类",text.collections.Contains(leaf));
      AnalysisResult prediction=json.Deserialize<AnalysisResult>(File.ReadAllText(Path.Combine(root,"fixture-analysis.json"),Encoding.UTF8));
      prediction.collections=new[]{"按主题/磁关联","物理/磁性/低维磁性"};prediction.tags=prediction.tags.Concat(new[]{"精读","阅读优先级:高"}).ToArray();
      using(FakeAiServer mock=new FakeAiServer("openai"){ResultOverride=prediction}) {
        ApiProfile profile=mock.Profile();profile.MaxCharacters=24000;profile.ClassificationPrompt="DUAL_NATIVE_CUSTOM_PROMPT";file.Analysis=await new AiClient().Analyze(profile,"本机模拟API",text);
        Check("API路径直接用于分类且不自动追加化学式",mock.Count==1&&file.Analysis.collections.SequenceEqual(prediction.collections)&&file.Analysis.classificationMode=="prompt");Check("自定义Prompt随原生提取文本一起发送",mock.LastBody.Contains("DUAL_NATIVE_CUSTOM_PROMPT")&&!mock.LastBody.Contains("formulaRoot"));
      }

      file.Status="Review";bool blocked=false;try{await client.Submit(file);}catch(InvalidOperationException){blocked=true;}Check("未确认时拒绝入库",blocked);
      BridgeConfig config=store.GetBridgeConfig();using(HttpClient http=new HttpClient(new HttpClientHandler{UseProxy=false})) {
        foreach(string mode in new[]{"unconfirmed","legacy-classification","invalid-path"}) {
          AnalysisResult plan=json.Deserialize<AnalysisResult>(json.Serialize(file.Analysis));if(mode=="legacy-classification")plan.classificationVersion="";if(mode=="invalid-path")plan.collections=new[]{"../非法路径"};
          using(HttpRequestMessage request=new HttpRequestMessage(HttpMethod.Post,"http://127.0.0.1:"+config.port+"/paper-inbox/submit")){request.Headers.Add("X-PaperInbox-Token",config.token);request.Content=new StringContent(json.Serialize(new{id=Guid.NewGuid().ToString("N"),path=file.Path,size=file.Size,lastModified=file.LastModified,plan=plan,confirmed=mode!="unconfirmed"}),Encoding.UTF8,"application/json");using(HttpResponseMessage response=await http.SendAsync(request))Check("插件拒绝"+mode,(int)response.StatusCode==400);}
        }
      }
      File.WriteAllText(Path.Combine(root,"analysis-only.json"),"{\"ready\":true}");for(int i=0;i<120&&!File.Exists(Path.Combine(root,"analysis-verified.json"));i++)await Task.Delay(250);Check("分析阶段数据库仍为零条目",File.Exists(Path.Combine(root,"analysis-verified.json")));
      file.Status="Approved";await client.Submit(file,true);await client.Submit(file,true);ServerJob first=await Wait(client,file.Id);Check("确认后完成材料分类入库",first.status=="done"&&!string.IsNullOrEmpty(first.attachmentKey));Check("PDF已自动在Zotero打开",first.message.Contains("已在 Zotero 阅读器中打开"));
      string duplicate=Path.Combine(root,"files","duplicate.pdf");File.Copy(info.Path,duplicate);DownloadFile dup=DownloadFile.FromPath(duplicate);PendingFile second=new PendingFile{Id=Guid.NewGuid().ToString("N"),Path=dup.Path,Name=dup.Name,Size=dup.Size,LastModified=dup.LastModified,Status="Approved",Analysis=json.Deserialize<AnalysisResult>(json.Serialize(file.Analysis))};second.Analysis.collections=new[]{"我的阅读/待精读"};second.Analysis.Validate();await client.Submit(second,true);ServerJob again=await Wait(client,second.Id);Check("自定义路径提交成功且复用原文献和附件",again.status=="done"&&again.parentKey==first.parentKey&&again.attachmentKey==first.attachmentKey);
      AnalysisResult wrong=json.Deserialize<AnalysisResult>(json.Serialize(file.Analysis));wrong.sourceHash=new string('0',64);PendingFile changed=new PendingFile{Id=Guid.NewGuid().ToString("N"),Path=info.Path,Name=info.Name,Size=info.Size,LastModified=info.LastModified,Status="Approved",Analysis=wrong};blocked=false;try{await client.Submit(changed);}catch(InvalidDataException){blocked=true;}Check("正文分析后文件变化会拦截",blocked);
      Check("两个来源文件均保留",File.Exists(info.Path)&&File.Exists(duplicate));
    }
  }
  static async Task<ServerJob> Wait(BridgeClient c,string id){for(int i=0;i<180;i++){BridgeStatus status=await c.Status(new[]{id});ServerJob job;if(status.requests.TryGetValue(id,out job)&&job.status!="pending")return job;await Task.Delay(250);}throw new TimeoutException("Import timeout");}
}
