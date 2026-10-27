using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using PaperInbox;
public static class DualInteractions {
 static List<object> results=new List<object>();static void Check(string name,bool value){results.Add(new{name=name,passed=value});if(!value)throw new Exception(name);}
 static IEnumerable<Control> All(Control c){foreach(Control x in c.Controls){yield return x;foreach(Control y in All(x))yield return y;}}
 static T Field<T>(object obj,string field){return (T)obj.GetType().GetField(field,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);}
 static Button Button(Control c,string text){return All(c).OfType<Button>().First(b=>b.Text==text);}
 static void Show(Form f){f.StartPosition=FormStartPosition.Manual;f.Location=new Point(-20000,-20000);f.ShowInTaskbar=false;f.Show();Application.DoEvents();}
 [STAThread]public static int Main(string[] args){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());string root=args[0];Directory.CreateDirectory(root);
  try{
   StateStore store=new StateStore(Path.Combine(root,"state"));
   using(ApiSettingsForm settings=new ApiSettingsForm(store)){
    Show(settings);Field<ComboBox>(settings,"providers").SelectedIndex=0;All(settings).OfType<Button>().First(b=>b.Text.StartsWith("分类 Prompt")).PerformClick();Application.DoEvents();
    TextBox prompt=Field<TextBox>(settings,"classificationPrompt");Check("设置可展开编辑分类Prompt",prompt.Visible&&prompt.Multiline&&prompt.Text.Replace("\r\n","\n")==AiClient.DefaultClassificationPrompt);
    AiTests.Render(settings,Path.Combine(root,"prompt-preview.png"));
    var restore=Button(settings,"恢复默认双分类");var scrollPanel=All(settings).OfType<FlowLayoutPanel>().First(c=>c.AutoScroll);Check("展开后规则和恢复默认按钮在可视区域",scrollPanel.RectangleToScreen(scrollPanel.ClientRectangle).Contains(restore.RectangleToScreen(restore.ClientRectangle)));
    prompt.Text="OPENAI_CUSTOM_RULE";Field<TextBox>(settings,"key").Text="local-test-key";Field<ComboBox>(settings,"providers").SelectedIndex=2;
    Check("切换服务商默认显示独立Prompt",prompt.Text.Replace("\r\n","\n")==AiClient.DefaultClassificationPrompt);
    prompt.Text="QWEN_CUSTOM_RULE";Field<TextBox>(settings,"model").Text="test-qwen";
    Field<ComboBox>(settings,"providers").SelectedIndex=0;Check("切回服务商恢复已编辑规则和密钥",prompt.Text=="OPENAI_CUSTOM_RULE"&&Field<TextBox>(settings,"key").Text=="local-test-key");
    Button(settings,"恢复默认双分类").PerformClick();Check("一键恢复默认双分类Prompt",prompt.Text.Replace("\r\n","\n")==AiClient.DefaultClassificationPrompt);
    Field<ComboBox>(settings,"providers").SelectedIndex=2;Check("恢复默认不覆盖其他服务商Prompt",prompt.Text=="QWEN_CUSTOM_RULE");
    All(settings).OfType<Button>().First(b=>b.Text.StartsWith("高级连接设置")).PerformClick();Application.DoEvents();Check("高级连接设置仍可编辑",Field<TextBox>(settings,"endpoint").Visible&&Field<ComboBox>(settings,"protocol").Visible);
    Button(settings,"保存设置").PerformClick();var saved=new AiSettingsStore(store).Load();Check("界面保存当前服务商和自定义Prompt",settings.DialogResult==DialogResult.OK&&saved.Selected=="qwen"&&saved.Current.ClassificationPrompt=="QWEN_CUSTOM_RULE"&&saved.Current.Model=="test-qwen");
    Check("默认规则保存为空标记并保留加密密钥",saved.Profile("openai").ClassificationPrompt==""&&AiSettingsStore.Decrypt(saved.Profile("openai").EncryptedKey)=="local-test-key"&&!File.ReadAllText(Path.Combine(store.Folder,"api-settings.json")).Contains("local-test-key"));
   }
   using(ApiSettingsForm settings=new ApiSettingsForm(store)){Show(settings);Check("重开设置恢复自定义Prompt",Field<TextBox>(settings,"classificationPrompt").Text=="QWEN_CUSTOM_RULE");Field<TextBox>(settings,"classificationPrompt").Text="未保存";settings.Close();Check("取消设置不写入Prompt",new AiSettingsStore(store).Load().Current.ClassificationPrompt=="QWEN_CUSTOM_RULE");}
   var plan=TopicTests.CuO();MaterialThemes.Upgrade(plan);plan.existingCollections=new[]{"按化学通式/MO（二元氧化物）/CuO","按化学通式/AMX2O6（辉石）/CaMnGe2O6","按主题/磁电效应"};var recent=new AppState();MaterialThemes.Remember(recent,plan);
   string[] pickedPaths,pickedSubjects;
   using(var picker=new MaterialHistoryForm(plan,recent.MaterialThemes,()=>System.Threading.Tasks.Task.FromResult(plan.existingCollections))){
    Show(picker);Field<ComboBox>(picker,"kind").SelectedItem="化学通式";Field<TextBox>(picker,"search").Text="CaMnGe₂O₆";Application.DoEvents();var list=Field<CheckedListBox>(picker,"list");Check("历史分类按类型筛选并支持下标搜索",list.Items.Count==1&&((MaterialThemeOption)list.Items[0]).Path.EndsWith("CaMnGe2O6"));list.SetItemChecked(0,true);
    Field<TextBox>(picker,"search").Text="磁电效应";Field<ComboBox>(picker,"kind").SelectedItem="研究主题";Check("历史研究主题能单独搜索",list.Items.Count==1&&((MaterialThemeOption)list.Items[0]).Path=="按主题/磁电效应");list.SetItemChecked(0,true);
    Field<ComboBox>(picker,"kind").SelectedItem="全部分类";Field<TextBox>(picker,"search").Text="";AiTests.Render(picker,Path.Combine(root,"history-preview.png"));Button(picker,"使用所选").PerformClick();
    Check("跨类型筛选仍保留两套已勾选路径",picker.DialogResult==DialogResult.OK&&picker.SelectedPaths.Length==2&&picker.SelectedPaths.Contains("按主题/磁电效应")&&picker.SelectedSubjects.Contains("CaMnGe2O6")&&!picker.SelectedSubjects.Contains("CuO"));pickedPaths=picker.SelectedPaths;pickedSubjects=picker.SelectedSubjects;
   }
   using(var picker=new MaterialHistoryForm(plan,recent.MaterialThemes)){Show(picker);Field<TextBox>(picker,"search").Text="CuO";Field<CheckedListBox>(picker,"list").SetItemChecked(0,true);Button(picker,"追加到路径").PerformClick();Check("历史选择可追加路径",picker.DialogResult==DialogResult.OK&&picker.Append);}
   using(var review=new ReviewForm(plan,recent.MaterialThemes)){
    Show(review);Button(review,"生成分类路径").PerformClick();Check("默认生成两套分类且不插入材料层",Field<TextBox>(review,"collections").Lines.Length==4&&Field<TextBox>(review,"collections").Text.Contains("按主题/磁电效应")&&Field<TextBox>(review,"collections").Text.Contains("按化学通式/MO（二元氧化物）/CuO"));
    AiTests.Render(review,Path.Combine(root,"dual-preview.png"));var mode=Field<ComboBox>(review,"generationMode");mode.SelectedIndex=1;Button(review,"生成分类路径").PerformClick();Check("仅化学通式模式不保留旧主题路径",Field<TextBox>(review,"collections").Lines.Length==1&&plan.classificationMode=="formula");
    mode.SelectedIndex=2;Button(review,"生成分类路径").PerformClick();Check("仅主题模式生成独立研究主题目录",Field<TextBox>(review,"collections").Lines.Length==3&&!Field<TextBox>(review,"collections").Text.Contains("按化学通式/")&&plan.classificationMode=="topic");
    Field<TextBox>(review,"research").Text="磁电效应；低温相变";mode.SelectedIndex=0;Button(review,"生成分类路径").PerformClick();Check("主题可手改并生成独立主题路径",Field<TextBox>(review,"collections").Text.Contains("按主题/低温相变")&&Field<TextBox>(review,"collections").Lines.Length==3);
    review.ApplyHistorySelection(pickedPaths,pickedSubjects,false);Check("历史选择可替换为两套路径",Field<TextBox>(review,"collections").Lines.Length==2&&Field<TextBox>(review,"subjects").Text.Contains("CaMnGe2O6")&&Field<TextBox>(review,"research").Text=="磁电效应");
    review.ApplyHistorySelection(new[]{"按化学通式/MO（二元氧化物）/CuO"},new[]{"CuO"},true);Check("追加保留主题及原材料目录",Field<TextBox>(review,"collections").Lines.Length==3&&Field<TextBox>(review,"tags").Text.Contains("材料:CaMnGe2O6")&&Field<TextBox>(review,"tags").Text.Contains("材料:CuO"));
    Field<TextBox>(review,"collections").Text="../bad";Button(review,"确认放入 Zotero").PerformClick();Check("最终确认拦截无效路径",review.DialogResult!=DialogResult.OK&&review.Visible);
    Field<TextBox>(review,"collections").Text="按化学通式/MO（二元氧化物）/CuO\r\n按主题/磁电效应";Field<TextBox>(review,"subjects").Text="CuO";Field<TextBox>(review,"authors").Text="作者甲\r\n作者乙";Field<ComboBox>(review,"type").SelectedIndex=2;Button(review,"确认放入 Zotero").PerformClick();
    Check("最终确认保存两套路径和完整元数据",review.DialogResult==DialogResult.OK&&plan.formulaCollections.Length==1&&plan.topicCollections.Length==1&&plan.authors.Length==2&&plan.itemType=="report"&&plan.researchTopics.Single()=="磁电效应");
   }
   var promptPlan=AiTests.Sample();promptPlan.title="Magnetic correlations in FeTa2O6（界面演示）";promptPlan.doi="";promptPlan.year="";promptPlan.authors=new string[0];promptPlan.summary="演示按主题整理FeTa2O6论文，不代表实际分析结果。";promptPlan.reason="自定义规则要求主题路径不含化学式，API返回按主题/磁关联。";promptPlan.collections=new[]{"按主题/磁关联"};promptPlan.promptCollections=new[]{"按主题/磁关联"};promptPlan.classificationMode="prompt";promptPlan.routingVersion=PromptRouting.Version;promptPlan.tags=new[]{"精读","领域:凝聚态物理"};promptPlan.materials=new MaterialAssignment[0];promptPlan.materialSubjects=new string[0];
   using(var review=new ReviewForm(promptPlan)) {
    Show(review);Check("按Prompt模式默认保留API完整路径",Field<ComboBox>(review,"generationMode").SelectedIndex==3&&Field<TextBox>(review,"collections").Text=="按主题/磁关联");
    Check("通用Prompt模式隐藏材料助手字段",!Field<TextBox>(review,"subjects").Visible&&!Field<TextBox>(review,"research").Visible);AiTests.Render(review,Path.Combine(root,"prompt-review-preview.png"));
    Field<TextBox>(review,"collections").Text="其他学科/待精读";Button(review,"恢复 API 路径").PerformClick();Check("按Prompt模式可恢复API原始路径",Field<TextBox>(review,"collections").Text=="按主题/磁关联");
    Field<TextBox>(review,"collections").Text="法学/合同法/合同解释";Button(review,"确认放入 Zotero").PerformClick();Check("其他学科路径及普通标签可最终确认",review.DialogResult==DialogResult.OK&&promptPlan.collections.Single()=="法学/合同法/合同解释"&&promptPlan.tags.Contains("精读")&&promptPlan.tags.Contains("领域:凝聚态物理"));
   }
   var state=new AppState{Folder=root,Initialized=true};state.Pending.Add(new PendingFile{Id="a",Name="Paper A.pdf",Path=Path.Combine(root,"a.pdf"),Status="Pending"});state.Pending.Add(new PendingFile{Id="b",Name="Paper B.pdf",Path=Path.Combine(root,"b.pdf"),Status="Review",Analysis=AiTests.Sample()});store.Save(state);
   using(var context=new TrayContext(store.Folder,true)){Show(context.Form);context.Form.Render();Application.DoEvents();var paper=All(context.Form).OfType<CheckBox>().First(c=>c.Text=="Paper A.pdf");paper.Checked=false;context.Form.Render();Check("刷新仍保留论文勾选状态",!paper.Checked);Button(context.Form,"忽略").PerformClick();Check("忽略只处理勾选文件",context.State.Pending.Count==1&&context.State.Pending[0].Id=="a");context.Form.Close();Check("关闭窗口不会导入未确认文件",context.State.Pending[0].Status=="Pending");context.DisposeForTest();}
   File.WriteAllText(Path.Combine(root,"results.json"),new JavaScriptSerializer().Serialize(new{success=true,checks=results}));return 0;
  }catch(Exception e){File.WriteAllText(Path.Combine(root,"results.json"),new JavaScriptSerializer().Serialize(new{success=false,checks=results,error=e.ToString()}));return 1;}
 }
}
