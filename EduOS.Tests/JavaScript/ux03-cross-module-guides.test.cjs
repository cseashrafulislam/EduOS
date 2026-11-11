const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const { guideFor, coverage } = require('../../EduOS.App/wwwroot/js/platform/page-context-help.js');
const view = (controller, action) => fs.readFileSync(
    path.join(root, 'EduOS.App/Views', controller, action + '.cshtml'), 'utf8');
const controller = name => fs.readFileSync(
    path.join(root, 'EduOS.App/Controllers', name + 'Controller.cs'), 'utf8');
const routes = {
    TeacherStudio: ['Index', 'Public', 'Cpd', 'CpdReview', 'AdaptiveGoals'],
    StudentSupport: ['Counseling', 'Scholarships'],
    FinanceAdjustments: ['Index', 'HostelClearances', 'TransportClearances', 'ReservationPreflight', 'DiscountAwards']
};

test('all 12 source-reviewed UX-03 page actions have distinct complete bilingual Help Drawer guides', () => {
    const seen = new Set();
    for (const [name, actions] of Object.entries(routes)) {
        const source = controller(name);
        for (const action of actions) {
            const route = `/${name}/${action}`;
            assert.match(source, new RegExp(`public IActionResult ${action}\\(\\)`), route);
            assert.ok(view(name, action).length, route);
            const en = guideFor(route, 'en-BD');
            const bn = guideFor(route, 'bn-BD');
            for (const [lang, guide] of [['en', en], ['bn', bn]]) {
                assert.ok(guide, `${route} ${lang}`);
                assert.ok(guide.steps.length >= 2 && guide.before && guide.result && guide.rules && guide.recovery,
                    `${route} ${lang} required sections`);
                assert.doesNotMatch(JSON.stringify(guide), /<[^>]*>/, `${route} ${lang} must be plain text`);
            }
            assert.notEqual(en.title, bn.title, route);
            assert.ok(!seen.has(en.title), `${route}: unique guide title`);
            seen.add(en.title);
        }
    }
    const totals = coverage();
    assert.equal(totals.en, totals.bn);
    assert.ok(totals.en >= 153);
});

test('shared header owns the only Info control on Teacher Studio and award register pages', () => {
    for (const action of routes.TeacherStudio) {
        const markup = view('TeacherStudio', action);
        assert.doesNotMatch(markup, /<summary[^>]*>ⓘ<\/summary>/, action);
        assert.doesNotMatch(markup, /Page manual|Page guide|Administrator guide/, action);
    }
    const awards = view('FinanceAdjustments', 'DiscountAwards');
    assert.doesNotMatch(awards, /<summary[^>]*>ⓘ<\/summary>/);
});

test('server access gates remain authoritative for guides, financial and safeguarding workflows', () => {
    const studio = controller('TeacherStudio');
    assert.match(studio, /Authorize\(Roles\s*=\s*"TenantAdmin,Principal,Teacher"\)/);
    assert.match(studio, /RequireModule\("LMS"\)/);
    assert.match(studio, /Authorize\(Roles\s*=\s*"Teacher"\)/);
    assert.match(studio, /Authorize\(Roles\s*=\s*"TenantAdmin,Principal"\)/);
    const support = controller('StudentSupport');
    assert.match(support, /RequireModule\("STUDENT"\)/);
    assert.match(support, /HttpGet, RequireModule\("FINANCE"\)/);
    const finance = controller('FinanceAdjustments');
    assert.match(finance, /Authorize\(Roles\s*=\s*"TenantAdmin"\)/);
    assert.match(finance, /RequireModule\("FINANCE"\)/);
    assert.match(finance, /ResponseCache\(NoStore\s*=\s*true/g);
});

test('UX-03 bilingual finance page headers and CPD search actions have mobile wraps', () => {
    for (const action of ['HostelClearances', 'TransportClearances', 'ReservationPreflight', 'DiscountAwards']) {
        const markup = view('FinanceAdjustments', action);
        assert.match(markup, /CurrentUICulture\.Name\s*==\s*"bn-BD"/, action);
        assert.match(markup, /bn\s*\?/, action);
    }
    for (const action of ['Cpd', 'CpdReview'])
        assert.match(view('TeacherStudio', action), /d-flex gap-2 flex-wrap/, action);
    assert.match(view('FinanceAdjustments', 'DiscountAwards'), /align-items-center flex-wrap gap-2/);
});
