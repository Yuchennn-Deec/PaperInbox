using System;
using System.Linq;
namespace PaperInbox {
 public static class PromptRouting {
  public const string Version="prompt-paths-v1",Pending="待核对/分类路径";
  public static bool TryPaths(string[] input,out string[] paths) {
   paths=new string[0];if(input==null||input.Length==0||input.Length>80)return false;
   if(input.Any(p=>p==null||!MaterialThemes.ValidPath(p.Trim())))return false;
   paths=input.Select(p=>p.Trim()).Distinct().ToArray();return paths.Length>0;
  }
  public static void Apply(AnalysisResult value,string[] requested,bool defaultRules,string[] existing) {
   string[] paths;bool valid=TryPaths(requested,out paths)&&!paths.Contains(FormulaCatalog.Pending)&&!paths.Contains(Pending);
   if(valid){value.collections=paths;value.classificationMode="prompt";value.routingVersion=Version;}
   else if(!defaultRules){value.collections=new[]{Pending};value.classificationMode="prompt";value.routingVersion=Version;value.reason=(value.reason??"")+"\nAPI未返回有效的collections分类路径，已保留分析供核对；请手动填写路径，无需重新调用API。";}
   value.promptCollections=(value.collections??new[]{Pending}).ToArray();value.existingCollections=existing??new string[0];
   value.materialSubjects=value.materialSubjects??new string[0];value.researchTopics=value.researchTopics??DualClassification.Topics(value.tags);
   value.classificationVersion=FormulaCatalog.Version;DualClassification.CapturePaths(value);
  }
 }
}
