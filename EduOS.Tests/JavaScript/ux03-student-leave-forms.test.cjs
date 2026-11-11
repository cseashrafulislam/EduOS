const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.join(__dirname, '../../EduOS.App/wwwroot/js/attendance/student-leaves.js'), 'utf8');
const flush = async () => { for (let i = 0; i < 6; i++) await new Promise(setImmediate); };
const deferred = () => { let release; const promise = new Promise(resolve => { release = resolve; }); return { promise, release }; };
const response = data => ({ ok: true, json: async () => ({ success: true, data }) });
const student = reference => ({ reference, fullName: 'Student ' + reference, studentCode: reference, rollNo: '1' });

function page() {
    const elements = new Map(), requests = [], gates = { search: [], leave: [], decision: [] };
    let confirmation = true;
    let listRows = [];
    const createElement = type => {
        const events = new Map();
        return { tagName: type.toUpperCase(), type, events, children: [], disabled: false, value: '',
            className: '', textContent: '', addEventListener(name, cb) { events.set(name, cb); },
            append(...children) { this.children.push(...children); },
            replaceChildren(...children) { this.children = children; } };
    };
    const get = id => {
        if (!elements.has(id)) {
            const el = createElement(id === 'leaveApplyForm' ? 'form' : 'div');
            el.id = id; el.files = [];
            el.querySelectorAll = () => ['leaveStart', 'leaveEnd', 'leaveReason', 'leaveFile', 'leaveSubmit'].map(get);
            el.reset = () => { for (const child of el.querySelectorAll()) child.value = ''; get('leaveFile').files = []; };
            elements.set(id, el);
        }
        return elements.get(id);
    };
    const document = { body: { dataset: { authTenantId: '25', authSessionReference: 'abc' } },
        documentElement: { lang: 'bn-BD' }, getElementById: get, createElement };
    get('studentLeavesApp').querySelector = () => ({ value: 'csrf' });
    const window = { events: new Map(), addEventListener(name, handler) { this.events.set(name, handler); } };
    const fetch = async (url, options = {}) => {
        requests.push({ url, options });
        const key = url.includes('/eligible-enrollments') ? 'search' : options.method === 'POST' ?
            (url.includes('/cancel') || url.includes('/review') ? 'decision' : 'leave') : null;
        if (key && gates[key].length) return gates[key].shift().promise;
        if (key === 'search') {
            const term = new URL(url, 'https://example.test').searchParams.get('search');
            return response({ items: [student(term)], totalPages: 1, totalCount: 1 });
        }
        if (key) return response({ id: 1 });
        return response({ items: listRows, totalPages: 1, totalCount: listRows.length });
    };
    vm.runInNewContext(source, { document, window, fetch, URLSearchParams, FormData,
        crypto: { randomUUID: () => 'uuid-static' }, Uint8Array,
        confirm: () => confirmation, prompt: () => 'Reviewed', console });
    const dispatch = (id, type) => {
        const event = { preventDefault() {} };
        assert.ok(get(id).events.get(type), id + ':' + type + ' missing');
        return get(id).events.get(type)(event);
    };
    const lookup = async reference => {
        get('leaveStudentTerm').value = reference;
        dispatch('leaveStudentTerm', 'input');
        dispatch('leaveStudentSearch', 'submit');
        await flush();
        return get('leaveStudentResults').children[0];
    };
    return { get, document, window, gates, requests, dispatch, lookup,
        setConfirmation: value => { confirmation = value; }, setListRows: value => { listRows = value; } };
}

test('A draft cannot silently transfer to another student; confirmed switching clears it', async () => {
    const p = page(); await flush();
    const first = await p.lookup('A1'); first.events.get('click')(); await flush();
    p.get('leaveReason').value = 'Medical leave';
    const second = await p.lookup('B2');
    p.setConfirmation(false); second.events.get('click')();
    assert.match(p.get('leaveStudentLabel').textContent, /A1/);
    assert.equal(p.get('leaveReason').value, 'Medical leave');
    p.setConfirmation(true); second.events.get('click')(); await flush();
    assert.match(p.get('leaveStudentLabel').textContent, /B2/);
    assert.equal(p.get('leaveReason').value, '');
});

test('Older student lookup response cannot repopulate changed search results', async () => {
    const p = page(), gate = deferred(); await flush();
    p.gates.search.push(gate);
    p.get('leaveStudentTerm').value = 'Old';
    p.dispatch('leaveStudentSearch', 'submit');
    const newButton = await p.lookup('New');
    assert.match(newButton.textContent, /New/);
    gate.release(response({ items: [student('Old')], totalPages: 1, totalCount: 1 }));
    await flush();
    assert.equal(p.get('leaveStudentResults').children.length, 1);
    assert.match(p.get('leaveStudentResults').children[0].textContent, /New/);
});

test('Leave submission stays single-flight and locks student switch until POST resolves', async () => {
    const p = page(); await flush();
    (await p.lookup('A1')).events.get('click')(); await flush();
    p.get('leaveStart').value = '2026-11-01'; p.get('leaveEnd').value = '2026-11-02';
    p.get('leaveReason').value = 'Medical';
    const gate = deferred(); p.gates.leave.push(gate);
    p.dispatch('leaveApplyForm', 'submit'); p.dispatch('leaveApplyForm', 'submit'); await flush();
    const posts = () => p.requests.filter(r => r.url === '/api/student-leaves' && r.options.method === 'POST');
    assert.equal(posts().length, 1);
    assert.equal(p.get('leaveStart').disabled, true);
    (await p.lookup('B2')).events.get('click')();
    assert.match(p.get('leaveStudentLabel').textContent, /A1/);
    gate.release(response({ id: 9 })); await flush();
    assert.equal(p.get('leaveStart').disabled, false);
    assert.match(p.get('leaveNotice').textContent, /জমা হয়েছে/);
});

test('Session change invalidates old student record and prevents further actions', async () => {
    const p = page(); await flush();
    (await p.lookup('A1')).events.get('click')(); await flush();
    p.get('leaveReason').value = 'Private medical note';
    p.document.body.dataset.authTenantId = '26';
    p.dispatch('leaveRefresh', 'click'); await flush();
    assert.equal(p.get('leaveStudentLabel').textContent, '');
    assert.equal(p.get('leaveSubmit').disabled, true);
    assert.equal(p.get('leaveRows').children.length, 0);
    assert.equal(p.get('leaveReason').value, '');
    assert.match(p.get('leaveNotice').textContent, /সেশন বদলেছে/);
});

test('Approve and reject cannot race against the same pending leave row', async () => {
    const p = page(); await flush();
    p.setListRows([{ id: 17, state: 2, reason: 'Medical', rowVersion: 'v1',
        fromDate: '2026-11-01', toDate: '2026-11-02' }]);
    p.dispatch('leaveRefresh', 'click'); await flush();
    const actions = p.get('leaveRows').children[0].children[3].children;
    const gate = deferred(); p.gates.decision.push(gate);
    actions[0].events.get('click')(); actions[1].events.get('click')();
    await flush();
    assert.equal(p.requests.filter(r => r.url.includes('/17/review') && r.options.method === 'POST').length, 1);
    gate.release(response({ id: 17 })); await flush();
    assert.equal(actions[0].disabled, false);
});

test('Unsaved leave text or file change triggers browser navigation protection', async () => {
    const p = page(); await flush();
    const guard = p.window.events.get('beforeunload');
    const event = () => ({ prevented: false, preventDefault() { this.prevented = true; } });
    const clean = event(); guard(clean); assert.equal(clean.prevented, false);
    p.get('leaveReason').value = 'Draft';
    const dirty = event(); guard(dirty); assert.equal(dirty.prevented, true);
});
