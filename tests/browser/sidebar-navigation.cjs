'use strict';
// Real Chromium + production sidebar-navigation.js on synthetic Razor-like
// authorized link sets. Not authenticated, SQL-backed or institutional UAT.
const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('playwright');
const script = path.resolve(__dirname, '../../EduOS.App/wwwroot/js/sidebar-navigation.js');
const referenceA = 'A'.repeat(64), referenceB = 'B'.repeat(64);
let tenant = '22', reference = referenceA, bangla = false;
function html() {
    return '<!doctype html><html lang="' + (bangla ? 'bn-BD' : 'en-BD') +
        '"><head><meta charset="utf-8"></head><body data-auth-tenant-id="' + tenant +
        '" data-auth-session-reference="' + reference + '">' +
        '<aside id="sidebarMenu" aria-label="Main navigation">' +
        '<nav data-sidebar-section="setup">' +
        '<a class="nav-link" href="/Dashboard/Index">Dashboard</a>' +
        '<a class="nav-link" href="/TenantUsers/Index">Users</a>' +
        '<a class="nav-link" href="/InstitutionManagement/Profile">Profile</a>' +
        '<a class="nav-link" href="/InstitutionManagement/Campuses">Campuses</a>' +
        '</nav><nav data-sidebar-section="management">' +
        '<a class="nav-link" href="/FeeReceivablesAging/Index">Fee Aging</a>' +
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
        await inject(page);
        const hrefs = await page.locator('nav a.nav-link').evaluateAll(links =>
            links.map(link => link.getAttribute('href')));
        assert.deepEqual(hrefs.sort(), [
            '/Dashboard/Index', '/TenantUsers/Index',
            '/InstitutionManagement/Profile', '/InstitutionManagement/Campuses',
            '/FeeReceivablesAging/Index'
        ].sort(), 'client grouping must not add/duplicate privileged routes');
        const access = page.locator('summary.sidebar-group-toggle').filter({
            hasText: mobile ? 'ব্যবহারকারী ও অনুমতি' : 'Users & Access'
        });
        const institution = page.locator('summary.sidebar-group-toggle').filter({
            hasText: mobile ? 'প্রতিষ্ঠান ও সেটিংস' : 'Institution & Settings'
        });
        assert.equal(await access.count(), 1);
        assert.equal(await institution.count(), 1);
        await access.focus();
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
        // Mid-page scope change must not overwrite state for another session.
        await page.evaluate(() => {
            document.body.dataset.authTenantId = '99';
            window.dispatchEvent(new Event('pagehide'));
        });
        assert.equal((await page.evaluate(() => JSON.parse(
            sessionStorage.getItem('eduos.sidebar.navigation.v1')))).scope,
        '23:' + referenceA);
        assert.deepEqual(errors, []);
    } finally { await context.close(); }
}
(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        await exercise(browser, false);
        await exercise(browser, true);
        console.log('Sidebar Chromium PASS: real grouped script, en-BD desktop/bn-BD mobile, keyboard, authorized links, tenant/session-scoped state.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
