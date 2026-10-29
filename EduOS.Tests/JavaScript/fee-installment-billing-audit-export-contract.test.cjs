const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/finance/fee-installment-plans.js'), 'utf8');
const controller = fs.readFileSync(path.join(root, 'EduOS.App/Controllers/FeeInstallmentAuditExportsController.cs'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/FeeInstallmentPlans/BillingAuditPrint.cshtml'), 'utf8');

test('audit CSV and print are server-requeried and gated by finance authorization', () => {
  assert.match(controller, /Authorize\(Roles = "TenantAdmin,Principal,Accountant"\)/);
  assert.match(controller, /RequireModule\("FINANCE"\)/);
  assert.match(controller, /GetInstallmentBillingAuditAsync\(reference, ct\)/g);
  assert.match(controller, /ResponseCache\(NoStore = true/);
  assert.match(controller, /HttpGet\("export.csv"\)/);
  assert.match(controller, /HttpGet\("print"\)/);
  assert.match(js, /billing-audit\/export.csv/);
  assert.match(js, /billing-audit\/print/);
  assert.match(js, /credentials: 'same-origin', cache: 'no-store'/);
  assert.match(js, /response.headers.get\('content-type'\)/);
  assert.match(view, /Journal link/);
  assert.match(view, /does not certify/);
});
