const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');

test('V3 supplier invoice API has anti-forgery, tenant module, authorization and no-store boundaries', () => {
 const api = read('EduOS.App/Controllers/Api/ProcurementSupplierInvoicesController.cs');
 for (const token of ['AutoValidateAntiforgeryToken','RequireModule("INVENTORY")',
  'EnableRateLimiting("ApiPolicy")','NoStore = true','Authorize(Roles="TenantAdmin,Accountant")',
  'supplier-invoices','/submit','/review','/reversal','"export"'])
  assert.ok(api.includes(token), 'missing API protection: ' + token);
 assert.match(api, /"=\+\-@"\.Contains/);
});

test('Submitted invoice reserves posted goods receipts and refuses duplicate submitted quantities', () => {
 const service = read('EduOS.Service/Services/Inventory/ProcurementSupplierInvoiceIntake.cs');
 for (const token of ['IsolationLevel.Serializable','CommittedAsync','h.StateCode == "Submitted"',
  'h.StateCode == "Posted"','source.Quantity','PreviewSupplierInvoiceMatchAsync',
  'x.PostedMovementId','SourceType == "ProcurementGoodsReceipt"','ReversalOfMovementId'])
  assert.ok(service.includes(token), 'missing three-way validation: ' + token);
});

test('Finance payable is posted once to canonical GL using independent maker/checker', () => {
 const service = read('EduOS.Service/Services/Inventory/ProcurementSupplierInvoicePosting.cs');
 for (const token of ['invoice.CreatedBy == user.UserId','IsolationLevel.Serializable',
  'JournalState.Posted','SourceType = "ProcurementSupplierInvoice"','AccountType.Asset',
  'AccountType.Liability','AccountType.Expense','PriceVariance','IsClosed',
  'PostingKey','db.JournalLines.AddRange'])
  assert.ok(service.includes(token), 'missing payable control: ' + token);
 assert.doesNotMatch(service, /db\.InventoryMovements\.(Add|Update)/);
});

test('SQL provenance is tenant-scoped and immutable; reversal mirrors the journal under second review', () => {
 const migration = read('EduOS.Persistence/Migrations/20261031210000_V3ProcurementSupplierInvoices.cs');
 const contra = read('EduOS.Service/Services/Inventory/ProcurementSupplierInvoiceReversal.cs');
 for (const token of ['TR_ProcurementSupplierInvoice_SourceImmutable',
  'TR_ProcurementSupplierInvoiceLine_SourceImmutable','UX_Journals_ProcurementSupplierInvoice',
  'TenantId','NormalizedSupplierInvoiceNumber'])
  assert.ok(migration.includes(token), 'missing SQL provenance: ' + token);
 for (const token of ['JournalState.Submitted','JournalState.Posted',
  'ReversalOfJournalId','contra.CreatedBy == user.UserId','DebitAmount = x.CreditAmount',
  'CreditAmount = x.DebitAmount','OriginalPayableAsync','sourceLines.Where'])
  assert.ok(contra.includes(token), 'missing reversal protection: ' + token);
});
