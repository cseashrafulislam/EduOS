const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

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

test('scoped Finance Razor renders one task and leaves unrelated forms unmounted', () => {
    assert.match(view, /@if \(feature is "structures" or "billing"\)/);
    assert.match(view, /@if \(feature == "structures"\)/);
    assert.match(view, /@if \(feature == "billing"\)/);
    assert.match(view, /@if \(feature == "ledger"\)/);
    assert.match(view, /@if \(feature == "structures" && User\.IsInRole\("TenantAdmin"\)\)/);
    assert.doesNotMatch(view, /feature == "structures" \? "" : "d-none"/);
    assert.doesNotMatch(view, /feature == "billing" \? "" : "d-none"/);
    assert.doesNotMatch(view, /feature == "ledger" \? "" : "d-none"/);
    for (const id of ['feeMonth','feeBillingYear','feeDueDate'])
        assert.match(view, new RegExp(`class="col-12 col-sm-4"[^>]*>[\\s\\S]*?id="${id}"`));
    assert.match(financeJs, /if\(activeFeature==='structures' && e\('feeStructureForm'\)\)/);
    assert.match(financeJs, /if\(activeFeature==='billing' && e\('feeInvoiceForm'\)\)/);
    assert.match(financeJs, /if\(activeFeature==='ledger' && e\('feeStudent'\)\)/);
});

test('Finance entry handlers and initial API calls stay within their own task scope', () => {
    const scopes = [
        ['structures', ['feeStructureForm','feeAddLine','feeStructureReset','feeCampus','feeYear','feeLevel','feeEffectiveFrom'],
            ['feeStructureForm','feeAddLine','feeStructureReset','feeCampus','feeYear','feeLevel'], 1],
        ['billing', ['feeInvoiceForm','feeBatch','feeInvoiceStructure','feeMonth','feeBillingYear','feeDueDate','feePreview','feePreviewCsv'],
            ['feeInvoiceForm','feeBatch','feeMonth','feeBillingYear','feeDueDate','feeInvoiceStructure','feePreview','feePreviewCsv'], 1],
        ['ledger', ['feeStudent','feeStudentSearch','feeFindStudent','feeLoadLedger','feePaymentForm','feeInvoicePrev','feeInvoiceNext','feeReceiptPrev','feeReceiptNext','feePaymentDate'],
            ['feeStudentSearch','feeStudent','feeFindStudent','feeLoadLedger','feePaymentForm','feeInvoicePrev','feeInvoiceNext','feeReceiptPrev','feeReceiptNext'], 0]
    ];
    for (const [feature, visible, bound, expectedQueries] of scopes) {
        const events = new Map(), requests = [];
        const nodes = new Map(visible.map(id => [id, {
            value: '', addEventListener(event) { events.set(id, [...(events.get(id) || []), event]); }
        }]));
        nodes.set('feeApp', { dataset: { activeFeature: feature } });
        const document = {
            documentElement: { lang: 'en-BD' },
            getElementById(id) { return nodes.get(id) || null; },
            querySelector() { return { content: 'test-antiforgery-token' }; }
        };
        vm.runInNewContext(financeJs, { document, fetch(url) { requests.push(url); return new Promise(() => {}); }, Date });
        assert.deepEqual([...events.keys()].sort(), [...bound].sort(), feature);
        assert.equal(requests.length, expectedQueries, `${feature}: unnecessary options fetch`);
        if (requests.length) assert.equal(requests[0], '/api/finance/fees/options');
    }
});

test('Finance task labels and async placeholder vocabulary follow the selected Bangla locale', () => {
    const source = read('EduOS.App/wwwroot/js/platform/task-workspace-locales.js');
    const node = { textContent: 'Fee structure', children: [], getAttribute() { return null; } };
    const rootElement = { querySelectorAll() { return [node]; } };
    const context = { document: { documentElement: { lang: 'bn-BD' }, querySelector() { return rootElement; } } };
    vm.runInNewContext(source, context);
    assert.equal(node.textContent, 'ফি কাঠামো');
    assert.equal(context.eduTaskStaticLabel('Choose active batch'), 'সক্রিয় ব্যাচ বাছুন');
    assert.equal(context.eduTaskStaticLabel('Monthly'), 'মাসিক');
    assert.equal(context.eduTaskStaticLabel('Example Campus Ltd.'), 'Example Campus Ltd.');
});
