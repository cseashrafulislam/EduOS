'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('A successful supplier-return review remains committed even if its list reload fails', async () => {
    const src = read('EduOS.App/wwwroot/js/procurement/supplier-returns.js');
    const start = src.indexOf('async function execute(callback)');
    const end = src.indexOf("el('srHelpToggle')", start);
    const messages = [];
    const context = { pending: false, reference: 'invoice-1',
        loadReturns: async () => { throw Error('List unavailable'); },
        status: (message, error = false) => messages.push({ message, error }) };
    vm.runInNewContext(src.slice(start, end) + '\nthis.executeReview=execute;', context);
    await context.executeReview(async () => ({ mutated: true }));
    assert.equal(context.pending, false);
    assert.equal(messages.at(-1).error, false);
    assert.match(messages.at(-1).message, /review saved; refresh failed/i);
    await context.executeReview(async () => { throw Error('403 forbidden'); });
    assert.equal(messages.at(-1).error, true);
    assert.equal(context.pending, false);
});

test('Supplier return form preserves uncertain writes and protects invoice switching', () => {
    const src = read('EduOS.App/wwwroot/js/procurement/supplier-returns.js');
    const view = read('EduOS.App/Views/ProcurementSupplierReturns/Index.cshtml');
    assert.match(view, /form-guard\.js"[\s\S]*supplier-returns\.js"/);
    assert.match(src, /invoice && draftGuard\?\.isDirty\(\) && !draftGuard\.confirmDiscard\(\)/);
    assert.match(src, /if \(pending \|\| !invoice \|\| !form\.reportValidity\(\)\) return/);
    assert.match(src, /requestKey \|\|= crypto\.randomUUID\(\);pending = true;form\.inert = true/);
    assert.match(src, /requestKey = null;form\.reset\(\)/);
    assert.match(src, /catch \(readError\) \{ status\('Return submitted;/);
    assert.match(src, /for \(const type of \['input','change'\]\)/);
    assert.match(src, /rowVersion: row\.rowVersion, approve: word === 'APPROVE'/);
});

test('Procurement dependent lookups cannot overwrite a draft modified after a selection request', () => {
    const goods = read('EduOS.App/wwwroot/js/procurement/goods-receipts.js');
    const po = read('EduOS.App/wwwroot/js/procurement/purchase-orders.js');
    const req = read('EduOS.App/wwwroot/js/procurement/requisitions.js');
    assert.match(goods, /const draftSnapshot=window\.EduOSFormGuard\?\.fingerprint/);
    assert.match(goods, /window\.EduOSFormGuard\.fingerprint\(\$\('grCreateForm'\)\)!==draftSnapshot/);
    assert.match(po, /\$\(isReq\?'poReqSearch':'poSupplierSearch'\)\.value\.trim\(\)!==term/);
    assert.match(req, /\$\('prItemTerm'\)\.value\.trim\(\) !== term/);
});
