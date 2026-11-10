'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('Goods receipt create, submit and valuation review use distinct stable keys per exact command', () => {
    const src = read('EduOS.App/wwwroot/js/procurement/goods-receipts.js');
    const excerpt = src.slice(src.indexOf('const retryKeys = new Map();'), src.indexOf('const createDialog ='));
    let counter = 0;
    const context = { crypto: { randomUUID: () => 'receipt-' + ++counter } };
    vm.runInNewContext(excerpt + '\nthis.key = requestKey;', context);
    const same = { purchaseOrderId: 22, locationId: 3, reason: 'Received with documentation',
        lines: [{ purchaseOrderLineId: 44, quantity: 5, lotOrBatchNo: 'LOT-A' }] };
    const first = context.key('create', same);
    assert.equal(first, context.key('create', structuredClone(same)));
    const altered = { ...same, lines: [{ ...same.lines[0], quantity: 6 }] };
    assert.notEqual(first, context.key('create', altered));
    assert.notEqual(context.key('goods-X/review', { decision: 'Approve', valuations: [] }),
        context.key('goods-X/review', { decision: 'Approve', valuations: [{ lineNo: 1, amount: 123 }] }));
    assert.notEqual(context.key('goods-X/submit', { rowVersion: 'a' }),
        context.key('goods-Y/submit', { rowVersion: 'a' }));
});

test('Purchase order editor refuses dismissal while a save is active or draft discard is declined', () => {
    const src = read('EduOS.App/wwwroot/js/procurement/purchase-orders.js');
    const excerpt = src.slice(src.indexOf('function close(dialog, force = false)'),
        src.indexOf('for (const [dialog,guard] of formGuards)', src.indexOf('function close(dialog, force = false)')));
    let accept = false;
    const dialog = { open: true, closed: 0, close() { this.closed++; this.open = false; } };
    const context = { busy: false, formGuards: new Map([[dialog, { confirmDiscard: () => accept }]]) };
    vm.runInNewContext(excerpt + '\nthis.closeDialog = close;', context);
    assert.equal(context.closeDialog(dialog), false);
    assert.equal(dialog.closed, 0);
    accept = true;context.busy = true;
    assert.equal(context.closeDialog(dialog), false);
    assert.equal(dialog.closed, 0);
    assert.equal(context.closeDialog(dialog, true), true, 'confirmed server success may close the locked form');
    assert.equal(dialog.closed, 1);
});

test('Requisition editor protects unsaved lines and never closes while saving', () => {
    const src = read('EduOS.App/wwwroot/js/procurement/requisitions.js');
    const excerpt = src.slice(src.indexOf('function closeForm(force = false)'),
        src.indexOf("dialog.addEventListener('cancel'", src.indexOf('function closeForm(force = false)')));
    const dialog = { open: true, closed: 0, close() { this.closed++; this.open = false; } };
    let allowed = false;
    const context = { busy: false, draftGuard: { confirmDiscard: () => allowed }, historyEpoch: 0,
        historyId: 15, dialog };
    vm.runInNewContext(excerpt + '\nthis.closeDraft = closeForm;', context);
    assert.equal(context.closeDraft(), false);
    allowed = true;context.busy = true;
    assert.equal(context.closeDraft(), false);
    context.busy = false;
    assert.equal(context.closeDraft(), true);
    assert.equal(dialog.closed, 1);
    assert.equal(context.historyId, null);
});

test('Procurement core forms use safe drafts, distinct dependent lookup generations and server-owned actions', () => {
    const po = read('EduOS.App/wwwroot/js/procurement/purchase-orders.js');
    const receipt = read('EduOS.App/wwwroot/js/procurement/goods-receipts.js');
    const req = read('EduOS.App/wwwroot/js/procurement/requisitions.js');
    const viewRoot = 'EduOS.App/Views/';
    for (const view of ['ProcurementPurchaseOrders/Index.cshtml', 'ProcurementGoodsReceipts/Index.cshtml',
        'ProcurementRequisitions/Index.cshtml'])
        assert.match(read(viewRoot + view), /form-guard\.js"[\s\S]*procurement\/[^"\n]+\.js"/);
    assert.match(po, /const lookupEpoch = \{req:0,supplier:0\}/);
    assert.match(po, /if \(createSignature && createSignature!==signature\)\$\('poRequestId'\)\.value=crypto\.randomUUID\(\)/);
    assert.match(po, /\$\('poCreateForm'\)\.inert=true/);
    assert.match(po, /\$\('poEditForm'\)\.inert=true/);
    assert.match(receipt, /if \(chosenPo && guarded\.get\(createDialog\)\?\.isDirty\(\)/);
    assert.match(receipt, /requestKey\('create',body\)/);
    assert.match(receipt, /requestKey\(action,body\)/);
    assert.match(receipt, /\$\('grReviewForm'\)\.inert = true/);
    assert.match(req, /if \(!id && draftSignature && draftSignature!==signature\)/);
    assert.match(req, /\$\('prForm'\)\.inert = true/);
    assert.match(req, /requestEpoch !== itemSearchEpoch \|\| picker\.hidden \|\| !dialog\.open/);
    for (const module of [po, receipt, req]) {
        assert.match(module, /RequestVerificationToken/);
        assert.match(module, /credentials:'same-origin'/);
    }
});
