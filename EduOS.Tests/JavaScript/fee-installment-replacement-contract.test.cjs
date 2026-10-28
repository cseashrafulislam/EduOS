const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const api = read('EduOS.App/Controllers/Api/FeeInstallmentPlansController.cs');
const view = read('EduOS.App/Views/FeeInstallmentPlans/Index.cshtml');
const js = read('EduOS.App/wwwroot/js/finance/fee-installment-plans.js');
const service = read('EduOS.Service/Services/Finance/FeeBillingInstallmentReplacement.cs');
const transition = read('EduOS.Service/Services/Finance/FeeBillingInstallmentPlans.cs');

test('replacement requires tenant admin, antiforgery, rowversion and explicit reason', () => {
    assert.match(api, /AutoValidateAntiforgeryToken/);
    assert.match(api, /HttpPost\("\{reference:guid\}\/replace"\)/);
    assert.match(api, /Authorize\(Roles = "TenantAdmin"\)/);
    assert.match(service, /!_user\.IsTenantAdmin/);
    assert.match(service, /old\.RowVersion/);
    assert.match(service, /request\.Reason\.Any\(char\.IsControl\)/);
    for (const id of ['feePlanReplace', 'feePlanReplaceDialog', 'feePlanReplaceReason',
        'feePlanReplaceStructure', 'feePlanReplaceConfirm']) assert.ok(view.includes('id="' + id + '"'));
    assert.match(js, /clientRequestId: replacementKey, rowVersion: selected\.rowVersion/);
});

test('replacement is atomic, non-destructive, and independently approved', () => {
    assert.match(service, /IsolationLevel\.Serializable/);
    assert.match(service, /StudentInvoices\.AsNoTracking\(\)\.AnyAsync/);
    assert.match(service, /old\.PreparedByUserId == _user\.UserId/);
    assert.match(service, /db\.StudentFeeInstallmentPlans\.Add\(replacement\)/);
    assert.match(service, /db\.StudentFeeInstallmentPlanEvents\.AddRange/);
    assert.match(service, /ActionCode = 4/);
    assert.match(service, /ActionCode = 2/);
    assert.match(transition, /Replacement cancellation evidence is missing/);
    assert.doesNotMatch(service, /db\.StudentInvoices\.Add/);
    assert.doesNotMatch(service, /db\.StudentPayments\.Add/);
});
