const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const read = name => fs.readFileSync(name, 'utf8');
const report = read('EduOS.Service/Services/Accounting/AccountingReportingCashFlowStatement.cs');
const setup = read('EduOS.Service/Services/Accounting/AccountingSetupCashFlowRules.cs');
const control = read('EduOS.Service/Services/Facilities/FacilitySupplierApInstitutionControl.cs');
const view = read('EduOS.App/Views/AccountingReports/Index.cshtml');
const setupView = read('EduOS.App/Views/AccountingSetup/Index.cshtml');
const api = read('EduOS.App/Controllers/Api/AccountingReportsController.cs');
const setupApi = read('EduOS.App/Controllers/Api/AccountingSetupController.cs');
const apApi = read('EduOS.App/Controllers/Api/CentralSupplierPayablesController.cs');
const js = read('EduOS.App/wwwroot/js/finance/accounting-cash-movements.js');
const setupJs = read('EduOS.App/wwwroot/js/finance/accounting-cash-flow-policies.js');
const apJs = read('EduOS.App/wwwroot/js/finance/accounting-additional-reports.js');

test('institution-wide AP control reads every source page and the independent entire GL', () => {
    assert.match(control, /BeginTransactionAsync\(IsolationLevel\.Serializable/);
    assert.match(control, /SupplierAgingWithGlAsync\(asOf, campusId, page, batchSize, ct\)/);
    assert.match(control, /count > maximumInvoices/);
    assert.match(control, /row\.GlReconciliationStatus != "Reconciled"/);
    assert.match(control, /var journals = db\.Journals\.AsNoTracking\(\)/);
    assert.match(control, /line\.CreditAmount - x\.DebitAmount|x\.CreditAmount - x\.DebitAmount/);
    assert.match(control, /UnattributedGlBalance/);
    assert.doesNotMatch(control, /SaveChanges|ExecuteUpdate|AddAsync/);
    assert.match(apApi, /\[HttpGet\("supplier-ap-control"\)\]/);
    assert.match(view, /id="supplierApControlRows"/);
    assert.match(apJs, /supplier-ap-control/);
    assert.match(apJs, /cents\(data\.subledgerOutstanding\) - fullGl/);
    assert.match(apJs, /institution-supplier-ap-control/);
});

test('approved account activity policy has independent maker checker and dated immutable history', () => {
    assert.match(setup, /CashFlowActor\(true, ct\)/);
    assert.match(setup, /rule\.SubmittedByUserId == user\.UserId/);
    assert.match(setup, /Version\(rule\.RowVersion, request\.RowVersion\)/);
    assert.match(setup, /rule\.StateCode != "Submitted"/);
    assert.match(setup, /BeginTransactionAsync\(IsolationLevel\.Serializable/);
    assert.match(setup, /ClientRequestId/);
    assert.match(setupApi, /HttpPost\("cash-flow-rules\/\{id:long\}\/review"\),Authorize\(Roles="TenantAdmin"\)/);
    assert.match(setupView, /id="cashFlowPolicyPanel"/);
    assert.match(setupView, /id="cashFlowRuleReviewPanel"/);
    assert.match(setupJs, /RequestVerificationToken/);
    assert.match(setupJs, /crypto\.randomUUID\(\)/);
});

test('classified cash-flow statement conserves cash, exposes unmapped movements and policy snapshot', () => {
    assert.match(report, /GetInstitutionalCashMovementsAsync\(fromDate, toDate, ct\)/);
    assert.match(report, /r\.StateCode == "Approved"/);
    assert.match(report, /r\.ReviewedAtUtc <= cutoff/);
    assert.match(report, /x\.EffectiveFromDate <= journal\.Key\.JournalDate/);
    assert.match(report, /UnclassifiedJournalCount\+\+/);
    assert.match(report, /statement\.UnclassifiedInflows/);
    assert.match(report, /cash\.Data\.Receipts/);
    assert.doesNotMatch(report, /SaveChangesAsync|ExecuteUpdateAsync|AddAsync/);
    assert.match(api, /HttpGet\("cash-flow-statement"\), Authorize\(Roles = "TenantAdmin"\)/);
    assert.match(view, /id="cashFlowPanel"/);
    assert.match(js, /INCOMPLETE — NOT FOR STATUTORY SUBMISSION/);
    assert.match(js, /data\.policyApprovedThroughUtc/);
    assert.match(js, /cents\(data\.openingCashAndBank\) \+ cents\(data\.netCashChange\)/);
});
