using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Web.Script.Serialization;

namespace PaperInbox {
  public sealed class BridgeConfig {
    public int protocolVersion {get;set;}
    public string token {get;set;}
    public int port {get;set;}
    public string bridgeVersion {get;set;}
  }
  public sealed class ServerJob {
    public string id {get;set;}
    public string status {get;set;}
    public string message {get;set;}
    public string result {get;set;}
    public string parentKey {get;set;}
    public string attachmentKey {get;set;}
  }
  public sealed class BridgeStatus {
    public bool ready {get;set;}
    public int protocolVersion {get;set;}
    public string version {get;set;}
    public Dictionary<string,ServerJob> requests {get;set;}
  }
  public sealed class SubmitResponse {public ServerJob request {get;set;}}
  public sealed class CollectionCatalog {public string[] collections {get;set;}}
  public sealed class ExtractResponse {public string text {get;set;}public string sourceHash {get;set;}public string[] collections {get;set;}public int totalPages {get;set;}}
  public sealed class BridgeUnavailable : Exception {public BridgeUnavailable(string message):base(message) {}}
  public sealed class BridgeClient : IDisposable {
    public static bool SupportsTopicRouting(BridgeStatus status){Version version;return status!=null&&status.ready&&status.protocolVersion==3&&Version.TryParse(status.version,out version)&&version.CompareTo(new Version(0,5,0))>=0;}
    readonly HttpClient client;
    readonly StateStore store;
    public BridgeClient(StateStore store) {
      this.store=store;
      client=new HttpClient(new HttpClientHandler{UseProxy=false});client.Timeout=TimeSpan.FromSeconds(90);
      client.DefaultRequestHeaders.UserAgent.ParseAdd("PaperInboxTray/0.5.0");
    }
    async Task<T> Post<T>(string endpoint,object body) {
      BridgeConfig config=store.GetBridgeConfig();
      using(HttpRequestMessage message=new HttpRequestMessage(HttpMethod.Post,"http://127.0.0.1:"+config.port.ToString()+endpoint)) {
        message.Headers.Add("X-PaperInbox-Token",config.token);
        message.Content=new StringContent(store.Json.Serialize(body),Encoding.UTF8,"application/json");
        HttpResponseMessage response;
        try {using(CancellationTokenSource timeout=new CancellationTokenSource(TimeSpan.FromSeconds(endpoint.EndsWith("/extract")?90:4)))response=await client.SendAsync(message,timeout.Token);}catch(HttpRequestException){throw new BridgeUnavailable("未连接到 Zotero；已确认的任务会保留。");}
        catch(TaskCanceledException){throw new BridgeUnavailable("Zotero 暂未响应，稍后会重试。");}
        using(response) {
          string result=await response.Content.ReadAsStringAsync();
          if(response.StatusCode==HttpStatusCode.NotFound)throw new BridgeUnavailable("Zotero 已运行，请安装或启用新版连接插件。");
          if(response.StatusCode==HttpStatusCode.Forbidden)throw new BridgeUnavailable("连接密钥尚未同步，请重新启动 Zotero 后重试。");
          if(response.StatusCode==HttpStatusCode.BadRequest)throw new InvalidDataException("Zotero 无法提取此 PDF 或确认结果不完整；请检查文件和连接插件版本。");
          if(response.StatusCode==HttpStatusCode.Conflict)throw new InvalidDataException("PDF 已发生变化，请等下载完成后重新确认。");
          if(!response.IsSuccessStatusCode)throw new BridgeUnavailable("Zotero 连接返回错误，任务已保留。");
          try {return store.Json.Deserialize<T>(result);}catch {throw new BridgeUnavailable("Zotero 连接响应格式不正确，请检查连接插件版本。");}
        }
      }
    }
    public Task<CollectionCatalog> Collections(){return Post<CollectionCatalog>("/paper-inbox/collections",new{});}
    public Task<BridgeStatus> Status(string[] ids) {return Post<BridgeStatus>("/paper-inbox/status",new{ids=ids});}
    public Task<ExtractResponse> Extract(PendingFile file,int maxPages,int maxCharacters) {return Post<ExtractResponse>("/paper-inbox/extract",new{id=file.Id,path=file.Path,size=file.Size,lastModified=file.LastModified,maxPages=maxPages,maxCharacters=maxCharacters});}
    public Task<SubmitResponse> Submit(PendingFile file,bool autoOpen=true) {
      if(file.Status!="Approved"&&file.Status!="Importing")throw new InvalidOperationException("该文件尚未得到用户确认。");
      if(file.Analysis!=null&&file.Analysis.classificationVersion!=FormulaCatalog.Version)throw new InvalidDataException("请重新分析并确认此文件的分类结果。");
      if(file.Analysis==null)throw new InvalidOperationException("尚未确认 API 分类结果。");
      return Post<SubmitResponse>("/paper-inbox/submit",new{id=file.Id,path=file.Path,size=file.Size,lastModified=file.LastModified,plan=file.Analysis,autoOpen=autoOpen,confirmed=true});
    }
    public void Dispose() {client.Dispose();}
  }
}
