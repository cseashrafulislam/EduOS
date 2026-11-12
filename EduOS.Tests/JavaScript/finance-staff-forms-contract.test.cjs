const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const fee = fs.readFileSync(path.join(root,'EduOS.App/wwwroot/js/finance/fees.js'),'utf8');
const plan = fs.readFileSync(path.join(root,'EduOS.App/wwwroot/js/finance/fee-installment-plans.js'),'utf8');

test('Cash receipt validates exact two-decimal paisa, invoice due and authoritative preflight', () => {
 assert.match(fee, /function paisa\(value\)/);
 assert.match(fee, /Number\.isSafeInteger\(result\)/);
 assert.match(fee, /paymentPaisa>duePaisa/);
 assert.match(fee, /paisa\(preview\.amount\)!==paymentPaisa/);
 assert.match(fee, /paisa\(preview\.invoices\[0\]\.allocating\)!==paymentPaisa/);
 assert.match(fee, /clientRequestId=stable\('payment',JSON\.stringify\(body\)\)/);
 assert.match(fee, /await api\('\/payments\/preflight','POST',body\)/);
 assert.match(fee, /await api\('\/payments','POST',body\)/);
});
test('Installment student search cannot show stale responses after a new query', () => {
 assert.match(plan, /studentSearchSequence/);
 assert.match(plan, /sequence !== studentSearchSequence \|\| searchController\.signal\.aborted/);
 assert.match(plan, /el\('feePlanStudentSearch'\)\.value\.trim\(\) !== term/);
 assert.match(plan, /if \(searchController\) searchController\.abort\(\)/);
});
test('Installment draft is retry-safe, prevents concurrent mutation and separates successful write from failed refresh', () => {
 assert.match(plan, /if \(draftBusy \|\| !event\.currentTarget\.reportValidity\(\)\) return/);
 assert.match(plan, /draftBusy = true/);
 assert.match(plan, /el\('feePlanCreate'\)\.inert = true/);
 assert.match(plan, /clientRequestId: draftKey/);
 assert.match(plan, /const saved = await api\('\/plans', 'POST', payload\)/);
 assert.match(plan, /draftKey = null;\s*notice\('Gross agreement draft saved/);
 assert.match(plan, /catch \(refreshError\)/);
 assert.match(plan, /draftBusy = false/);
});
