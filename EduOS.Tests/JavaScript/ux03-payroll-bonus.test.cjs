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
const fail = (message,status=503) => ({ok: false,status, json: async () => ({success: false, message})});
function deferred() {let resolve;const promise = new Promise(done => {resolve = done;});return {promise, resolve};}

function harness() {
    const nodes = new Map(), calls = [];
    let dirty = false, discardAllowed = true, confirmationAllowed = true;
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
            reset() {for(const id of ['bonusEmployee','bonusDate','bonusAmount','bonusReason'])get(id).value='';}
        };
    }
    const get = id => {if(!nodes.has(id))nodes.set(id, element());return nodes.get(id);};
    const document = {
        documentElement: {lang: 'en-BD'},getElementById: get,
        createElement: element
    };
    const context = {
        document, Date, Number, String, JSON, URLSearchParams, Math, confirm: () => confirmationAllowed,
        globalThis: {EduOSFormGuard: {create: () => ({
            isDirty: () => dirty,confirmDiscard: () => discardAllowed,
            markClean: () => {dirty = false;}
        })}},
        crypto: {randomUUID: (() => {let n = 0;return () => `e40542e6-424b-43b8-81c1-${String(++n).padStart(12,'0')}`;})()},
        fetch(url, options) {const task = deferred();calls.push({url: String(url), options, ...task});return task.promise;}
    };
    vm.runInNewContext(source, context, {filename: 'bonuses.js'});
    return {get, calls,
      set dirty(value) {dirty = value;},set discardAllowed(value) {discardAllowed = value;},
      set confirmationAllowed(value) {confirmationAllowed = value;}};
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

test('bonus lookup is separate from financial form and Enter submits bounded search',async()=>{
 assert.match(view,/id="bonusEmployeeSearchForm"[\s\S]*id="bonusCreate"/);
 const h=harness();h.get('bonusEmployeeSearch').value='Alice';
 h.get('bonusEmployeeSearchForm').trigger('submit');
 const lookup=find(h,'employee-options?');
 assert.match(lookup.url,/pageSize=50/);
 lookup.resolve(ok({items:[{reference:'e-1',fullName:'Alice',employeeCode:'E1'}],totalCount:1}));
 await flush();assert.equal(h.get('bonusEmployee').children[1].value,'e-1');
 assert.equal(h.get('bonusEmployeeSearch').value,'Alice');
});

test('cancelled new employee search retains a dirty bonus and selected employee',async()=>{
 const h=harness();h.get('bonusEmployee').disabled=false;
 h.get('bonusEmployee').value='old-employee';h.get('bonusAmount').value='400';
 h.get('bonusEmployeeSearch').value='Different staff';
 h.dirty=true;h.discardAllowed=false;h.get('bonusFind').trigger('click');
 assert.equal(h.calls.filter(x=>x.url.includes('employee-options?')).length,0);
 assert.equal(h.get('bonusEmployee').value,'old-employee');
 assert.equal(h.get('bonusAmount').value,'400');
});

test('a late new employee result cannot discard a draft edited while searching',async()=>{
 const h=harness();h.get('bonusEmployeeSearch').value='Alice';h.get('bonusFind').trigger('click');
 const lookup=find(h,'employee-options?');
 h.get('bonusAmount').value='250';h.dirty=true;h.discardAllowed=false;
 lookup.resolve(ok({items:[{reference:'e-1',fullName:'Alice',employeeCode:'E1'}],totalCount:1}));
 await flush();assert.equal(h.get('bonusAmount').value,'250');
 assert.equal(h.get('bonusEmployee').value,'');
});

test('confirmed bonus stays successful after failed readback and prevents accidental replay',async()=>{
 const h=harness();h.get('bonusEmployee').disabled=false;h.get('bonusEmployee').value='e-1';
 h.get('bonusDate').value='2026-11-10';h.get('bonusAmount').value='150.25';
 h.get('bonusReason').value='Performance';h.get('bonusCreate').trigger('submit');
 find(h,'/bonuses',1).resolve(ok({id:13}));await flush();
 find(h,'/bonuses?',1).resolve(fail('Read-only register down'));await flush();
 assert.match(h.get('bonusNotice').className,/alert-success/);
 assert.match(h.get('bonusNotice').textContent,/Bonus was recorded; register refresh unavailable/);
 h.get('bonusAmount').value='150.25';h.get('bonusReason').value='Performance';
 h.get('bonusCreate').trigger('submit');
 assert.equal(h.calls.filter(x=>x.url.endsWith('/bonuses')&&x.options?.method==='POST').length,1);
 assert.match(h.get('bonusNotice').textContent,/This bonus was recorded/);
});

test('revoked bonus register clears sensitive state and blocks subsequent financial write',async()=>{
 const h=harness();h.get('bonusEmployee').disabled=false;h.get('bonusEmployee').value='e-1';
 h.get('bonusAmount').value='200';find(h,'/bonuses?').resolve(fail('HR payroll denied',403));
 await flush();assert.equal(h.get('bonusWorkspace').inert,true);
 assert.equal(h.get('bonusEmployee').value,'');
 h.get('bonusCreate').trigger('submit');
 assert.equal(h.calls.filter(x=>x.url.endsWith('/bonuses')&&x.options?.method==='POST').length,0);
});
