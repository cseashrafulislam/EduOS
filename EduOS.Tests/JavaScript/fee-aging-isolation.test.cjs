'use strict';
const test = require('node:test'), assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const aging = read('EduOS.App/wwwroot/js/finance/fee-receivables-aging.js');
const invoice = read('EduOS.App/wwwroot/js/finance/fee-reconciliation.js');
const payment = read('EduOS.App/wwwroot/js/finance/fee-payment-reconciliation.js');
const view = read('EduOS.App/Views/FeeReceivablesAging/Index.cshtml');
const api = read('EduOS.App/Controllers/Api/FeeReceivablesAgingController.cs');
test('aging report is localized, page-scoped, precision-safe and fails closed on stale scope', () => {
 assert.match(view, /CultureInfo.CurrentUICulture.Name == "bn-BD"/);
 assert.match(view, /feeAgingApp/);
 assert.match(api, /\[RequireModule\("FINANCE"\)\]/);
 assert.match(aging, /function invalidate\(\)/);
 assert.match(aging, /generation !== state\.generation/);
 assert.match(aging, /controller\.abort\(\)/);
 assert.match(aging, /function validate\(data, expectedPage, scope\)/);
 assert.match(aging, /data\?\.agingDate !== scope\.agingDate/);
 assert.match(aging, /JSON\.stringify\(current\) !== JSON\.stringify\(state\.active\)/);
 assert.match(aging, /BigInt/);
 assert.match(aging, /\\uFEFF/);
 assert.match(aging, /\\u0000-\\u001f/);
 assert.match(aging, /t\('Invoice', 'ইনভয়েস'\)/);
 assert.match(aging, /response\.status === 401 \|\| response\.status === 403/);
 assert.doesNotMatch(aging, /throw Error\(json\.message/);
});
test('both reconciliations cancel old requests and deny old invoice/receipt rows', () => {
 for (const script of [invoice, payment]) {
  assert.match(script, /function invalidate\(\)/);
  assert.match(script, /controller\.abort\(\)/);
  assert.match(script, /token !== generation/);
  assert.match(script, /JSON\.stringify\(scope\) !== JSON\.stringify\(current\(\)\)/);
  assert.match(script, /data\.page !== next/);
  assert.match(script, /response\.status === 401 \|\| response\.status === 403/);
  assert.match(script, /BigInt/);
  assert.doesNotMatch(script, /throw Error\(result\.message/);
 }
});
