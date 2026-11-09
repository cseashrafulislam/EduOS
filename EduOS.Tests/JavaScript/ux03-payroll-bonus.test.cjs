'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const source = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/payroll/bonuses.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/PayrollBonuses/Index.cshtml'), 'utf8');
const flush = () => new Promise(resolve => setImmediate(resolve));
const ok = data => ({ok: true, json: async () => ({success: true, data})});
const fail = message => ({ok: false, json: async () => ({success: false, message})});
function deferred() {let resolve;const promise = new Promise(done => {resolve = done;});return {promise, resolve};}

function harness() {
    const nodes = new Map(), calls = [];
    function element() {
        const events = new Map();
        return {
            value: '', disabled: false, checked: false, children: [], textContent: '',
            addEventListener: (type, cb) => events.set(type, cb),
            trigger(type) {return events.get(type)?.({preventDefault() {}, currentTarget: this});},
            replaceChildren(...rows) {this.children = rows;},
            append(...rows) {this.children.push(...rows);},
            querySelector: () => ({value: 'csrf'}),
            reportValidity: () => true,
            reset() {for(const id of ['bonusEmployeeSearch','bonusEmployee','bonusDate','bonusAmount','bonusReason'])get(id).value='';}
        };
    }
    const get = id => {if(!nodes.has(id))nodes.set(id, element());return nodes.get(id);};
    const document = {
        documentElement: {lang: 'en-BD'},getElementById: get,
        createElement: element
    };
    const context = {
        document, Date, Number, String, JSON, URLSearchParams, Math, confirm: () => true,
        crypto: {randomUUID: (() => {let n = 0;return () => `e40542e6-424b-43b8-81c1-${String(++n).padStart(12,'0')}`;})()},
        fetch(url, options) {const task = deferred();calls.push({url: String(url), options, ...task});return task.promise;}
    };
    vm.runInNewContext(source, context, {filename: 'bonuses.js'});
    return {get, calls};
}
function find(h, segment, index = 0) {
    const c = h.calls.filter(x => x.url.includes(segment));
    assert.ok(c[index], `Missing ${segment} request #${index + 1}`);
    return c[index];
}

test('bonus view loads shared draft protection and requires a fresh employee search', () => {
    assert.match(view, /form-guard\.js"[\s\S]*payroll\/bonuses\.js"/);
    assert.match(view, /id="bonusEmployee"[^>]*disabled/);
    assert.match(view, /id="bonusClear"/);
    assert.match(source, /formGuard\?\.markClean\(\)/);
    assert.match(source, /formGuard\.confirmDiscard\(\)/);
});

test('stale employee searches cannot replace the current authorized picker', async () => {
    const h = harness();
    h.get('bonusEmployeeSearch').value = 'Old';h.get('bonusEmployeeSearch').trigger('input');
    h.get('bonusFind').trigger('click');
    h.get('bonusEmployeeSearch').value = 'New';h.get('bonusEmployeeSearch').trigger('input');
    h.get('bonusFind').trigger('click');
    find(h, 'employee-options?', 1).resolve(ok({items: [{reference: 'new-id',fullName: 'New Staff',employeeCode: 'N'}],totalCount: 1}));
    await flush();
    find(h, 'employee-options?', 0).resolve(ok({items: [{reference: 'old-id',fullName: 'Old Staff',employeeCode: 'O'}],totalCount: 1}));
    await flush();
    assert.equal(h.get('bonusEmployee').children[1].value, 'new-id');
    assert.equal(h.get('bonusEmployee').disabled, false);
});

test('a failed bonus acknowledgement retains one request key and duplicate submits stay single-flight', async () => {
    const h = harness();
    h.get('bonusEmployee').disabled = false;h.get('bonusEmployee').value = 'employee-a';
    h.get('bonusDate').value = '2026-11-09';h.get('bonusAmount').value = '100.25';
    h.get('bonusReason').value = 'Outstanding work';
    h.get('bonusCreate').trigger('submit');h.get('bonusCreate').trigger('submit');
    assert.equal(h.calls.filter(x => x.url.endsWith('/bonuses') && x.options?.method === 'POST').length, 1);
    assert.equal(h.get('bonusCreate').inert, true);
    const first = find(h, '/bonuses', 1);
    const firstBody = JSON.parse(first.options.body);
    assert.equal(first.options.headers.RequestVerificationToken, 'csrf');
    first.resolve(fail('Timeout before acknowledgement'));
    await flush();
    assert.equal(h.get('bonusAmount').value, '100.25');
    assert.equal(h.get('bonusCreate').inert, false);
    h.get('bonusCreate').trigger('submit');
    const second = find(h, '/bonuses', 2);
    assert.equal(JSON.parse(second.options.body).clientRequestId, firstBody.clientRequestId);
    second.resolve(ok({id: 12}));await flush();
    assert.equal(h.get('bonusAmount').value, '');
    find(h, '/bonuses?', 1).resolve(ok({items: [],totalCount: 0,totalPages: 1}));
    await flush();
    assert.match(h.get('bonusNotice').textContent, /Bonus recorded/);
});
