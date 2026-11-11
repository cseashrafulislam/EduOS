const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = name => fs.readFileSync(path.join(__dirname, '../../EduOS.App/wwwroot/js', name), 'utf8');
const flush = async () => { for (let i = 0; i < 8; i++) await new Promise(setImmediate); };
const deferred = () => { let resolve; const promise = new Promise(done => { resolve = done; }); return { promise, resolve }; };
const success = data => ({ ok:true, json:async () => ({ success:true, data }) });
const failure = () => ({ ok:false, json:async () => ({ success:false, message:'Readback unavailable' }) });

function mount(script, rootId, permissions, responder) {
    const elements = new Map(), requests = [], observers = [];
    let discard = true, pendingSwitch = false;
    function create(tag = 'div') {
        const handlers = new Map();
        return { tagName:tag.toUpperCase(), handlers, children:[], value:'', textContent:'', checked:false,
            disabled:false, className:'', dataset:{}, type:'', id:'',
            classList:{ values:new Set(['d-none']), add(value) { this.values.add(value); },
                remove(value) { this.values.delete(value); } },
            addEventListener(name, fn) { handlers.set(name, fn); },
            append(...items) { this.children.push(...items); },
            replaceChildren(...items) { this.children = items; },
            get options() { return this.children; },
            querySelectorAll() { return this.controls || []; },
            scrollIntoView() {}, focus() {}, reset() {} };
    }
    const get = id => {
        if (!elements.has(id)) { const node = create(); node.id = id; elements.set(id, node); }
        return elements.get(id);
    };
    get(rootId).dataset = permissions;
    get(rootId).querySelector = () => ({ value:'csrf' });
    const body = { dataset:{ authTenantId:'11', authSessionReference:'a' }, append() {} };
    const document = { body, getElementById:get, createElement:create,
        querySelector:() => ({ content:'csrf' }) };
    function MutationObserver(callback) { observers.push(callback); this.observe = () => {}; this.disconnect = () => {}; }
    const window = { EduOSSessionTransitionPending:() => pendingSwitch,
        EduOSFormGuard:{ create:() => ({ confirmDiscard:() => discard, markClean() {} }) } };
    const fetch = (url, options = {}) => {
        requests.push({ url, options }); return responder(url, options);
    };
    vm.runInNewContext(source(script), { document, window, fetch, MutationObserver, URLSearchParams,
        URL, crypto:{ randomUUID:(() => { let n = 0; return () => 'retry-' + ++n; })() },
        confirm:() => true, prompt:() => 'Rejected after review', setTimeout:() => {},
        console });
    const dispatch = (id, event) => {
        const element = get(id), handler = element.handlers.get(event);
        assert.ok(handler, id + ':' + event + ' missing');
        return handler({ preventDefault() {}, currentTarget:element });
    };
    const click = node => {
        assert.ok(node.handlers.has('click'), 'Missing click handler');
        return node.handlers.get('click')({ currentTarget:node, preventDefault() {} });
    };
    const switchTenant = () => {
        body.dataset.authTenantId = '12'; observers.forEach(callback => callback());
    };
    return { get, requests, dispatch, click, switchTenant, setDiscard: value => { discard = value; },
        setPendingSwitch:value => { pendingSwitch = value; },
        posts:() => requests.filter(x => x.options.method && x.options.method !== 'GET') };
}

const item = { id:41, code:'I-41', name:'Paper', unitCode:'Each',
    categoryCode:'A', reorderLevel:2, isStockTracked:true, isAssetTracked:false, isActive:true, rowVersion:'v1' };
const location = { id:52, code:'L-52', name:'Store', campusId:3, address:'Building A', isActive:true, rowVersion:'v2' };
const inventoryData = url => url.includes('/locations?') ?
    { items:[location], totalPages:1, totalCount:1 } : { items:[item], totalPages:1, totalCount:1 };
function catalog(responder) {
    return mount('inventory/catalog.js', 'inventoryCatalog', { canManage:'true' },
        responder || ((url, options) => options.method && options.method !== 'GET' ?
            success({}) : success(inventoryData(url))));
}
test('Inventory catalog item and location save single-flight independently, preserving form lock', async () => {
    const itemGate = deferred(), locationGate = deferred();
    const page = catalog((url, options) => {
        if (options.method === 'POST') return url.endsWith('/items') ? itemGate.promise : locationGate.promise;
        return success(inventoryData(url));
    });
    await flush();
    page.get('itemCode').value = 'I-91'; page.get('itemName').value = 'Pen';
    page.get('locationCode').value = 'L-91'; page.get('locationName').value = 'Cabinet';
    page.get('itemForm').controls = [page.get('itemCode')];
    page.get('locationForm').controls = [page.get('locationCode')];
    page.dispatch('itemForm', 'submit'); page.dispatch('itemForm', 'submit');
    page.dispatch('locationForm', 'submit'); page.dispatch('locationForm', 'submit');
    await flush();
    assert.equal(page.posts().length, 2);
    assert.equal(page.get('itemCode').disabled, true);
    assert.equal(page.get('locationCode').disabled, true);
    itemGate.resolve(success({})); locationGate.resolve(success({})); await flush();
    assert.equal(page.get('itemCode').disabled, false);
    assert.equal(page.get('locationCode').disabled, false);
});
test('Inventory catalog prevents dirty-form replacement and drops old-tenant rows', async () => {
    const page = catalog(); await flush();
    page.setDiscard(false);
    page.click(page.get('itemRows').children[0].children.at(-1).children[0]);
    assert.equal(page.get('itemId').value, '');
    page.setDiscard(true);
    page.click(page.get('itemRows').children[0].children.at(-1).children[0]);
    assert.equal(page.get('itemId').value, 41);
    page.switchTenant();
    assert.equal(page.get('itemRows').children.length, 0);
    assert.equal(page.get('locationRows').children.length, 0);
    page.dispatch('itemForm', 'submit'); await flush();
    assert.equal(page.posts().length, 0);
});
test('Inventory catalog ignores older item search responses', async () => {
    const first = deferred(), page = catalog((url, options) => {
        if (url.includes('/items?') && !options.method) return first.promise;
        return success(inventoryData(url));
    });
    page.dispatch('itemSearchForm', 'submit');
    await flush();
    assert.equal(page.get('itemRows').children[0].children[0].textContent, 'I-41');
    first.resolve(success({ items:[{ ...item, code:'STALE' }], totalPages:1, totalCount:1 }));
    await flush();
    assert.equal(page.get('itemRows').children[0].children[0].textContent, 'I-41');
});

const feeData = { feeHeads:[{ id:71, code:'TUITION', name:'Tuition', defaultFrequency:2,
    incomeAccountId:10, receivableAccountId:20, rowVersion:'v1', isActive:true }],
    postingAccounts:[{ id:10, accountType:4, code:'REV', name:'Revenue' },
        { id:20, accountType:1, code:'AR', name:'Receivable' }] };
function fees(responder) {
    return mount('finance/fee-head-accounting.js', 'feeHeadAccounting', { write:'true' },
        responder || ((url, options) => options.method && options.method !== 'GET' ?
            success({}) : success(feeData)));
}
test('Fee head creation and inline GL mapping are both single-flight', async () => {
    const headGate = deferred(), mappingGate = deferred();
    const page = fees((url, options) => {
        if (options.method === 'POST') return headGate.promise;
        if (options.method === 'PUT') return mappingGate.promise;
        return success(feeData);
    });
    await flush();
    page.get('feeHeadForm').controls = [page.get('feeHeadCode')];
    page.get('feeHeadCode').value = 'NEW'; page.get('feeHeadName').value = 'New Fee';
    page.dispatch('feeHeadForm', 'submit'); page.dispatch('feeHeadForm', 'submit'); await flush();
    assert.equal(page.posts().length, 1);
    assert.equal(page.get('feeHeadCode').disabled, true);
    headGate.resolve(success({})); await flush();
    const action = page.get('feeHeadRows').children[0].children.at(-1);
    page.click(action.children[0]); page.click(action.children[0]); await flush();
    assert.equal(page.posts().length, 2);
    mappingGate.resolve(success({})); await flush();
    assert.equal(page.get('feeHeadCode').disabled, false);
});
test('Fee head edit respects unsaved changes and tenant switch blocks further saves', async () => {
    const page = fees(); await flush();
    page.setDiscard(false);
    page.click(page.get('feeHeadRows').children[0].children.at(-1).children[1]);
    assert.equal(page.get('feeHeadId').value, '');
    page.setDiscard(true);
    page.click(page.get('feeHeadRows').children[0].children.at(-1).children[1]);
    assert.equal(page.get('feeHeadId').value, 71);
    page.switchTenant(); page.dispatch('feeHeadForm', 'submit'); await flush();
    assert.equal(page.posts().length, 0);
    assert.equal(page.get('feeHeadRows').children.length, 0);
});

const studentData = { openPeriods:[{ id:5, startDate:'2026-11-01', endDate:'2026-11-30', name:'November' }],
    cashAccounts:[{ id:6, code:'CASH', name:'Cash' }], campuses:[{ id:3, name:'Main' }],
    payments:[{ paymentReference:'p-1', receiptNumber:'R-1', studentCode:'S-1',
        studentName:'Student', campusId:3, paymentDate:'2026-11-11', currencyCode:'BDT',
        amount:100, rowVersion:'v1', posted:false }], totalCount:1, pageSize:25, page:1 };
test('Student Payment GL post cannot double-submit when confirmed write readback fails', async () => {
    const gate = deferred(); let reads = 0;
    const page = mount('finance/student-payment-posting.js', 'studentPaymentPosting',
        { post:'true' }, (url, options) => options.method === 'POST' ? gate.promise :
            ++reads === 1 ? success(studentData) : failure());
    await flush();
    page.get('paymentPostingPeriod').value = '5'; page.get('paymentPostingCash').value = '6';
    const button = page.get('paymentPostingRows').children[0].children.at(-1).children[0];
    page.click(button); page.click(button); await flush();
    assert.equal(page.posts().length, 1);
    gate.resolve(success({ journalNumber:'J-101' })); await flush();
    page.click(button); await flush();
    assert.equal(page.posts().length, 1);
    assert.match(page.get('paymentPostingNotice').textContent, /Posted; refresh/);
});
test('Student Payment GL removes old-tenant register and refuses posting', async () => {
    const page = mount('finance/student-payment-posting.js', 'studentPaymentPosting',
        { post:'true' }, () => success(studentData));
    await flush();
    const button = page.get('paymentPostingRows').children[0].children.at(-1).children[0];
    page.switchTenant(); page.click(button); await flush();
    assert.equal(page.posts().length, 0);
    assert.equal(page.get('paymentPostingRows').children.length, 0);
});

const payrollData = { openPeriods:[{ id:5, name:'November', startDate:'2026-11-01', endDate:'2026-11-30' }],
    accounts:[{ id:6, code:'CASH', name:'Cash' }],
    payments:[{ reference:'pay-1', runNumber:'RUN-1', employeeName:'Employee',
        paymentDate:'2026-11-11', paymentMethod:1, amount:200, netSalaryAmount:200,
        settledAmount:0, remainingAmount:200, state:2,
        reconciliationStatus:'Pending', rowVersion:'v1' }], totalCount:1, pageSize:10 };
test('Payroll GL confirmation remains single-flight and does not repost after readback failure', async () => {
    const gate = deferred(); let reads = 0;
    const page = mount('finance/payroll-payment-posting.js', 'payrollPaymentPosting',
        { post:'true' }, (url, options) => options.method === 'POST' ? gate.promise :
            ++reads === 1 ? success(payrollData) : failure());
    await flush();
    page.click(page.get('payrollPaymentRows').children[0].children.at(-1).children[0]);
    page.get('payrollPaymentPeriod').value = '5'; page.get('payrollPaymentAccount').value = '6';
    page.dispatch('payrollPaymentConfirm', 'click'); page.dispatch('payrollPaymentConfirm', 'click');
    await flush(); assert.equal(page.posts().length, 1);
    gate.resolve(success({ journalNumber:'PY-1' })); await flush();
    page.dispatch('payrollPaymentConfirm', 'click'); await flush();
    assert.equal(page.posts().length, 1);
    assert.match(page.get('payrollPaymentNotice').textContent, /posted; refresh/i);
});
test('Payroll read-only register safely clears on tenant switch without hidden write panels', async () => {
    const page = mount('finance/payroll-payment-posting.js', 'payrollPaymentPosting',
        { post:'false' }, () => success(payrollData));
    await flush();
    page.switchTenant();
    assert.equal(page.get('payrollPaymentRows').children.length, 0);
    assert.match(page.get('payrollPaymentNotice').textContent, /session changed/);
});
test('Payroll return reviewer locks same receipt across opposite decisions', async () => {
    const gate = deferred();
    const page = mount('finance/payroll-payment-posting.js', 'payrollPaymentPosting',
        { post:'true' }, (url, options) => options.method === 'POST' ? gate.promise :
            success({ ...payrollData, payments:[{ ...payrollData.payments[0], state:3,
                reconciliationStatus:'Reconciled', journalReference:'J-1',
                returnState:2, returnReference:'return-1', returnRowVersion:'rv',
                returnEvidenceReference:'RECEIPT-1' }] }));
    await flush();
    const actions = page.get('payrollPaymentRows').children[0].children.at(-1).children;
    page.click(actions[1]); page.click(actions[2]); await flush();
    assert.equal(page.posts().length, 1);
    gate.resolve(success({ journalNumber:'RETURN-1' })); await flush();
    page.click(actions[2]); await flush();
    assert.equal(page.posts().length, 1);
});
test('Payroll returned salary preserves request identity after uncertain response', async () => {
    const gate = deferred(), attempts = [gate.promise, Promise.resolve(failure())];
    const page = mount('finance/payroll-payment-posting.js', 'payrollPaymentPosting',
        { post:'true' }, (url, options) => options.method === 'POST' ?
            attempts.shift() : success({ ...payrollData, payments:[{ ...payrollData.payments[0],
                state:3, journalReference:'GL-1', reconciliationStatus:'Reconciled' }] }));
    await flush();
    const actions = page.get('payrollPaymentRows').children[0].children.at(-1).children;
    page.click(actions[1]);
    page.get('payrollPaymentPeriod').value = '5';
    page.get('payrollReturnDate').value = '2026-11-11';
    page.get('payrollReturnEvidence').value = 'REC-1';
    page.get('payrollReturnReason').value = 'Employee returned cash';
    page.dispatch('payrollReturnSubmit', 'click');
    page.dispatch('payrollReturnSubmit', 'click');
    await flush();
    assert.equal(page.posts().length, 1);
    assert.equal(page.get('payrollReturnEvidence').disabled, true);
    gate.resolve(failure()); await flush();
    page.dispatch('payrollReturnSubmit', 'click'); await flush();
    assert.equal(page.posts().length, 2);
    const keys = page.posts().map(x => JSON.parse(x.options.body).clientRequestId);
    assert.equal(keys[0], keys[1]);
    assert.equal(page.get('payrollReturnEvidence').disabled, false);
});
test('Payroll bank worksheet is never downloaded after institution switch during request', async () => {
    const gate = deferred();
    const page = mount('finance/payroll-payment-posting.js', 'payrollPaymentPosting',
        { post:'true' }, (url, options) => url.includes('bank-review.csv') ?
            gate.promise : success({ ...payrollData,
                payments:[{ ...payrollData.payments[0], paymentMethod:2, bankLedgerId:6,
                    runReference:'ba2a7c41-4514-4987-88fd-b5c7314a011b' }] }));
    await flush();
    const actions = page.get('payrollPaymentRows').children[0].children.at(-1).children;
    page.click(actions.at(-1)); await flush();
    assert.equal(page.requests.some(x => x.url.includes('bank-review.csv')), true);
    page.switchTenant();
    gate.resolve({ ok:true, headers:{ get:() => 'text/csv' }, blob:async () => new Blob(['salary']) });
    await flush();
    assert.equal(page.get('payrollPaymentRows').children.length, 0);
    assert.match(page.get('payrollPaymentNotice').textContent, /session changed/);
});
