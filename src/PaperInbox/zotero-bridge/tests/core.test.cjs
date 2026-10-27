const test = require('node:test');
const assert = require('node:assert/strict');
const Core = require('../core.js');
global.PaperInboxCore = Core;
global.Zotero = {logError() {}};
const Controller = require('../controller.js');
const file = (name, size = 100, lastModified = 1) => ({path: 'C:\\Downloads\\' + name, name, type: 'regular', size, lastModified});

test('首次启动只建立基线，不弹出旧 PDF', () => {
  const state = Core.emptyState(), tracker = new Core.DownloadTracker(state);
  assert.deepEqual(tracker.observe([file('old.pdf')], 0), []);
  assert.deepEqual(tracker.observe([file('old.pdf')], 20000), []);
  assert.equal(state.pending.length, 0);
});
test('新 PDF 大小和时间稳定后只入队一次', () => {
  const state = Core.emptyState(), tracker = new Core.DownloadTracker(state, 12000);
  tracker.observe([], 0);
  tracker.observe([file('new.pdf', 100)], 1000);
  tracker.observe([file('new.pdf', 200, 2)], 8000);
  assert.equal(tracker.observe([file('new.pdf', 200, 2)], 15000).length, 0);
  assert.equal(tracker.observe([file('new.pdf', 200, 2)], 21000).length, 1);
  assert.equal(tracker.observe([file('new.pdf', 200, 2)], 40000).length, 0);
});
test('部分下载、空文件和目录不入队', () => {
  assert.equal(Core.isPDF(file('a.pdf.crdownload')), false);
  assert.equal(Core.isPDF(file('a.pdf.part')), false);
  assert.equal(Core.isPDF(file('a.pdf', 0)), false);
  assert.equal(Core.isPDF({...file('a.pdf'), type: 'directory'}), false);
});
test('稍后及重启保留待处理记录，不重复创建任务', () => {
  let state = Core.emptyState(), tracker = new Core.DownloadTracker(state, 1);
  tracker.observe([], 0); tracker.observe([file('a.pdf')], 1); tracker.observe([file('a.pdf')], 3);
  state = JSON.parse(JSON.stringify(state)); tracker = new Core.DownloadTracker(state, 1);
  tracker.observe([file('a.pdf')], 100); tracker.observe([file('a.pdf')], 200);
  assert.equal(state.pending.length, 1);
});
test('待处理文件内容改变后更新指纹并要求持久化', () => {
  const state = Core.emptyState(), tracker = new Core.DownloadTracker(state, 1);
  tracker.observe([], 0); tracker.observe([file('a.pdf')], 1); tracker.observe([file('a.pdf')], 3);
  tracker.observe([file('a.pdf', 200, 2)], 5); tracker.observe([file('a.pdf', 200, 2)], 7);
  assert.equal(state.pending.length, 1);
  assert.equal(state.pending[0].fingerprint, '200:2');
  assert.equal(tracker.changed, true);
});
test('Zotero 关闭期间的下载在重新启动后发现', () => {
  const state = Core.emptyState(); new Core.DownloadTracker(state).observe([file('old.pdf')], 0);
  const tracker = new Core.DownloadTracker(state, 1);
  tracker.observe([file('old.pdf'), file('offline.pdf')], 100);
  assert.equal(tracker.observe([file('old.pdf'), file('offline.pdf')], 102).length, 1);
});
test('同一文件新版本可再次提示，文件名大小写变化不会重复入队', () => {
  const state = Core.emptyState(), tracker = new Core.DownloadTracker(state, 1);
  tracker.observe([file('A.PDF')], 0);
  assert.equal(tracker.observe([file('a.pdf')], 1).length, 0);
  tracker.observe([file('a.pdf', 300, 2)], 2);
  assert.equal(tracker.observe([file('a.pdf', 300, 2)], 4).length, 1);
});
test('材料、主题、方法使用不同维度', () => {
  const result = Core.classify({title: 'Magnetoelectric effect in garnet Mn3Al2Ge3O12', abstractNote: 'Magnetic susceptibility, heat capacity and neutron diffraction.'});
  assert.ok(result.collections.includes('石榴石体系/Mn3Al2Ge3O12'));
  for (const tag of ['主题:磁电耦合','方法:磁化率与磁化测量','方法:比热测量','方法:中子衍射']) assert.ok(result.tags.includes(tag));
});
test('沿用已有辉石分类，保留 HTML 化学式识别', () => {
  const result = Core.classify({title:'Magnetic structure of CaMnGe<sub>2</sub>O<sub>6</sub> pyroxene'});
  assert.ok(result.collections.includes('CaXGe2O6'));
  assert.ok(result.collections.includes('辉石体系'));
});
test('综述、实验方法和未知内容分别处理', () => {
  assert.ok(Core.classify({title:'Recent progress in studies of cobalt-based quasi-1-dimensional quantum magnets'}).collections.includes('综述与理论背景'));
  assert.ok(Core.classify({title:'Development of a neutron single-crystal diffractometer'}).collections.includes('实验与分析方法'));
  assert.deepEqual(Core.classify({title:'未知资料'}).collections, ['待核对','待核对/分类']);
  assert.equal(Core.classify({title:'未知资料'}).confident, false);
});
test('钴基链材料从摘要补足体系，但不把引用文献 DOI 用于合并', () => {
  const result = Core.classify({title:'Spin disorder in an Ising honeycomb chain cobaltate', abstractNote:'A quasi-one-dimensional Ba3Co2O6(CO3)0.7 antiferromagnet.'});
  assert.ok(result.collections.includes('钴基链状材料/Ba3Co2O6(CO3)x 与相关相'));
  assert.equal(Core.normalizeDOI('https://doi.org/10.1103/PhysRevB.89.054431'), '10.1103/physrevb.89.054431');
});
test('监测不会导入文件；必须显式确认', async () => {
  const controller = new Controller({}); let imports = 0, prompts = 0;
  controller.state.initialized = true; controller.tracker = new Core.DownloadTracker(controller.state, 0);
  controller.listFiles = async () => [file('new.pdf')]; controller.isReadyPDF = async () => true;
  controller.persist = async () => {}; controller.openPanel = () => { prompts++; }; controller.importOne = async () => { imports++; };
  await controller.scan(); await controller.scan();
  assert.equal(imports, 0); assert.equal(prompts, 1); assert.equal(controller.state.pending.length, 1);
  await assert.rejects(controller.process([controller.state.pending[0].id]), /确认/);
  assert.equal(imports, 0);
});
test('忽略保留指纹，之后扫描不会重新询问同一版本', async () => {
  const controller = new Controller({}); controller.persist = async () => {};
  controller.state.initialized = true; controller.tracker = new Core.DownloadTracker(controller.state, 0);
  controller.tracker.observe([file('a.pdf')], 0); controller.tracker.observe([file('a.pdf')], 1);
  await controller.ignore([controller.state.pending[0].id]);
  assert.equal(controller.tracker.observe([file('a.pdf')], 10).length, 0);
  assert.equal(controller.state.history[0].status, 'ignored');
});
test('批量确认只处理所选文件，错误任务可重试', async () => {
  const controller = new Controller({}); controller.persist = async () => {};
  controller.state.pending = [{...file('a.pdf'),id:'a'}, {...file('b.pdf'),id:'b'}];
  controller.importOne = async job => { if(job.id==='a') throw new Error('network'); return {status:'imported',message:'ok'}; };
  await controller.process(['a'], {approved:true});
  assert.equal(controller.state.pending.length, 2);
  assert.equal(controller.state.pending[0].status, 'error');
  controller.importOne = async () => ({status:'imported',message:'ok'});
  await controller.process(['b'], {approved:true});
  assert.deepEqual(controller.state.pending.map(p=>p.id), ['a']);
  assert.equal(controller.busy, false);
});
