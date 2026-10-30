const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const root = path.join(__dirname, '..');
test('兼容范围只包含已支持的 Zotero 9 与 10', () => {
  const manifest = JSON.parse(fs.readFileSync(path.join(root, 'manifest.json'), 'utf8'));
  assert.equal(manifest.version, '0.5.1');
  assert.equal(manifest.applications.zotero.strict_min_version, '9.0');
  assert.equal(manifest.applications.zotero.strict_max_version, '10.0.*');
});

for (const [version, expectedAction] of [['9.0.6', 'getFulltext'], ['10.0.6', 'pdf.getFulltext']]) {
  test(`Zotero ${version} 正确提取正文且不创建文库附件`, async () => {
    const calls = [];
    const pdf = new Uint8Array([37, 80, 68, 70]);
    const scope = {
      module: { exports: {} },
      Zotero: {
        version,
        File: { pathToFile: p => ({ path: p, normalize() {} }) },
        PDFWorker: {
          _enqueue: async (fn, priority) => { assert.equal(priority, true); return fn(); },
          _query: async (action, data, transfer) => {
            calls.push(action);
            assert.equal(data.maxPages, 12);
            assert.equal(data.buf.byteLength, pdf.byteLength);
            assert.equal(transfer[0], data.buf);
            return { text: 'A paper about magnetic correlations', totalPages: 7 };
          }
        },
        Libraries: { userLibraryID: 1 },
        Collections: { getByLibrary: () => [] },
        Attachments: { importFromFile() { throw new Error('Preview must not import'); } },
        Item() { throw new Error('Preview must not create an item'); },
        logError(e) { throw e; }
      },
      IOUtils: { stat: async () => ({ type: 'regular', size: 4, lastModified: 123 }), read: async () => pdf }
    };
    vm.createContext(scope);
    vm.runInContext(fs.readFileSync(path.join(root, 'bridge.js'), 'utf8'), scope);
    const bridge = new scope.module.exports({ isReadyPDF: async () => true, sha256: async () => 'a'.repeat(64), state: { externalRequests: {} } }, '0.5.1');
    bridge.token = 'test-token';
    const response = await bridge.handle('/paper-inbox/extract', {
      headers: { 'X-PaperInbox-Token': 'test-token' },
      data: { id: '1234567890abcdef1234567890abcdef', path: 'paper.pdf', size: 4, lastModified: 123 }
    });
    assert.equal(response[0], 200);
    const body = JSON.parse(response[2]);
    assert.equal(body.text, 'A paper about magnetic correlations');
    assert.equal(body.sourceHash, 'a'.repeat(64));
    assert.equal(body.totalPages, 7);
    assert.deepEqual(calls, [expectedAction]);
  });
}
