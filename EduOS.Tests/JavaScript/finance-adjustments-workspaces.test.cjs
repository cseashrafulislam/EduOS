const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const controller = read('EduOS.App/Controllers/FinanceAdjustmentsController.cs');
const view = read('EduOS.App/Views/FinanceAdjustments/Index.cshtml');
const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
const fineClient = read('EduOS.App/wwwroot/js/finance/finance-fine-register.js');
const adjustmentClient = read('EduOS.App/wwwroot/js/finance/finance-adjustments.js');
const { groupFor, categoryFor } = require('../../EduOS.App/wwwroot/js/sidebar-navigation.js');
const { guideFor } = require('../../EduOS.App/wwwroot/js/platform/page-context-help.js');

test('finance adjustment tasks get separate tenant-admin/FINANCE pages without changing canonical forms', () => {
    assert.match(controller, /\[Authorize\(Roles="TenantAdmin"\)\]/);
    assert.match(controller, /\[RequireModule\("FINANCE"\)\]/);
    assert.match(controller, /Index\(\)=>View\(\)/, 'legacy Index remains for existing direct links');
    for (const [action, feature, category] of [
        ['Policies','policies','settings'],['StudentAdjustments','students','operations'],
        ['FineRegister','fines','reports'],['Refunds','refunds','operations']
    ]) {
        assert.match(controller, new RegExp(`IActionResult ${action}\\(\\)=>Feature\\("${feature}"\\)`));
        assert.match(layout, new RegExp(`Path: "/FinanceAdjustments/${action}"`));
        assert.ok(view.includes(`feature == "${feature}"`));
        assert.equal(groupFor('management', '/FinanceAdjustments/' + action), 'finance');
        assert.equal(categoryFor('management', 'finance', '/FinanceAdjustments/' + action), category);
        for (const locale of ['en-BD','bn-BD']) {
            const guide = guideFor('/FinanceAdjustments/' + action, locale);
            assert.ok(guide && guide.steps.length >= 3 && guide.example && guide.recovery);
        }
    }
    assert.match(view, /data-active-feature="@feature"/);
    for (const id of ['adjustDiscountRuleForm','adjustFineRuleForm','adjustStudentSearch',
        'adjustAssignForm','adjustApplyForm','fineRegisterRoot','adjustRefundRequestForm','adjustStudentFineRows'])
        assert.ok(view.includes(`id="${id}"`), 'existing secured form removed ' + id);
    for (const action of ['HostelClearances','TransportClearances','ReservationPreflight','DiscountAwards'])
        assert.match(layout, new RegExp(`Path: "/FinanceAdjustments/${action}"`));
    assert.doesNotMatch(view, /href="\/FinanceAdjustments\/HostelClearances"/);
    assert.doesNotMatch(view, /href="\/FeeOperations\/Index"/);
});

test('fine-register Inspect opens its student context on the dedicated route without opening write forms', () => {
    assert.match(view, /data-adjust-inspect-context/);
    assert.match(fineClient, /function inspect\(row\)/);
    assert.match(fineClient, /querySelectorAll\('\[data-adjust-inspect-context\]'\)\.forEach\(panel => panel\.classList\.remove\('d-none'\)\)/);
    assert.match(fineClient, /select\.dispatchEvent\(new Event\('change'/);
    assert.match(fineClient, /activeFeature !== 'all' && activeFeature !== 'fines'/);
    assert.match(adjustmentClient, /if\(activeFeature!=='policies'\)await loadRefunds\(\)/);
    assert.match(adjustmentClient, /if\(activeFeature!=='refunds'\)/);
    assert.match(view, /id="adjustApplyForm"/);
    assert.match(view, /id="adjustRefundSection"/);
});
