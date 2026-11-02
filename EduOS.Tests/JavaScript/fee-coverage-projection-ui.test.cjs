'use strict';
const test = require('node:test'), assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path');
const root = path.resolve(__dirname,'../..');
const read = p => fs.readFileSync(path.join(root,p),'utf8');
for (const [name,controller,script,view] of [
 ['coverage','FeeInvoiceCoverage','fee-invoice-coverage','FeeInvoiceCoverage'],
 ['projection','FeeInstallmentProjection','fee-installment-projection','FeeInstallmentProjection']
]) {
 test(name + ' is FINANCE-authorized, bilingual, read-only and fails closed on stale or denied responses', () => {
  const page = read('EduOS.App/Controllers/' + controller + 'Controller.cs');
  const js = read('EduOS.App/wwwroot/js/finance/' + script + '.js');
  const razor = read('EduOS.App/Views/' + view + '/Index.cshtml');
  assert.match(page, /Authorize\(Roles = "TenantAdmin,Principal,Accountant"\)/);
  assert.match(page, /RequireModule\("FINANCE"\)/);
  assert.match(razor, /CultureInfo.CurrentUICulture.Name == "bn-BD"/);
  assert.doesNotMatch(razor, /ⓘ|<details/);
  assert.match(js, /document\.documentElement\.lang/);
  assert.match(js, /function (invalidate|cancelStale)\(\)/);
  assert.match(js, /controller\.abort\(\)|pending\.abort\(\)/);
  assert.match(js, /identity !== context\(\)/);
  assert.match(js, /response\.status === 401 \|\| response\.status === 403/);
  assert.doesNotMatch(js, /throw Error\(json\.message/);
  assert.match(js, /\\uFEFF/);
  assert.match(js, /\\u0000-\\u001f/);
  assert.match(js, /URL\.revokeObjectURL/);
  assert.match(js, /window\.print\(\)/);
  assert.doesNotMatch(js, /method:\s*['"]POST/);
 });
}
test('coverage validates page, cohort, status and batch totals before CSV', () => {
 const js = read('EduOS.App/wwwroot/js/finance/fee-invoice-coverage.js');
 assert.match(js, /data\.academicBatchId !== Number\(query\.get\('academicBatchId'\)\)/);
 assert.match(js, /data\.students\.page !== requested/);
 assert.match(js, /data\.eligible !== data\.billed \+ data\.unbilled \+ data\.review/);
 assert.match(js, /statusLabel\(x\.status\)/);
});
test('projection retains signed-cent BigInt, monthly scope and no financial mutation', () => {
 const js = read('EduOS.App/wwwroot/js/finance/fee-installment-projection.js');
 assert.match(js, /BigInt/);
 assert.match(js, /data\.feeStructureId !== Number\(query\.get\('feeStructureId'\)\)/);
 assert.match(js, /m\.year !== Math\.floor\(monthIndex\/12\)/);
 assert.match(js, /grossForCurrentCohort/);
 assert.match(js, /t\('Observed invoices','পর্যবেক্ষিত ইনভয়েস'\)/);
});
