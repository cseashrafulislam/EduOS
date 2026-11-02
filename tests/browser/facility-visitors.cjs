'use strict';
// Genuine Chromium + production Razor-derived markup/JS; synthetic API/session only.
// This is NOT authenticated SQL-backed browser or institutional UAT.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../..');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/FacilityVisitors/Index.cshtml'), 'utf8');
const script = path.join(root, 'EduOS.App/wwwroot/js/facilities/visitors.js');
const bootstrap = path.join(root, 'EduOS.App/wwwroot/lib/bootstrap/dist/css/bootstrap.min.css');
const ok = data => ({ success: true, data });
const visit = { id: 17, campusId: 12, hostUserId: 3, visitorName: 'Guest <img src=x onerror=alert(1)>',
    purpose: 'Meeting', stateCode: 'PendingHost', rowVersion: 'AQIDBA==',
    visitStartUtc: new Date(Date.now() + 3600000).toISOString(),
    visitEndUtc: new Date(Date.now() + 7200000).toISOString() };
function html(bn, manager) {
    const start = view.indexOf('<section class="container-fluid py-3" id="facilityVisitors"');
    const end = view.indexOf('@section Scripts', start);
    assert(start >= 0 && end > start, 'Visitor Razor markup changed');
    let h = view.slice(start, end);
    h = h.replace(/@\(\s*bn\s*\?\s*"([^"]*)"\s*:\s*"([^"]*)"\s*\)/g,
        (_, bd, en) => bn ? bd : en);
    h = h.replace(/@if \(manager\)\s*\{([\s\S]*?)\n    \}/g,
        (_, block) => manager ? block : '');
    h = h.replace(/@bn\.ToString\(\)\.ToLowerInvariant\(\)/g, String(bn));
    h = h.replace(/@manager\.ToString\(\)\.ToLowerInvariant\(\)/g, String(manager));
    h = h.replace(/@\(User\.FindFirst\([\s\S]*?\?\.Value\)/g, manager ? '2' : '3');
    h = h.replace('@Html.AntiForgeryToken()', '<input name="__RequestVerificationToken" value="visitor-csrf">');
    assert(!/@\(bn|@if \(manager\)|@Html\.AntiForgeryToken/.test(h), 'Unprocessed Razor');
    return '<!doctype html><html lang="' + (bn ? 'bn-BD' : 'en-BD') +
        '"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head><body>' +
        h + '</body></html>';
}
async function run(browser, bn, manager, mobile) {
    const context = await browser.newContext({ viewport: mobile ? { width: 390, height: 844 } : { width: 1280, height: 800 } });
    const page = await context.newPage(), calls = [], errors = [];
    page.on('pageerror', e => errors.push(e.message));
    await page.route('http://localhost/**', async route => {
        const req = route.request(), url = new URL(req.url());
        if (!url.pathname.startsWith('/api/'))
            return route.fulfill({ contentType: 'text/html', body: html(bn, manager) });
        calls.push({ url: url.pathname, method: req.method(),
            token: req.headers()['requestverificationtoken'], body: req.postDataJSON?.() });
        const respond = data => route.fulfill({ contentType: 'application/json', body: JSON.stringify(ok(data)) });
        if (url.pathname.endsWith('/hosts')) return respond({ items: [{ userId: 3, displayName: 'Staff Host' }] });
        if (req.method() === 'POST') return respond({ ...visit, stateCode: 'Approved' });
        return respond({ items: [visit], totalCount: 1, page: 1, pageSize: 50 });
    });
    try {
        await page.goto('http://localhost/FacilityVisitors/Index');
        await page.addStyleTag({ path: bootstrap });
        await page.addScriptTag({ path: script });
        await page.locator('#fvRows article').first().waitFor();
        assert.equal(await page.locator('#fvRows img').count(), 0, 'Visitor PII must never become markup');
        assert.match(await page.locator('#fvRows article').first().innerText(), /Guest <img/);
        assert.equal(await page.locator('#fvCreate').count(), manager ? 1 : 0);
        if (!manager) {
            await page.locator('#fvRows button').filter({ hasText: bn ? 'অনুমোদন' : 'Approve' }).click();
            assert.ok(calls.some(c => c.url.endsWith('/hostapprove') && c.method === 'POST' &&
                c.token === 'visitor-csrf' && c.body.clientCommandId));
        } else {
            await page.locator('#fvCampus').fill('12');
            await page.locator('#fvCampus').dispatchEvent('change');
            await page.locator('#fvHost option[value="3"]').waitFor({ state: 'attached' });
            await page.locator('#fvHost').selectOption('3');
            await page.locator('#fvName').fill('Visitor Demo');
            await page.locator('#fvPurpose').fill('Campus meeting');
            await page.locator('#fvStart').fill('2030-11-02T10:00');
            await page.locator('#fvEnd').fill('2030-11-02T11:00');
            await page.locator('#fvCreate button[type="submit"]').click();
            assert.ok(calls.some(c => c.url === '/api/facilities/visitors' && c.method === 'POST' &&
                c.token === 'visitor-csrf' && c.body.visitorName === 'Visitor Demo'));
        }
        assert.deepEqual(errors, []);
        if (mobile) assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1), true);
    } finally { await context.close(); }
}
(async () => {
    const browser = await chromium.launch({ headless: true });
    try {
        await run(browser, false, true, false);
        await run(browser, true, true, true);
        await run(browser, false, false, true);
        await run(browser, true, false, false);
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
