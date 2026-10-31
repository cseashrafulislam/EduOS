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

test('Canonical inventory reversal cannot orphan a reserved or posted Procurement payable', () => {
 const stock = read('EduOS.Service/Services/Inventory/InventoryMovementReversalIdempotency.cs');
 for (const token of ['original.SourceType == "ProcurementGoodsReceipt"',
  'db.ProcurementSupplierInvoices.Any', 'h.StateCode == "Submitted"',
  'h.StateCode == "Posted"','reverse the payable first'])
  assert.ok(stock.includes(token), 'missing stock/Finance reversal handshake: ' + token);
});

test('Supplier settlement instructions use canonical unposted Finance journals and separate maker/checker', () => {
 const service=read('EduOS.Service/Services/Inventory/ProcurementSupplierInvoiceSettlement.cs');
 const api=read('EduOS.App/Controllers/Api/ProcurementSupplierInvoicesController.cs');
 const ui=read('EduOS.App/wwwroot/js/procurement/supplier-settlement-instructions.js');
 for(const token of ['IsolationLevel.Serializable','SettlementSource = "ProcurementSupplierPayment"',
  'JournalState.Submitted','JournalState.Approved','ReservedSettlementAsync','InvoiceRowVersion',
  'BankAccounts','AccountType.Asset','AccountType.Liability','journal.CreatedBy == user.UserId',
  'SourceId = invoice.Id','IdempotencyKey = request.ClientRequestId'])
  assert.ok(service.includes(token), 'missing settlement protection: '+token);
 assert.doesNotMatch(service,/JournalState.Posted\s*[,;}]/);
 for(const token of ['RequireModule("FINANCE")','/settlements','ReviewSettlement','RequestSettlement'])
  assert.ok(api.includes(token), 'missing Finance API protection: '+token);
 for(const token of ['credentials:\'same-origin\'','RequestVerificationToken','textContent','rowVersion',
  'procurement-settlement-select'])assert.ok(ui.includes(token), 'missing UI safety: '+token);
});

test('Independent reviewer can revoke approved unposted supplier settlement and release reservation', () => {
 const service=read('EduOS.Service/Services/Inventory/ProcurementSupplierInvoiceSettlement.cs');
 const ui=read('EduOS.App/wwwroot/js/procurement/supplier-settlement-instructions.js');
 const sql=read('EduOS.Tests/App/SqlServerV3ProcurementSupplierInvoiceTests.cs');
 for(const token of ['revokingApproval','JournalState.Approved && !request.Approve',
  'journal.PostedAt.HasValue','Approved instruction revoked','journal.ApprovedByUserId = user.UserId',
  'Finance decision audit exceeds journal capacity'])
  assert.ok(service.includes(token),'missing safe revocation control: '+token);
 for(const token of ['Revoke approval','ssDecision','revoking','ssReviewTitle'])
  assert.ok(ui.includes(token),'missing bilingual reviewer action: '+token);
 for(const token of ['checkerRevoke','released.ReservedAmount','releasedTrace',
  'Replacement source-linked instruction','recorded.ApprovedByUserId'])
  assert.ok(sql.includes(token),'missing SQL revocation regression: '+token);
 assert.doesNotMatch(service,/db\.InventoryMovements\.(Add|Update)/);
});
