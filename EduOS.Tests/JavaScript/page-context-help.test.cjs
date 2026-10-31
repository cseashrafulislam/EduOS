const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
const css = read('EduOS.App/wwwroot/css/site.css');
const js = read('EduOS.App/wwwroot/js/platform/page-context-help.js');
const { guideFor, coverage, normalizeRoute, validateGuide } =
    require('../../EduOS.App/wwwroot/js/platform/page-context-help.js');
const routes = ['/FeeOperations/Index', '/Students/Index', '/Attendance/Index', '/PayrollOperations/Index', '/FacilityWorkOrders/Index'];

test('all reviewed route manuals have full bilingual structure, matching steps and examples', () => {
    assert.deepEqual(coverage(), { routes: 5, en: 5, bn: 5 });
    for (const route of routes) {
        const en = guideFor(route, 'en-BD');
        const bn = guideFor(route, 'bn-BD');
        assert.ok(en && bn, route);
        assert.equal(en.version, bn.version);
        assert.equal(en.steps.length, bn.steps.length);
        assert.ok(en.steps.length >= 3);
        for (const field of ['title','purpose','before','example','result','rules','recovery']) {
            assert.ok(en[field].trim() && bn[field].trim(), route + ':' + field);
            assert.doesNotMatch(en[field] + bn[field], /<[^>]*>/);
        }
        assert.deepEqual(en.related.map(x => x.path), bn.related.map(x => x.path));
        assert.notEqual(en.purpose, bn.purpose);
    }
});
test('unverified and malicious routes cannot inherit a generic or privileged manual', () => {
    for (const route of ['/Admin/Index', '/FeeOperations/Other', '/FeeOperations/Index?tenant=2',
        '//evil.example', '/PayrollOperations/Index#private', '/']) {
        assert.equal(guideFor(route, 'en-BD'), null, route);
    }
    assert.equal(normalizeRoute('/Students'), '/students/index');
    assert.equal(normalizeRoute('/Students/Index'), '/students/index');
    assert.equal(validateGuide({ reviewed: false, version: 1 }), false);
});
test('shared drawer is one user-initiated accessible renderer, not hover-triggered popover', () => {
    assert.match(layout, /id="pageContextHelp"[^>]+type="button"/);
    assert.match(layout, /aria-controls="pageHelpDrawer"/);
    assert.match(layout, /<dialog class="page-help-drawer" id="pageHelpDrawer" aria-labelledby="pageHelpTitle" aria-modal="true"/);
    assert.match(layout, /id="pageHelpContent"/);
    assert.match(layout, /page-context-help\.js" asp-append-version="true"/);
    assert.match(js, /dialog\.showModal\(\)/);
    assert.match(js, /trigger\.addEventListener\('click'/);
    assert.match(js, /dialog\.addEventListener\('close', \(\) => trigger\.focus\(\)\)/);
    assert.doesNotMatch(js, /mouseenter|setTimeout|innerHTML|eval\(|fetch\(|localStorage/);
    assert.match(js, /document\.querySelectorAll\('nav\[data-sidebar-section\] a\.nav-link, a\[data-feature-discovery-link\]'\)/);
    assert.match(js, /normalizeRoute\(href\) === target/);
    assert.match(css, /\.page-help-drawer::backdrop/);
    assert.match(css, /max-width: 575\.98px/);
    assert.match(css, /data-workspace-theme="dark"\] \.page-help-drawer/);
    assert.match(css, /prefers-color-scheme: dark/);
});
test('the pilot removes duplicate header Info controls but preserves workflow operations', () => {
    const fees = read('EduOS.App/Views/FeeOperations/Index.cshtml');
    const students = read('EduOS.App/Views/Students/Index.cshtml');
    const attendance = read('EduOS.App/Views/Attendance/Index.cshtml');
    const payroll = read('EduOS.App/Views/PayrollOperations/Index.cshtml');
    assert.doesNotMatch(fees, /aria-label="Fee billing guide"/);
    assert.doesNotMatch(students, /id="studentDirectoryHelp"/);
    assert.doesNotMatch(attendance, /aria-label="Attendance help"/);
    for (const id of ['feePreview','feeGenerate','feeCollect']) assert.match(fees, new RegExp('id="' + id + '"'));
    for (const id of ['studentSearch','studentExportPage']) assert.match(students, new RegExp('id="' + id + '"'));
    for (const id of ['saveAttendance','finalizeAttendance']) assert.match(attendance, new RegExp('id="' + id + '"'));
    assert.match(payroll, /id="payrollGenerate"/);
    assert.match(layout, /data-language-selector/);
});


test('shared localization catalogue has exact en/bn key parity and pilot Razor keys resolve', () => {
    const en = read('EduOS.App/Resources/Localization/SharedResource.resx');
    const bn = read('EduOS.App/Resources/Localization/SharedResource.bn-BD.resx');
    const keys = xml => new Set([...xml.matchAll(/<data\s+name="([^"]+)"/g)].map(x => x[1]));
    const enKeys = keys(en), bnKeys = keys(bn);
    assert.ok(enKeys.size >= 800, 'unexpectedly small resource catalogue');
    assert.deepEqual([...enKeys].sort(), [...bnKeys].sort(), 'raw translation keys would leak in one language');
    for (const page of ['EduOS.App/Views/Shared/_Layout.cshtml',
        'EduOS.App/Views/Students/Index.cshtml', 'EduOS.App/Views/Admissions/Index.cshtml']) {
        for (const match of read(page).matchAll(/@T\["([^"]+)"\]/g))
            assert.ok(enKeys.has(match[1]), page + ': ' + match[1]);
    }
});
test('language switch posts antiforgery and returns to the current local route', () => {
    const controller = read('EduOS.App/Controllers/LocalizationController.cs');
    const cookie = read('EduOS.App/Localization/PreferredCultureCookie.cs');
    assert.match(layout, /<html lang="@currentCulture"/);
    assert.match(layout, /<form asp-controller="Localization" asp-action="SetLanguage" method="post"/);
    assert.match(layout, /name="returnUrl" value="@returnUrl"/);
    assert.match(layout, /value="en-BD"/);
    assert.match(layout, /value="bn-BD"/);
    assert.match(controller, /\[ValidateAntiForgeryToken\]/);
    assert.match(controller, /PreferredCultureCookie\.TryNormalize/);
    assert.match(controller, /PreferredCultureCookie\.Write/);
    assert.match(controller, /Url\.IsLocalUrl\(returnUrl\)/);
    assert.match(cookie, /bn-BD/);
    assert.match(cookie, /en-BD/);
    assert.match(js, /document\.documentElement\.lang/);
});

test('facilities manual follows actual independent return, correction and non-posting contract', () => {
    const en = guideFor('/FacilityWorkOrders/Index', 'en-BD');
    const bn = guideFor('/FacilityWorkOrders/Index', 'bn-BD');
    assert.ok(en && bn);
    assert.match(en.steps.join(' '), /Return for correction/);
    assert.match(bn.steps.join(' '), /সংশোধনের জন্য ফেরত/);
    assert.match(en.rules, /does not issue spare parts or post Finance GL/);
    assert.match(en.recovery, /409/);
    assert.deepEqual(en.related.map(x => x.path), ['/FacilitySpaces/Index']);
});
