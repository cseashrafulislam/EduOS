'use strict';
// UX D source-only evidence; never a replacement for browser, SQL or Full Final QA.
const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'../..'),read=p=>fs.readFileSync(path.join(root,p),'utf8');
const views=['FeeDiscountRules','AdmissionCashReceipts','FeeReceivablesAging','FeeHeadAccounting',
 'AccountingSetup','AccountingJournals','InstitutionBudgets','AccountingReports','FeeOperations','StudentInvoicePosting','StudentPaymentPosting'];
test('first UX D finance source views use one shared title and balanced forms',()=>{
 for(const name of views){const src=read('EduOS.App/Views/'+name+'/Index.cshtml');
  assert.match(src,/ViewData\["Title"\]/,name);
  assert.doesNotMatch(src.slice(0,950),/<header\b|<h1\b|content-header-card/i,name);
  assert.equal((src.match(/<form\b/gi)||[]).length,(src.match(/<\/form>/gi)||[]).length,name);
 }
});
test('admission cash receipt remains journal-linked, session-scoped and retry-safe',()=>{
 const view=read('EduOS.App/Views/AdmissionCashReceipts/Index.cshtml');
 const js=read('EduOS.App/wwwroot/js/admissions/cash-receipts.js');
 for(const id of ['admissionCashCollect','admissionCashPeriod','admissionCashLedger','admissionIncomeLedger','admissionCashReceiptPrint'])
  assert.ok(view.includes('id="'+id+'"'),id);
 assert.match(js,/clientRequestId: requestId/);
 assert.match(js,/applicantRowVersion: applicant\.rowVersion/);
 assert.match(js,/headers\.RequestVerificationToken = token/);
 assert.match(view,/asp-controller="Admissions"/);
});
test('fee head and COA internal codes are created by the server, not entered manually',()=>{
 const fee=read('EduOS.Service/Services/Finance/FeeHeadAccountingService.cs');
 const coa=read('EduOS.Service/Services/Accounting/AccountingSetupService.cs');
 assert.match(fee,/FHD-/);assert.match(coa,/ACC-/);
 assert.match(fee,/request\.ClientRequestId/);assert.match(coa,/request\.ClientRequestId/);
 assert.match(read('EduOS.App/Views/FeeHeadAccounting/Index.cshtml'),/id="feeHeadCode" type="hidden"/);
 assert.match(read('EduOS.App/Views/AccountingSetup/Index.cshtml'),/id="accountCode" type="hidden"/);
 assert.match(read('EduOS.App/wwwroot/js/finance/fee-head-accounting.js'),/createRequestId = crypto\.randomUUID\(\)/);
 const js=read('EduOS.App/wwwroot/js/finance/accounting-setup.js');
 assert.match(js,/createAccountRequestId = crypto\.randomUUID\(\)/);
 assert.match(js,/register could not reload/);
});
test('central accounts and fee billing retain scopes, reconciliation semantics and no generic Refresh',()=>{
 const report=read('EduOS.App/Views/AccountingReports/Index.cshtml');
 const fees=read('EduOS.App/Views/FeeOperations/Index.cshtml');
 assert.match(report,/Accrual income\/expense and actual bank cash movements/);
 assert.match(report,/Load saved drafts/);
 assert.doesNotMatch(report,/>Refresh<\/button>/i);
 for(const scope of ['structures','billing','ledger'])assert.ok(fees.includes('"'+scope+'"'));
 for(const id of ['feeSaveStructure','feePreview','feeGenerate','feeLoadLedger'])
  assert.ok(fees.includes('id="'+id+'"'),id);
 assert.match(fees,/data-active-feature=/);
});
test('invoice and receipt GL posting screens preserve related workflows without duplicate titles',()=>{
 const invoice=read('EduOS.App/Views/StudentInvoicePosting/Index.cshtml');
 const payment=read('EduOS.App/Views/StudentPaymentPosting/Index.cshtml');
 assert.match(invoice,/asp-controller="StudentPaymentPosting"/);
 assert.match(payment,/asp-controller="StudentInvoicePosting"/);
 for(const id of ['invoicePostingPeriod','invoicePostingUnposted'])assert.ok(invoice.includes('id="'+id+'"'),id);
 for(const id of ['paymentPostingCash','paymentPostingPeriod','paymentPostingUnposted'])
  assert.ok(payment.includes('id="'+id+'"'),id);
 assert.match(invoice,/Posted journals are immutable/);
 assert.match(payment,/One campus per receipt/);
});
test('ledger credits all thirty-nine UX D source routes and explicitly defers Final QA',()=>{
 const doc=read('docs/ux/lanes/UX_D.md');
 const routes=[...doc.matchAll(/^- \[([ x])\] \x60(\/[^\x60]+)\x60/gm)];
 assert.equal(routes.length,39);
 assert.equal(routes.filter(x=>x[1]==='x').length,39);
 assert.match(doc,/39\/39 UX D routes/);
 assert.match(doc,/Final QA/);
});
