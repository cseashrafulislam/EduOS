const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const js = read('EduOS.App/wwwroot/js/finance/fee-installment-plans.js');
const view = read('EduOS.App/Views/FeeInstallmentPlans/Index.cshtml');
const api = read('EduOS.App/Controllers/Api/FeeInstallmentPlansController.cs');
const preview = read('EduOS.Service/Services/Finance/FeeBillingInstallmentPreview.cs');
const issue = read('EduOS.Service/Services/Finance/FeeBillingInstallmentInvoices.cs');

test('preview is explicitly requested, antiforgery protected, and never issues an invoice', () => {
    assert.match(api, /AutoValidateAntiforgeryToken/);
    assert.match(api, /HttpPost\("\{reference:guid\}\/preview-month"\)/);
    assert.match(api, /PreviewInstallmentMonthAsync\(reference, request, ct\)/);
    for (const id of ['feePlanPreview','feePlanPreviewDialog','feePlanPreviewRows',
        'feePlanPreviewSummary','feePlanPreviewClose','feePlanPreviewIssue'])
        assert.match(view, new RegExp('id="' + id + '"'));
    assert.match(js, /previewSequence/);
    assert.match(js, /selected\?\.rowVersion !== rowVersion/);
    assert.match(js, /\/preview-month', 'POST'/);
    assert.match(js, /\.textContent = month/);
    assert.match(preview, /StudentFeeInstallmentMonthPreviewDto/);
    assert.doesNotMatch(preview, /db\.StudentInvoices\.Add|db\.SaveChangesAsync/);
});
test('preview shares discount policy and rejects stale, unapproved and already invoiced months', () => {
    assert.match(preview, /CanAccessBillingCampusAsync/);
    assert.match(preview, /Convert\.ToBase64String\(plan\.RowVersion\)/);
    assert.match(preview, /actions\.Count != 2/);
    assert.match(preview, /InstallmentPlanDtoAsync\(db, plan, true, ct\)/);
    assert.match(preview, /db\.StudentInvoices\.AsNoTracking\(\)\.AnyAsync/);
    assert.match(preview, /CalculateLineDiscounts/);
    assert.match(issue, /GetActiveInstallmentDiscountRulesAsync/);
});
