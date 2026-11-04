const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('Facilities non-stock vendor invoices own one source and post balanced canonical expense/payable', () => {
    const service = read('EduOS.Service/Services/Facilities/FacilityVendorServiceBillService.cs');
    const model = read('EduOS.Core/Entities/Facilities/FacilityVendorServiceBill.cs');
    const migration = read('EduOS.Persistence/Migrations/20261104121000_FacilityVendorServiceBills.cs');
    assert.match(service, /SourceType = "FacilityVendorServiceBill"/);
    assert.match(service, /JournalState\.Posted/);
    assert.match(service, /DebitAmount = bill.AmountBdt/);
    assert.match(service, /CreditAmount = bill.AmountBdt/);
    assert.match(service, /AccountType\.Expense/);
    assert.match(service, /AccountType\.Liability/);
    assert.match(service, /HasStockSupplierInvoice/);
    assert.match(service, /"vendorverify"/);
    assert.match(service, /lastCompletion <= lastReturn/);
    assert.match(service, /IsolationLevel\.Serializable/);
    assert.match(service, /ReviewFingerprint/);
    assert.match(service, /LiveActor\(bill.CampusId, true, ct\)/);
    assert.match(model, /FacilityWorkOrderId/);
    assert.match(model, /AmountBdt/);
    assert.match(migration, /UX_Journals_FacilityVendorServiceBill/);
    assert.match(migration, /FK_FacilityVendorServiceBills_ProcurementSuppliers_TenantId_SupplierId/);
});

test('Facilities supplier payable reversal is a separate independently approved full contra', () => {
    const service = read('EduOS.Service/Services/Facilities/FacilityVendorServiceBillReversal.cs');
    const migration = read('EduOS.Persistence/Migrations/20261104121000_FacilityVendorServiceBills.cs');
    assert.match(service, /"FacilityVendorServiceBillReversal"/);
    assert.match(service, /ReversalOfJournalId = original.Id/);
    assert.match(service, /DebitAmount = line.CreditAmount, CreditAmount = line.DebitAmount/);
    assert.match(service, /contra.CreatedBy == user.UserId/);
    assert.match(service, /prior.Zip\(lines\)/);
    assert.match(service, /bill.StateCode = "Reversed"/);
    assert.match(service, /JournalState\.Submitted/);
    assert.match(service, /JournalState\.Posted/);
    assert.match(migration, /UX_Journals_FacilityVendorServiceBillReversal/);
});

test('Facilities labour allocation is posted PayrollRun-derived and never books another salary journal', () => {
    const service = read('EduOS.Service/Services/Facilities/FacilityLabourCostService.cs');
    const model = read('EduOS.Core/Entities/Facilities/FacilityLabourCostAllocation.cs');
    const migration = read('EduOS.Persistence/Migrations/20261104121100_FacilityLabourCostAllocations.cs');
    assert.match(service, /PayrollRunState\.Posted/);
    assert.match(service, /"PayrollRun"/);
    assert.match(service, /"PayrollRunReversal"/);
    assert.match(service, /PayrollEmployeeId/);
    assert.match(service, /employee.UserId == order.AssignedTechnicianUserId/);
    assert.match(service, /Facilities\.PayrollStandardMinutes\./);
    assert.match(service, /decimal\.Round\(source.Value.Gross \* request.ActualWorkMinutes/);
    assert.match(service, /reserved \+ amount > source.Value.Gross/);
    assert.match(service, /UPDLOCK,HOLDLOCK/);
    assert.match(service, /RequestedByUserId == user.UserId/);
    assert.match(model, /PayrollGrossSnapshotBdt/);
    assert.doesNotMatch(service, /db\.Journals\.Add/);
    assert.doesNotMatch(service, /db\.JournalLines\.Add/);
    assert.match(migration, /AK_PayrollEmployees_TenantId_Id/);
    assert.match(migration, /AK_PayrollRuns_TenantId_Id/);
    assert.match(migration, /AK_Employees_TenantId_Id/);
});

test('Facilities financial API is tenant-session/CSRF protected and SQL smoke includes real tests', () => {
    const serviceApi = read('EduOS.App/Controllers/Api/FacilityVendorServiceBillsApiController.cs');
    const labourApi = read('EduOS.App/Controllers/Api/FacilityLabourCostsApiController.cs');
    const ci = read('.github/workflows/sqlserver-migration.yml');
    for (const api of [serviceApi, labourApi]) {
        assert.match(api, /RequireModule\("FACILITIES"\)/);
        assert.match(api, /AutoValidateAntiforgeryToken/);
        assert.match(api, /EnableRateLimiting\("ApiPolicy"\)/);
        assert.match(api, /AuthSessionRegistry.ToPublicReference/);
        assert.match(api, /X-EduOS-Session-Reference/);
        assert.match(api, /X-EduOS-Tenant-Id/);
    }
    assert.match(ci, /SqlServerFacilityVendorBillingTests/);
    assert.match(ci, /SqlServerFacilityLabourAllocationTests/);
    assert.match(read('EduOS.Tests/App/SqlServerFacilityVendorBillingTests.cs'), /"Reversed", bill.StateCode/);
    assert.match(read('EduOS.Tests/App/SqlServerFacilityLabourAllocationTests.cs'), /PayrollSourceReversed/);
});
