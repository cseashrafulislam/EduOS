const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const view = read('EduOS.App/Views/AuditLogs/Index.cshtml');
const js = read('EduOS.App/wwwroot/js/audit-logs/index.js');
const service = read('EduOS.Service/Services/System/AuditLogService.cs');

test('audit CSV export uses the existing privileged read-only workflow and a bounded query', () => {
    assert.match(view, /id="auditExport"/);
    assert.match(js, /\/api\/v1\/AuditLog\/export\?/);
    assert.match(service, /Take\(1001\)/);
    assert.match(service, /rows\.Count > 1000/);
    assert.match(service, /FilterQuery\(filter\)/);
    assert.match(service, /_user\.TenantId/);
    assert.match(service, /CsvCell/);
    assert.doesNotMatch(js, /innerHTML|outerHTML|insertAdjacentHTML/);
});

test('export retains active UTC filters and downloads a CSV', async () => {
    const nodes = new Map(), calls = [], links = [];
    const node = id => {
        if (!nodes.has(id)) nodes.set(id, {
            value: '', textContent: '', disabled: false, className: '', listeners: [],
            addEventListener(name, fn) { this.listeners[name] = fn; },
            replaceChildren() {}, append() {}, focus() {}
        });
        return nodes.get(id);
    };
    for (const id of ['auditFilterForm','auditRows','auditFrom','auditTo','auditEntity',
        'auditAction','auditUserId','auditStatus','auditPrevious','auditNext','auditClear',
        'auditPage','auditCount','auditAlert','auditExport']) node(id);
    node('auditFrom').value = '2026-10-01';
    node('auditEntity').value = 'Campus';
    const document = {
        getElementById: node,
        createElement: tag => tag === 'a'
            ? { click() { links.push(this.download); }, remove() {} }
            : { textContent: '', append() {}, colSpan: 0 },
        body: { append() {} }
    };
    const fetch = async (url, options) => {
        calls.push({ url, options });
        return url.includes('/export?')
            ? { ok: true, blob: async () => new Blob(['csv']) }
            : { ok: true, json: async () => ({ success: true, data: { items: [], totalPages: 1, totalCount: 0 } }) };
    };
    const URLStub = { createObjectURL: () => 'blob:mock', revokeObjectURL() {} };
    const vm = require('node:vm');
    vm.runInNewContext(js, { document, fetch, URL: URLStub, URLSearchParams,
        AbortController, setTimeout: fn => fn(), Blob });
    await new Promise(resolve => setImmediate(resolve));
    await node('auditExport').listeners.click();
    const exportCall = calls.find(c => c.url.includes('/export?'));
    assert.ok(exportCall, 'CSV request missing');
    const q = new URL(exportCall.url, 'https://example.test').searchParams;
    assert.equal(q.get('fromUtc'), '2026-10-01T00:00:00Z');
    assert.equal(q.get('entityName'), 'Campus');
    assert.equal(q.has('page'), false);
    assert.equal(exportCall.options.credentials, 'same-origin');
    assert.equal(exportCall.options.cache, 'no-store');
    assert.deepEqual(links, ['audit-logs.csv']);
    assert.equal(node('auditExport').disabled, false);
});
