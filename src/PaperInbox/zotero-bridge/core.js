/* Pure matching and download-state logic. No file, network, or library writes. */
var PaperInboxCore = (() => {
  'use strict';
  function text(value) {
    return String(value || '').replace(/<[^>]*>/g, '').replace(/&amp;/g, '&').replace(/\s+/g, ' ').trim();
  }
  const flat = value => text(value).replace(/\s+/g, '').toLowerCase();
  const keyFor = path => String(path).replace(/\\/g, '/').toLowerCase();
  const fingerprint = file => `${file.size}:${file.lastModified}`;
  const isPDF = file => file.type === 'regular' && /\.pdf$/i.test(file.name || file.path) && file.size > 0;
  function normalizeDOI(value) {
    const match = text(value).match(/10\.\d{4,9}\/[^\s<>"\]]+/i);
    return match ? match[0].replace(/[.,;]+$/, '').toLowerCase() : '';
  }
  function classify(item) {
    const title = text(item.title), t = title.toLowerCase(), f = flat(title);
    const context = `${t} ${text(item.abstractNote).toLowerCase()}`;
    const paths = new Set(), tags = new Set();
    function add(path) {
      const parts = path.split('/');
      for (let i = 1; i <= parts.length; i++) paths.add(parts.slice(0, i).join('/'));
    }
    const scientific = f + ' ' + flat(item.abstractNote);
    if (/pyroxene|clinopyroxene|(?:ge|si)2o6/.test(f)) {
      add('辉石体系');
      if (/ca.*(?:ge|si)2o6|ca-bearing/.test(f)) add('CaXGe2O6');
      if (/na(?:fe|v|cr|mn|al|m).*?(?:ge|si)2o6|li(?:v|cr)ge2o6/.test(f)) add('NaFeSi2O6');
      if (/sr(?:co|m)ge2o6|cobalt-basedpyroxenes/.test(f)) add('SrCoGe2O6');
      if (/srmnge2o6/.test(f)) add('辉石体系/SrMnGe2O6');
    }
    if (/garnet|hyperkagome|гранат/.test(t) || /mn3.*ge3o12/.test(f)) {
      add('石榴石体系');
      for (const element of ['Al', 'Cr', 'Fe', 'V']) {
        if (f.includes(`mn3${element.toLowerCase()}2ge3o12`)) add(`石榴石体系/Mn3${element}2Ge3O12`);
      }
      if (/crystal chemistry of the garnets|antiferromagnetic garnets/.test(t)) add('石榴石体系/综述与晶体化学');
    }
    if (/spinel|ni2geo4|geni2o4|coga2o4|co2geo4/.test(f)) add('尖晶石体系');
    if (/li2nige3o8|complexlithiumspinels/.test(f)) add('Li2NiGe3O8');
    if (/sr3nige5o14|m5_/.test(f)) add('Sr3NiGe5O14');
    if (/ba3co2o6|ba8co7o21|cose2o5|ca3coiro6|co\(nb|conb2o6|cota2o6|spin-chainoxides|cobalt-basedquasi|isinghoneycombchain/.test(f)) add('钴基链状材料');
    if (/ba3co2o6|ba8co7o21/.test(scientific)) add('钴基链状材料/Ba3Co2O6(CO3)x 与相关相');
    if (/cose2o5/.test(f)) add('钴基链状材料/CoSe2O5');
    if (/co\(nb|conb2o6|cota2o6/.test(f)) add('钴基链状材料/Co(Nb,Ta)2O6');
    if (/ca3coiro6/.test(f)) add('钴基链状材料/Ca3CoIrO6');
    if (/honeycomb|triangular|na2co2teo6|na2ni2teo6|na3ni2bio6|ba3ninb2o9|ninb2o6/.test(f)) {
      add('蜂窝与三角晶格磁体');
      if (f.includes('na2co2teo6')) add('蜂窝与三角晶格磁体/Na2Co2TeO6');
      if (f.includes('na2ni2teo6')) add('蜂窝与三角晶格磁体/Na2Ni2TeO6');
      if (f.includes('na3ni2bio6')) add('Na3Ni2BiO6');
    }
    if (/te3o18/.test(f)) add('碲氧化物体系/AB2C6Te3O18');
    if (/co3bpo7|srco2bpo7/.test(f)) add('硼磷酸盐磁体');
    if (/fe2mo3o8|mntio3/.test(f)) add('其他磁电氧化物');
    if (/gdvo3|gd2pdge6|k3ln\(po4\)2/.test(f)) add('稀土磁性材料');
    if (/melilite|ba2coge2o7|a2cosi2o7|sr2mge2o7/.test(f)) add('黄长石体系');
    if (/review|recent progress|strongly geometrically|antiferromagnetic garnets|crystal chemistry of the garnets|toroidal moment in condensed|competing magnetic exchange|过渡金属化合物/.test(t)) add('综述与理论背景');
    if (/tutorial|guide to interpreting|diffractometer|rmc-discord|neutron scattering studies/.test(t)) add('实验与分析方法');
    const rules = [
      ['主题:磁电耦合', /magnetoelectric|multiferroi|ferrotoroid|磁电/],
      ['主题:阻挫磁性', /frustrat|阻挫/],
      ['主题:量子磁性', /quantum magnet|kitaev|ising|spin-half|spin-1\/2/],
      ['主题:磁结构', /magnetic structur|spin structur|magnetic order|spin order|antiferromagnetic order/],
      ['主题:自旋动力学', /spin dynamic|magnetic excitation|spin excitation|magnon|spin.freezing/],
      ['主题:自旋玻璃', /spin.glass|cluster glass/],
      ['主题:磁热效应', /magnetocaloric|magnetic entropy|demagnetization refrigeration/],
      ['主题:晶体结构', /crystal structur|crystal chem|crystallograph/],
      ['主题:介电与光学性质', /dielectric|optical|luminescen|phosphor/],
      ['主题:电催化与离子输运', /electrocatal|lithium diffusion|lithium-ion conduction|anode electrode/],
      ['主题:热电与热输运', /thermoelectric|thermopower|thermal transport|transport properties/],
      ['方法:磁化率与磁化测量', /susceptibil|magnetization|磁化率/],
      ['方法:比热测量', /specific heat|heat capacity|比热/],
      ['方法:中子衍射', /neutron.?diffraction|neutron single-crystal diffractometer|neutron powder|нейтронограф/],
      ['方法:非弹性中子散射', /inelastic neutron/],
      ['方法:X射线衍射', /x-ray diffraction|x-ray powder diffraction|\bxrd\b/],
      ['方法:第一性原理计算', /first.principles|density functional|ab initio/],
      ['方法:磁共振', /\bnmr\b|nuclear magnetic resonance/],
      ['方法:蒙特卡洛', /monte carlo/]
    ];
    for (const [tag, pattern] of rules) if (pattern.test(context)) tags.add(tag);
    const confident = paths.size > 0;
    if (!confident) { add('待核对/分类'); tags.add('整理:待确认分类'); }
    return {collections: [...paths].sort(), tags: [...tags].sort(), confident};
  }
  function emptyState() {
    return {schema: 1, initialized: false, enabled: true, notify: true, folder: '', known: {}, pending: [], history: [], libraryHashes: {}};
  }
  class DownloadTracker {
    constructor(state, stableMs = 12000) { this.state = state; this.stableMs = stableMs; this.observed = new Map(); }
    observe(files, now) {
      this.changed = false;
      const pdfs = files.filter(isPDF), added = [];
      if (!this.state.initialized) {
        for (const file of pdfs) this.state.known[keyFor(file.path)] = fingerprint(file);
        this.state.initialized = true;
        this.changed = true;
        return added;
      }
      const existing = new Set(pdfs.map(f => keyFor(f.path)));
      for (const path of this.observed.keys()) if (!existing.has(path)) this.observed.delete(path);
      for (const file of pdfs) {
        const key = keyFor(file.path), fp = fingerprint(file);
        if (this.state.known[key] === fp) continue;
        const previous = this.observed.get(key);
        if (!previous || previous.fingerprint !== fp) { this.observed.set(key, {fingerprint: fp, since: now}); continue; }
        if (now - previous.since < this.stableMs) continue;
        const pending = this.state.pending.find(p => keyFor(p.path) === key);
        if (pending) {
          if (pending.fingerprint !== fp && pending.status !== 'processing') {
            Object.assign(pending, file, {fingerprint: fp, error: '', status: 'pending'});
            this.changed = true;
          }
        } else {
          const entry = {...file, id: `${now}-${added.length}-${this.state.pending.length}`, fingerprint: fp, status: 'pending', error: ''};
          this.state.pending.push(entry); added.push(entry);
          this.changed = true;
        }
        this.state.known[key] = fp;
        this.changed = true;
        this.observed.delete(key);
      }
      for (const key of Object.keys(this.state.known)) if (!existing.has(key)) { delete this.state.known[key]; this.changed = true; }
      return added;
    }
  }
  return {text, flat, keyFor, fingerprint, isPDF, normalizeDOI, classify, emptyState, DownloadTracker};
})();
if (typeof module !== 'undefined') module.exports = PaperInboxCore;
