const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
const mis = read('EduOS.Service/Services/Platform/CentralInstitutionMisService.cs');
const controller = read('EduOS.App/Controllers/Api/CentralInstitutionMisController.cs');
const totals = read('EduOS.Service/Services/Accounting/AccountingReportingIncomeExpenseTotals.cs');
const financeDetails = read('EduOS.Service/Services/Accounting/AccountingReportingIncomeExpenseDetails.cs');

test('institution-wide MIS requires live tenant-admin membership and never writes alternate KPIs', () => {
    assert.match(controller, /Authorize\(Roles = "TenantAdmin"\)/);
    assert.match(controller, /RequireModule\("REPORTING"\)/);
    assert.match(controller, /NoStore = true/);
    assert.match(mis, /db\.CurrentTenantId != user\.TenantId/);
    assert.match(mis, /MembershipStatus\.Active/);
    assert.match(mis, /r\.NormalizedName == "TENANTADMIN"/);
    assert.match(mis, /x\.TenantId == tenant && !x\.IsDeleted/);
    assert.doesNotMatch(mis, /SaveChangesAsync|ExecuteUpdateAsync|ExecuteDeleteAsync/);
});

test('MIS distinguishes unavailable modules from zero and reads posted GL through canonical finance', () => {
    assert.match(mis, /IsCurrentTenantModuleAvailableAsync\(module\)/);
    assert.match(mis, /allowed \? await value\(\) : \(decimal\?\)null/);
    assert.match(mis, /GetInstitutionalIncomeExpenseTotalsAsync/);
    assert.match(mis, /totals\.Data\.NetSurplus/);
    assert.match(mis, /Scope = "Period", Unit = "BDT"/);
    assert.doesNotMatch(mis, /StudentPayments|InventoryMovements.*Sum/);
    // Operational invoice receivables are allowed only as distinct CURRENT
    // source-owned metrics, never as a substitute for posted GL income.
    assert.match(mis, /Key = "outstanding_fee_invoices", ModuleCode = "FINANCE", Source = "StudentInvoices"/);
    assert.match(mis, /Key = "outstanding_fee_amount", ModuleCode = "FINANCE", Source = "StudentInvoices"/);
    assert.match(mis, /Scope = "Current", Unit = "BDT", Available = true/);
    assert.match(mis, /AddFinance\("posted_income"[\s\S]*?totals\.Data\.TotalIncome\)/);
    assert.match(mis, /AddFinance\("net_surplus"[\s\S]*?totals\.Data\.NetSurplus\)/);
});

test('aggregate finance totals keep integrity checks and no fixed posting-account ceiling', () => {
    assert.match(totals, /HistoricalPostingErrorAsync\(fromDate, toDate, null, ct\)/);
    assert.match(totals, /IgnoreQueryFilters\(\)/);
    assert.match(totals, /x\.Debit != x\.Credit/);
    assert.match(totals, /Posted\.Where\(j =>/);
    assert.match(totals, /GroupBy\(x => 1\)/);
    assert.doesNotMatch(totals, /Take\(5000\)|balances\.Count > 5000|ToListAsync/);
    assert.match(financeDetails, /GetInstitutionalIncomeExpenseTotalsAsync\(fromDate, toDate, ct\)/);
});
