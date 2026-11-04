const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');

test('legacy cash approval is strictly scoped, independent and does not post journals', () => {
    const src = read('EduOS.Service/Services/Accounting/AccountingSetupLegacyCashDesignations.cs');
    assert.match(src, /TenantMatched \|\| !user\.IsTenantAdmin/);
    assert.match(src, /proposal\.SubmittedByUserId == user\.UserId/);
    assert.match(src, /Version\(proposal\.RowVersion, request\.RowVersion\)/);
    assert.match(src, /db\.JournalLines\.IgnoreQueryFilters\(\)/);
    assert.match(src, /db\.BankAccounts\.AsNoTracking\(\)/);
    assert.match(src, /account\.IsCashInHand = true/);
    assert.doesNotMatch(src, /db\.Journals\.(Add|Remove|Update)\(/);
});

test('legacy cash workflow exposes CSRF protected authorized routes and a real review UI', () => {
    const controller = read('EduOS.App/Controllers/Api/AccountingSetupController.cs');
    const view = read('EduOS.App/Views/AccountingSetup/Index.cshtml');
    const js = read('EduOS.App/wwwroot/js/finance/accounting-setup.js');
    assert.match(controller, /AutoValidateAntiforgeryToken/);
    assert.match(controller, /cash-designations/);
    assert.match(controller, /Authorize\(Roles="TenantAdmin"\)/);
    assert.match(view, /historicalCashPanel/);
    assert.match(view, /historicalCashApprove/);
    assert.match(view, /historicalCashReject/);
    assert.match(js, /clientRequestId:cashSubmitKey/);
    assert.match(js, /reviewRequestId:cashReviewKey/);
    assert.match(js, /rowVersion:cashSelected\.rowVersion/);
});
