'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('Facilities parts Finance is restricted to canonical source with independently approved balanced GL', () => {
    const api = read('EduOS.App/Controllers/Api/FacilityPartsFinanceApiController.cs');
    const create = read('EduOS.Service/Services/Inventory/FacilityPartsFinanceRecognition.cs');
    assert.match(api, /RequireModule\("INVENTORY"\), RequireModule\("FACILITIES"\), RequireModule\("FINANCE"\)/);
    assert.match(api, /AutoValidateAntiforgeryToken/);
    assert.match(create, /SourceType = "InventoryMovement", SourceId = movement\.Id/);
    assert.match(create, /await StockCost\(movement\.Id, ct\)/);
    assert.match(create, /journal\.CreatedBy == user\.UserId/);
    assert.match(create, /AccountsValid\(lines\[0\]\.AccountId, lines\[1\]\.AccountId, ct\)/);
    assert.match(create, /OpenPeriod\(journal\.AccountingPeriodId, journal\.JournalDate, ct\)/);
});

test('Facility GL and stock correction are one transaction and normal stock reversal is blocked after Finance linkage', () => {
    const reverse = read('EduOS.Service/Services/Inventory/FacilityPartsFinanceReversal.cs');
    const stock = read('EduOS.Service/Services/Inventory/InventoryMovementFacilityFinanceUnwind.cs');
    const generic = read('EduOS.Service/Services/Inventory/InventoryMovementReverse.cs');
    const model = read('EduOS.Persistence/Context/EduOSDbContext.cs');
    assert.match(reverse, /ReverseFacilityIssueInTransactionAsync/);
    assert.match(reverse, /await db\.SaveChangesAsync\(ct\); await tx\.CommitAsync\(ct\)/);
    assert.match(stock, /db\.Database\.CurrentTransaction == null/);
    assert.match(stock, /Submitted independent Finance reversal/);
    assert.match(generic, /Finance-linked facility stock must use/);
    assert.match(model, /UX_Journals_FacilityPartsExpense/);
    assert.match(model, /UX_Journals_FacilityPartsReversal/);
});

test('Finance desk has bounded source discovery, tenant-session proof and bilingual manual', () => {
    const service = read('EduOS.Service/Services/Inventory/FacilityPartsFinanceListing.cs');
    const js = read('EduOS.App/wwwroot/js/inventory/facility-parts-finance.js');
    const html = read('EduOS.App/Views/InventoryMovements/FacilityFinance.cshtml');
    const guide = read('EduOS.App/wwwroot/js/platform/page-context-help.js');
    assert.match(service, /Skip\(\(page - 1\) \* pageSize\)\.Take\(pageSize\)/);
    assert.match(service, /UserCampusAccesses\.Any/);
    assert.match(js, /X-EduOS-Session-Reference/);
    assert.match(js, /X-EduOS-Tenant-Id/);
    assert.match(js, /EduOSVerifySessionContext/);
    assert.match(js, /commandId\(key, payload\)/);
    assert.match(html, /fpfReverseReviewPanel/);
    assert.match(guide, /'\/inventorymovements\/facilityfinance\/index'/);
});
