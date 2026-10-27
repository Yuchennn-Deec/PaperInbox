using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace PaperInbox {
  public sealed class FormulaFamily {public string code {get;set;}public string formula {get;set;}public string name {get;set;}public string definition {get;set;}}
  public sealed class FormulaData {public string version {get;set;}public string root {get;set;}public FormulaFamily[] families {get;set;}public string[] existingPaths {get;set;}}
  public sealed class MaterialAssignment {public string family {get;set;}public string compound {get;set;}public string evidence {get;set;}public string collectionPath {get;set;}public string evidenceKind {get;set;}}
  public static class FormulaCatalog {
    public const string Version="chemical-formula-v1",Root="按化学通式";
    public const string Pending=Root+"/待核对（信息不足或附件缺失）/AI识别待核对";
    public const string TopicRoot="按主题",RoutingVersion="flat-topics-v4";
    static readonly FormulaData data=Load();
    static FormulaData Load(){using(Stream stream=typeof(FormulaCatalog).Assembly.GetManifestResourceStream("PaperInbox.formula-catalog.json"))using(StreamReader reader=new StreamReader(stream))return new JavaScriptSerializer().Deserialize<FormulaData>(reader.ReadToEnd());}
    public static FormulaData Data {get{return data;}}
    public static string[] MaterialPaths(AnalysisResult value){return (value.materials??new MaterialAssignment[0]).Where(m=>m!=null&&!string.IsNullOrWhiteSpace(m.collectionPath)).Select(m=>m.collectionPath).Distinct().Take(8).ToArray();}
    public static bool UpgradeTopicRouting(AnalysisResult value){return MaterialThemes.Upgrade(value);}
    public static string PromptCatalog(string[] livePaths) {
      var paths=(livePaths??new string[0]).Concat(data.existingPaths).Distinct().ToArray();
      return new JavaScriptSerializer().Serialize(new{formulaRoot=Root,topicRoot=TopicRoot,families=data.families,existingPaths=paths});
    }
    // Normalize typography only. Formula letters stay case-sensitive (Co is not CO).
    public static string Normalize(string value) {
      string s=Regex.Replace(value??"",@"<[^>]*>","");
      for(int i=0;i<10;i++){s=s.Replace((char)('₀'+i),(char)('0'+i));}
      return Regex.Replace(s.Replace('−','-').Replace('–','-').Replace('′','\''),@"\s+","");
    }
    public static void Classify(AnalysisResult result,string document,string[] livePaths=null) {
      var tags=new List<string>((result.tags??new string[0]).Where(t=>t!=null&&!t.StartsWith("化学通式:")&&!t.StartsWith("材料:")&&t!="整理:通式待核对"));
      var verified=new List<MaterialAssignment>();var paths=new List<string>();string normalized=Normalize(document);
      foreach(MaterialAssignment m in (result.materials??new MaterialAssignment[0]).Take(8)) {
        if(m==null)continue;FormulaFamily family=data.families.FirstOrDefault(f=>f.code==m.family);
        string compound=(m.compound??"").Trim(),evidence=(m.evidence??"").Trim(),proof=Normalize(evidence);
        if(proof.Length<12||proof.Length>500||!normalized.Contains(proof))continue;
        bool nonformula=family!=null&&(family.code=="background"||family.code=="nonstoich");
        if(compound.Length==0||compound.Length>80)continue;
        string path;
        if(nonformula){if(compound.IndexOfAny(new[]{'/','\\','\r','\n'})>=0)continue;path=Root+"/"+family.name+"/"+compound;}
        else{if(!MaterialThemes.IsFormula(compound)||!proof.Contains(Normalize(compound)))continue;path=MaterialThemes.PathFor(compound,m.family,livePaths);string group=string.Join("/",path.Split('/').Take(2))+"/";if(!string.IsNullOrEmpty(m.collectionPath)&&(livePaths??new string[0]).Contains(m.collectionPath)&&m.collectionPath.StartsWith(group,StringComparison.Ordinal))path=m.collectionPath;}
        if(!MaterialThemes.ValidPath(path))continue;
        paths.Add(path);verified.Add(new MaterialAssignment{family=m.family,compound=compound,evidence=evidence,collectionPath=path,evidenceKind="pdf"});
        if(!nonformula)tags.Add("材料:"+compound);
        if(family!=null)tags.Add("化学通式:"+family.formula);
      }
      if(verified.Count==0&&result.isAcademic) {
        foreach(string subject in MaterialThemes.TitleSubjects(result.title)) {
          if(!normalized.Contains(Normalize(subject)))continue;
          string path=MaterialThemes.PathFor(subject,null,livePaths);paths.Add(path);
          verified.Add(new MaterialAssignment{family="title-material",compound=subject,evidence="题名给出该材料，PDF文字中也能找到该化学式。",collectionPath=path,evidenceKind="title+pdf"});tags.Add("材料:"+subject);
        }
      }
      result.materials=verified.ToArray();result.materialSubjects=verified.Where(m=>MaterialThemes.IsFormula(m.compound)).Select(m=>m.compound).Distinct().ToArray();
      bool confident=result.isAcademic&&!double.IsNaN(result.confidence)&&!double.IsInfinity(result.confidence)&&result.confidence>=0.7;
      if(paths.Count==0||!confident){paths.Clear();paths.Add(Pending);tags.Add("整理:材料主题需核对");}
      result.collections=paths.Distinct().ToArray();result.tags=MaterialThemes.TagsFor(tags,result.materialSubjects,result.collections);result.classificationVersion=Version;
      result.routingVersion=RoutingVersion;result.existingCollections=livePaths??new string[0];result.classificationMode=paths.Contains(Pending)?"review":"material";result.materialSource=verified.Any(m=>m.evidenceKind=="pdf")?"pdf":"title+pdf";
      DualClassification.ApplyAuto(result);
      if(verified.Count==0)result.reason=(result.reason??"")+"\n没有可靠的材料化学式，请从历史分类选择或手动填写材料；研究主题仍可单独核对。";
    }
  }
}
