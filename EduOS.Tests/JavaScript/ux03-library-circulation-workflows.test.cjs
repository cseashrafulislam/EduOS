const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname,
    '../../EduOS.App/wwwroot/js/library/circulation.js'), 'utf8');
const flush = async () => { for (let n = 0; n < 7; n++) await new Promise(setImmediate); };
const deferred = () => { let release; const promise = new Promise(resolve => { release = resolve; });
    return { release, promise }; };
const response = (data, success = true) => ({ ok: success,
    json: async () => ({ success, message: success ? '' : 'Uncertain result', data }) });

function page() {
    const elements = new Map(), requests = [], gates = { post: [], export: [], list: [] }, observers = [];
    let uuid = 0, downloads = 0;
    const make = tag => {
        const handlers = new Map();
        return { tagName: tag.toUpperCase(), handlers, children: [], value: '', disabled: false,
            textContent: '', hidden: false, className: '',
            classList: { values: new Set(['d-none']), add(key) { this.values.add(key); },
                remove(key) { this.values.delete(key); } },
            addEventListener(type, handler) { handlers.set(type, handler); },
            append(...children) { this.children.push(...children); },
            replaceChildren(...children) { this.children = children; },
            add(option) { this.children.push(option); },
            focus() {}, click() { downloads++; }, remove() {} };
    };
    const get = id => {
        if (!elements.has(id)) { const element = make('div'); element.id = id; elements.set(id, element); }
        return elements.get(id);
    };
    const issue = { reference: 'book-11', bookTitle: 'Algebra', studentName: 'Student',
        state: 1, dueDate: '2026-12-10', rowVersion: 'v1' };
    const record = { issue, campusId: 1, daysOverdue: 0, isOverdue: false };
    const data = { items: [record], totalPages: 1, totalCount: 1 };
    const document = { body: { dataset: { authTenantId: '25', authSessionReference: 'current' },
        append: () => {} }, documentElement: { lang: 'en-BD' },
        getElementById: get, createElement: make,
        querySelector: () => ({ content: 'csrf' }) };
    const window = { EduOSSessionTransitionPending: () => false, confirm: () => true, print() {} };
    const fetch = (url, options = {}) => {
        requests.push({ url, options });
        if (options.method === 'POST') return gates.post.shift()?.promise || response({});
        if (url.includes('/export?')) return gates.export.shift()?.promise || response({});
        if (url.includes('/circulation?')) return gates.list.shift()?.promise || response(data);
        return response({ items: [], totalPages: 1 });
    };
    function Observer(callback) { observers.push(callback); this.observe = () => {}; this.disconnect = () => {}; }
    const URLWithBlob = class extends URL {};
    URLWithBlob.createObjectURL = () => 'blob:export';
    URLWithBlob.revokeObjectURL = () => {};
    vm.runInNewContext(source, { document, window, fetch,
        MutationObserver: Observer, URL: URLWithBlob, URLSearchParams,
        Option: function Option(label, value) { this.textContent = label; this.value = String(value); },
        crypto: { randomUUID: () => 'request-' + ++uuid },
        setTimeout, Date, console });
    const fire = (id, type) => {
        const node = get(id), handler = node.handlers.get(type);
        assert.ok(handler, id + ':' + type + ' not bound');
        return handler({ preventDefault() {}, currentTarget: node });
    };
    const chooseDirect = () => {
        get('directIssueBorrowerType').value = 'Student';
        get('directIssueStudent').value = 'student-1';
        get('directIssueCopy').value = '11';
        get('directIssueBook').value = 'book-1';
        get('directIssueDueDate').value = new Date(Date.now() + 15 * 86400000).toISOString().slice(0, 10);
    };
    const switchTenant = () => {
        document.body.dataset.authTenantId = '26'; observers.forEach(callback => callback());
    };
    return { get, fire, chooseDirect, switchTenant, requests, gates,
        posts: () => requests.filter(x => x.options.method === 'POST'),
        downloads: () => downloads };
}

test('Direct library issue submits exactly once even if form is redispatched while pending', async () => {
    const p = page(); await flush(); p.chooseDirect();
    const gate = deferred(); p.gates.post.push(gate);
    p.fire('directIssueForm', 'submit'); p.fire('directIssueForm', 'submit'); await flush();
    assert.equal(p.posts().length, 1);
    assert.equal(p.get('directIssueBook').disabled, true);
    assert.equal(p.get('directIssueDueDate').disabled, true);
    gate.release(response({ issueId: 11 })); await flush();
    assert.equal(p.get('directIssueDueDate').disabled, false);
});

test('Uncertain issue retry reuses request key; an edited copy gets a new request key', async () => {
    const p = page(); await flush(); p.chooseDirect();
    p.gates.post.push({ promise: Promise.resolve(response(null, false)) },
        { promise: Promise.resolve(response(null, false)) });
    p.fire('directIssueForm', 'submit'); await flush();
    p.fire('directIssueForm', 'submit'); await flush();
    const keys = p.posts().map(x => JSON.parse(x.options.body).clientRequestId);
    assert.equal(keys[0], keys[1]);
    p.get('directIssueCopy').value = '12'; p.fire('directIssueCopy', 'change');
    p.fire('directIssueForm', 'submit'); await flush();
    assert.notEqual(JSON.parse(p.posts()[2].options.body).clientRequestId, keys[1]);
});

test('Pending renewal cannot be cancelled or posted again until server resolves', async () => {
    const p = page(); await flush();
    const row = p.get('circulationRows').children[0];
    row.children.at(-1).children[1].handlers.get('click')();
    const gate = deferred(); p.gates.post.push(gate);
    p.fire('circulationRenewalForm', 'submit'); p.fire('circulationRenewalForm', 'submit');
    p.fire('circulationRenewalCancel', 'click'); await flush();
    assert.equal(p.posts().length, 1);
    assert.equal(p.get('circulationRenewalForm').classList.values.has('d-none'), false);
    gate.release(response(null, false)); await flush();
    assert.equal(p.get('circulationRenewalDue').disabled, false);
    assert.equal(p.get('circulationRenewalForm').classList.values.has('d-none'), false);
});

test('Tenant change purges the borrower/book rows and rejects old issue command', async () => {
    const p = page(); await flush(); p.chooseDirect();
    assert.equal(p.get('circulationRows').children.length, 1);
    p.switchTenant();
    p.fire('directIssueForm', 'submit'); await flush();
    assert.equal(p.posts().length, 0);
    assert.equal(p.get('directIssueStudent').children.length, 0);
    assert.equal(p.get('directIssueStudentQuery').value, '');
    assert.match(p.get('circulationAlert').textContent, /session changed/);
});

test('Library export discards response after search or page changes', async () => {
    const p = page(); await flush();
    const gate = deferred(); p.gates.export.push(gate);
    p.fire('circulationCsv', 'click'); await flush();
    p.get('circulationSearch').value = 'different search';
    gate.release({ ok: true, blob: async () => new Blob(['copy']) }); await flush();
    assert.equal(p.downloads(), 0);
    assert.equal(p.get('circulationCsv').disabled, false);
});
