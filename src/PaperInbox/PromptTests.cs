using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
namespace PaperInbox {
 public static class PromptTests {
  public static void Run(string folder,Action<string,bool> check) {
   string[] paths;check("Prompt支持任意学科的多层路径",PromptRouting.TryPaths(new[]{"机器学习/表示学习/对比学习","法学/合同法"},out paths)&&paths.Length==2);
   foreach(string bad in new[]{"../合同法","法学//合同法","法学/..","A/B/C/D/E/F","法学/含\n换行"})check("路径格式拦截："+bad.Replace("\n","换行"),!PromptRouting.TryPaths(new[]{bad},out paths));
   check("超过80条路径不静默截断",!PromptRouting.TryPaths(Enumerable.Range(0,81).Select(i=>"主题/"+i).ToArray(),out paths));
   check("同一主题不随材料重复生成",DualClassification.Generate(new[]{"CuO","FeTa2O6"},new[]{"磁关联"},"topic").SequenceEqual(new[]{"按主题/磁关联"}));
   check("材料助手可去掉旧主题名中的材料前缀",DualClassification.TopicPaths(new[]{"CuO多铁性"},new[]{"CuO"}).Single()=="按主题/多铁性");
   var old=TopicTests.CuO();old.routingVersion="dual-classification-v3";old.classificationMode="both";old.materialSubjects=new[]{"CuO"};old.collections=new[]{"按化学通式/MO（二元氧化物）/CuO","按主题/CuO/磁电效应"};old.researchTopics=new[]{"磁电效应"};
   var store=new StateStore(Path.Combine(folder,"prompt-migration"));var state=new AppState();state.Pending.Add(new PendingFile{Id="preserve",Status="Review",Analysis=old});store.Save(state);var loaded=store.Load();var migrated=loaded.Pending.Single().Analysis;
   check("旧默认待核对路径本机去除材料层",migrated.collections.Contains("按主题/磁电效应")&&!migrated.collections.Any(p=>p.StartsWith("按主题/CuO/"))&&loaded.Pending.Single().Status=="Review"&&migrated.sourceHash==old.sourceHash);
   check("旧分析迁移前有独立备份",File.Exists(Path.Combine(store.Folder,"state.json.before-flat-topics-v4")));
   var generic=AiTests.Sample();generic.collections=new[]{"法学/合同法"};generic.classificationMode="prompt";generic.routingVersion=PromptRouting.Version;check("自定义Prompt路径不被材料迁移覆盖",!MaterialThemes.Upgrade(generic)&&generic.collections.Single()=="法学/合同法");
   generic.tags=new[]{"合同解释","领域:法学"};generic.Validate();check("任意学科标签保持原样",generic.tags.SequenceEqual(new[]{"合同解释","领域:法学"}));
   generic.existingCollections=new[]{"法学/合同法","机器学习/表示学习"};check("历史分类可读非材料学科路径",MaterialThemes.Options(generic,null).Any(o=>o.Path=="机器学习/表示学习"));
   Task.Run(()=>Network(check)).GetAwaiter().GetResult();
  }
  static async Task Network(Action<string,bool> check) {
   foreach(string protocol in new[]{"openai","anthropic","gemini"})using(var server=new FakeAiServer(protocol)) {
    var p=server.Profile();p.ClassificationPrompt="只按研究主题归类，不包含材料层。返回按主题/磁关联。";var answer=TopicTests.CuO();answer.title="Magnetic correlations in FeTa2O6";answer.collections=new[]{"按主题/磁关联"};answer.tags=new[]{"磁关联","领域:凝聚态物理"};server.ResultOverride=answer;
    var result=await new AiClient().Analyze(p,"local",new ExtractResponse{text=new string('文',2000),sourceHash=new string('b',64),collections=new[]{"按主题/FeTa2O6/二维反铁磁体"}});
    check(protocol+"自定义Prompt路径原样采用且不追加化学式",result.collections.SequenceEqual(new[]{"按主题/磁关联"})&&result.classificationMode=="prompt"&&result.promptCollections.SequenceEqual(result.collections));
    check(protocol+"通用规则不注入材料通式目录",!server.LastBody.Contains("formulaRoot")&&server.LastBody.Contains("collections")&&result.tags.Contains("领域:凝聚态物理"));
    p.ClassificationPrompt="";result=await new AiClient().Analyze(p,"local",new ExtractResponse{text="The compound CuO shows a magnetoelectric response. "+new string('a',300),sourceHash=new string('b',64)});check(protocol+"默认方案也优先采用有效API路径",result.collections.SequenceEqual(new[]{"按主题/磁关联"}));
    p.ClassificationPrompt="按法学子领域分类。";answer.collections=new[]{"法学/合同法/合同解释"};answer.tags=new[]{"合同解释"};result=await new AiClient().Analyze(p,"local",new ExtractResponse{text=new string('法',2000),sourceHash=new string('c',64),collections=new string[0]});
    check(protocol+"其他学科不被强制改为材料路径",result.collections.Single()=="法学/合同法/合同解释"&&result.tags.Single()=="合同解释");
    answer.collections=new[]{"../非法"};result=await new AiClient().Analyze(p,"local",new ExtractResponse{text=new string('法',2000),sourceHash=new string('c',64)});check(protocol+"非法路径保留分析供手工核对",result.collections.Single()==PromptRouting.Pending&&result.title==answer.title&&result.reason.Contains("无需重新调用API"));
   }
  }
 }
}
