const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');

function harness() {
    const source = fs.readFileSync(path.resolve(__dirname,
        '../../EduOS.App/wwwroot/js/inventory/asset-capitalization-readiness.js'), 'utf8')
        .replace(/^import .*;\r?\n/gm, '')
        .replace(/export (async )?function /g, (_, asyncWord) => (asyncWord || '') + 'function ');
    const requests = new Map(), notices = [], exports = [];
    const elements = new Map();
    const makeElement = () => ({
        children: [], listeners: {}, textContent: '', disabled: false,
        classList: { values: new Set(['d-none']), add(x) { this.values.add(x); },
            remove(x) { this.values.delete(x); }, contains(x) { return this.values.has(x); } },
        append(...x) { this.children.push(...x); },
        replaceChildren(...x) { this.children = x; },
        addEventListener(type, fn) { this.listeners[type] = fn; },
        click() { this.listeners.click?.(); }, remove() {}
    });
    const el = id => { if (!elements.has(id)) elements.set(id, makeElement()); return elements.get(id); };
    const api = url => new Promise((resolve, reject) => requests.set(url, { resolve, reject }));
    const document = { createElement: () => makeElement(), body: { append() {} } };
    const context = { api, el, td: value => ({ textContent: String(value) }),
        notice: value => notices.push(value), capitalizationCsv: value => value.assetTag,
        document, Blob: class { constructor(content) { this.content = content; } },
        URL: { createObjectURL(blob) { exports.push(blob.content[0]); return 'blob:test'; }, revokeObjectURL() {} },
        setTimeout: fn => fn(), encodeURIComponent };
    vm.runInNewContext(source + '\nthis.exports = {showCapitalizationReadiness, bindCapitalizationReadiness};', context);
    context.exports.bindCapitalizationReadiness();
    const data = tag => ({ assetTag: tag, statusCode: 'ReviewRequired', candidateLineCount: 1,
        candidates: [{ receiptReference: tag, postedMovementReference: 'move-' + tag }] });
    return { ...context.exports, requests, notices, exports, el, data };
}

test('asset evidence always belongs to the most recently selected asset, including CSV', async () => {
    const h = harness();
    const old = h.showCapitalizationReadiness('A');
    const latest = h.showCapitalizationReadiness('B');
    h.requests.get('/B/capitalization-readiness').resolve(h.data('B'));
    await latest;
    h.requests.get('/A/capitalization-readiness').resolve(h.data('A'));
    await old;
    assert.match(h.el('assetCapitalizationStatus').textContent, /^B ·/);
    assert.equal(h.el('assetCapitalizationRows').children[0].children[0].textContent, 'B');
    h.el('assetCapitalizationExport').click();
    assert.deepEqual(h.exports, ['B']);
    assert.deepEqual(h.notices, []);
});

test('closing the panel invalidates pending responses and prevents stale CSV', async () => {
    const h = harness();
    const pending = h.showCapitalizationReadiness('A');
    h.el('assetCapitalizationClose').click();
    h.requests.get('/A/capitalization-readiness').resolve(h.data('A'));
    await pending;
    assert.equal(h.el('assetCapitalizationPanel').classList.contains('d-none'), true);
    assert.equal(h.el('assetCapitalizationExport').disabled, true);
    assert.equal(h.el('assetCapitalizationRows').children.length, 0);
    h.el('assetCapitalizationExport').click();
    assert.deepEqual(h.exports, []);
});

test('late errors cannot overwrite a newer success; current errors remain visible', async () => {
    const h = harness();
    const old = h.showCapitalizationReadiness('A');
    const latest = h.showCapitalizationReadiness('B');
    h.requests.get('/B/capitalization-readiness').resolve(h.data('B'));
    await latest;
    h.requests.get('/A/capitalization-readiness').reject(new Error('old failure'));
    await old;
    assert.match(h.el('assetCapitalizationStatus').textContent, /^B ·/);
    assert.deepEqual(h.notices, []);
    const failure = h.showCapitalizationReadiness('C');
    h.requests.get('/C/capitalization-readiness').reject(new Error('current failure'));
    await failure;
    assert.match(h.el('assetCapitalizationStatus').textContent, /Evidence unavailable/);
    assert.deepEqual(h.notices, ['current failure']);
    assert.equal(h.el('assetCapitalizationExport').disabled, true);
});
