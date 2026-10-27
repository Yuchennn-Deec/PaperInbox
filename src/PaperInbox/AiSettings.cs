using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using System.Drawing;

namespace PaperInbox {
  public sealed class ProviderPreset {
    public string Id,Name,Protocol,Endpoint;
    public override string ToString(){return Name;}
    public static readonly ProviderPreset[] All={
      new ProviderPreset{Id="openai",Name="OpenAI",Protocol="openai",Endpoint="https://api.openai.com/v1/chat/completions"},
      new ProviderPreset{Id="deepseek",Name="DeepSeek",Protocol="openai",Endpoint="https://api.deepseek.com/chat/completions"},
      new ProviderPreset{Id="qwen",Name="通义千问 / 阿里云百炼",Protocol="openai",Endpoint="https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions"},
      new ProviderPreset{Id="siliconflow",Name="硅基流动",Protocol="openai",Endpoint="https://api.siliconflow.cn/v1/chat/completions"},
      new ProviderPreset{Id="moonshot",Name="Moonshot / Kimi",Protocol="openai",Endpoint="https://api.moonshot.cn/v1/chat/completions"},
      new ProviderPreset{Id="anthropic",Name="Anthropic / Claude",Protocol="anthropic",Endpoint="https://api.anthropic.com/v1/messages"},
      new ProviderPreset{Id="gemini",Name="Google / Gemini",Protocol="gemini",Endpoint="https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent"},
      new ProviderPreset{Id="custom",Name="自定义 / 其他兼容接口",Protocol="openai",Endpoint=""}
    };
  }
  public sealed class ApiProfile {
    public string Protocol {get;set;}
    public string Endpoint {get;set;}
    public string Model {get;set;}
    public string EncryptedKey {get;set;}
    public bool JsonMode {get;set;}
    public int MaxCharacters {get;set;}
    public int MaxPages {get;set;}
    public string ClassificationPrompt {get;set;}
    public ApiProfile(){Protocol="openai";Endpoint="";Model="";EncryptedKey="";MaxCharacters=24000;MaxPages=12;}
  }
  public sealed class AiConfiguration {
    public string Selected {get;set;}
    public Dictionary<string,ApiProfile> Profiles {get;set;}
    public AiConfiguration(){Selected="deepseek";Profiles=new Dictionary<string,ApiProfile>();}
    public ApiProfile Profile(string id) {
      ApiProfile p;if(Profiles.TryGetValue(id,out p))return p;
      ProviderPreset preset=ProviderPreset.All.FirstOrDefault(x=>x.Id==id)??ProviderPreset.All.Last();
      p=new ApiProfile{Protocol=preset.Protocol,Endpoint=preset.Endpoint};Profiles[id]=p;return p;
    }
    public ApiProfile Current {get{return Profile(Selected);}}
  }
  public sealed class AiSettingsStore {
    readonly StateStore store;
    public AiSettingsStore(StateStore store){this.store=store;}
    public AiConfiguration Load(){string path=Path.Combine(store.Folder,"api-settings.json");if(!File.Exists(path))return new AiConfiguration();AiConfiguration value=store.Json.Deserialize<AiConfiguration>(File.ReadAllText(path,Encoding.UTF8));if(value==null||value.Profiles==null)throw new InvalidDataException("API 设置无法读取。");return value;}
    public void Save(AiConfiguration value){StateStore.WriteAtomic(Path.Combine(store.Folder,"api-settings.json"),store.Json.Serialize(value));}
    public static string Encrypt(string key){if(string.IsNullOrEmpty(key))return "";return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key),null,DataProtectionScope.CurrentUser));}
    public static string Decrypt(string encrypted){if(string.IsNullOrEmpty(encrypted))return "";try{return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(encrypted),null,DataProtectionScope.CurrentUser));}catch{throw new InvalidDataException("无法解密此 Windows 用户的 API 密钥，请在设置中重新填写。");}}
  }
}
