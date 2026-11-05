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
        h + '<div id="eduosSessionShield" hidden></div></body></html>';
}
async function run(browser, bn, manager, mobile) {
    const context = await browser.newContext({ viewport: mobile ? { width: 390, height: 844 } : { width: 1280, height: 800 } });
    const page = await context.newPage(), calls = [], errors = [];
    const reference = 'A'.repeat(64), tenant = '12';
    const scopeHeaders = { 'X-EduOS-Session-Reference': reference, 'X-EduOS-Tenant-Id': tenant };
    page.on('pageerror', e => errors.push(e.message));
    await page.route('http://localhost/**', async route => {
        const req = route.request(), url = new URL(req.url());
        if (!url.pathname.startsWith('/api/'))
            return route.fulfill({ contentType: 'text/html', body: html(bn, manager) });
        calls.push({ url: url.pathname, method: req.method(),
            token: req.headers()['requestverificationtoken'], body: req.postDataJSON?.() });
        const respond = data => route.fulfill({ contentType: 'application/json',
            headers: scopeHeaders, body: JSON.stringify(ok(data)) });
        if (url.pathname.endsWith('/export'))
            return route.fulfill({ contentType: 'text/csv; charset=utf-8', headers: scopeHeaders,
                body: 'VisitId,CampusId,State,VisitStartUtc,VisitEndUtc,CheckedInUtc,CheckedOutUtc,IncidentCount\r\n17,12,CheckedIn,,,,,1\r\n' });
        if (url.pathname.endsWith('/occupancy')) return respond({
            campusId: 12, checkedInCount: 1, overdueCount: 1, maxConcurrent: 2,
            asOfUtc: new Date().toISOString()
        });
        if (url.pathname.endsWith('/hosts')) return respond({ items: [{ userId: 3, displayName: 'Staff Host' }] });
        if (url.pathname.endsWith('/history')) return respond({ items: [{ actionCode: 'incident',
            occurredAtUtc: new Date().toISOString(), reason: manager ? 'Observed badge handoff.' : null }] });
        if (req.method() === 'POST') return respond({ ...visit, stateCode: 'Approved' });
        return respond({ items: [{ ...visit, stateCode: manager ? 'CheckedIn' : 'PendingHost' }], totalCount: 1, page: 1, pageSize: 50 });
    });
    try {
        await page.goto('http://localhost/FacilityVisitors/Index');
        assert.equal(await page.locator('#fvOccupancy').count(), manager ? 1 : 0,
            'Manager-only occupancy form must not appear for a host');
        await page.evaluate(() => {
            document.body.dataset.authTenantId = '12';
            document.body.dataset.authSessionReference = 'A'.repeat(64);
            window.EduOSVerifySessionContext = async () => {};
            window.EduOSSessionTransitionPending = () => false;
        });
        await page.addStyleTag({ path: bootstrap });
        await page.addScriptTag({ path: script });
        await page.locator('#fvRows article').first().waitFor();
        assert.equal(await page.locator('#fvRows img').count(), 0, 'Visitor PII must never become markup');
        assert.match(await page.locator('#fvRows article').first().innerText(), /Guest <img/);
        assert.equal(await page.locator('#fvCreate').count(), manager ? 1 : 0);
        if (!manager) {
            assert.equal(await page.locator('#fvExport').count(), 0);
            const hostPost = page.waitForResponse(response => response.request().method() === 'POST' &&
                new URL(response.url()).pathname.endsWith('/hostapprove'));
            await page.locator('#fvRows button').filter({ hasText: bn ? 'অনুমোদন' : 'Approve' }).click();
            await hostPost;
            assert.ok(calls.some(c => c.url.endsWith('/hostapprove') && c.method === 'POST' &&
                c.token === 'visitor-csrf' && c.body.clientCommandId));
        } else {
            await page.locator('#fvCampus').fill('12');
            // Blur fires the real change event once; do not double-dispatch.
            await page.locator('#fvCampus').press('Tab');
            await page.locator('#fvHost option[value="3"]').waitFor({ state: 'attached' });
            await page.locator('#fvHost').selectOption('3');
            await page.locator('#fvName').fill('Visitor Demo');
            await page.locator('#fvPurpose').fill('Campus meeting');
            await page.locator('#fvStart').fill('2030-11-02T10:00');
            await page.locator('#fvEnd').fill('2030-11-02T11:00');
            const invalid = await page.locator('#fvCreate').evaluate(form =>
                Array.from(form.elements).filter(el => el.willValidate && !el.checkValidity())
                    .map(el => ({ id: el.id, value: el.value, message: el.validationMessage })));
            assert.deepEqual(invalid, [], 'Preregistration fixture must satisfy native form validation');
            const createPost = page.waitForResponse(response => response.request().method() === 'POST' &&
                new URL(response.url()).pathname === '/api/facilities/visitors', { timeout: 8000 });
            await page.locator('#fvCreate button[type="submit"]').click();
            await createPost.catch(async error => {
                const state = await page.locator('#fvStatus').innerText();
                throw new Error('Preregistration POST absent: ' + state +
                    '; page errors=' + JSON.stringify(errors) + '; calls=' + JSON.stringify(calls) +
                    '; original=' + error.message);
            });
            assert.ok(calls.some(c => c.url === '/api/facilities/visitors' && c.method === 'POST' &&
                c.token === 'visitor-csrf' && c.body.visitorName === 'Visitor Demo'));
            await page.locator('#fvOccupancyCampus').fill('12');
            const occupancyResponse = page.waitForResponse(response =>
                new URL(response.url()).pathname.endsWith('/occupancy'));
            await page.locator('#fvOccupancy button[type="submit"]').click();
            await occupancyResponse;
            // HTTP completion is not a render barrier; wait for the real async UI update.
            await page.waitForFunction(() => /1\s*\/\s*2/.test(
                document.getElementById('fvOccupancyResult')?.textContent || ''));
            assert.match(await page.locator('#fvOccupancyResult').innerText(), /1\s*\/\s*2/);
            assert.match(await page.locator('#fvOccupancyResult').innerText(),
                bn ? /নির্ধারিত সময় পার:\s*1/ : /Overdue:\s*1/);
            assert.doesNotMatch(await page.locator('#fvOccupancyResult').innerText(),
                /Guest|GUEST-01|Staff Host/, 'Occupancy must remain identity-free');
            assert.ok(calls.some(c => c.url.endsWith('/occupancy') && c.method === 'GET'));
            await page.locator('#fvExportCampus').fill('12');
            await page.locator('#fvExportFrom').fill('2026-11-01T00:00');
            await page.locator('#fvExportTo').fill('2026-11-02T00:00');
            const downloadEvent = page.waitForEvent('download');
            await page.locator('#fvExportButton').click();
            const download = await downloadEvent;
            const csv = fs.readFileSync(await download.path(), 'utf8');
            assert.match(csv, /VisitId,CampusId,State/);
            assert.doesNotMatch(csv, /Guest|Meeting|GUEST-01|badge handoff/);
            assert.ok(calls.some(c => c.url.endsWith('/export') && c.method === 'POST' &&
                c.token === 'visitor-csrf' && c.body.campusId === 12 &&
                c.body.fromUtc && c.body.toUtc));
            await page.locator('#fvReason').fill('Observed badge handoff.');
            const incidentPost = page.waitForResponse(response => response.request().method() === 'POST' &&
                new URL(response.url()).pathname.endsWith('/incident'));
            await page.locator('#fvRows button').filter({ hasText: bn ? 'ঘটনা নথিভুক্ত' : 'Report incident' }).first().click();
            await incidentPost;
            assert.ok(calls.some(c => c.url.endsWith('/incident') && c.token === 'visitor-csrf' &&
                c.body.reason === 'Observed badge handoff.' && c.body.clientCommandId));
            await page.locator('#fvRows button').filter({ hasText: bn ? 'ইতিহাস' : 'History' }).first().click();
            await page.locator('[data-visitor-history]').first().getByText('Observed badge handoff.', { exact: false }).waitFor();
        }
        // A stale successful 200 is just as unsafe as a revoked 403.
        // Cover both cross-tenant and rotated-cookie response races in Chromium.
        const staleSession = manager && bn;
        const staleTenant = !manager && !bn;
        await page.route('http://localhost/api/facilities/visitors?*', route =>
            staleSession || staleTenant
                ? route.fulfill({ status: 200, contentType: 'application/json',
                    headers: { 'X-EduOS-Session-Reference': staleSession ? 'B'.repeat(64) : reference,
                        'X-EduOS-Tenant-Id': staleTenant ? '99' : tenant },
                    body: JSON.stringify(ok({ items: [visit], totalCount: 1, page: 1, pageSize: 50 })) })
                : route.fulfill({ status: 403, contentType: 'application/json',
                    body: JSON.stringify({ success: false, message: 'Access revoked' }) }));
        // A pending response may already revoke access before this synthetic click.
        // Programmatic click still exercises the denied path on a hidden control.
        await page.locator('#fvRefresh').evaluate(button => button.click());
        await page.waitForFunction(() => document.querySelectorAll('#fvRows article').length === 0);
        assert.equal(await page.locator('#fvRows article').count(), 0);
        assert.match(await page.locator('#fvStatus').innerText(),
            bn ? /ভিজিটর প্রবেশাধিকার বাতিল/ : /Visitor access revoked/);
        assert.equal(await page.locator('#fvRefresh').isVisible(), false);
        if (manager) assert.equal(await page.locator('#fvCreate').isVisible(), false);
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
