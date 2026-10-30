var PaperInboxBridge = class {
  constructor(controller, version) {
    this.controller = controller; this.version = version;
    this.token = ''; this.timer = null; this.pumping = false;
    this.routes = ['/paper-inbox/status','/paper-inbox/submit','/paper-inbox/extract','/paper-inbox/collections'];
  }
  async start() {
    let dir = Zotero.Prefs.get('extensions.paper-inbox-bridge.sharedDir', true);
    if (!dir) {
      try { dir = PathUtils.join(Services.dirsvc.get('LocalAppData', Ci.nsIFile).path, 'PaperInbox'); }
      catch (_) { dir = PathUtils.join(Services.dirsvc.get('Home', Ci.nsIFile).path, 'AppData', 'Local', 'PaperInbox'); }
    }
    await IOUtils.makeDirectory(dir, {ignoreExisting:true});
    this.configPath = PathUtils.join(dir, 'bridge.json');
    let saved = {};
    if (await IOUtils.exists(this.configPath)) {
      try { saved = JSON.parse(await IOUtils.readUTF8(this.configPath)); } catch (_) {}
    }
    if (/^[a-f0-9]{64}$/.test(saved.token || '')) this.token = saved.token;
    else {
      const bytes = Cc['@mozilla.org/security/random-generator;1'].createInstance(Ci.nsIRandomGenerator).generateRandomBytes(32);
      this.token = Array.from(bytes, b=>b.toString(16).padStart(2,'0')).join('');
    }
    let port;
    try { port = Zotero.Server.port; } catch (_) { port = Zotero.Prefs.get('httpServer.port') || 23119; }
    await IOUtils.writeUTF8(this.configPath, JSON.stringify({protocolVersion:3,token:this.token,port,bridgeVersion:this.version}), {tmpPath:this.configPath+'.tmp'});
    this.controller.state.externalRequests = this.controller.state.externalRequests || {};
    for (const job of this.controller.state.pending) {
      if(job.externalApproved&&(!job.plan||job.plan.classificationVersion!=='chemical-formula-v1')){
        job.externalApproved=false;job.status='error';job.error='分类已升级为化学通式，请重新分析并确认入库。';
        const record=Object.values(this.controller.state.externalRequests).find(r=>r.jobId===job.id);
        if(record){record.status='error';record.message=job.error;}
      }
    }
    await this.controller.persist();
    const bridge = this;
    for (const route of this.routes) {
      const Endpoint = function() {};
      Endpoint.prototype = {
        supportedMethods:['POST'], supportedDataTypes:['application/json'],
        async init(request) { return bridge.handle(route, request); }
      };
      Zotero.Server.Endpoints[route] = Endpoint;
    }
    this.timer = setInterval(()=>this.pump().catch(e=>Zotero.logError(e)), 500);
  }
  authorized(request) {
    if (request.headers.origin) return false;
    const provided = request.headers['X-PaperInbox-Token'] || '';
    if (typeof provided !== 'string' || provided.length !== this.token.length) return false;
    let difference = 0;
    for (let i=0;i<this.token.length;i++) difference |= provided.charCodeAt(i)^this.token.charCodeAt(i);
    return difference === 0;
  }
  json(code, body) { return [code,'application/json',JSON.stringify(body)]; }
  async handle(route, request) {
    if (!this.authorized(request)) return this.json(403,{error:'NOT_AUTHORIZED'});
    const data = request.data || {};
    if (route === '/paper-inbox/status') {
      const requests = {};
      for (const id of Array.isArray(data.ids) ? data.ids.slice(0,100) : []) {
        if (this.controller.state.externalRequests[id]) requests[id] = this.controller.state.externalRequests[id];
      }
      return this.json(200,{ready:true,protocolVersion:3,version:this.version,zoteroVersion:Zotero.version,processing:this.controller.busy,requests});
    }
    if(route==='/paper-inbox/collections'){const all=Zotero.Collections.getByLibrary(Zotero.Libraries.userLibraryID,true);const paths=all.map(c=>{const parts=[c.name];let parent=c.parentID;while(parent){const p=Zotero.Collections.get(parent);if(!p)break;parts.unshift(p.name);parent=p.parentID;}return parts.join('/');});return this.json(200,{collections:paths.slice(0,1000)});}
    if (!/^[a-f0-9-]{20,50}$/i.test(data.id || '') || typeof data.path !== 'string' || !/\.pdf$/i.test(data.path)) return this.json(400,{error:'INVALID_REQUEST'});
    const existing = route === '/paper-inbox/submit' && this.controller.state.externalRequests[data.id];
    if (existing) return this.json(200,{request:existing});
    try {
      const file = Zotero.File.pathToFile(data.path);
      file.normalize();
      const stat = await IOUtils.stat(file.path);
      if (stat.type !== 'regular' || stat.size !== data.size || stat.lastModified !== data.lastModified) return this.json(409,{error:'FILE_CHANGED'});
      if (route === '/paper-inbox/extract') {
        if (stat.size > 150*1024*1024 || !await this.controller.isReadyPDF({path:file.path,size:stat.size})) return this.json(400,{error:'PDF_NOT_READY'});
        const sourceHash = await this.controller.sha256(file.path);
        const bytes = await IOUtils.read(file.path);
        const buf = bytes.buffer.slice(bytes.byteOffset,bytes.byteOffset+bytes.byteLength);
        const maxPages = Math.max(1,Math.min(50,Number(data.maxPages)||12));
        // Zotero 10 namespaces document-worker PDF actions. Keep extraction
        // read-only: no temporary library attachment is needed before approval.
        const action = Number.parseInt(Zotero.version, 10) >= 10 ? 'pdf.getFulltext' : 'getFulltext';
        const result = await Zotero.PDFWorker._enqueue(()=>Zotero.PDFWorker._query(action,{buf,maxPages},[buf]),true);
        if (await this.controller.sha256(file.path) !== sourceHash) return this.json(409,{error:'FILE_CHANGED'});
        const all = Zotero.Collections.getByLibrary(Zotero.Libraries.userLibraryID,true);
        const paths = all.map(c=>{const parts=[c.name];let parent=c.parentID;while(parent){const p=Zotero.Collections.get(parent);if(!p)break;parts.unshift(p.name);parent=p.parentID;}return parts.join('/');});
        return this.json(200,{text:(result.text||'').slice(0,Math.max(2000,Math.min(80000,Number(data.maxCharacters)||24000))),sourceHash,totalPages:result.totalPages||0,collections:paths.slice(0,1000)});
      }
      const plan = data.plan;
      if (data.confirmed !== true || !plan || plan.classificationVersion!=='chemical-formula-v1' || !/^[a-f0-9]{64}$/.test(plan.sourceHash||'')) return this.json(400,{error:'CONFIRMATION_REQUIRED'});
      if (!['journalArticle','thesis','report','bookSection','document'].includes(plan.itemType) || !Array.isArray(plan.collections) || (plan.collections.length===0||plan.collections.length>80) || !Array.isArray(plan.tags) || plan.tags.length>30
        || !Array.isArray(plan.authors) || plan.authors.length>30 || typeof plan.title!=='string' || plan.title.length>500
        || typeof plan.doi!=='string' || (plan.doi&&!/^10\.\d{4,9}\/\S+$/.test(plan.doi))
        || plan.collections.some(p=>typeof p!=='string'||p.length>240||p.split('/').length>5||p.split('/').some(s=>!s.trim()||s==='.'||s==='..'||s.length>80)||/[\x00-\x1f]/.test(p))
        || plan.tags.some(t=>typeof t!=='string'||t.length>100||/[\x00-\x1f]/.test(t))
        || plan.authors.some(a=>typeof a!=='string'||a.length>150)) return this.json(400,{error:'INVALID_PLAN'});
      if (await this.controller.sha256(file.path) !== plan.sourceHash) return this.json(409,{error:'FILE_CHANGED'});
      const job = {id:'external:'+data.id,name:PathUtils.filename(file.path),path:file.path,size:stat.size,lastModified:stat.lastModified,
        fingerprint:PaperInboxCore.fingerprint(stat),status:'pending',error:'',externalApproved:true,plan,autoOpen:data.autoOpen===true};
      this.controller.state.pending.push(job);
      const record = {id:data.id,jobId:job.id,status:'pending',name:job.name,message:'已接收确认结果，等待入库。',autoOpen:job.autoOpen};
      this.controller.state.externalRequests[data.id] = record;
      await this.controller.persist();
      this.pump().catch(error=>Zotero.logError(error));
      return this.json(201,{request:record});
    } catch (error) {
      Zotero.logError(error);
      return this.json(400,{error:'FILE_NOT_READABLE'});
    }
  }
  async pump() {
    const c=this.controller;
    if(this.pumping || c.busy || c.stopped) return;
    if(!c.state.pending.some(j=>j.externalApproved)&&!Object.values(c.state.externalRequests).some(r=>r.status==='pending'))return;
    this.pumping=true;
    try {
      const approved=c.state.pending.filter(j=>j.externalApproved);
      if(approved.length) await c.process(approved.map(j=>j.id),{approved:true});
      for(const record of Object.values(c.state.externalRequests)) {
        if(record.status!=='pending')continue;
        const result=c.state.history.find(h=>h.jobId===record.jobId);
        if(!result)continue;
        record.status=result.status==='error'?'error':'done';
        record.message=result.message;
        record.result=result.status;
        if(result.parentKey)record.parentKey=result.parentKey;
        if(result.attachmentKey)record.attachmentKey=result.attachmentKey;
        if (record.status==='done' && record.autoOpen && record.attachmentKey && !record.openedAt) {
          try {
            const attachment=Zotero.Items.getByLibraryAndKey(Zotero.Libraries.userLibraryID,record.attachmentKey);
            await Zotero.Reader.open(attachment.id,null,{openInWindow:false});
            Zotero.getMainWindow()?.focus();
            record.openedAt=new Date().toISOString();record.message+=' 已在 Zotero 阅读器中打开 PDF。';
          }catch(error){Zotero.logError(error);record.openError=String(error);record.message+=' 入库已完成，自动打开失败，请在 Zotero 中打开附件。';}
        }
        const pending=c.state.pending.find(j=>j.id===record.jobId);
        if(pending && record.status==='error')pending.externalApproved=false;
      }
      await c.persist();
    } finally {this.pumping=false;}
  }
  stop() {
    if(this.timer)clearInterval(this.timer);
    this.timer=null;
    for(const route of this.routes)delete Zotero.Server.Endpoints[route];
  }
};
if(typeof module!=='undefined')module.exports=PaperInboxBridge;
