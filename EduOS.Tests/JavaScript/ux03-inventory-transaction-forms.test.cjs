'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const source = name => read(`EduOS.App/wwwroot/js/inventory/${name}.js`)
    .replace(/^import .*;\s*$/gm, '').replace(/^export /gm, '');

function keyHarness() {
    let next = 0;
    const context = { crypto: { randomUUID: () => 'request-' + ++next } };
    vm.runInNewContext(source('movements-form-safety') +
        '\nthis.safety = { commandKey, refreshAfterCommit };', context);
    return { ...context.safety, crypto: context.crypto };
}

function formHarness(name) {
    const safety = keyHarness(), elements = new Map(), submissions = [];
    let errors = 2, refreshError = false, pendingPost;
    const get = id => {
        if (!elements.has(id)) elements.set(id, {
            id, value: '', disabled: false, required: false, inert: false, handlers: {},
            classList: { toggle() {} }, reportValidity: () => true,
            addEventListener(name, handler) { this.handlers[name] = handler; },
            reset() { for (const control of elements.values()) if (control !== this) control.value = ''; }
        });
        return elements.get(id);
    };
    const editor = { lines: [], clear() { this.lines.length = 0; } };
    const notices = [];
    const context = {
        ...safety, el: get, crypto: safety.crypto,
        document: { documentElement: { lang: 'en' } },
        window: { confirm: () => true, EduOSFormGuard: { create: () => ({ markClean() {} }) } },
        notice: (message, success = false) => notices.push({ message, success }),
        createLineEditor: () => editor,
        catalogs: async () => ({ locations: [{ id: 2, name: 'Main' }], items: [{ id: 3, name: 'Feed' }] }),
        fill: (target, rows) => { target.rows = rows; },
        setTimeout: callback => { context.search = callback; return 1; }, clearTimeout() {},
        api: async (url, method, body) => {
            assert.equal(method, 'POST');
            submissions.push({ url, body: structuredClone(body) });
            if (pendingPost) await pendingPost;
            if (errors-- > 0) throw Error('Uncertain network result');
            return {};
        }
    };
    get('movementType').value = 'Issue';
    get('movementFrom').value = '2';
    get('movementTo').value = '4';
    get('movementReason').value = 'Recorded stock discrepancy';
    get('movementForm').reset = () => { get('movementReason').value = ''; };
    get('adjustmentDirection').value = 'Decrease';
    get('adjustmentCost').value = '0';
    get('adjustmentLocation').value = '2';
    get('adjustmentItem').value = '3';
    get('adjustmentQuantity').value = '5';
    get('adjustmentReason').value = 'Documented shortage on shelf';
    get('inventoryAdjustmentForm').reset = () => { get('adjustmentReason').value = ''; };
    vm.runInNewContext(source(name), context);
    return { get, editor, context, submissions, notices,
        setPending: promise => { pendingPost = promise; },
        setErrors: value => { errors = value; },
        setRefreshError: value => { refreshError = value; },
        refresh: async () => { if (refreshError) throw Error('List unavailable'); } };
}

test('Inventory retry key changes only if operation or immutable payload changes', () => {
    const { commandKey } = keyHarness();
    const keys = commandKey();
    const original = keys.for('receipt', { lines: [{ inventoryItemId: 2, quantity: 5 }] });
    assert.equal(original, keys.for('receipt', { lines: [{ inventoryItemId: 2, quantity: 5 }] }));
    assert.notEqual(original, keys.for('receipt', { lines: [{ inventoryItemId: 2, quantity: 6 }] }));
    assert.notEqual(keys.for('receipt', { item: 2 }), keys.for('adjustment', { item: 2 }));
});

test('Inventory movement form prevents concurrent POSTs and keeps the same key after uncertainty', async () => {
    const h = formHarness('movements-form');
    await h.context.initForm(h.refresh);
    h.editor.lines.push({ inventoryItemId: 3, quantity: 5, lotOrBatchNo: 'A', unitCost: 0, label: 'Feed' });
    let finish;
    h.setPending(new Promise(resolve => { finish = resolve; }));
    const submit = h.get('movementForm').handlers.submit;
    const first = submit({ preventDefault() {} });
    await submit({ preventDefault() {} });
    assert.equal(h.submissions.length, 1, 'second click must not send another POST');
    assert.equal(h.get('movementForm').inert, true);
    finish(); await first; h.setPending(null);
    assert.equal(h.editor.lines.length, 1, 'network uncertainty must retain line data');
    await submit({ preventDefault() {} });
    assert.equal(h.submissions[0].body.clientRequestId, h.submissions[1].body.clientRequestId);
    h.editor.lines[0].quantity = 6;
    await submit({ preventDefault() {} });
    assert.notEqual(h.submissions[1].body.clientRequestId, h.submissions[2].body.clientRequestId);
    assert.equal(h.editor.lines.length, 0, 'confirmed success clears the draft');
    assert.equal(h.get('movementForm').inert, false);
});

test('Stock adjustment keeps reason on failures and rotates idempotency key for changed quantity', async () => {
    const h = formHarness('movements-adjustments');
    await h.context.initAdjustments(h.refresh);
    const submit = h.get('inventoryAdjustmentForm').handlers.submit;
    await submit({ preventDefault() {} });
    assert.equal(h.get('adjustmentReason').value, 'Documented shortage on shelf');
    await submit({ preventDefault() {} });
    assert.equal(h.submissions[0].body.clientRequestId, h.submissions[1].body.clientRequestId);
    h.get('adjustmentQuantity').value = '7';
    await submit({ preventDefault() {} });
    assert.notEqual(h.submissions[1].body.clientRequestId, h.submissions[2].body.clientRequestId);
    assert.equal(h.get('adjustmentReason').value, '');
});

test('Confirmed movement POST stays successful when the following list refresh fails', async () => {
    const h = formHarness('movements-form');
    await h.context.initForm(h.refresh);
    h.setErrors(0); h.setRefreshError(true);
    h.editor.lines.push({ inventoryItemId: 3, quantity: 5, lotOrBatchNo: 'A', unitCost: 0, label: 'Feed' });
    await h.get('movementForm').handlers.submit({ preventDefault() {} });
    assert.equal(h.notices.at(-1).success, true);
    assert.match(h.notices.at(-1).message, /list could not be refreshed/i);
    assert.equal(h.editor.lines.length, 0);
});

test('Physical stock count start locks concurrent writes and reuses only an unchanged failed command', async () => {
    const { commandKey, refreshAfterCommit, crypto } = keyHarness();
    const elements = new Map(), posts = [];
    const node = id => {
        if (!elements.has(id)) elements.set(id, {
            id, value: '', checked: false, inert: false, handlers: {}, dataset: {},
            classList: { add() {}, remove() {}, toggle() {}, contains() { return false; } },
            addEventListener(type, handler) { this.handlers[type] = handler; },
            setAttribute() {}, replaceChildren() {}, append() {},
            querySelectorAll() { return []; }, reportValidity: () => true
        });
        return elements.get(id);
    };
    let endPost, pending, attempts = 0;
    const countDetail = { reference: 'count-one', stateCode: 'Draft', lines: [], createdByUserId: 1 };
    const context = {
        crypto, commandKey, refreshAfterCommit, URLSearchParams, el: node,
        root: { dataset: { create: 'true', post: 'false', userId: '1' } },
        document: { documentElement: { lang: 'en' }, createElement: tag => ({
            tagName: tag, dataset: {}, classList: { add() {} }, append() {},
            setAttribute() {}, addEventListener() {}, value: ''
        }) },
        window: { EduOSFormGuard: { create: () => ({ markClean() {}, isDirty: () => false }) } },
        activeLocations: async () => [{ id: 2, code: 'ST', name: 'Store' }],
        catalogs: async () => ({ items: [{ id: 3, name: 'Feed' }] }),
        fill() {}, td: value => ({ value }), notice() {},
        setTimeout: fn => { context.search = fn; return 1; }, clearTimeout() {},
        api: async (url, method, body) => {
            if (method === 'POST') {
                posts.push(structuredClone(body)); attempts++;
                if (pending) await pending;
                if (attempts <= 2) throw Error('Uncertain count');
                return countDetail;
            }
            if (url.startsWith('movements/counts?')) return { totalPages: 1, totalCount: 0, items: [] };
            if (url === 'movements/counts/count-one') return countDetail;
            throw Error('Unexpected API ' + url);
        }
    };
    node('countItem').value = '3';
    node('countItem').selectedOptions = [{ textContent: 'Feed' }];
    node('countLocation').value = '2';
    node('countStartReason').value = 'Annual inventory verification';
    vm.runInNewContext(source('movements-physical-counts'), context);
    await context.initPhysicalCounts();
    node('countAddLine').handlers.click();
    pending = new Promise(resolve => { endPost = resolve; });
    const submit = node('countStartForm').handlers.submit;
    const first = submit({ preventDefault() {} });
    await submit({ preventDefault() {} });
    assert.equal(posts.length, 1);
    assert.equal(node('countStartForm').inert, true);
    endPost(); await first; pending = null;
    await submit({ preventDefault() {} });
    assert.equal(posts[0].clientRequestId, posts[1].clientRequestId);
    node('countBlind').checked = true;
    await submit({ preventDefault() {} });
    assert.notEqual(posts[1].clientRequestId, posts[2].clientRequestId);
    assert.equal(node('countStartForm').inert, false);
    assert.equal(node('countStartReason').value, '');
});

test('Inventory count and ledger forms preserve authoritative commands and guard context switches', () => {
    const counts = read('EduOS.App/wwwroot/js/inventory/movements-physical-counts.js');
    const ledger = read('EduOS.App/wwwroot/js/inventory/movements-ledger.js');
    const view = read('EduOS.App/Views/InventoryMovements/Index.cshtml');
    assert.match(counts, /startKeys\.for\('count\/start', body\)/);
    assert.match(counts, /submitKeys\.for\(reference \+ '\/submit', body\)/);
    assert.match(counts, /reviewKeys\.for\(reference \+ '\/review', body\)/);
    assert.match(counts, /if \(submitForm\?\.inert \|\| reviewForm\?\.inert\) return/);
    assert.match(ledger, /receiving\.has\(reference\)/);
    assert.match(ledger, /fulfilKeys\.for\(target\.reference \+ '\/' \+ action, body\)/);
    assert.match(ledger, /const target = fulfilTarget/);
    assert.match(ledger, /pendingReversals\.get\(row\.reference\)\.for/);
    assert.match(view, /form-guard\.js"[\s\S]*movements\.js"/);
});
