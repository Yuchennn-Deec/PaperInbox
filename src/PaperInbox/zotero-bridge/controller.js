var PaperInboxController = class {
  constructor(options) {
    this.options = options;
    this.state = PaperInboxCore.emptyState();
    this.tracker = new PaperInboxCore.DownloadTracker(this.state);
    this.windows = new Map(); this.listeners = new Set();
    this.panel = null; this.timer = null; this.stopped = false;
    this.scanning = false; this.busy = false; this.progress = '';
    this.writeQueue = Promise.resolve(); this.lastQueuedValue = ''; this.statePath = '';
  }
  async start() {
    const dir = PathUtils.join(Zotero.Profile.dir, 'paper-inbox');
    await IOUtils.makeDirectory(dir, {ignoreExisting: true});
    this.statePath = PathUtils.join(dir, 'state.json');
    if (await IOUtils.exists(this.statePath)) {
      try {
        const data = JSON.parse(await IOUtils.readUTF8(this.statePath));
        if (data.schema !== 1 || !Array.isArray(data.pending) || typeof data.known !== 'object') throw new Error('不支持的状态文件');
        this.state = {...PaperInboxCore.emptyState(), ...data};
        for (const job of this.state.pending) if (job.status === 'processing') job.status = 'pending';
      } catch (error) {
        this.state.enabled = false;
        this.state.watcherError = '历史状态读取失败，监测已暂停；请在设置中重新启用。';
        await IOUtils.copy(this.statePath, this.statePath + '.unreadable-' + Date.now());
        Zotero.logError(error);
      }
    }
    if (!this.state.folder) this.state.folder = this.defaultFolder();
    this.tracker = new PaperInboxCore.DownloadTracker(this.state);
    if (this.options.bridgeMode) {
      this.state.enabled = false;
      await this.persist();
      return;
    }
    for (const win of Zotero.getMainWindows()) this.attachWindow(win);
    await this.persist();
    await this.scan();
    this.timer = setInterval(() => this.scan().catch(error => this.report(error)), 5000);
  }
  defaultFolder() {
    try { return Services.dirsvc.get('DfltDwnld', Ci.nsIFile).path; }
    catch (_) { return PathUtils.join(Services.dirsvc.get('Home', Ci.nsIFile).path, 'Downloads'); }
  }
  persist() {
    if (!this.statePath) return Promise.resolve();
    const value = JSON.stringify(this.state, null, 2);
    if(value===this.lastQueuedValue)return this.writeQueue;
    this.lastQueuedValue=value;
    this.writeQueue = this.writeQueue.catch(() => {}).then(() => IOUtils.writeUTF8(this.statePath, value, {tmpPath: this.statePath + '.tmp'})).catch(error=>{this.lastQueuedValue='';throw error;});
    return this.writeQueue;
  }
  report(error) {
    Zotero.logError(error);
    this.state.watcherError = String(error.message || error);
    this.emit();
  }
  subscribe(callback) { this.listeners.add(callback); return () => this.listeners.delete(callback); }
  emit() {
    for (const node of this.windows.values()) node.setAttribute('label', '论文收件箱…' + (this.state.pending.length ? ` (${this.state.pending.length})` : ''));
    for (const callback of this.listeners) { try { callback(); } catch (error) { Zotero.logError(error); } }
  }
  attachWindow(win) {
    if (this.windows.has(win)) return;
    const parent = win.document.getElementById('menu_ToolsPopup');
    if (!parent) return;
    const node = win.document.createXULElement('menuitem');
    node.id = 'paper-inbox-open';
    node.addEventListener('command', () => this.openPanel());
    parent.appendChild(node); this.windows.set(win, node); this.emit();
  }
  detachWindow(win) { this.windows.get(win)?.remove(); this.windows.delete(win); }
  openPanel() {
    if (this.stopped) return;
    if (this.panel && !this.panel.closed) { this.panel.focus(); this.emit(); return; }
    const win = Zotero.getMainWindow();
    if (!win) return;
    this.panel = win.openDialog('chrome://paper-inbox/content/panel.xhtml', 'paper-inbox-panel',
      'chrome,dialog=no,resizable,centerscreen,width=920,height=700', {controller: this});
  }
  async listFiles() {
    const files = [];
    for (const path of await IOUtils.getChildren(this.state.folder)) {
      if (!/\.pdf$/i.test(path)) continue;
      try {
        const info = await IOUtils.stat(path);
        files.push({path, name: PathUtils.filename(path), size: info.size, lastModified: info.lastModified, type: info.type});
      } catch (_) { /* A browser may rename the file between enumeration and stat. */ }
    }
    return files;
  }
  async isReadyPDF(file) {
    try {
      const head = new TextDecoder('latin1').decode(await IOUtils.read(file.path, {maxBytes: 1024}));
      const tail = new TextDecoder('latin1').decode(await IOUtils.read(file.path, {offset: Math.max(0, file.size - 4096), maxBytes: 4096}));
      return head.includes('%PDF-') && tail.includes('%%EOF');
    } catch (_) { return false; }
  }
  async scan() {
    if (this.stopped || !this.state.enabled || this.scanning) return;
    this.scanning = true;
    try {
      const files = await this.listFiles();
      const ready = [];
      for (const file of files) if (PaperInboxCore.isPDF(file) && await this.isReadyPDF(file)) ready.push(file);
      const wasInitialized = this.state.initialized;
      const added = this.tracker.observe(ready, Date.now());
      this.state.watcherError = '';
      if (this.tracker.changed || !wasInitialized) await this.persist();
      this.emit();
      if (added.length && this.state.notify) this.openPanel();
    } catch (error) { this.report(new Error('无法检查下载目录：' + (error.message || error))); }
    finally { this.scanning = false; }
  }
  async saveSettings({enabled, notify, folder}) {
    if (this.busy) throw new Error('请等待当前导入完成后再修改设置。');
    const info = await IOUtils.stat(folder);
    if (info.type !== 'directory') throw new Error('请选择一个文件夹。');
    if (PaperInboxCore.keyFor(folder) !== PaperInboxCore.keyFor(this.state.folder)) {
      this.state.folder = folder; this.state.known = {}; this.state.initialized = false;
      this.tracker = new PaperInboxCore.DownloadTracker(this.state);
    }
    this.state.enabled = !!enabled; this.state.notify = !!notify;
    await this.persist(); await this.scan(); this.emit();
  }
  async chooseFolder(win) {
    const picker = Cc['@mozilla.org/filepicker;1'].createInstance(Ci.nsIFilePicker);
    picker.init(win.browsingContext, '选择 PDF 下载目录', Ci.nsIFilePicker.modeGetFolder);
    const result = await new Promise(resolve => picker.open(resolve));
    return result === Ci.nsIFilePicker.returnOK ? picker.file.path : null;
  }
  async ignore(ids) {
    if (this.busy) throw new Error('请等待当前导入完成。');
    const selected = new Set(ids);
    for (const job of this.state.pending.filter(p => selected.has(p.id))) this.history({name: job.name, status: 'ignored', message: '已忽略，原文件保留。'});
    this.state.pending = this.state.pending.filter(p => !selected.has(p.id));
    await this.persist(); this.emit();
  }
  history(entry) { this.state.history.unshift({...entry, time: new Date().toISOString()}); this.state.history.length = Math.min(100, this.state.history.length); }
  async process(ids, {approved = false} = {}) {
    if (!approved) throw new Error('需要先在待处理窗口确认导入。');
    if (this.stopped || this.busy) return;
    const selected = new Set(ids);
    const jobs = this.state.pending.filter(p => selected.has(p.id));
    if (!jobs.length) return;
    this.busy = true;
    try {
      for (const job of jobs) {
        if (this.stopped) break;
        job.status = 'processing'; job.error = ''; this.progress = '正在处理：' + job.name;
        await this.persist(); this.emit();
        try {
          const result = await this.importOne(job);
          this.history({jobId: job.id, name: job.name, ...result});
          this.state.pending = this.state.pending.filter(p => p.id !== job.id);
        } catch (error) {
          Zotero.logError(error); job.status = 'error'; job.error = String(error.message || error);
          this.history({jobId: job.id, name: job.name, status: 'error', message: job.error});
        }
        await this.persist(); this.emit();
      }
    } finally { this.busy = false; this.progress = ''; await this.persist(); this.emit(); }
  }
  async sha256(path) {
    const bytes = await IOUtils.read(path);
    const hasher = Cc['@mozilla.org/security/hash;1'].createInstance(Ci.nsICryptoHash);
    hasher.init(hasher.SHA256); hasher.update(bytes, bytes.length);
    return Array.from(hasher.finish(false), c => c.charCodeAt(0).toString(16).padStart(2, '0')).join('');
  }
  async findIdentical(hash, sourceSize) {
    const items = await Zotero.Items.getAll(Zotero.Libraries.userLibraryID, false, false);
    for (const att of items) {
      if (!att.isAttachment() || !att.isPDFAttachment()) continue;
      const path = await att.getFilePathAsync();
      if (!path) continue;
      try {
        const stat = await IOUtils.stat(path);
        if(sourceSize!==undefined&&stat.size!==sourceSize)continue;
        let entry = this.state.libraryHashes[att.key];
        if (!entry || entry.path !== path || entry.size !== stat.size || entry.lastModified !== stat.lastModified) {
          entry = {path, size: stat.size, lastModified: stat.lastModified, hash: await this.sha256(path)};
          this.state.libraryHashes[att.key] = entry;
        }
        if (entry.hash === hash) return att;
      } catch (error) { Zotero.logError(error); }
    }
    return null;
  }
  async collection(path) {
    let parentID = null, result;
    for (const name of path.split('/')) {
      result = Zotero.Collections.getByLibrary(Zotero.Libraries.userLibraryID,true).find(c => c.name === name && (c.parentID || null) === parentID);
      if (!result) {
        result = new Zotero.Collection(); result.libraryID = Zotero.Libraries.userLibraryID; result.name = name;
        if (parentID) result.parentID = parentID;
        await result.saveTx();
      }
      parentID = result.id;
    }
    return result.id;
  }
  async organize(item, paths, tags) {
    await item.loadAllData();
    for (const path of paths) {const parts=path.split('/');for(let i=1;i<=parts.length;i++)item.addToCollection(await this.collection(parts.slice(0,i).join('/')));}
    for (const tag of tags) item.addTag(tag, 0);
    await item.saveTx();
  }
  async importOne(job) {
    const stat = await IOUtils.stat(job.path);
    if (PaperInboxCore.fingerprint(stat) !== job.fingerprint) throw new Error('文件大小或修改时间已变化，请等下载完成后重试。');
    if (!await this.isReadyPDF({...job, size: stat.size})) throw new Error('PDF 尚未完成下载或无法读取。');
    const sourceHash = await this.sha256(job.path);
    if (job.plan && sourceHash !== job.plan.sourceHash) throw new Error('PDF 内容在分析后发生变化，请重新分析并确认。');
    let attachment = job.attachmentKey && Zotero.Items.getByLibraryAndKey(Zotero.Libraries.userLibraryID, job.attachmentKey);
    let identical = false;
    if (!attachment) { attachment = await this.findIdentical(sourceHash, stat.size); identical = !!attachment; }
    if (!attachment) {
      attachment = await Zotero.Attachments.importFromFile({file: job.path, libraryID: Zotero.Libraries.userLibraryID,
        contentType: 'application/pdf', title: job.name, collections: [await this.collection('下载收件箱')]});
      job.attachmentKey = attachment.key;
      await this.persist();
      const stored = await attachment.getFilePathAsync();
      if (!stored || await this.sha256(stored) !== sourceHash) throw new Error('附件与来源内容不一致，已保留条目，请核对后重试。');
    }
    let extracted = '';
    try { if(!job.plan){const result = await Zotero.PDFWorker.getFullText(attachment.id, 2); extracted = result.text || '';} }
    catch (error) { Zotero.logError(error); }
    if (!attachment.parentID && job.plan && job.plan.title.trim()) {
      const parent=new Zotero.Item(job.plan.itemType||'document');parent.libraryID=Zotero.Libraries.userLibraryID;
      parent.setField('title',job.plan.title);if(job.plan.doi){if(Zotero.ItemFields.isValidForType(Zotero.ItemFields.getID('DOI'),parent.itemTypeID))parent.setField('DOI',job.plan.doi);else parent.setField('extra','DOI: '+job.plan.doi);}
      if(/^(18|19|20|21)\d{2}$/.test(job.plan.year||''))parent.setField('date',job.plan.year);
      parent.setCreators((job.plan.authors||[]).map(name=>({creatorType:'author',fieldMode:1,lastName:name})));
      await parent.saveTx();attachment.parentID=parent.id;await attachment.saveTx();
      await this.organize(parent,['下载收件箱'],[]);
    }
    if (!attachment.parentID && !job.plan && !Zotero.HTTP.browserIsOffline()) {
      this.progress = '正在识别书目信息：' + job.name; this.emit();
      await Zotero.RecognizeDocument.recognizeItems([attachment]);
      attachment = await Zotero.Items.getAsync(attachment.id);
    }
    if (!attachment.parentID) {
      await this.organize(attachment, ['下载收件箱','待核对', '待核对/书目信息',...(job.plan?.collections||[])], ['整理:书目信息待核对',...(job.plan?.tags||[])]);
      return {status: 'unidentified', attachmentKey: attachment.key, message: 'PDF 已保存，未能识别完整书目信息，已放入待核对。'};
    }
    const parent = await Zotero.Items.getAsync(attachment.parentID);
    await parent.loadAllData();
    const classification = job.plan ? {collections:job.plan.collections,tags:[...job.plan.tags,'整理:API分类'],confident:job.plan.confidence>=0.7&&job.plan.isAcademic} : PaperInboxCore.classify({title: parent.getField('title'), abstractNote: parent.getField('abstractNote') || extracted.slice(0, 2500)});
    await this.organize(parent, classification.collections, classification.tags);
    const doi = PaperInboxCore.normalizeDOI(parent.getField('DOI'));
    let duplicates = [];
    if (doi && !identical) {
      duplicates = (await Zotero.Items.getAll(Zotero.Libraries.userLibraryID, true, false)).filter(i => i.id !== parent.id && i.isRegularItem() && PaperInboxCore.normalizeDOI(i.getField('DOI')) === doi);
      if (duplicates.length) await this.organize(parent, ['待核对', '待核对/重复候选'], ['整理:重复候选']);
    }
    const message = duplicates.length ? '书目信息和分类已完成；相同 DOI 已有记录，已标记重复候选，未自动合并。'
      : identical ? '已在库中找到相同内容的 PDF，已复用原附件并检查分类。'
      : classification.confident ? '书目信息已识别，并完成确认的分类和标签。' : '书目信息已识别，分类需确认，已放入待核对。';
    return {status: duplicates.length ? 'duplicate' : identical ? 'identical' : 'imported', parentKey: parent.key, attachmentKey: attachment.key, message};
  }
  async stop() {
    this.stopped = true;
    if (this.timer) clearInterval(this.timer);
    this.timer = null;
    for (const win of [...this.windows.keys()]) this.detachWindow(win);
    if (this.panel && !this.panel.closed) this.panel.close();
    this.listeners.clear();
    await this.persist();
  }
};
if (typeof module !== 'undefined') module.exports = PaperInboxController;
