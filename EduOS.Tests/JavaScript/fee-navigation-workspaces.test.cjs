const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const controller = read('EduOS.App/Controllers/FeeOperationsController.cs');
const view = read('EduOS.App/Views/FeeOperations/Index.cshtml');
const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
const financeJs = read('EduOS.App/wwwroot/js/finance/fees.js');
const { groupFor, categoryFor } = require('../../EduOS.App/wwwroot/js/sidebar-navigation.js');
const { guideFor } = require('../../EduOS.App/wwwroot/js/platform/page-context-help.js');

test('the finance page has three focused authorized URLs without a giant page-top link forest', () => {
    assert.match(controller, /Authorize\(Roles="TenantAdmin,Principal,Accountant"\)/);
    assert.match(controller, /RequireModule\("FINANCE"\)/);
    assert.match(controller, /Index\(\)=>RedirectToAction\(nameof\(Ledger\)\)/);
    for (const [action, feature] of [['Structures','structures'], ['Billing','billing'], ['Ledger','ledger']]) {
        assert.match(controller, new RegExp(`IActionResult ${action}\\(\\)=>Feature\\("${feature}"\\)`));
        assert.match(layout, new RegExp(`Action: "${action}"`));
        assert.ok(view.includes(`feature == "${feature}"`));
        const route = '/FeeOperations/' + action;
        assert.equal(groupFor('management', route), 'finance');
        assert.equal(categoryFor('management', 'finance', route), action === 'Structures' ? 'settings' : 'operations');
        for (const culture of ['en-BD','bn-BD']) {
            const guide = guideFor(route, culture);
            assert.ok(guide && guide.steps.length >= 3 && guide.example && guide.recovery, route + culture);
        }
    }
    assert.doesNotMatch(view, /Discount Rule Administration<\/a>/);
    assert.doesNotMatch(view, /href="\/FeeInstallmentProjection\/Index"/);
    assert.match(view, /data-active-feature="@feature"/);
});

test('existing protected finance entry forms and preview/cash safeguards are preserved', () => {
    for (const id of ['feeStructureForm', 'feeRegisterRows', 'feeStructureCloseForm',
        'feeInvoiceForm', 'feePreview', 'feeGenerate', 'feePreviewCsv',
        'feeStudentSearch', 'feeInvoices', 'feePaymentForm', 'feeCollect', 'feeReceipts'])
        assert.match(view, new RegExp(`id="${id}"`));
    assert.match(view, /@if \(User\.IsInRole\("TenantAdmin"\)\)/);
    assert.match(financeJs, /feePreview/);
    assert.match(financeJs, /feeCollect/);
    assert.match(view, /Existing invoices will not be duplicated/);
    for (const action of ['StudentInvoicePosting','StudentPaymentPosting','FeeDueCalendar',
        'FeeInstallmentProjection','FeeInstallmentPlans','FeeInvoiceCoverage','FeeDiscountAttributionReview']) {
        assert.match(layout, new RegExp(`Path: "/${action}/Index"`));
        assert.equal(groupFor('management', '/' + action + '/Index'), 'finance');
    }
});
