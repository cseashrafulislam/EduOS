const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/finance/fee-installment-plans.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/FeeInstallmentPlans/Index.cshtml'), 'utf8');
const api = fs.readFileSync(path.join(root, 'EduOS.App/Controllers/Api/FeeInstallmentPlansController.cs'), 'utf8');
const service = fs.readFileSync(path.join(root, 'EduOS.Service/Services/Finance/FeeBillingInstallmentInvoices.cs'), 'utf8');

test('approved instalment month billing is a real antiforgery-protected API action', () => {
  assert.match(api, /AutoValidateAntiforgeryToken/);
  assert.match(api, /HttpPost\("\{reference:guid\}\/issue-month"\)/);
  assert.match(api, /IssueInstallmentMonthAsync\(reference, request, ct\)/);
  for (const id of ['feePlanIssueMonth','feePlanIssue']) {
    assert.match(view, new RegExp('id="'+id+'"'));
  }
  assert.match(js, /clientRequestId: invoiceKey\.id, monthStart: month, rowVersion: selected\.rowVersion/);
  assert.match(js, /selected\.stateCode !== 3/);
});
test('plan month invoice is deterministic and checks scope, approval, conflicts and line integrity', () => {
  assert.match(service, /InstallmentMonthKey\(plan\.PublicId, request\.MonthStart\)/);
  assert.match(service, /IsolationLevel\.Serializable/);
  assert.match(service, /CanAccessBillingCampusAsync/);
  assert.match(service, /actions\.Count != 2/);
  assert.match(service, /priorLines\.Sum\(x => x\.NetAmount\)/);
  assert.match(service, /CalculateLineDiscounts/);
  assert.match(service, /db\.StudentInvoiceLines\.Add/);
});
