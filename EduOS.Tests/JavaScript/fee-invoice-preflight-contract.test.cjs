const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/finance/fees.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/FeeOperations/Index.cshtml'), 'utf8');
const api = fs.readFileSync(path.join(root, 'EduOS.App/Controllers/Api/FeeBillingController.cs'), 'utf8');
test('monthly invoice preflight has a real authorized API and visible register', () => {
  assert.match(api, /HttpPost\("invoices\/preflight"\)/);
  assert.match(api, /PreviewInvoiceBatchAsync\(request, ct\)/);
  for (const id of ['feePreview','feePreviewRows','feePreviewSummary','feePreviewCsv']) {
    assert.match(view, new RegExp('id="'+id+'"'));
  }
  assert.match(js, /api\('\/invoices\/preflight','POST',body\)/);
  assert.match(js, /preview\.canGenerate/);
});
test('preview CSV export escapes spreadsheet formulas and invalidates stale previews', () => {
  assert.match(js, /feePreviewCsv/);
  assert.match(js, /\[=\+\\-@\]/);
  assert.match(js, /invalidateInvoicePreview/);
  assert.match(js, /state\.invoicePreview = null/);
});
