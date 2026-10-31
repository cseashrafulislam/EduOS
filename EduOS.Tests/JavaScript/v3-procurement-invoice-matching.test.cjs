const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');

test('V3 supplier invoice preflight has role, plan, CSRF and rate-limit at the API boundary', () => {
 const c = read('EduOS.App/Controllers/Api/ProcurementSupplierInvoiceMatchingController.cs');
 assert.match(c, /Authorize\(Roles = "TenantAdmin,Principal,Accountant"\)/);
 assert.match(c, /RequireModule\("INVENTORY"\)/);
 assert.match(c, /AutoValidateAntiforgeryToken/);
 assert.match(c, /EnableRateLimiting\("ApiPolicy"\)/);
 assert.match(c, /NoStore = true/);
});

test('Invoice preview requires posted canonical receipt and refuses reversal and cross-campus IDOR', () => {
 const s = read('EduOS.Service/Services/Inventory/InventoryMovementProcurementInvoiceMatching.cs');
 for (const rule of ['VisibleReceipts()', 'StateCode != "Posted"',
  'SourceType == "ProcurementGoodsReceipt"', 'ReversalOfMovementId.HasValue',
  'movement.SourceId != x.Id', 'movement.ToLocationId != x.LocationId',
  'PriceMode', 'VarianceReason', 'ProposedAmount', 'DifferenceFromStockValue'])
  assert.ok(s.includes(rule), 'missing preflight boundary: ' + rule);
 assert.doesNotMatch(s, /db\.Journals\.Add|db\.StudentInvoices\.Add|db\.ProcurementSupplierInvoices\.Add/);
});

test('Bilingual invoice matching UI does not silently issue a payable and encodes API results', () => {
 const html = read('EduOS.App/Views/ProcurementSupplierInvoiceMatching/Index.cshtml');
 const js = read('EduOS.App/wwwroot/js/procurement/supplier-invoice-matching.js');
 assert.match(html, /data-bn=/);
 assert.match(html, /aria-expanded="false"/);
 assert.match(html, /does not save an invoice, book a payable/);
 assert.match(js, /credentials:'same-origin'/);
 assert.match(js, /RequestVerificationToken/);
 assert.match(js, /textContent/);
 assert.doesNotMatch(js, /innerHTML|insertAdjacentHTML|\.html\(/);
 assert.match(js, /Preview only: no payable or invoice created/);
});
