using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PaperInbox {
  public sealed class MaterialHistoryForm : Form {
    readonly AnalysisResult value;readonly IEnumerable<MaterialTheme> recent;readonly Func<Task<string[]>> refresh;
    readonly TextBox search=new TextBox();readonly ComboBox source=new ComboBox(),kind=new ComboBox();readonly CheckedListBox list=new CheckedListBox();
    readonly Label status=Design.Label("",9,Design.Muted);readonly SoftButton reload=new SoftButton{Text="刷新Zotero目录",Width=150};
    readonly HashSet<string> selected=new HashSet<string>(StringComparer.Ordinal);MaterialThemeOption[] options;bool rendering,loading;
    public string[] SelectedPaths {get;private set;}public string[] SelectedSubjects {get;private set;}public bool Append {get;private set;}
    public MaterialHistoryForm(AnalysisResult value,IEnumerable<MaterialTheme> recent=null,Func<Task<string[]>> refresh=null) {
      this.value=value;this.recent=recent??new MaterialTheme[0];this.refresh=refresh;
      Design.Window(this,"历史分类 · 论文收件箱",new Size(900,650),new Size(740,540));
      TableLayoutPanel root=Design.Stack(5);root.Padding=new Padding(24,18,24,18);root.RowStyles.Add(new RowStyle(SizeType.Absolute,76));root.RowStyles.Add(new RowStyle(SizeType.Absolute,84));root.RowStyles.Add(new RowStyle(SizeType.Percent,100));root.RowStyles.Add(new RowStyle(SizeType.Absolute,38));root.RowStyles.Add(new RowStyle(SizeType.Absolute,60));Controls.Add(root);
      Panel header=new Panel{Dock=DockStyle.Fill};Label heading=Design.Heading("选择历史分类",21);heading.SetBounds(0,0,620,38);Label note=Design.Label("可以同时勾选化学通式和研究主题；确认过的分类优先显示。",10,Design.Muted);note.SetBounds(0,40,780,28);header.Controls.AddRange(new Control[]{heading,note});root.Controls.Add(header,0,0);
      TableLayoutPanel tools=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=2,Margin=Padding.Empty};tools.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,124));tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,134));tools.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,160));tools.RowStyles.Add(new RowStyle(SizeType.Absolute,25));tools.RowStyles.Add(new RowStyle(SizeType.Percent,100));Label searchLabel=Design.Label("搜索主题或分类路径",9,Design.Muted),sourceLabel=Design.Label("来源",9,Design.Muted);searchLabel.Dock=sourceLabel.Dock=DockStyle.Fill;tools.Controls.Add(searchLabel,0,0);tools.Controls.Add(sourceLabel,2,0);Label kindLabel=Design.Label("分类类型",9,Design.Muted);kindLabel.Dock=DockStyle.Fill;tools.Controls.Add(kindLabel,1,0);
      search.Dock=DockStyle.Fill;search.Font=Design.Font(10);search.AccessibleName="搜索历史分类";search.Margin=new Padding(0,2,12,8);source.Dock=DockStyle.Fill;source.DropDownStyle=ComboBoxStyle.DropDownList;source.Items.AddRange(new object[]{"全部","最近使用","Zotero分类","本篇建议","预置分类"});source.SelectedIndex=0;source.Margin=new Padding(0,0,10,8);reload.Dock=DockStyle.Fill;reload.Margin=new Padding(0,0,0,6);Surface searchField=Design.Field(search,false);searchField.Margin=new Padding(0,0,12,8);tools.Controls.Add(searchField,0,1);kind.Dock=DockStyle.Fill;kind.DropDownStyle=ComboBoxStyle.DropDownList;kind.Items.AddRange(new object[]{"全部分类","化学通式","研究主题","自定义"});kind.SelectedIndex=0;kind.Margin=new Padding(0,0,10,8);Surface kindField=Design.Field(kind,false),sourceField=Design.Field(source,false);kindField.Margin=sourceField.Margin=new Padding(0,0,10,8);tools.Controls.Add(kindField,1,1);tools.Controls.Add(sourceField,2,1);tools.Controls.Add(reload,3,1);root.Controls.Add(tools,0,1);
      Surface surface=new Surface{Dock=DockStyle.Fill,Padding=new Padding(12),Margin=Padding.Empty};list.Dock=DockStyle.Fill;list.Font=Design.Font(10);list.BorderStyle=BorderStyle.None;list.CheckOnClick=true;list.HorizontalScrollbar=true;list.IntegralHeight=false;list.AccessibleName="历史分类列表";surface.Controls.Add(list);root.Controls.Add(surface,0,2);status.Dock=DockStyle.Fill;status.TextAlign=ContentAlignment.MiddleLeft;root.Controls.Add(status,0,3);
      FlowLayoutPanel actions=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};SoftButton replace=new SoftButton{Text="使用所选",Tone="primary",Width=132},append=new SoftButton{Text="追加到路径",Width=134},cancel=new SoftButton{Text="取消",Tone="quiet",Width=90,DialogResult=DialogResult.Cancel};actions.Controls.AddRange(new Control[]{replace,append,cancel});root.Controls.Add(actions,0,4);CancelButton=cancel;
      search.TextChanged+=(s,e)=>RenderOptions();source.SelectedIndexChanged+=(s,e)=>RenderOptions();kind.SelectedIndexChanged+=(s,e)=>RenderOptions();list.ItemCheck+=(s,e)=>{if(rendering)return;var item=(MaterialThemeOption)list.Items[e.Index];if(e.NewValue==CheckState.Checked)selected.Add(item.Path);else selected.Remove(item.Path);UpdateStatus();};
      replace.Click+=(s,e)=>Choose(false);append.Click+=(s,e)=>Choose(true);reload.Click+=async(s,e)=>await RefreshCatalog();
      options=MaterialThemes.Options(value,this.recent);RenderOptions();reload.Enabled=refresh!=null;
      Shown+=async(s,e)=>{if(refresh!=null)await RefreshCatalog();};
    }
    void UpdateStatus(){status.Text=list.Items.Count+" 条可选分类  ·  已选 "+selected.Count+" 条";}
    void RenderOptions(){if(options==null)return;rendering=true;list.BeginUpdate();try{list.Items.Clear();foreach(MaterialThemeOption option in MaterialThemes.Filter(options,search.Text,source.SelectedItem as string??"全部",kind.SelectedItem as string??"全部分类"))list.Items.Add(option,selected.Contains(option.Path));}finally{list.EndUpdate();rendering=false;}UpdateStatus();}
    async Task RefreshCatalog(){if(loading||refresh==null)return;loading=true;reload.Enabled=false;reload.Text="正在刷新…";try{string[] paths=await refresh();if(IsDisposed)return;value.existingCollections=paths??new string[0];options=MaterialThemes.Options(value,recent,paths);RenderOptions();}catch{if(!IsDisposed)status.Text="暂未连接Zotero，可继续选择已有记录；刷新不会调用外部API。";}finally{loading=false;if(!IsDisposed){reload.Enabled=true;reload.Text="刷新Zotero目录";}}}
    void Choose(bool append){if(selected.Count==0){status.Text="请先勾选至少一个历史分类。";return;}SelectedPaths=options.Where(o=>selected.Contains(o.Path)).Select(o=>o.Path).ToArray();SelectedSubjects=options.Where(o=>selected.Contains(o.Path)&&o.Subject.Length>0).Select(o=>o.Subject).Distinct().ToArray();Append=append;DialogResult=DialogResult.OK;Close();}
  }
}
