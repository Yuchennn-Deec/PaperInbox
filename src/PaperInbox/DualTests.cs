using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
namespace PaperInbox {
 public static class DualTests {
  public static void Run(string folder,Action<string,bool> check) {
   string[] subjects={"CuO"},topics={"磁电效应","阻挫磁性"};
   string[] both=DualClassification.Generate(subjects,topics,"both");
   check("默认同时生成通式与独立主题路径",both.Length==3&&both.Contains("按化学通式/MO（二元氧化物）/CuO")&&both.Contains("按主题/磁电效应"));
   check("可单独生成化学通式",DualClassification.Generate(subjects,topics,"formula").All(p=>p.StartsWith("按化学通式/")));
   check("可单独生成研究主题",DualClassification.Generate(subjects,topics,"topic").Length==2&&DualClassification.Generate(subjects,topics,"topic").All(p=>p.StartsWith("按主题/")&&p.Split('/').Length==2));
   check("缺材料时主题目录不猜化学式",DualClassification.Generate(new string[0],topics,"both").SequenceEqual(new[]{"按主题/磁电效应","按主题/阻挫磁性"}));
   check("方法标签不会自动成为研究主题",DualClassification.Topics(new[]{"方法:中子衍射","主题：磁电效应"}).SequenceEqual(new[]{"磁电效应"}));
   check("主题中的斜杠不增加目录层级",DualClassification.TopicPaths(new[]{"自旋/晶格"},subjects).Single()=="按主题/自旋／晶格");
   var many=DualClassification.Generate(new[]{"CuO","NiO","CoO","MnO","FeO","ZnO","MgO","CaO"},Enumerable.Range(1,8).Select(i=>"主题"+i).ToArray(),"both");
   AnalysisResult a=TopicTests.CuO();a.collections=many;a.Validate();check("多材料共享主题目录不重复",many.Length==16&&a.collections.Length==16);
   a=TopicTests.CuO();MaterialThemes.Upgrade(a);var options=MaterialThemes.Options(a,new MaterialTheme[0]);
   check("历史分类可按化学通式筛选",MaterialThemes.Filter(options,"CuO","全部","化学通式").Any()&&MaterialThemes.Filter(options,"CuO","全部","化学通式").All(o=>o.Kind=="化学通式"));
   check("历史分类可按研究主题搜索筛选",MaterialThemes.Filter(options,"磁电效应","全部","研究主题").Single().Path=="按主题/磁电效应");
   check("双分类及单分类模式可从路径判别",DualClassification.ModeFor(both)=="both"&&DualClassification.ModeFor(new[]{both[0]})=="formula"&&DualClassification.ModeFor(new[]{both[1]})=="topic");
   check("默认Prompt明确要求两套分类",AiClient.DefaultClassificationPrompt.Contains("按化学通式")&&AiClient.DefaultClassificationPrompt.Contains("按研究主题")&&AiClient.DefaultClassificationPrompt.Contains("collections中同时给出两套完整路径"));
   check("空Prompt恢复默认并自动附加输出格式",AiClient.BuildSystemPrompt(new ApiProfile{ClassificationPrompt="  "}).StartsWith(AiClient.DefaultClassificationPrompt)&&AiClient.BuildSystemPrompt(new ApiProfile()).Contains("【程序输出格式要求】"));
   check("用户Prompt与固定JSON要求共同发送",AiClient.BuildSystemPrompt(new ApiProfile{ClassificationPrompt="仅按本文证据处理"}).StartsWith("仅按本文证据处理\n\n【程序输出格式要求】"));
   bool rejected=false;try{AiClient.BuildSystemPrompt(new ApiProfile{ClassificationPrompt=new string('p',20001)});}catch(InvalidDataException){rejected=true;}check("超长Prompt在发送前拦截",rejected);
   var state=new StateStore(Path.Combine(folder,"dual-prompt-profiles"));var store=new AiSettingsStore(state);var config=new AiConfiguration();
   config.Profile("openai").ClassificationPrompt="规则A";config.Profile("deepseek").ClassificationPrompt="规则B";config.Profile("openai").EncryptedKey=AiSettingsStore.Encrypt("local-test-key");store.Save(config);var loaded=store.Load();
   check("Prompt按服务商分别保存且密钥保留",loaded.Profile("openai").ClassificationPrompt=="规则A"&&loaded.Profile("deepseek").ClassificationPrompt=="规则B"&&AiSettingsStore.Decrypt(loaded.Profile("openai").EncryptedKey)=="local-test-key");
   Task.Run(()=>Network(check)).GetAwaiter().GetResult();
  }
  static async Task Network(Action<string,bool> check) {
   foreach(string protocol in new[]{"openai","anthropic","gemini"})using(var server=new FakeAiServer(protocol)) {
    ApiProfile p=server.Profile();p.ClassificationPrompt="CUSTOM_DUAL_RULE_2026";server.ResultOverride=TopicTests.CuO();server.ResultOverride.collections=DualClassification.Generate(new[]{"CuO"},new[]{"多铁性","磁电效应","磁场诱导相变"},"both");
    var extracted=new ExtractResponse{text="The magnetoelectric effect and phase transitions in CuO are investigated. "+new string('a',300),sourceHash=new string('b',64),collections=new string[0]};
    var result=await new AiClient().Analyze(p,"local",extracted);
    check(protocol+"实际发送自定义Prompt及固定结构",server.Count==1&&server.LastBody.Contains("CUSTOM_DUAL_RULE_2026")&&server.LastBody.Contains("materials")&&server.LastBody.Contains("isAcademic"));
    check(protocol+"响应同时进入两套分类",result.formulaCollections.Length==1&&result.topicCollections.Contains("按主题/磁电效应"));
   }
  }
 }
}
