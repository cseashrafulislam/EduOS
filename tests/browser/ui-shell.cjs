'use strict';
// Production shared-shell JS against synthetic role-authorized navigation.
// Real authenticated staging and visual sign-off remain separate gates.
const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../..');
const navScript = path.join(root, 'EduOS.App/wwwroot/js/sidebar-navigation.js');
const shellScript = path.join(root, 'EduOS.App/wwwroot/js/platform/ui-shell.js');
const cssPath = path.join(root, 'EduOS.App/wwwroot/css/site.css');

function html(bn) {
    return '<!doctype html><html lang="' + (bn ? 'bn-BD' : 'en-BD') + '"><head><meta charset="utf-8"></head>' +
        '<body data-auth-tenant-id="22" data-auth-session-reference="' + 'A'.repeat(64) + '">' +
        '<aside id="sidebarMenu"><div class="menu-title">' + (bn ? 'একাডেমিক' : 'Academics') + '</div>' +
        '<nav data-sidebar-section="academics"><a class="nav-link" href="/Students/Index"><span>Students</span></a></nav>' +
        '<div class="menu-title">' + (bn ? 'ব্যবস্থাপনা' : 'Management') + '</div>' +
        '<nav data-sidebar-section="management"><a class="nav-link" href="/FeeOperations/Index">' +
        '<span>Fee collection</span></a></nav></aside>' +
        '<header><div class="page-heading"><nav id="pageBreadcrumbs" class="page-breadcrumbs" hidden></nav>' +
        '<h1>Fee collection</h1></div></header>' +
        '<main class="page-body"><section id="shellCard" class="edu-surface">' +
        '<div id="emptyState" class="edu-state-panel"><h2>No results</h2></div>' +
        '<span id="loadingPlaceholder" class="edu-skeleton"></span></section></main>' +
        '<div id="eduToastStack" class="edu-toast-stack"></div></body></html>';
}

async function exercise(browser, bn) {
    const context = await browser.newContext({
        viewport: bn ? { width: 390, height: 844 } : { width: 1280, height: 800 },
        reducedMotion: bn ? 'reduce' : 'no-preference'
    });
    const page = await context.newPage(), errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.route('http://eduos.test/**', route => route.fulfill({ contentType: 'text/html; charset=utf-8', body: html(bn) }));
    try {
        await page.goto('http://eduos.test/FeeOperations/Index');
        await page.addStyleTag({ path: cssPath });
        await page.addScriptTag({ path: navScript });
        await page.addScriptTag({ path: shellScript });
        assert.deepEqual(await page.locator('#pageBreadcrumbs li').allTextContents(), bn
            ? ['ব্যবস্থাপনা', 'ফি ও হিসাব', 'Fee collection']
            : ['Management', 'Fees & Accounting', 'Fee collection']);
        assert.equal(await page.locator('#pageBreadcrumbs li[aria-current="page"]').count(), 1);
        assert.equal(await page.locator('#pageBreadcrumbs').isHidden(), bn,
            'mobile uses the title without a cramped breadcrumb');
        assert.equal(await page.locator('#shellCard').evaluate(element => getComputedStyle(element).borderStyle), 'solid');
        if (bn) assert.equal(await page.locator('#loadingPlaceholder').evaluate(element =>
            getComputedStyle(element).animationName), 'none');
        await page.evaluate(() => { document.documentElement.dataset.workspaceTheme = 'dark'; });
        assert.equal(await page.locator('#shellCard').evaluate(element =>
            getComputedStyle(element).backgroundColor), 'rgb(23, 34, 53)');
        await page.evaluate(() => window.EduOSNotify('<img src=x onerror=alert(1)>', 'error'));
        assert.equal(await page.locator('#eduToastStack img').count(), 0, 'unsafe message must remain text');
        assert.match(await page.locator('#eduToastStack [role="alert"]').innerText(), /<img src=x onerror=alert\(1\)>/);
        await page.locator('#eduToastStack button').click();
        assert.equal(await page.locator('.edu-toast').count(), 0);
        await page.evaluate(() => {
            for (let i = 0; i < 5; i++) window.EduOSNotify('Status ' + i, 'warning');
        });
        assert.equal(await page.locator('.edu-toast').count(), 3);
        await page.evaluate(() => {
            document.body.dataset.authTenantId = '33';
            window.dispatchEvent(new Event('focus'));
            window.EduOSNotify('Stale tenant message', 'success');
        });
        assert.equal(await page.locator('.edu-toast').count(), 0, 'stale shell must not show tenant data');
        assert.deepEqual(errors, []);
    } finally { await context.close(); }
}

(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        await exercise(browser, false);
        await exercise(browser, true);
        console.log('Shared shell Chromium PASS: authorized breadcrumbs, bilingual desktop/mobile, safe toasts, tenant isolation.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
