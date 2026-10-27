const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict');
const root=__dirname,checks=[];
function check(name,value){assert.ok(value,name);checks.push({name,passed:true});}
(async()=>{
  const core=require('./zotero-bridge/core.js');
  const existing=[{id:1,name:'按化学通式',parentID:null},{id:2,name:'AMX2O6（辉石）',parentID:1},{id:3,name:'SrMGe2O6（M=Mn,Co）',parentID:2}];
  const attachments=[{key:'large',size:99999},{key:'same-size',size:100}].map(row=>({...row,isAttachment:()=>true,isPDFAttachment:()=>true,getFilePathAsync:async()=>row.key}));
  let reads=[],writes=0;
  const scope={PaperInboxCore:core,IOUtils:{stat:async path=>({size:attachments.find(a=>a.key===path).size,lastModified:1})},Zotero:{Libraries:{userLibraryID:1},Items:{getAll:async()=>attachments},Collections:{getByLibrary:(_id,recursive)=>recursive?existing:existing.filter(c=>!c.parentID)},Collection:function(){throw Error('Existing collection should be reused')},logError:e=>{throw e;}},module:{exports:{}}};
  vm.createContext(scope);vm.runInContext(fs.readFileSync(root+'/zotero-bridge/controller.js','utf8'),scope);const Controller=scope.module.exports,c=new Controller({bridgeMode:true});
  c.sha256=async p=>{reads.push(p);return 'match';};
  const match=await c.findIdentical('match',100);
  check('查重仅计算大小相符的附件哈希',match.key==='same-size'&&reads.length===1&&reads[0]==='same-size');
  check('递归复用已有通式子类',await c.collection('按化学通式/AMX2O6（辉石）/SrMGe2O6（M=Mn,Co）')===3);
  const ids=new Set([77]),tags=[];await c.organize({loadAllData:async()=>{},addToCollection:id=>ids.add(id),addTag:t=>tags.push(t),saveTx:async()=>{}},['按化学通式/AMX2O6（辉石）/SrMGe2O6（M=Mn,Co）'],['化学通式:AMX2O6']);
  check('入库同时加入通式根、家族及材料子类并保留原分类',[1,2,3,77].every(id=>ids.has(id))&&tags.includes('化学通式:AMX2O6'));
  vm.runInContext(fs.readFileSync(root+'/zotero-bridge/bridge.js','utf8'),scope);const Bridge=scope.module.exports;c.state.externalRequests={};c.persist=async()=>writes++;const bridge=new Bridge(c,'0.4.0');await bridge.pump();
  check('空闲桥接不反复写入状态文件',writes===0);
  const out={success:true,checks};fs.writeFileSync(process.argv[2],JSON.stringify(out,null,2));process.stdout.write(JSON.stringify(out));
})().catch(error=>{process.stdout.write(String(error.stack));process.exitCode=1;});
