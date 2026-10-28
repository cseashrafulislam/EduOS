const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/finance/fee-installment-plans.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/FeeInstallmentPlans/Index.cshtml'), 'utf8');
const api = fs.readFileSync(path.join(root, 'EduOS.App/Controllers/Api/FeeInstallmentPlansController.cs'), 'utf8');
const service = fs.readFileSync(path.join(root, 'EduOS.Service/Services/Finance/FeeBillingInstallmentBillingAudit.cs'), 'utf8');

test('plan audit has a scoped read-only endpoint and real responsive UI', () => {
  assert.match(api, /HttpGet\("\{reference:guid\}\/billing-audit"\)/);
  assert.match(api, /GetInstallmentBillingAuditAsync\(reference, ct\)/);
  for (const id of ['feePlanAuditOpen','feePlanAuditRows','feePlanAuditCsv','feePlanAuditPrint']) {
    assert.ok(view.includes('id="' + id + '"'));
  }
  assert.match(js, /billing-audit/);
  assert.match(js, /textContent/);
  assert.match(js, /csvCell/);
  assert.match(js, /afterprint/);
});

test('audit compares frozen heads, invoice totals and receipt allocations with tenant/campus boundaries', () => {
  assert.match(service, /CanAccessBillingCampusAsync\(plan\.CampusId, ct\)/);
  assert.match(service, /x\.TenantId == tenant && x\.StudentEnrollmentId == plan\.StudentEnrollmentId/);
  assert.match(service, /keys\.Contains\(x\.ClientRequestId\)/);
  assert.match(service, /billedLines\.Sum\(x => x\.DiscountAmount\)/);
  assert.match(service, /db\.PaymentAllocations\.AsNoTracking\(\)/);
  assert.match(service, /allocated\.GetValueOrDefault\(invoice\.Id\) != invoice\.PaidAmount/);
  assert.match(service, /InvoiceState\.Cancelled/);
});
