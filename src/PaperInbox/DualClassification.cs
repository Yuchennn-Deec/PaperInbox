using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace PaperInbox {
  public static class DualClassification {
    public const string TopicRoot="按主题",Version="flat-topics-v4";
    public static string Kind(string path){return path!=null&&(path==FormulaCatalog.Root||path.StartsWith(FormulaCatalog.Root+"/",StringComparison.Ordinal))?"化学通式":path!=null&&(path==TopicRoot||path.StartsWith(TopicRoot+"/",StringComparison.Ordinal))?"研究主题":"自定义";}
    public static string[] Topics(IEnumerable<string> tags){return (tags??new string[0]).Where(t=>t!=null).Select(t=>Regex.Match(t.Trim(),@"^主题\s*[:：]\s*(.+)$")).Where(m=>m.Success).Select(m=>m.Groups[1].Value.Trim()).Where(t=>t.Length>0&&t.Length<=80&&!t.Any(char.IsControl)).Distinct().Take(8).ToArray();}
    public static string[] TopicsFromPaths(IEnumerable<string> paths){return (paths??new string[0]).Where(p=>Kind(p)=="研究主题").Select(p=>p.Split('/')).Where(p=>p.Length>=3||(p.Length==2&&!MaterialThemes.IsFormula(p[1]))).Select(p=>p.Last()).Distinct().ToArray();}
    public static string CleanTopicName(string raw,IEnumerable<string> subjects) {
      string topic=(raw??"").Trim();foreach(string subject in (subjects??new string[0]).Where(x=>MaterialThemes.IsFormula(x)).OrderByDescending(x=>x.Length)) {
        if(topic.StartsWith(subject,StringComparison.Ordinal)&&topic.Length>subject.Length&&!Regex.IsMatch(topic.Substring(subject.Length,1),@"[A-Za-z0-9]"))topic=topic.Substring(subject.Length).TrimStart(' ',':','：','/','／','-','—');
      }return topic;
    }
    public static string[] TopicPaths(IEnumerable<string> topics,IEnumerable<string> subjects,string[] existing=null) {
      return (topics??new string[0]).Select(t=>CleanTopicName(t,subjects)).Where(t=>!string.IsNullOrWhiteSpace(t)).Select(t=>TopicRoot+"/"+t.Trim().Replace('/','／').Replace('\\','＼')).Where(MaterialThemes.ValidPath).Distinct().Take(8).ToArray();
    }
    public static string[] Generate(string[] subjects,string[] topics,string mode,string[] existing=null) {
      var paths=new List<string>();if(mode!="topic")paths.AddRange(MaterialThemes.PathsFromSubjects(subjects,existing));if(mode!="formula")paths.AddRange(TopicPaths(topics,subjects,existing));return paths.Distinct().Take(80).ToArray();
    }
    public static void ApplyAuto(AnalysisResult value) {
      value.formulaCollections=(value.collections??new string[0]).Where(p=>Kind(p)=="化学通式"&&p!=FormulaCatalog.Pending).Distinct().ToArray();
      value.researchTopics=Topics(value.tags).Select(t=>CleanTopicName(t,value.materialSubjects)).Where(t=>t.Length>0).Distinct().ToArray();value.topicCollections=value.isAcademic?TopicPaths(value.researchTopics,value.materialSubjects,value.existingCollections):new string[0];
      value.collections=value.formulaCollections.Concat(value.topicCollections).Distinct().ToArray();if(value.collections.Length==0)value.collections=new[]{FormulaCatalog.Pending};
      value.routingVersion=Version;value.classificationMode="both";
    }
    public static void CapturePaths(AnalysisResult value){value.formulaCollections=(value.collections??new string[0]).Where(p=>Kind(p)=="化学通式"&&p!=FormulaCatalog.Pending).ToArray();value.topicCollections=(value.collections??new string[0]).Where(p=>Kind(p)=="研究主题").ToArray();}
    public static string ModeFor(IEnumerable<string> paths){bool formula=paths.Any(p=>Kind(p)=="化学通式"),topic=paths.Any(p=>Kind(p)=="研究主题");return formula&&topic?"both":formula?"formula":topic?"topic":"custom";}
  }
}
