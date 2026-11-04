const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('institution budget is planning data with immutable GL-based actual reporting', () => {
    const service = read('EduOS.Service/Services/Accounting/InstitutionBudgetService.cs');
    assert.match(service, /GetIncomeStatementAsync\(budget\.StartDate, budget\.EndDate/);
    assert.match(service, /StateCode != "Approved"/);
    assert.match(service, /IsUnbudgeted = true/);
    assert.doesNotMatch(service, /JournalLines\.Add|Journals\.Add|TotalIncomeBdt\s*=/);
});

test('maker checker, version, overlap and idempotency are enforced on server', () => {
    const service = read('EduOS.Service/Services/Accounting/InstitutionBudgetService.cs');
    assert.match(service, /budget\.SubmittedByUserId == user\.UserId/);
    assert.match(service, /MatchVersion\(budget\.RowVersion, request\.RowVersion\)/);
    assert.match(service, /x\.StateCode == "Approved" && x\.StartDate <= budget\.EndDate && x\.EndDate >= budget\.StartDate/);
    assert.match(service, /IsolationLevel\.Serializable/);
    assert.match(service, /ClientRequestId == request\.ClientRequestId/);
    assert.match(service, /request\.ReviewRequestId/);
});

test('budget schema and API protect tenant and all historical lines', () => {
    const service = read('EduOS.Service/Services/Accounting/InstitutionBudgetService.cs');
    const migration = read('EduOS.Persistence/Migrations/20261104130000_InstitutionApprovedBudgets.cs');
    const api = read('EduOS.App/Controllers/Api/InstitutionBudgetsController.cs');
    assert.match(service, /db\.CurrentTenantId == user\.TenantId/);
    assert.match(migration, /AK_InstitutionBudgetLines_TenantId_Id/);
    assert.match(migration, /FK_InstitutionBudgetLines_Accounts_TenantId_AccountId/);
    assert.match(migration, /IX_InstitutionBudgets_TenantId_ClientRequestId/);
    assert.match(api, /RequireModule\("FINANCE"\)/);
    assert.match(api, /ValidateAntiForgeryToken/);
    assert.match(api, /Authorize\(Roles = "TenantAdmin"\)/);
    const ui = read('EduOS.App/wwwroot/js/finance/institution-budgets.js');
    assert.match(ui, /data\.items/);
    assert.match(ui, /crypto\.randomUUID\(\)/);
    assert.match(ui, /safe\(x\.accountName\)/);
});
