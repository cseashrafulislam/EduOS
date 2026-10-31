// Independent whole-system QA: reviewed route/role and actual shared Help Drawer behavior.
// This is a DOM contract simulation, not authenticated browser/UAT certification.
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const source = read('EduOS.App/wwwroot/js/platform/page-context-help.js');
const { guideFor, coverage } = require('../../EduOS.App/wwwroot/js/platform/page-context-help.js');
const routes = [
    ['/Admissions/Index', 'Admissions', 'ADMISSION', 'TenantAdmin,AdmissionOfficer'],
    ['/StudentInvoicePosting/Index', 'StudentInvoicePosting', 'FINANCE', 'TenantAdmin,Principal,Accountant'],
    ['/FeeOperations/Index', 'FeeOperations', 'FINANCE', 'TenantAdmin,Principal,Accountant'],
    ['/Students/Index', 'Students', 'STUDENT', 'TenantAdmin,AdmissionOfficer'],
    ['/Attendance/Index', 'Attendance', 'ATTENDANCE', 'TenantAdmin,Principal,VicePrincipal,Teacher'],
    ['/PayrollOperations/Index', 'PayrollOperations', 'PAYROLL', 'TenantAdmin,Principal,HR,Accountant,Staff,Teacher'],
    ['/FacilityWorkOrders/Index', 'FacilityWorkOrders', 'FACILITIES', 'TenantAdmin,Principal,FacilitiesManager,FacilitiesApprover,FacilitiesTechnician,FacilitiesRequester']
];
function browser(route, lang, authorized = [], native = true) {
    const doc = { activeElement: null, documentElement: { lang } };
    function node(tag = 'div') {
        return {
            tag, children: [], handlers: {}, attrs: {}, hidden: false, open: false, textContent: '',
            append(...xs) { this.children.push(...xs); },
            replaceChildren(...xs) { this.children = xs; },
            setAttribute(k, v) { this.attrs[k] = String(v); },
            removeAttribute(k) { delete this.attrs[k]; },
            getAttribute(k) { return this.attrs[k] ?? (k === 'href' ? this.href ?? null : null); },
            addEventListener(k, fn) { (this.handlers[k] ||= []).push(fn); },
            fire(k, e = {}) { for (const fn of this.handlers[k] || []) fn({ target: this, preventDefault() {}, ...e }); },
            focus() { doc.activeElement = this; },
            querySelectorAll(selector) {
                assert.equal(selector, 'button, a[href]');
                const found = [];
                const visit = x => {
                    if (x.tag === 'button' || (x.tag === 'a' && x.getAttribute('href'))) found.push(x);
                    x.children.forEach(visit);
                };
                visit(this);
                return found;
            }
        };
    }
    const ids = ['pageContextHelp', 'pageHelpDrawer', 'pageHelpTitle', 'pageHelpContent', 'pageHelpClose'];
    const elements = Object.fromEntries(ids.map((id, i) => [id, node(i === 0 || i === 4 ? 'button' : 'div')]));
    const [trigger, dialog, title, content, close] = ids.map(id => elements[id]);
    if (native) {
        dialog.showModal = () => { dialog.open = true; };
        dialog.close = () => { dialog.open = false; dialog.fire('close'); };
    }
    doc.getElementById = id => elements[id] || null;
    doc.createElement = tag => node(tag);
    doc.querySelectorAll = selector => {
        assert.equal(selector, 'nav[data-sidebar-section] a.nav-link, a[data-feature-discovery-link]');
        return authorized.map(href => ({ getAttribute: k => k === 'href' ? href : null }));
    };
    vm.runInNewContext(source, { document: doc, window: { location: { pathname: route } }, module: { exports: {} } },
        { timeout: 1000 });
    const descendants = (element, tag) =>
        [...(element.tag === tag ? [element] : []), ...element.children.flatMap(x => descendants(x, tag))];
    return { doc, trigger, dialog, title, content, close, descendants };
}
test('reviewed Page Information routes match actual controller role/module gates in both locales', () => {
    const counts = coverage();
    assert.ok(counts.routes >= routes.length);
    assert.equal(counts.en, counts.routes);
    assert.equal(counts.bn, counts.routes);
    for (const [route, controller, module, expectedRoles] of routes) {
        const code = read('EduOS.App/Controllers/' + controller + 'Controller.cs');
        const match = code.match(/\[Authorize\(Roles\s*=\s*"([^"]+)"\)/);
        assert.ok(match, 'missing role gate ' + route);
        assert.deepEqual(match[1].split(',').sort(), expectedRoles.split(',').sort(), route);
        assert.ok(code.includes('RequireModule("' + module + '")'), 'missing module gate ' + route);
        assert.match(code, /public IActionResult Index\(\)\s*=>\s*View\(\)/);
        for (const lang of ['en-BD', 'bn-BD']) {
            const manual = guideFor(route, lang);
            assert.ok(manual && manual.steps.length >= 3 && manual.example && manual.recovery && manual.rules,
                route + ' ' + lang);
        }
    }
});
test('both locales render on demand, filter related links by authorized navigation and restore focus', () => {
    for (const lang of ['en-BD', 'bn-BD']) {
        const b = browser('/Students/Index', lang,
            ['/Admissions/Index', '/StudentLifecycle/Index?tenant=999', '//evil.example']);
        assert.equal(b.dialog.open, false);
        assert.equal(b.content.children.length, 0);
        b.trigger.fire('click');
        assert.equal(b.dialog.open, true);
        assert.equal(b.doc.activeElement, b.close);
        assert.equal(b.title.textContent, guideFor('/Students/Index', lang).title);
        assert.equal(b.descendants(b.content, 'section').length, 8);
        const links = b.descendants(b.content, 'a');
        assert.deepEqual(links.map(x => x.getAttribute('href')), ['/Admissions/Index']);
        assert.equal(links[0].textContent, guideFor('/Students/Index', lang).related[0].label);
        b.close.fire('click');
        assert.equal(b.dialog.open, false);
        assert.equal(b.doc.activeElement, b.trigger);
    }
});
test('unknown or unauthorized route does not inherit a private guide or related destinations', () => {
    for (const route of ['/Admin/Index', '/FacilityWorkOrders/Other', '//evil.example']) {
        const b = browser(route, 'en-BD', ['/Admissions/Index', '/Secret/Index']);
        b.trigger.fire('click');
        assert.equal(b.descendants(b.content, 'section').length, 0);
        assert.equal(b.descendants(b.content, 'a').length, 0);
        assert.match(b.descendants(b.content, 'p')[0].textContent, /not yet available/);
    }
});
test('mobile dialog fallback responds to tap and Escape, returning focus to header Info', () => {
    const b = browser('/FacilityWorkOrders/Index', 'bn-BD', [], false);
    b.trigger.fire('click');
    assert.equal(b.dialog.getAttribute('open'), '');
    assert.equal(b.doc.activeElement, b.close);
    let prevented = false;
    b.dialog.fire('keydown', { key: 'Escape', preventDefault() { prevented = true; } });
    assert.equal(prevented, true);
    assert.equal(b.dialog.getAttribute('open'), null);
    assert.equal(b.dialog.hidden, true);
    assert.equal(b.doc.activeElement, b.trigger);
});
test('language navigation has antiforgery, allowlisted cookie and safe redirect contracts', () => {
    const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
    const controller = read('EduOS.App/Controllers/LocalizationController.cs');
    const cookie = read('EduOS.App/Localization/PreferredCultureCookie.cs');
    assert.match(layout, /<html lang="@currentCulture"/);
    assert.match(layout, /asp-controller="Localization" asp-action="SetLanguage" method="post"/);
    assert.match(layout, /name="returnUrl" value="@returnUrl"/);
    assert.match(controller, /\[ValidateAntiForgeryToken\]/);
    assert.match(controller, /PreferredCultureCookie\.Write\(HttpContext, selectedCulture\)/);
    assert.match(controller, /Url\.IsLocalUrl\(returnUrl\)/);
    assert.match(cookie, /CookieRequestCultureProvider\.DefaultCookieName/);
    assert.match(cookie, /HttpOnly = true/);
    // Real tenant-switch locale persistence and screen-reader/mobile browser QA remain NOT RUN.
});

test('all reviewed route guides have complete distinct bilingual sections without raw keys', () => {
    for (const [route] of routes) {
        const en = guideFor(route, 'en-BD');
        const bn = guideFor(route, 'bn-BD');
        for (const key of ['title', 'purpose', 'before', 'example', 'result', 'rules', 'recovery']) {
            assert.ok(en[key].trim().length >= 10 && bn[key].trim().length >= 10, route + ' ' + key);
            assert.notEqual(en[key], bn[key], route + ' untranslated ' + key);
            assert.doesNotMatch(en[key] + bn[key], /\{\{[^}]+\}\}|\b(?:undefined|null)\b/i);
        }
        assert.equal(en.steps.length, bn.steps.length, route);
        assert.ok(en.steps.length >= 3, route);
        for (let i = 0; i < en.steps.length; i++) {
            assert.notEqual(en.steps[i], bn.steps[i], route + ' untranslated step ' + i);
            assert.ok(/\p{Script=Bengali}/u.test(bn.steps[i]), route + ' missing Bangla step ' + i);
        }
        assert.ok(/\p{Script=Bengali}/u.test(bn.example), route + ' missing Bangla example');
    }
});
test('new admission and invoice GL guides expose only navigation-authorized related routes', () => {
    for (const [route, allowed] of [
        ['/Admissions/Index', '/Students/Index'],
        ['/StudentInvoicePosting/Index', '/FeeOperations/Index']
    ]) {
        for (const lang of ['en-BD', 'bn-BD']) {
            const b = browser(route, lang, [allowed, '/PrivateFinance/Index', '//evil.example']);
            b.trigger.fire('click');
            assert.equal(b.dialog.open, true);
            assert.equal(b.descendants(b.content, 'section').length, 8);
            const links = b.descendants(b.content, 'a');
            assert.deepEqual(links.map(x => x.getAttribute('href')), [allowed]);
            assert.equal(links[0].textContent, guideFor(route, lang).related[0].label);
            b.close.fire('click');
            assert.equal(b.doc.activeElement, b.trigger);
        }
    }
});
