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
 const csvGuard = read('EduOS.App/Controllers/Api/ProcurementCsvExportGuard.cs');
 assert.match(api, /ProcurementCsvExportGuard\.Cell\(value\)/);
 assert.match(csvGuard, /"=\+\-@"\.Contains/);
 assert.match(api, /ExportFailure\(response\)/);
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

test('Supplier settlement instructions require independent review before confirmed canonical Finance posting', () => {
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

test('Supplier settlement execution and reversal use the original Finance GL with bank evidence and source-linked net aging', () => {
 const execution=read('EduOS.Service/Services/Inventory/ProcurementSupplierSettlementPosting.cs');
 const contract=read('EduOS.Core/DTOs/Inventory/ProcurementSupplierSettlementDtos.cs');
 const api=read('EduOS.App/Controllers/Api/ProcurementSupplierInvoicesController.cs');
 const aging=read('EduOS.Service/Services/Inventory/ProcurementSupplierInvoiceAging.cs');
 const trace=read('EduOS.Service/Services/Enterprise/V3EnterpriseSupplierPayableTraceService.cs');
 const page=read('EduOS.App/Views/ProcurementSupplierInvoiceMatching/Index.cshtml');
 const ui=read('EduOS.App/wwwroot/js/procurement/supplier-settlement-instructions.js');
 for(const required of ['PostSettlementAsync','RequestSettlementReversalAsync','ReviewSettlementReversalAsync',
  'IsolationLevel.Serializable','JournalState.Posted','BankTransactionReference','CheckedSettlementLinesAsync',
  'SettlementReversalSource','ReversalOfJournalId','Version(source.RowVersion',
  'Bank transaction reference already confirms another settlement'])
  assert.ok(execution.includes(required),'missing bank/GL source guard: '+required);
 for(const required of ['PostSupplierSettlementRequest','RequestSupplierSettlementReversal',
  'ReviewSupplierSettlementReversal','ReversalRowVersion','PostedAtUtc'])
  assert.ok(contract.includes(required),'missing settlement API contract: '+required);
 for(const route of ['/post','/reversal','/review'])assert.ok(api.includes(route));
 assert.match(api,/RequireModule\("FINANCE"\)/);
 assert.match(aging,/SourceLinkedOutstandingAmount=conflicted\?null/);
 assert.match(trace,/SettlementPostedNetAmount = postedSettlement/);
 for(const id of ['ssPostForm','ssReverseForm','ssContraForm','ssBankReference'])
  assert.ok(page.includes(id)&&ui.includes(id),'missing source-linked UI '+id);
 assert.ok(!execution.includes('HttpClient'),'GL confirmation must not initiate a bank transfer');
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

test('Maker withdrawal of unapproved settlement is source-linked, idempotent and releases only the reservation', () => {
 const service=read('EduOS.Service/Services/Inventory/ProcurementSupplierInvoiceSettlement.cs');
 const api=read('EduOS.App/Controllers/Api/ProcurementSupplierInvoicesController.cs');
 const ui=read('EduOS.App/wwwroot/js/procurement/supplier-settlement-instructions.js');
 const view=read('EduOS.App/Views/ProcurementSupplierInvoiceMatching/Index.cshtml');
 const sql=read('EduOS.Tests/App/SqlServerV3ProcurementSupplierInvoiceTests.cs');
 for(const token of ['WithdrawSettlementAsync','journal.CreatedBy != user.UserId',
  'journal.State != JournalState.Submitted','Version(journal.RowVersion, request.RowVersion)',
  'Maker withdrawal (','EndsWith(marker, StringComparison.Ordinal)',
  'JournalState.Rejected','IsolationLevel.Serializable','journal.UpdatedBy = user.UserId'])
  assert.ok(service.includes(token),'missing maker withdrawal safety: '+token);
 assert.match(api,/settlements\/\{instruction:guid\}\/withdraw/);
 assert.match(api,/WithdrawSupplierSettlementInstructionRequest/);
 for(const token of ['ssWithdrawDialog','ssWithdrawForm','ssWithdrawReason',
  'ssWithdrawSubmit','Confirm withdrawal','প্রত্যাহার নিশ্চিত করুন'])
  assert.ok(view.includes(token),'missing bilingual withdrawal dialog: '+token);
 for(const token of ['withdrawKey ||= crypto.randomUUID()',
  'clientRequestId:withdrawKey','selectedInstruction.rowVersion',
  'Number(x.makerId)===Number','chosen!==selectionSeq',
  'RequestVerificationToken',"credentials:'same-origin'"])
  assert.ok(ui.includes(token),'missing safe maker UI: '+token);
 for(const token of ['WithdrawSettlementAsync','withdrawal.ClientRequestId',
  'afterWithdrawal.Data!.SettlementReservedAmount','withdrawnJournal.PostedAt'])
  assert.ok(sql.includes(token),'missing real SQL withdrawal regression: '+token);
 assert.doesNotMatch(service,/db\.InventoryMovements\.(Add|Update)/);
});

test('Settlement UI refuses a stale cross-invoice review and uses the browser-local planned date', async () => {
 const vm = require('node:vm');
 const script = read('EduOS.App/wwwroot/js/procurement/supplier-settlement-instructions.js');
 class Element {
  constructor(id) {
   this.id = id; this.dataset = {}; this.children = []; this.listeners = {};
   this.value = ''; this.open = false; this.disabled = false;
   this.classList = { add() {}, remove() {} };
  }
  addEventListener(event, callback) { this.listeners[event] = callback; }
  append(...children) { this.children.push(...children); }
  replaceChildren(...children) { this.children = children; }
  showModal() { this.open = true; }
  close() { this.open = false; }
  querySelectorAll() { return []; }
 }
 const elements = {};
 const get = id => elements[id] ||= new Element(id);
 get('supplierSettlement').dataset = { maker: 'true', reviewer: 'true' };
 get('supplierInvoiceMatch').dataset.userId = '100';
 const handlers = {}, posts = [];
 const document = {
  getElementById: get, documentElement: { lang: 'en-BD' },
  querySelector: () => ({ content: 'csrf-token' }),
  createElement: tag => new Element(tag)
 };
 const window = { addEventListener: (event, callback) => { handlers[event] = callback; } };
 const fetch = async (url, options) => {
  if (options?.method === 'POST') posts.push(url);
  const data = url.endsWith('/options')
   ? { availableAmount: 50, banks: [], periods: [] }
   : url.includes('?page=')
    ? { totalPages: 1, items: [{
      reference: 'aaaa1111-0000-0000-0000-000000000000',
      state: 'Submitted', amount: 25, plannedDate: '2026-11-01',
      makerId: 200, rowVersion: 'abc'
     }] }
    : {};
  return { ok: true, json: async () => ({ success: true, data }) };
 };
 // 2026-11-01 00:15 in Dhaka is still 2026-10-31 in UTC.
 class DhakaDate extends Date {
  constructor(...args) { super(...(args.length ? args : ['2026-10-31T18:15:00Z'])); }
  getFullYear() { return 2026; }
  getMonth() { return 10; }
  getDate() { return 1; }
 }
 vm.runInNewContext(script, {
  document, window, fetch, Date: DhakaDate, URLSearchParams,
  crypto: { randomUUID: () => 'client-request-id' },
  Option: class { constructor(name, value) { this.name = name; this.value = value; } }
 });
 const flush = async () => { for (let i = 0; i < 20; i++) await Promise.resolve(); };
 handlers['procurement-settlement-select']({ detail: {
  reference: '11111111-0000-0000-0000-000000000000',
  invoiceNumber: 'A', rowVersion: 'x'
 } });
 await flush();
 assert.equal(get('ssDate').value, '2026-11-01', 'planned date must be local, not UTC');
 const reviewButton = get('ssRows').children[0]?.children[5]?.children[0];
 assert.ok(reviewButton, 'independent review action rendered');
 reviewButton.listeners.click();
 assert.equal(get('ssReviewDialog').open, true);
 handlers['procurement-settlement-select']({ detail: {
  reference: '22222222-0000-0000-0000-000000000000',
  invoiceNumber: 'B', rowVersion: 'y'
 } });
 await flush();
 assert.equal(get('ssReviewDialog').open, false, 'old invoice review must close');
 await get('ssReviewForm').listeners.submit({ preventDefault() {} });
 assert.deepEqual(posts, [], 'no review POST may combine invoice B with instruction A');
});
