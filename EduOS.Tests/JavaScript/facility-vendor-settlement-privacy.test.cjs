const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = file => fs.readFileSync(path.join(__dirname, '../..', file), 'utf8');

test('vendor settlement never treats approval as bank execution and requires independent maker checker', () => {
    const payment = read('EduOS.Service/Services/Facilities/FacilityVendorPaymentSettlement.cs');
    const posting = read('EduOS.Service/Services/Facilities/FacilityVendorPaymentPosting.cs');
    assert.match(payment, /VendorPaymentSource = "FacilityVendorServicePayment"/);
    assert.match(payment, /State = JournalState\.Submitted/);
    assert.match(payment, /j\.State = request\.Approve \? JournalState\.Approved : JournalState\.Rejected/);
    assert.match(payment, /j\.CreatedBy == user\.UserId/);
    assert.match(payment, /LiveActor\(bill\.CampusId, true, ct\)/);
    assert.match(payment, /Reserved/);
    assert.match(posting, /j\.State != JournalState\.Approved/);
    assert.match(posting, /j\.State = JournalState\.Posted/);
    assert.match(posting, /BankTransactionReference/);
    assert.match(posting, /j\.ApprovedByUserId == user\.UserId/);
    assert.match(posting, /j\.CreatedBy != user\.UserId/);
    assert.match(posting, /ProcurementSupplierPayment/);
    assert.match(posting, /bank statement clearance is separate/i);
});

test('reversal requires bank unmatch, live source and independent exact contra', () => {
    const payment = read('EduOS.Service/Services/Facilities/FacilityVendorPaymentSettlement.cs');
    const contra = read('EduOS.Service/Services/Facilities/FacilityVendorPaymentPosting.cs');
    const invoiceContra = read('EduOS.Service/Services/Facilities/FacilityVendorServiceBillReversal.cs');
    assert.match(payment, /ActiveVendorPaymentsAsync/);
    assert.match(invoiceContra, /HasActiveVendorPaymentsAsync\(bill.Id, ct\)/);
    assert.match(contra, /ActiveBankMatchAsync/);
    assert.match(contra, /OriginalMatchEventId/);
    assert.match(contra, /ReversalOfJournalId = payment.Id/);
    assert.match(contra, /DebitAmount = x.CreditAmount, CreditAmount = x.DebitAmount/);
    assert.match(contra, /pair.First.DebitAmount != pair.Second.CreditAmount/);
    assert.match(contra, /contra.CreatedBy == user.UserId/);
    assert.match(contra, /BankReversalReference/);
});

test('supplier aging unions stock and nonstock sources at SQL paging with as-of contra adjustment', () => {
    const aging = read('EduOS.Service/Services/Facilities/FacilitySupplierPayableAging.cs');
    const migration = read('EduOS.Persistence/Migrations/20261104121200_FacilityVendorSettlementJournalLineage.cs');
    assert.match(aging, /stock.Concat\(service\)/);
    assert.match(aging, /Skip\(\(page - 1\) \* pageSize\)\.Take\(pageSize\)/);
    assert.match(aging, /JournalDate <= asOf/);
    assert.match(aging, /InvoiceReversedAsOf = isReversed/);
    assert.match(aging, /Days1To30OnPage/);
    assert.match(aging, /Days61To90OnPage/);
    assert.match(aging, /Over90DaysOnPage/);
    assert.match(migration, /UX_Journals_FacilityVendorPaymentReversal/);
    assert.match(migration, /UX_Journals_FacilityVendorPaymentBankReference/);
    assert.match(migration, /\[State\] = 4/);
    assert.doesNotMatch(migration, /\[State\] = 3/);
});

test('approved visitor redaction cleans only source-linked legacy audit JSON and retains evidence', () => {
    const visitor = read('EduOS.Service/Services/Facilities/FacilityVisitorService.cs');
    const sql = read('EduOS.Tests/App/SqlServerFacilityVisitorTests.cs');
    assert.match(visitor, /await CanRedactAsync\(visit, ct\)/);
    assert.match(visitor, /ReviewerLiveAsync\(ct\)/);
    assert.match(visitor, /x.EntityName == nameof\(FacilityVisitorVisit\)/);
    assert.match(visitor, /x.EntityName == nameof\(FacilityVisitorEvent\)/);
    assert.match(visitor, /eventIds.Contains\(x.EntityId.Value\)/);
    assert.match(visitor, /SetProperty\(x => x.OldValue, \(string\?\)null\)/);
    assert.match(visitor, /SetProperty\(x => x.NewValue, \(string\?\)null\)/);
    assert.match(visitor, /VisitorLegacyAuditPayloadRedacted/);
    assert.match(visitor, /PrivacyAuditReportAsync/);
    assert.match(visitor, /IgnoreQueryFilters\(\)/);
    assert.match(sql, /Action == "SeparateCase"/);
    assert.match(sql, /Assert.NotNull\(separate.OldValue\)/);
    assert.match(sql, /Assert.Equal\(1, await db.AuditLogs.CountAsync/);
});

test('Finance UI and privacy reports require tenant session binding and user action', () => {
    const financeApi = read('EduOS.App/Controllers/Api/FacilityVendorServiceBillsApiController.cs');
    const visitorsApi = read('EduOS.App/Controllers/Api/FacilityVisitorsApiController.cs');
    const page = read('EduOS.App/Views/FacilityCosts/Index.cshtml');
    const js = read('EduOS.App/wwwroot/js/facilities/costs.js');
    const smoke = read('.github/workflows/sqlserver-migration.yml');
    assert.match(financeApi, /supplier-aging/);
    assert.match(financeApi, /service\.PostPaymentAsync/);
    assert.match(financeApi, /service\.ReviewPaymentReversalAsync/);
    assert.match(financeApi, /AuthSessionRegistry\.ToPublicReference/);
    assert.match(financeApi, /AutoValidateAntiforgeryToken/);
    assert.match(visitorsApi, /HttpGet\("privacy-audit"\)/);
    assert.match(visitorsApi, /Authorize\(Roles = "TenantAdmin,Principal"\)/);
    assert.match(page, /fcPaymentPanel/);
    assert.match(page, /fcAgingRows/);
    assert.match(js, /loadPaymentPanel/);
    assert.match(js, /submitPaymentAction/);
    assert.match(js, /EduOSVerifySessionContext/);
    assert.doesNotMatch(js, /\.innerHTML\s*=/);
    assert.match(smoke, /SqlServerFacilityVendorBillingTests/);
    assert.match(smoke, /SqlServerFacilityVisitorTests/);
});
