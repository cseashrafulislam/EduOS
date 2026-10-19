const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const js = fs.readFileSync(path.resolve(__dirname, '../../EduOS.App/wwwroot/js/communication/deliveries.js'), 'utf8');

test('delivery export forwards filters and restores button state', async () => {
    const nodes = new Map(), calls = [], downloads = [];
    const node = id => {
        if (!nodes.has(id)) nodes.set(id, {
            value: '', textContent: '', disabled: false, listeners: {},
            classList: { add() {}, remove() {} },
            addEventListener(name, fn) { this.listeners[name] = fn; },
            replaceChildren() {}, append() {}
        });
        return nodes.get(id);
    };
    node('deliveryChannel').value = '2';
    node('deliveryState').value = '5';
    const document = {
        getElementById: node,
        addEventListener(name, fn) { if (name === 'DOMContentLoaded') fn(); },
        createElement(tag) {
            if (tag === 'a') return { click() { downloads.push(this.download); }, remove() {} };
            return { textContent: '', append() {}, colSpan: 0 };
        },
        body: { append() {} }
    };
    const fetch = async (url, options) => {
        calls.push({ url, options });
        return url.includes('/export?')
            ? { ok: true, blob: async () => new Blob(['csv']) }
            : { ok: true, json: async () => ({ success: true, data: { items: [], totalPages: 1 } }) };
    };
    const URLStub = { createObjectURL: () => 'blob:mock', revokeObjectURL() {} };
    vm.runInNewContext(js, { document, fetch, URL: URLStub, URLSearchParams, setTimeout: fn => fn() });
    await node('deliveryExport').listeners.click();
    const call = calls.find(x => x.url.includes('/export?'));
    assert.ok(call);
    const query = new URL(call.url, 'https://example.test').searchParams;
    assert.equal(query.get('channel'), '2');
    assert.equal(query.get('state'), '5');
    assert.equal(query.has('page'), false);
    assert.equal(call.options.credentials, 'same-origin');
    assert.deepEqual(downloads, ['communication-deliveries.csv']);
    assert.equal(node('deliveryExport').disabled, false);
});
