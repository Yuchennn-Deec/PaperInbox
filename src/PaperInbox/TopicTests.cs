using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
namespace PaperInbox {
 public static class TopicTests {
  public static AnalysisResult CuO(){return new AnalysisResult{isAcademic=true,title="Magnetoelectric effect and phase transitions in CuO in external magnetic fields",authors=new string[0],doi="",year="",confidence=.98,classificationVersion=FormulaCatalog.Version,sourceHash=new string('a',64),materials=new MaterialAssignment[0],collections=new[]{FormulaCatalog.Pending},tags=new[]{"主题:CuO多铁性","主题:磁电效应","主题:磁场诱导相变","方法:中子衍射","整理:通式待核对"},summary="研究CuO的磁电效应及磁场诱导相变。",reason="主要研究材料为CuO，研究磁电效应与磁场诱导相变。"};}
  public static void Run(string folder,Action<string,bool> check) {
   string cuo="按化学通式/MO（二元氧化物）/CuO",pyroxene="按化学通式/AMX2O6（辉石）/CaMnGe2O6";
   check("历史主题功能要求配套新版插件",!BridgeClient.SupportsTopicRouting(new BridgeStatus{ready=true,protocolVersion=3,version="0.4.2"})&&BridgeClient.SupportsTopicRouting(new BridgeStatus{ready=true,protocolVersion=3,version="0.5.0"}));
   AnalysisResult a=CuO();FormulaCatalog.Classify(a,"The magnetoelectric effect and field induced phase transitions in CuO are investigated.");a.Validate();
   check("材料主题以CuO化学式为核心",a.formulaCollections.SequenceEqual(new[]{cuo})&&a.topicCollections.Length==3&&a.materialSubjects.SequenceEqual(new[]{"CuO"}));
   check("研究主题生成独立目录且不插入材料层",a.topicCollections.Contains("按主题/磁电效应")&&a.topicCollections.All(p=>p.StartsWith("按主题/")&&p.Split('/').Length==2)&&a.tags.Contains("主题:磁电效应"));
   check("方法标签继续保留",a.tags.Contains("方法:中子衍射")&&a.tags.Contains("材料:CuO"));
   check("CuO复用已有具体材料目录",MaterialThemes.PathFor("CuO",null,new[]{"氧化物/CuO"})==cuo);
   check("NaNiO2按正确化学计量归类",MaterialThemes.PathFor("NaNiO2").Contains("ABO2（同计量比含氧化合物）"));
   check("未知结构仍保留具体化学式",MaterialThemes.PathFor("Mo5Si3")=="按化学通式/未归入通式/Mo5Si3");
   check("材料化学式支持下标与固溶变量",MaterialThemes.IsFormula("CuO")&&MaterialThemes.IsFormula("Ni1−xMnxPS3")&&MaterialThemes.IsFormula("CaMnGe₂O₆"));
   check("研究主题和普通缩写不被当作化学式",!MaterialThemes.IsFormula("磁电效应")&&!MaterialThemes.IsFormula("CuO多铁性")&&!MaterialThemes.IsFormula("API")&&MaterialThemes.TitleSubjects("PDF API NO CuO").SequenceEqual(new[]{"CuO"}));
   a=CuO();a.collections=new[]{"我的研究/CuO"};a.confidence=.3;a.Validate();check("手填分类路径不被低置信度覆盖",a.collections.Single()=="我的研究/CuO");
   a=CuO();a.collections=new string[0];bool invalid=false;try{a.Validate();}catch(InvalidDataException){invalid=true;}check("空路径仍明确拦截",invalid);
   a=CuO();a.title="A generic magnetism review";a.tags=new string[0];FormulaCatalog.Classify(a,"No specific chemical material was investigated.");check("没有材料证据时仍待核对",a.collections.SequenceEqual(new[]{FormulaCatalog.Pending}));
   StateStore store=new StateStore(Path.Combine(folder,"material-migration"));AppState state=new AppState();state.Pending.Add(new PendingFile{Id="preserve-id",Status="Review",Size=100,LastModified=1,Analysis=CuO()});store.Save(state);AppState loaded=store.Load();
   check("旧CuO分析无付费重跑即可给出材料主题",loaded.Pending[0].Analysis.formulaCollections.Single()==cuo&&loaded.Pending[0].Analysis.topicCollections.Length==3&&loaded.Pending[0].Analysis.materialSource=="title"&&loaded.Pending[0].Analysis.sourceHash==new string('a',64));
   check("迁移保留确认门槛和原分析理由",loaded.Pending[0].Status=="Review"&&loaded.Pending[0].Id=="preserve-id"&&loaded.Pending[0].Analysis.previousReason==CuO().reason&&File.Exists(Path.Combine(store.Folder,"state.json.before-flat-topics-v4")));
   state.Pending[0].Status="Approved";store.Save(state);check("已确认任务不静默改换路径",store.Load().Pending[0].Analysis.collections.Single()==FormulaCatalog.Pending);
   a=CuO();a.collections=new[]{"按主题/磁电效应"};a.classificationMode="topic";check("上一版研究主题可升级成两套分类",MaterialThemes.Upgrade(a)&&a.formulaCollections.Single()==cuo&&a.topicCollections.Length==3);
   a=CuO();a.collections=new[]{"自选/CuO"};a.classificationMode="custom";check("用户手改路径不会被迁移覆盖",!MaterialThemes.Upgrade(a)&&a.collections.Single()=="自选/CuO");
   a=CuO();MaterialThemes.Upgrade(a);AppState history=new AppState();MaterialThemes.Remember(history,a);MaterialThemes.Remember(history,a);
   check("确认过的主题去重并累计使用次数",history.MaterialThemes.Count==4&&history.MaterialThemes.All(h=>h.UseCount==2&&h.Subjects.Single()=="CuO"));
   StateStore historyStore=new StateStore(Path.Combine(folder,"material-history"));historyStore.Save(history);check("历史主题跨重启保留",historyStore.Load().MaterialThemes.Any(h=>h.Path==cuo)&&historyStore.Load().MaterialThemes.Count==4);
   a.existingCollections=new[]{cuo,pyroxene,"综述与理论背景","待核对/分类"};MaterialThemeOption[] options=MaterialThemes.Options(a,history.MaterialThemes);
   check("本机记录与Zotero分类合并去重",options.Count(o=>o.Path==cuo)==1&&options.First().Path==cuo&&options.First().Source=="最近使用");
   check("可搜索化学式与下标",MaterialThemes.Filter(options,"CaMnGe₂O₆").Any(o=>o.Path==pyroxene)&&MaterialThemes.Filter(options,"cuo").Any(o=>o.Path==cuo));
   check("可单独筛选最近使用与Zotero来源",MaterialThemes.Filter(options,"","最近使用").Length==4&&MaterialThemes.Filter(options,"CaMnGe2O6","Zotero分类").Length==1);
   check("历史排除待核对及非材料非主题目录",!options.Any(o=>o.Path.Contains("待核对"))&&options.Any(o=>o.Path=="综述与理论背景"));
   int before=history.MaterialThemes.Count;using(MaterialHistoryForm picker=new MaterialHistoryForm(a,history.MaterialThemes)){AiTests.Render(picker,Path.Combine(folder,"dual-history-preview.png"));picker.Close();}check("取消历史选择不改确认记录",history.MaterialThemes.Count==before&&history.MaterialThemes[0].UseCount==2);
   using(ReviewForm review=new ReviewForm(a,history.MaterialThemes))AiTests.Render(review,Path.Combine(folder,"dual-review-preview.png"));
  }
 }
}
