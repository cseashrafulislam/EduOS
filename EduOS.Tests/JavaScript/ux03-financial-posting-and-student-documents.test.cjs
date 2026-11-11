const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const script = name => fs.readFileSync(path.join(__dirname, '../../EduOS.App/wwwroot/js', name), 'utf8');
const flush = async () => { for (let i = 0; i < 8; i++) await new Promise(setImmediate); };
const deferred = () => { let resolve; const promise = new Promise(done => { resolve = done; }); return { promise, resolve }; };
const ok = data => ({ ok: true, json: async () => ({ success: true, data }) });
const failed = () => ({ ok: false, json: async () => ({ success: false, message: 'Network outcome uncertain' }) });

function harness(source, rootId, responder) {
    const elements = new Map(), requests = [], events = new Map();
    const callbacks = [];
    let confirmed = true;
    const create = type => {
        const handlers = new Map();
        return { type, handlers, children: [], disabled: false, hidden: false,
            value: '', textContent: '', className: '', files: [],
            classList: { values: new Set(['d-none']), add(key) { this.values.add(key); },
                remove(key) { this.values.delete(key); } },
            addEventListener(name, callback) { handlers.set(name, callback); },
            append(...items) { this.children.push(...items); },
            replaceChildren(...items) { this.children = items; },
            add(item) { this.children.push(item); },
            get options() { return this.children; },
            scrollIntoView() {}, focus() {} };
    };
    const get = id => {
        if (!elements.has(id)) { const node = create('div'); node.id = id; elements.set(id, node); }
        return elements.get(id);
    };
    get(rootId).querySelector = () => ({ value: 'csrf' });
    const document = {
        body: { dataset: { authTenantId: '25', authSessionReference: 'session-a' } },
        getElementById: get, createElement: create,
        querySelector: () => ({ content: 'csrf' }),
        createTextNode: value => ({ textContent: value })
    };
    const window = { addEventListener(type, handler) { events.set('window:' + type, handler); },
        EduOSSessionTransitionPending: () => false, confirm: () => confirmed,
        print() {} };
    function Observer(callback) { callbacks.push(callback); this.observe = () => {}; this.disconnect = () => {}; }
    const fetch = (url, options = {}) => {
        requests.push({ url, options });
        return responder(url, options);
    };
    class SimpleFormData { constructor() { this.items = []; } append(...args) { this.items.push(args); } }
    vm.runInNewContext(source, { document, window, fetch,
        MutationObserver: Observer, FormData: SimpleFormData,
        URLSearchParams, URL, crypto: { randomUUID: (() => { let n = 0; return () => 'request-' + ++n; })() },
        Option: function Option(label, value) { this.textContent = label; this.value = String(value); },
        confirm: () => confirmed, location: { search: '' }, console });
    const dispatch = (id, name, values = {}) => {
        const element = get(id), handler = element.handlers.get(name);
        assert.ok(handler, `Missing ${id}:${name}`);
        return handler({ preventDefault() {}, currentTarget: element, ...values });
    };
    const switchTenant = () => {
        document.body.dataset.authTenantId = '26';
        callbacks.forEach(callback => callback());
    };
    return { get, document, requests, events, dispatch, switchTenant,
        confirm: value => { confirmed = value; },
        posts: () => requests.filter(request => request.options.method === 'POST') };
}

function disbursementPage(pending) {
    const data = { loans: [
        { loanId: 11, employeeName: 'Employee One', issueDate: '2026-11-01', principalAmount: 100,
            outstandingAmount: 100, installmentAmount: 10, rowVersion: 'v1' },
        { loanId: 12, employeeName: 'Employee Two', issueDate: '2026-11-01', principalAmount: 200,
            outstandingAmount: 200, installmentAmount: 20, rowVersion: 'v2' }],
        openPeriods: [{ id: 2, name: 'November' }],
        assetAccounts: [{ id: 3, code: 'Loan', name: 'Employee Loan' },
            { id: 4, code: 'Cash', name: 'Cash Drawer' }], totalCount: 2, pageSize: 10 };
    const page = harness(script('finance/loan-disbursement.js'), 'loanDisbursement',
        (url, options) => options.method === 'POST' ? pending?.shift()?.promise ||
            ok({ journalNumber: 'J-1' }) : ok(data));
    return { page, choose: (index = 0) => {
        page.get('loanRows').children[index].children.at(-1).children[0].handlers.get('click')();
        page.get('loanPeriod').value = '2'; page.get('loanReceivable').value = '3';
        page.get('loanCash').value = '4';
    } };
}

test('Loan disbursement permits one in-flight post and cannot switch loan mid-post', async () => {
    const gate = deferred(), pending = [gate];
    const { page, choose } = disbursementPage(pending); await flush(); choose();
    page.dispatch('loanConfirm', 'click'); page.dispatch('loanConfirm', 'click');
    choose(1); await flush();
    assert.equal(page.posts().length, 1);
    assert.match(page.posts()[0].url, /\/11\/post$/);
    gate.resolve(ok({ journalNumber: 'J-1' })); await flush();
    assert.equal(page.get('loanConfirmPanel').classList.values.has('d-none'), true);
});

test('Loan disbursement discards stale register and clears private rows on tenant change', async () => {
    const gate = deferred();
    const page = harness(script('finance/loan-disbursement.js'), 'loanDisbursement',
        () => gate.promise);
    page.switchTenant();
    gate.resolve(ok({ loans: [{ loanId: 90 }], totalCount: 1, pageSize: 10 }));
    await flush();
    assert.equal(page.get('loanRows').children.length, 0);
    assert.match(page.get('loanNotice').textContent, /session changed/);
});

function recoveryPage(pending) {
    const data = { loans: [
        { loanId: 31, employeeName: 'Employee', issueDate: '2026-10-01',
            principalAmount: 500, outstandingAmount: 300, rowVersion: 'v3' }],
        openPeriods: [{ id: 2, name: 'November' }], cashAccounts: [{ id: 4, name: 'Cash' }],
        totalCount: 1, pageSize: 10 };
    const page = harness(script('finance/loan-recovery.js'), 'loanRecovery',
        (url, options) => options.method === 'POST' ? pending.shift()?.promise ||
            ok({ journalNumber: 'J-2', outstandingAmount: 250 }) : ok(data));
    const choose = () => {
        page.get('recoveryRows').children[0].children.at(-1).children[0].handlers.get('click')();
        page.get('recoveryPeriod').value = '2'; page.get('recoveryCash').value = '4';
        page.get('recoveryAmount').value = '50'; page.get('recoveryDate').value = '2026-11-11';
    };
    return { page, choose };
}

test('Loan recovery has a single pending request and reuses retry ID only for identical amount', async () => {
    const gate = deferred(), pending = [gate, { promise: Promise.resolve(failed()) },
        { promise: Promise.resolve(failed()) }];
    const { page, choose } = recoveryPage(pending); await flush(); choose();
    page.dispatch('recoveryForm', 'submit'); page.dispatch('recoveryForm', 'submit');
    await flush(); assert.equal(page.posts().length, 1);
    page.dispatch('recoveryCancel', 'click');
    assert.equal(page.get('recoveryConfirmPanel').classList.values.has('d-none'), false);
    gate.resolve(failed()); await flush();
    page.dispatch('recoveryForm', 'submit'); await flush();
    const first = JSON.parse(page.posts()[0].options.body);
    const second = JSON.parse(page.posts()[1].options.body);
    assert.equal(first.clientRequestId, second.clientRequestId);
    page.get('recoveryAmount').value = '75';
    page.dispatch('recoveryForm', 'submit'); await flush();
    const third = JSON.parse(page.posts()[2].options.body);
    assert.notEqual(third.clientRequestId, second.clientRequestId);
    assert.equal(third.amount, 75);
});

test('Loan recovery clears register and prevents POST after tenant context change', async () => {
    const { page, choose } = recoveryPage([]); await flush(); choose();
    page.switchTenant(); page.dispatch('recoveryForm', 'submit'); await flush();
    assert.equal(page.posts().length, 0);
    assert.equal(page.get('recoveryRows').children.length, 0);
});

function admissionPage(pending) {
    const data = { today: '2026-11-11', openPeriods: [
        { id: 2, name: 'Nov', startDate: '2026-11-01', endDate: '2026-11-30' }],
        cashAccounts: [{ id: 3, name: 'Cash' }], incomeAccounts: [{ id: 4, name: 'Income' }],
        applicants: [{ applicantReference: 'a1', applicationNumber: 'A-1', applicantName: 'Applicant',
            campusName: 'Central', applicationFee: 150, currencyCode: 'BDT', state: 2, rowVersion: 'v1' }] };
    const page = harness(script('admissions/cash-receipts.js'), 'admissionCashWorkspace',
        (url, options) => options.method === 'POST' ? pending.shift()?.promise ||
            ok({ receiptNumber: 'R1' }) : ok(data));
    const choose = () => {
        page.get('admissionCashApplicants').children[0].children.at(-1).children[0].handlers.get('click')();
        page.get('admissionCashPeriod').value = '2'; page.get('admissionCashLedger').value = '3';
        page.get('admissionIncomeLedger').value = '4';
    };
    return { page, choose };
}

test('Admission collection stays single-flight despite filter and repeated submit', async () => {
    const gate = deferred(), pending = [gate];
    const { page, choose } = admissionPage(pending); await flush(); choose();
    page.dispatch('admissionCashCollectForm', 'submit');
    page.dispatch('admissionCashCollectForm', 'submit');
    page.dispatch('admissionCashSearchForm', 'submit');
    await flush();
    assert.equal(page.posts().length, 1);
    assert.equal(page.get('admissionCashCollectPanel').hidden, false);
    gate.resolve(ok({ receiptNumber: 'R-1', journalNumber: 'J-1' })); await flush();
    assert.equal(page.get('admissionCashCollectPanel').hidden, true);
    assert.equal(page.get('admissionCashReceiptPrint').hidden, false);
});

test('Admission uncertain write retries with same key but edited date gets a fresh ID', async () => {
    const pending = [{ promise: Promise.resolve(failed()) }, { promise: Promise.resolve(failed()) },
        { promise: Promise.resolve(failed()) }];
    const { page, choose } = admissionPage(pending); await flush(); choose();
    page.dispatch('admissionCashCollectForm', 'submit'); await flush();
    page.dispatch('admissionCashCollectForm', 'submit'); await flush();
    const keys = page.posts().map(x => JSON.parse(x.options.body).clientRequestId);
    assert.equal(keys[0], keys[1]);
    page.get('admissionCashDate').value = '2026-11-12';
    page.dispatch('admissionCashCollectForm', 'submit'); await flush();
    assert.notEqual(JSON.parse(page.posts()[2].options.body).clientRequestId, keys[1]);
});

function documentPage(pending = {}) {
    const page = harness(script('students/documents.js'), 'studentDocumentsApp', (url, options) => {
        if (options.method === 'POST' && url.endsWith('/files'))
            return pending.upload?.shift()?.promise || ok({ reference: 'file-1' });
        if (options.method === 'POST' && url.includes('/api/student-documents/'))
            return pending.attach?.shift()?.promise || ok({ reference: 'doc-1' });
        if (url === '/api/student-documents/types') return ok([{ code: 'Birth', name: 'Birth certificate' }]);
        if (/^\/api\/students\?/.test(url)) {
            const search = new URL(url, 'https://example.test').searchParams.get('search');
            return pending.search?.shift()?.promise || ok({ items: [
                { reference: search, studentCode: search, fullName: search }] });
        }
        if (/^\/api\/students\//.test(url)) {
            const reference = url.split('/').pop();
            return pending.student?.shift()?.promise || ok({ reference, rowVersion: 'v1',
                fullName: reference, studentCode: reference });
        }
        return pending.documents?.shift()?.promise || ok([]);
    });
    const search = async reference => {
        page.get('documentSearch').value = reference;
        page.dispatch('documentSearchForm', 'submit'); await flush();
        assert.ok(page.get('documentMatches').children.length,
            'Search result missing: ' + page.get('documentNotice').textContent);
        return page.get('documentMatches').children[0];
    };
    const select = async reference => {
        (await search(reference)).handlers.get('click')(); await flush();
    };
    const upload = () => {
        page.get('documentType').value = 'Birth';
        page.get('documentFile').files = [{ size: 300, name: 'certificate.pdf' }];
        return page.dispatch('documentAttachForm', 'submit');
    };
    return { page, search, select, upload };
}

test('Student Documents cannot attach staged upload to a different student while busy', async () => {
    const gate = deferred(), { page, select, search, upload } =
        documentPage({ upload: [gate] });
    await flush(); await select('AA'); upload(); await flush();
    (await search('BB')).handlers.get('click')(); await flush();
    assert.match(page.get('documentSelected').textContent, /AA/);
    gate.resolve(ok({ reference: 'file-1' })); await flush();
    const posts = page.posts();
    assert.equal(posts.length, 2);
    assert.match(posts[1].url, /\/students\/AA$/);
    assert.doesNotMatch(posts[1].url, /\/students\/BB$/);
});

test('Student Document attach retry reuses staged private file without another upload', async () => {
    const { page, select, upload } = documentPage({ attach: [
        { promise: Promise.resolve(failed()) }, { promise: Promise.resolve(ok({ reference: 'doc-1' })) }] });
    await flush(); await select('AA'); upload(); await flush(); upload(); await flush();
    assert.equal(page.posts().filter(x => x.url.endsWith('/files')).length, 1);
    assert.equal(page.posts().filter(x => x.url.endsWith('/students/AA')).length, 2);
    assert.equal(page.get('documentFile').value, '');
    assert.match(page.get('documentNotice').textContent, /attached/);
});

test('Student Document unsaved draft refuses switch and session change clears private rows', async () => {
    const { page, select, search } = documentPage(); await flush(); await select('AA');
    page.get('documentTitle').value = 'Private evidence';
    page.confirm(false);
    (await search('BB')).handlers.get('click')(); await flush();
    assert.match(page.get('documentSelected').textContent, /AA/);
    assert.equal(page.get('documentTitle').value, 'Private evidence');
    const leaving = { prevented: false, preventDefault() { this.prevented = true; } };
    page.events.get('window:beforeunload')(leaving);
    assert.equal(leaving.prevented, true);
    page.switchTenant();
    assert.equal(page.get('documentSelected').textContent, '');
    assert.equal(page.get('documentTitle').value, '');
    assert.equal(page.get('documentRows').children.length, 0);
});

test('Student Document late selection lookup cannot replace newer student', async () => {
    const gate = deferred(), { page, select, search } = documentPage({ student: [gate] });
    await flush();
    (await search('AA')).handlers.get('click')(); await flush();
    await select('BB');
    gate.resolve(ok({ reference: 'AA', rowVersion: 'v1', fullName: 'AA', studentCode: 'AA' }));
    await flush();
    assert.match(page.get('documentSelected').textContent, /BB/);
});
