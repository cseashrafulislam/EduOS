const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../../EduOS.App/wwwroot/js/finance/institution-budgets.js'), 'utf8');

function deferred() {
    let release;
    const promise = new Promise(resolve => { release = resolve; });
    return { promise, release };
}
const response = (data, ok = true) => ({ ok, json: async () => ({ success: ok, data, message: ok ? '' : 'Timeout' }) });
const flush = async () => { for (let i = 0; i < 8; i++) await new Promise(setImmediate); };

function page() {
    const elements = new Map(), events = new Map(), requests = [], pending = [];
    const get = id => {
        if (!elements.has(id)) {
            const item = { id, value: '', disabled: false, textContent: '', innerHTML: '',
                dataset: id === 'institutionBudgets' ? { reviewer: 'true' } : {},
                classList: { values: new Set(['d-none']), add(name) { this.values.add(name); },
                    remove(name) { this.values.delete(name); }, toggle(name, enabled) {
                        if (enabled) this.values.add(name); else this.values.delete(name);
                    } },
                addEventListener(type, handler) { events.set(id + ':' + type, handler); },
                querySelectorAll: () => [], reset() { this.value = ''; } };
            elements.set(id, item);
        }
        return elements.get(id);
    };
    const form = get('budgetForm');
    form.querySelectorAll = () => ['budgetName', 'budgetFrom', 'budgetTo', 'budgetAccount', 'budgetAmount'].map(get);
    form.reset = () => { for (const el of form.querySelectorAll()) el.value = ''; };
    const document = { body: { dataset: { authTenantId: '25', authSessionReference: 'active' } },
        documentElement: { lang: 'bn-BD' }, getElementById: get,
        querySelector: () => ({ value: 'csrf' }) };
    const window = { addEventListener(type, handler) { events.set('window:' + type, handler); } };
    const fetch = async (url, options = {}) => {
        requests.push({ url, options });
        if (url.includes('/setup/accounts')) return { json: async () => ({ success: true,
            data: { items: [{ id: 4, code: '400', name: 'Fees', accountType: 4, isActive: true, isGroup: false }], totalPages: 1 } }) };
        if (options.method === 'POST') {
            if (pending.length) return pending.shift().promise;
            return response(url.endsWith('/review') ? {} : { id: 9 });
        }
        if (url.endsWith('/actual')) return response({ lines: [], actualIncomeBdt: 0, plannedIncomeBdt: 0, actualExpenseBdt: 0, plannedExpenseBdt: 0 });
        if (/\/budgets\/\d+$/.test(url)) return response({ id: 9, name: 'Budget', startDate: '2026-01-01',
            endDate: '2026-12-31', stateCode: 'Submitted', rowVersion: 'v1', submittedByUserId: 2, lines: [] });
        return response({ items: [], totalCount: 0, totalPages: 1 });
    };
    let sequence = 0;
    vm.runInNewContext(source, { document, window, fetch, URLSearchParams,
        crypto: { randomUUID: () => 'uuid-' + (++sequence) },
        confirm: () => true, console });
    const dispatch = (id, type, extra = {}) => {
        const handler = events.get(id + ':' + type);
        assert.ok(handler, 'Missing ' + id + ':' + type);
        return handler({ preventDefault() {}, target: { closest: () => ({ dataset: { open: '9' } }) }, ...extra });
    };
    const posts = () => requests.filter(x => x.options.method === 'POST');
    return { get, document, pending, dispatch, posts, events, requests };
}

async function addDraft(p) {
    await flush();
    p.get('budgetName').value = 'Annual Budget';
    p.get('budgetFrom').value = '2026-01-01'; p.get('budgetTo').value = '2026-12-31';
    p.get('budgetAccount').value = '4'; p.get('budgetAmount').value = '100';
    p.dispatch('budgetAddLine', 'click');
    await flush();
}

test('Budget create is single-flight even under repeated programmatic submit', async () => {
    const p = page(); await addDraft(p);
    const gate = deferred(); p.pending.push(gate);
    p.dispatch('budgetForm', 'submit'); p.dispatch('budgetForm', 'submit');
    await flush();
    assert.equal(p.posts().length, 1);
    assert.equal(p.get('budgetName').disabled, true);
    gate.release(response({ id: 9 })); await flush();
    assert.equal(p.get('budgetName').disabled, false);
    assert.match(p.get('budgetNotice').textContent, /জমা হয়েছে/);
});

test('Uncertain budget retry reuses the key; an edited command gets a new key', async () => {
    const p = page(); await addDraft(p);
    p.pending.push({ promise: Promise.resolve(response(null, false)) },
        { promise: Promise.resolve(response(null, false)) });
    p.dispatch('budgetForm', 'submit'); await flush();
    p.dispatch('budgetForm', 'submit'); await flush();
    assert.equal(p.posts().length, 2);
    const keys = p.posts().map(x => JSON.parse(x.options.body).clientRequestId);
    assert.equal(keys[0], keys[1]);
    p.get('budgetName').value = 'Changed Budget';
    p.dispatch('budgetForm', 'submit'); await flush();
    assert.notEqual(JSON.parse(p.posts()[2].options.body).clientRequestId, keys[1]);
});

test('Budget review has one pending command and preserves retry identity', async () => {
    const p = page(); await flush();
    p.dispatch('budgetRows', 'click'); await flush();
    p.get('budgetReviewReason').value = 'Approved for year';
    const gate = deferred(); p.pending.push(gate);
    p.dispatch('budgetApprove', 'click'); p.dispatch('budgetApprove', 'click');
    await flush();
    assert.equal(p.posts().length, 1);
    assert.equal(p.get('budgetApprove').disabled, true);
    gate.release(response(null, false)); await flush();
    assert.equal(p.get('budgetApprove').disabled, false);
    p.dispatch('budgetApprove', 'click'); await flush();
    assert.equal(p.posts().length, 2);
    const a = JSON.parse(p.posts()[0].options.body), b = JSON.parse(p.posts()[1].options.body);
    assert.equal(a.reviewRequestId, b.reviewRequestId);
});

test('Institution switch while a read is pending blocks old-tenant data', async () => {
    const p = page(); await flush();
    p.document.body.dataset.authTenantId = '26';
    p.dispatch('budgetRows', 'click'); await flush();
    assert.match(p.get('budgetNotice').textContent, /সেশন বদলেছে/);
    assert.equal(p.get('budgetDetails').classList.values.has('d-none'), true);
});

test('Dirty budget protects navigation and accepts a clean form', async () => {
    const p = page(); await flush();
    const event = () => ({ prevented: false, preventDefault() { this.prevented = true; }, returnValue: undefined });
    const beforeUnload = p.events.get('window:beforeunload');
    const pristine = event(); beforeUnload(pristine); assert.equal(pristine.prevented, false);
    p.get('budgetName').value = 'Work in progress';
    const dirty = event(); beforeUnload(dirty); assert.equal(dirty.prevented, true);
});
