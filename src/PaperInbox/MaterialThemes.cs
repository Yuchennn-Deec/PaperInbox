using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PaperInbox {
  public sealed class MaterialTheme {
    public string Path {get;set;}
    public string[] Subjects {get;set;}
    public string LastUsedUtc {get;set;}
    public int UseCount {get;set;}
  }
  public sealed class MaterialThemeOption {
    public string Path,Subject,Source;
    public string Kind {get{return DualClassification.Kind(Path);}}
    public string Topic {get{return DualClassification.TopicsFromPaths(new[]{Path}).FirstOrDefault()??"";}}
    public override string ToString(){return (string.IsNullOrEmpty(Topic)?(string.IsNullOrEmpty(Subject)?Path:Subject):Topic)+"  ·  "+Kind+"  ·  "+Source+"  ·  "+Path;}
  }
  public static class MaterialThemes {
    public const string RoutingVersion="flat-topics-v4";
    static readonly HashSet<string> elements=new HashSet<string>(("H He Li Be B C N O F Ne Na Mg Al Si P S Cl Ar K Ca Sc Ti V Cr Mn Fe Co Ni Cu Zn Ga Ge As Se Br Kr Rb Sr Y Zr Nb Mo Tc Ru Rh Pd Ag Cd In Sn Sb Te I Xe Cs Ba La Ce Pr Nd Pm Sm Eu Gd Tb Dy Ho Er Tm Yb Lu Hf Ta W Re Os Ir Pt Au Hg Tl Pb Bi Po At Rn Fr Ra Ac Th Pa U Np Pu Am Cm Bk Cf Es Fm Md No Lr Rf Db Sg Bh Hs Mt Ds Rg Cn Nh Fl Mc Lv Ts Og").Split(' '));
    static readonly HashSet<string> generic=new HashSet<string>(new[]{"A","M","X","R","T","Ln"});
    static string Norm(string value){return FormulaCatalog.Normalize(value).Replace('／','/');}
    public static bool ValidPath(string path){return !string.IsNullOrWhiteSpace(path)&&path.Length<=240&&!path.Any(char.IsControl)&&path.Split('/').Length<=5&&path.Split('/').All(p=>p.Trim().Length>0&&p.Length<=80&&p!="."&&p!="..");}
    public static bool IsFormula(string value,bool allowGeneric=true) {
      string s=Norm(value);
      if(s.Length==0||s.Length>80||!Regex.IsMatch(s,@"^[A-Za-z0-9().\[\]·+\-/'αβγδεδ]+$"))return false;
      MatchCollection tokens=Regex.Matches(s,@"[A-Z][a-z]?");
      string rest=Regex.Replace(s,@"[A-Z][a-z]?","");
      bool placeholders=tokens.Cast<Match>().Any(m=>!elements.Contains(m.Value));
      bool allowedPlaceholder=!placeholders||Regex.IsMatch(s,@"\d")||FormulaCatalog.Data.families.Any(f=>Norm(f.formula)==s);
      return tokens.Count>=1&&allowedPlaceholder&&tokens.Cast<Match>().All(m=>elements.Contains(m.Value)||(allowGeneric&&generic.Contains(m.Value)))&&Regex.IsMatch(rest,@"^[0-9().\[\]·+\-/'xynpαβγδεδ]*$");
    }
    public static string SubjectFromPath(string path) {
      string[] parts=(path??"").Split('/');if(parts.Length>=3&&parts[0]==DualClassification.TopicRoot&&IsFormula(parts[1]))return parts[1];string leaf=parts.Last();
      Match prefix=Regex.Match(leaf,@"^[A-Za-z0-9().\[\]·+\-−/′'αβγδεδ]+");
      if(!prefix.Success)return "";string value=prefix.Value.TrimEnd('.','-');return IsFormula(value)?value:"";
    }
    public static string[] TitleSubjects(string title) {
      string plain=Regex.Replace(title??"",@"<[^>]*>","");
      for(int i=0;i<10;i++)plain=plain.Replace((char)('₀'+i),(char)('0'+i));
      List<string> result=new List<string>();
      foreach(Match m in Regex.Matches(plain,@"(?<![A-Za-z0-9])(?:[A-Z][a-z]?(?:\d+(?:\.\d+)?)?){2,}(?![A-Za-z0-9])")) {
        if(Regex.IsMatch(plain.Substring(m.Index+m.Length),@"^\s+\d"))continue;
        if(IsFormula(m.Value,false)&&Regex.IsMatch(m.Value,@"[a-z0-9]")&&!result.Contains(m.Value))result.Add(m.Value);
      }
      return result.Take(8).ToArray();
    }
    static string InferFamily(string compound) {
      string s=Norm(compound);MatchCollection ms=Regex.Matches(s,@"([A-Z][a-z]?)(\d*)");
      if(string.Concat(ms.Cast<Match>().Select(m=>m.Value))!=s)return null;
      var counts=new Dictionary<string,int>();
      foreach(Match m in ms){int n=1;if(m.Groups[2].Value.Length>0&&(!int.TryParse(m.Groups[2].Value,out n)||n>10000))return null;if(counts.ContainsKey(m.Groups[1].Value))counts[m.Groups[1].Value]+=n;else counts[m.Groups[1].Value]=n;}
      int oxygen;if(!counts.TryGetValue("O",out oxygen))return null;
      int[] others=counts.Where(p=>p.Key!="O").Select(p=>p.Value).ToArray();
      if(others.Length==1&&others[0]==1&&oxygen==1)return "monoxide";
      if(others.Length==1&&others[0]==1&&oxygen==2)return "dioxide";
      if(others.Length==1&&others[0]==2&&oxygen==3)return "sesquioxide";
      if(others.Length==2&&others.All(n=>n==1)&&oxygen==2)return "abo2";
      return null;
    }
    public static string PathFor(string subject,string declaredFamily=null,string[] livePaths=null) {
      string normalized=Norm(subject),safe=subject.Trim().Replace('/','／');
      if(!IsFormula(subject))throw new ArgumentException("材料主题请填写化学式，如CuO；研究内容请放在标签中。");
      string[] known=(livePaths??new string[0]).Concat(FormulaCatalog.Data.existingPaths).Where(ValidPath).Distinct().ToArray();
      string existing=known.FirstOrDefault(p=>p.StartsWith(FormulaCatalog.Root+"/",StringComparison.Ordinal)&&Norm(p.Split('/').Last())==normalized)
        ??known.FirstOrDefault(p=>p.StartsWith(FormulaCatalog.Root+"/",StringComparison.Ordinal)&&Norm(SubjectFromPath(p))==normalized);
      if(existing!=null)return existing;
      existing=known.FirstOrDefault(p=>Norm(p.Split('/').Last())==normalized&&!p.Contains("待核对"));if(existing!=null)return existing;
      string code=InferFamily(subject)??declaredFamily;
      FormulaFamily family=FormulaCatalog.Data.families.FirstOrDefault(f=>f.code==code&&f.code!="review"&&f.code!="background"&&f.code!="nonstoich");
      if(family!=null&&Norm(family.formula)==normalized)return FormulaCatalog.Root+"/"+family.name;
      return FormulaCatalog.Root+"/"+(family==null?"未归入通式":family.name)+"/"+safe;
    }
    public static string[] PathsFromSubjects(IEnumerable<string> subjects,string[] livePaths=null) {return (subjects??new string[0]).Where(s=>!string.IsNullOrWhiteSpace(s)).Select(s=>PathFor(s,null,livePaths)).Distinct().Take(8).ToArray();}
    public static string[] TagsFor(IEnumerable<string> original,IEnumerable<string> subjects,IEnumerable<string> paths) {
      var tags=new List<string>((subjects??new string[0]).Where(s=>!string.IsNullOrWhiteSpace(s)).Select(s=>"材料:"+s));
      foreach(string path in paths??new string[0]){string[] parts=path.Split('/');if(parts.Length<2||parts[0]!=FormulaCatalog.Root)continue;FormulaFamily family=FormulaCatalog.Data.families.FirstOrDefault(f=>f.name==parts[1]&&f.code!="review"&&f.code!="background"&&f.code!="nonstoich");if(family!=null)tags.Add("化学通式:"+family.formula);}
      return tags.Concat((original??new string[0]).Where(t=>t!=null&&!t.StartsWith("材料:")&&!t.StartsWith("化学通式:"))).Distinct().ToArray();
    }
    public static bool CanUpgrade(AnalysisResult value) {
      return value!=null&&value.routingVersion!=RoutingVersion&&value.classificationMode!="custom"&&value.classificationMode!="history"&&value.classificationMode!="prompt"&&value.collections!=null&&value.collections.Length>0&&value.collections.All(p=>p==FormulaCatalog.Pending||DualClassification.Kind(p)!="自定义")&&(TitleSubjects(value.title).Length>0||FormulaCatalog.MaterialPaths(value).Length>0||DualClassification.Topics(value.tags).Length>0);
    }
    public static bool Upgrade(AnalysisResult value) {
      if(!CanUpgrade(value))return false;
      string previousMode=value.routingVersion=="dual-classification-v3"?value.classificationMode:"both";
      string[] subjects=(value.materialSubjects??new string[0]).Where(s=>IsFormula(s)).Distinct().ToArray();
      if(subjects.Length==0)subjects=(value.materials??new MaterialAssignment[0]).Where(m=>m!=null&&IsFormula(m.compound)).Select(m=>m.compound).Distinct().ToArray();
      bool title=value.materialSource=="title"||subjects.Length==0;if(subjects.Length==0)subjects=TitleSubjects(value.title);
      string[] formula=(value.collections??new string[0]).Where(p=>DualClassification.Kind(p)=="化学通式"&&p!=FormulaCatalog.Pending).ToArray();if(formula.Length==0)formula=PathsFromSubjects(subjects,value.existingCollections);
      value.materialSubjects=subjects;value.researchTopics=(value.researchTopics??DualClassification.Topics(value.tags)).Select(t=>DualClassification.CleanTopicName(t,subjects)).Where(t=>t.Length>0).Distinct().ToArray();value.formulaCollections=formula;value.topicCollections=DualClassification.TopicPaths(value.researchTopics,subjects,value.existingCollections);
      value.collections=(previousMode=="topic"?new string[0]:formula).Concat(previousMode=="formula"?new string[0]:value.topicCollections).Distinct().ToArray();if(value.collections.Length==0)value.collections=new[]{FormulaCatalog.Pending};value.routingVersion=RoutingVersion;value.classificationMode=previousMode;value.materialSource=title?"title":"pdf";
      value.tags=(value.tags??new string[0]).Where(t=>t!="整理:通式待核对").Concat(title?new[]{"整理:材料主题需核对"}:new string[0]).ToArray();value.tags=TagsFor(value.tags,subjects,value.collections);
      if(string.IsNullOrEmpty(value.previousReason))value.previousReason=value.reason;
      value.reason="已同时生成化学通式分类和研究主题分类。材料："+(subjects.Length==0?"未指定":string.Join("、",subjects))+"。"+(title?"材料化学式来自题名线索，请核对。":"")+"主题目录不再添加材料层；无需重新调用API。";return true;
    }
    public static void Remember(AppState state,AnalysisResult value) {
      if(state.MaterialThemes==null)state.MaterialThemes=new List<MaterialTheme>();string confirmedAt=DateTime.UtcNow.ToString("o");
      foreach(string path in (value.collections??new string[0]).Where(ValidPath)) {
        MaterialTheme entry=state.MaterialThemes.FirstOrDefault(t=>t.Path==path);
        if(entry==null){entry=new MaterialTheme{Path=path};state.MaterialThemes.Add(entry);}
        entry.Subjects=(value.materialSubjects??new string[0]).ToArray();entry.LastUsedUtc=confirmedAt;entry.UseCount++;
      }
      state.MaterialThemes=state.MaterialThemes.OrderByDescending(t=>t.LastUsedUtc,StringComparer.Ordinal).Take(200).ToList();
    }
    public static MaterialThemeOption[] Options(AnalysisResult value,IEnumerable<MaterialTheme> recent,string[] livePaths=null) {
      var options=new List<MaterialThemeOption>();var seen=new HashSet<string>(StringComparer.Ordinal);
      Action<string,string,string> add=(path,subject,source)=>{if(ValidPath(path)&&!path.Contains("待核对")&&seen.Add(path))options.Add(new MaterialThemeOption{Path=path,Subject=subject??SubjectFromPath(path),Source=source});};
      foreach(MaterialTheme theme in (recent??new MaterialTheme[0]).OrderByDescending(t=>t.LastUsedUtc,StringComparer.Ordinal)){string subject=SubjectFromPath(theme.Path);if(subject.Length==0&&DualClassification.Kind(theme.Path)!="研究主题"&&theme.Subjects!=null&&theme.Subjects.Length==1)subject=theme.Subjects[0];add(theme.Path,subject,"最近使用");}
      foreach(string path in livePaths??value.existingCollections??new string[0])if(ValidPath(path))add(path,null,"Zotero分类");
      foreach(string path in value.collections??new string[0])if(ValidPath(path))add(path,null,"本篇建议");
      foreach(string path in FormulaCatalog.Data.existingPaths)if(SubjectFromPath(path).Length>0)add(path,null,"预置分类");
      return options.OrderBy(o=>o.Source=="最近使用"?0:(value.collections??new string[0]).Contains(o.Path)?1:o.Source=="Zotero分类"?2:3).ToArray();
    }
    public static MaterialThemeOption[] Filter(IEnumerable<MaterialThemeOption> options,string query,string source="全部",string kind="全部分类") {
      string q=Norm(query??"");return options.Where(o=>(source=="全部"||o.Source==source)&&(kind=="全部分类"||o.Kind==kind)&&(q.Length==0||Norm(o.Path+" "+o.Subject).IndexOf(q,StringComparison.OrdinalIgnoreCase)>=0)).ToArray();
    }
  }
}
