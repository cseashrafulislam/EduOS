'use strict';
// Real Chromium + production sidebar-navigation.js on synthetic Razor-like
// authorized link sets. Not authenticated, SQL-backed or institutional UAT.
const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('playwright');
const script = path.resolve(__dirname, '../../EduOS.App/wwwroot/js/sidebar-navigation.js');
const css = path.resolve(__dirname, '../../EduOS.App/wwwroot/css/site.css');
const referenceA = 'A'.repeat(64), referenceB = 'B'.repeat(64);
let tenant = '22', reference = referenceA, bangla = false;
function html() {
    return '<!doctype html><html lang="' + (bangla ? 'bn-BD' : 'en-BD') +
        '"><head><meta charset="utf-8"></head><body data-auth-tenant-id="' + tenant +
        '" data-auth-session-reference="' + reference + '">' +
        '<aside class="sidebar" id="sidebarMenu" aria-label="Main navigation">' +
        '<div class="menu-title">Main</div>' +
        '<nav data-sidebar-section="setup">' +
        '<a class="nav-link" href="/Dashboard/Index">Dashboard</a>' +
        '<a class="nav-link" href="/TenantUsers/Index">Users</a>' +
        '<a class="nav-link" href="/InstitutionManagement/Profile">Profile</a>' +
        '<a class="nav-link" href="/InstitutionManagement/Campuses">Campuses</a>' +
        '</nav><div class="menu-title">Academics</div><nav data-sidebar-section="academics">' +
        '<a class="nav-link" href="/CareerEmployers/Index">Employers</a>' +
        '<a class="nav-link" href="/CareerOpportunities/Index">Opportunities</a>' +
        '<a class="nav-link" href="/CareerPlacements/Index">Placements</a>' +
        '<a class="nav-link" href="/LearnerIdentities/AccessHistory">' +
            (bangla ? 'পরিচয় তথ্য দেখার ইতিহাস' : 'Identity Access History') + '</a>' +
        '</nav><div class="menu-title">Management</div><nav data-sidebar-section="management">' +
        '<a class="nav-link" href="/FeeReceivablesAging/Index">Fee Aging</a>' +
        '<a class="nav-link" href="/FacilityWorkOrders/Index">Work Orders</a>' +
        '<a class="nav-link" href="/FacilitySpaces/Index">Facility Spaces</a>' +
        '<a class="nav-link" href="/EmployeeAttendance/Index">' +
            (bangla ? 'কর্মীর উপস্থিতি' : 'Employee Attendance') + '</a>' +
        '<a class="nav-link" href="/LeaveEntitlements/Index">' +
            (bangla ? 'ছুটির প্রাপ্যতা' : 'Leave Entitlements') + '</a>' +
        '<a class="nav-link" href="/FeeDiscountRules/Index">' +
            (bangla ? 'ফি ছাড়ের নিয়ম' : 'Discount Rules') + '</a>' +
        '</nav></aside></body></html>';
}
async function inject(page) { await page.addScriptTag({ path: script }); }
async function exercise(browser, mobile) {
    tenant = '22'; reference = referenceA; bangla = mobile;
    const context = await browser.newContext({
        viewport: mobile ? { width: 390, height: 844 } : { width: 1280, height: 800 }
    });
    const page = await context.newPage(), errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.route('http://eduos.test/**', route =>
        route.fulfill({ contentType: 'text/html; charset=utf-8', body: html() }));
    try {
        await page.goto('http://eduos.test/Dashboard/Index');
        await page.addStyleTag({ path: css });
        await inject(page);
        const academicSection = page.locator('nav[data-sidebar-section="academics"]');
        const managementSection = page.locator('nav[data-sidebar-section="management"]');
        const academicToggle = page.locator('button.sidebar-section-trigger[aria-controls="sidebar-section-academics"]');
        const managementToggle = page.locator('button.sidebar-section-trigger[aria-controls="sidebar-section-management"]');
        assert.equal(await academicSection.evaluate(el => el.hidden), true,
            'Dashboard starts with the Academics section closed');
        assert.equal(await managementSection.evaluate(el => el.hidden), true,
            'Dashboard starts with the Management section closed');
        assert.equal(await academicToggle.getAttribute('aria-expanded'), 'false');
        await academicToggle.focus();
        await page.keyboard.press('Enter');
        assert.equal(await academicSection.evaluate(el => el.hidden), false,
            'keyboard opens the Academics section');
        assert.equal(await managementSection.evaluate(el => el.hidden), true);
        await page.waitForFunction(() => JSON.parse(
            sessionStorage.getItem('eduos.sidebar.navigation.v1') || '{}').section === 'academics');
        await page.goto('http://eduos.test/Dashboard/Index');
        await page.addStyleTag({ path: css });
        await inject(page);
        assert.equal(await academicSection.evaluate(el => el.hidden), true,
            'returning to Dashboard must not restore a previously expanded section');
        assert.equal(await managementSection.evaluate(el => el.hidden), true);
        await managementToggle.click();
        assert.equal(await academicSection.evaluate(el => el.hidden), true,
            'only one expanded section remains visible');
        assert.equal(await managementSection.evaluate(el => el.hidden), false);
        assert.equal(await managementToggle.getAttribute('aria-expanded'), 'true');
        await page.goto('http://eduos.test/Dashboard/Index');
        await page.addStyleTag({ path: css });
        await inject(page);
        assert.equal(await managementSection.evaluate(el => el.hidden), true);
        if (!mobile) {
            await page.evaluate(() => {
                document.documentElement.dataset.workspaceNavigation = 'top';
                window.dispatchEvent(new Event('eduos:workspace-preferences'));
            });
            assert.equal(await academicSection.evaluate(el => el.hidden), false,
                'top navigation must expose every authorized section');
            assert.equal(await managementSection.evaluate(el => el.hidden), false);
            await page.evaluate(() => {
                document.documentElement.dataset.workspaceNavigation = 'left';
                window.dispatchEvent(new Event('eduos:workspace-preferences'));
            });
            assert.equal(await academicSection.evaluate(el => el.hidden), true);
            assert.equal(await managementSection.evaluate(el => el.hidden), true);
        }
        const hrefs = await page.locator('nav a.nav-link').evaluateAll(links =>
            links.map(link => link.getAttribute('href')));
        assert.deepEqual(hrefs.sort(), [
            '/Dashboard/Index', '/TenantUsers/Index',
            '/InstitutionManagement/Profile', '/InstitutionManagement/Campuses',
            '/FeeReceivablesAging/Index', '/FacilityWorkOrders/Index',
            '/FacilitySpaces/Index', '/CareerEmployers/Index',
            '/CareerOpportunities/Index', '/CareerPlacements/Index',
            '/LearnerIdentities/AccessHistory', '/EmployeeAttendance/Index',
            '/LeaveEntitlements/Index', '/FeeDiscountRules/Index'
        ].sort(), 'client grouping must not add/duplicate privileged routes');
        const access = page.locator('summary.sidebar-group-toggle').filter({
            hasText: mobile ? 'ব্যবহারকারী ও অনুমতি' : 'Users & Access'
        });
        const institution = page.locator('summary.sidebar-group-toggle').filter({
            hasText: mobile ? 'প্রতিষ্ঠান ও সেটিংস' : 'Institution & Settings'
        });
        assert.equal(await access.count(), 0,
            'a singleton Users route stays directly discoverable without a pointless accordion');
        assert.equal(await institution.count(), 1);
        assert.equal(await page.locator('a.nav-link[href="/TenantUsers/Index"] .sidebar-group-icon').count(), 1,
            'one-destination module has the same visual icon as grouped modules');
        assert.equal(await page.locator('.sidebar-submenu a.nav-link').first().evaluate(el =>
            getComputedStyle(el.querySelector('.edu-icon') || el, '::before').content), '""',
        'nested routes have a uniform visual marker');
        const career = page.locator('summary.sidebar-group-toggle').filter({
            hasText: mobile ? 'ক্যারিয়ার ও কর্মসংস্থান' : 'Career & Placement'
        });
        const campus = page.locator('summary.sidebar-group-toggle').filter({
            hasText: mobile ? 'ক্যাম্পাস পরিচালনা' : 'Campus Operations'
        });
        assert.equal(await career.count(), 1, 'career links must be discoverable in their task group');
        assert.equal(await campus.count(), 1, 'facilities must be grouped with campus operations');
        assert.deepEqual(await career.evaluate(el => [...el.parentElement.querySelectorAll('a.nav-link')]
            .map(a => a.getAttribute('href')).sort()),
            ['/CareerEmployers/Index', '/CareerOpportunities/Index', '/CareerPlacements/Index'].sort());
        assert.deepEqual(await campus.evaluate(el => [...el.parentElement.querySelectorAll('a.nav-link')]
            .map(a => a.getAttribute('href')).sort()),
            ['/FacilityWorkOrders/Index', '/FacilitySpaces/Index'].sort());
        assert.equal(await page.locator('details.sidebar-group[data-sidebar-group="academics:other"]').count(), 0);
        assert.equal(await page.locator('details.sidebar-group[data-sidebar-group="management:other"]').count(), 0);
        assert.equal(await page.locator('.sidebar-group-count,.sidebar-category-count').count(), 0,
            'static menu quantities must not appear as notification badges');
        assert.equal(await page.locator('details.sidebar-group[data-sidebar-group="setup:access"] details.sidebar-category').count(), 0,
            'single-category groups should expose authorized links directly');
        assert.equal(await page.locator('details.sidebar-group[data-sidebar-group="academics:career"] details.sidebar-category').count(), 2,
            'career employers and placements remain in distinct, meaningful settings/operations categories');
        for (const [route, label] of [
            ['/LearnerIdentities/AccessHistory', mobile ? 'পরিচয় তথ্য দেখার ইতিহাস' : 'Identity Access History'],
            ['/EmployeeAttendance/Index', mobile ? 'কর্মীর উপস্থিতি' : 'Employee Attendance'],
            ['/LeaveEntitlements/Index', mobile ? 'ছুটির প্রাপ্যতা' : 'Leave Entitlements'],
            ['/FeeDiscountRules/Index', mobile ? 'ফি ছাড়ের নিয়ম' : 'Discount Rules']
        ]) {
            assert.equal(await page.locator('a.nav-link[href="' + route + '"]').textContent(), label);
        }
        assert.equal(await page.locator('details.sidebar-group[data-sidebar-group="academics:learners"]').count(), 0,
            'one authorized learner history route remains a direct link, not an empty accordion');
        assert.equal(await page.locator('a.nav-link[href="/LearnerIdentities/AccessHistory"] .sidebar-group-icon').count(), 1,
            'singleton learner route retains a recognizable module icon');
        assert.equal(await page.locator('details.sidebar-group[data-sidebar-group="management:people"]').count(), 1);
        assert.equal(await page.locator('details.sidebar-group[data-sidebar-group="management:finance"]').count(), 1);
        await page.locator('a.nav-link[href="/TenantUsers/Index"]').focus();
        await page.keyboard.press('Home');
        assert.equal(await page.evaluate(() => document.activeElement?.getAttribute('href')),
            '/Dashboard/Index');
        await page.keyboard.press('ArrowDown');
        assert.equal(await page.evaluate(() => document.activeElement?.textContent.includes(
            document.documentElement.lang === 'bn-BD' ? 'ব্যবহারকারী' : 'Users')), true);
        await page.keyboard.press('End');
        assert.equal(await page.evaluate(() => document.activeElement?.tagName), 'SUMMARY');
        await institution.click();
        assert.equal(await institution.evaluate(el => el.parentElement.open), true);
        await page.waitForFunction(() => JSON.parse(
            sessionStorage.getItem('eduos.sidebar.navigation.v1') || '{}').group === 'setup:institution');
        await page.evaluate(() => window.dispatchEvent(new Event('pagehide')));
        const saved = await page.evaluate(() => JSON.parse(
            sessionStorage.getItem('eduos.sidebar.navigation.v1')));
        assert.equal(saved.scope, '22:' + referenceA);
        assert.equal(saved.group, 'setup:institution');
        await page.goto('http://eduos.test/Dashboard/Index');
        await inject(page);
        assert.equal(await institution.evaluate(el => el.parentElement.open), true,
            'same tenant and session restores navigation state');
        // A different tenant must not restore the previous institution menu.
        tenant = '23';
        await page.goto('http://eduos.test/Dashboard/Index');
        await inject(page);
        assert.equal(await institution.evaluate(el => el.parentElement.open), false);
        await institution.click();
        await page.waitForFunction(() => JSON.parse(
            sessionStorage.getItem('eduos.sidebar.navigation.v1') || '{}').scope === '23:' + 'A'.repeat(64));
        await page.evaluate(() => window.dispatchEvent(new Event('pagehide')));
        assert.equal((await page.evaluate(() => JSON.parse(
            sessionStorage.getItem('eduos.sidebar.navigation.v1')))).scope,
        '23:' + referenceA);
        // Same tenant, different authenticated session also starts fresh.
        reference = referenceB;
        await page.goto('http://eduos.test/Dashboard/Index');
        await inject(page);
        assert.equal(await institution.evaluate(el => el.parentElement.open), false);
        await page.goto('http://eduos.test/CareerEmployers/Index');
        await inject(page);
        assert.equal(await academicSection.evaluate(el => el.hidden), false,
            'a deep link must reveal its own academic section');
        assert.equal(await managementSection.evaluate(el => el.hidden), true);
        // A fresh session may save its own default state; a later mid-page
        // tenant change must not overwrite that state with stale shell events.
        const beforeMismatch = await page.evaluate(() =>
            sessionStorage.getItem('eduos.sidebar.navigation.v1'));
        await page.evaluate(() => {
            document.body.dataset.authTenantId = '99';
            window.dispatchEvent(new Event('pagehide'));
        });
        assert.equal(await page.evaluate(() =>
            sessionStorage.getItem('eduos.sidebar.navigation.v1')), beforeMismatch);
        assert.deepEqual(errors, []);
    } finally { await context.close(); }
}
(async () => {
    const browser = await chromium.launch({
        headless: true,
        args: ['--no-sandbox'],
        ...(process.env.EDUOS_CHROMIUM_EXECUTABLE ? { executablePath: process.env.EDUOS_CHROMIUM_EXECUTABLE } : {})
    });
    try {
        await exercise(browser, false);
        await exercise(browser, true);
        console.log('Sidebar Chromium PASS: real grouped script, en-BD desktop/bn-BD mobile, keyboard, authorized links, tenant/session-scoped state.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
