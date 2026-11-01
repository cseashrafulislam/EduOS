'use strict';
// Genuine Chromium rendering with production work-orders.js and Razor-derived markup.
// API and session responses are synthetic: NOT authenticated server/SQL or institute UAT.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const view = read('EduOS.App/Views/FacilityWorkOrders/Index.cshtml');
const script = path.join(root, 'EduOS.App/wwwroot/js/facilities/work-orders.js');
const bootstrap = path.join(root, 'EduOS.App/wwwroot/lib/bootstrap/dist/css/bootstrap.min.css');
const record = { id: 7, campusId: 12, facilitySpaceId: 5, summary: 'Broken light',
    priorityCode: 'High', stateCode: 'Approved', requesterUserId: 1, triagedByUserId: 2,
    workOrderState: 'Approved', workOrderRowVersion: 'AQIDBA==', rowVersion: 'AQIDBA==' };
const ok = data => ({ success: true, data });
const deferred = () => { let resolve; const promise = new Promise(r => { resolve = r; }); return { promise, resolve }; };
function html(bn, manager) {
    const a = view.indexOf('<section class="container-fluid py-3" id="facilityWorkflow"');
    const b = view.indexOf('@section Scripts', a);
    assert(a >= 0 && b > a, 'Facility Work Orders Razor view contract changed');
    let h = view.slice(a, b);
    h = h.replace(/@\(\s*bn \? "([^"]*)" : "([^"]*)"\s*\)/g, (_, bd, en) => bn ? bd : en);
    h = h.replace(/@bn\.ToString\(\)\.ToLowerInvariant\(\)/g, String(bn));
    h = h.replace(/@manager\.ToString\(\)\.ToLowerInvariant\(\)/g, String(manager));
    h = h.replace(/@approver\.ToString\(\)\.ToLowerInvariant\(\)/g, 'false');
    h = h.replace(/@technician\.ToString\(\)\.ToLowerInvariant\(\)/g, 'false');
    h = h.replace(/@\(\s*User\.FindFirst\([\s\S]*?\?\.Value\)/g, '3');
    assert(!/@\(bn \?|@manager\.|@technician\.|@approver\./.test(h), 'Unprocessed Razor expression');
    return '<!doctype html><html lang="' + (bn ? 'bn-BD' : 'en-BD') +
        '"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">' +
        '<meta name="request-verification-token" content="facility-csrf"></head>' +
        '<body data-auth-session-reference="' + 'A'.repeat(64) + '" data-auth-tenant-id="12">' +
        '<div id="eduosSessionShield" hidden></div>' + h + '</body></html>';
}
async function setup(browser, bn = false, manager = true, mobile = false) {
    const context = await browser.newContext({ viewport: mobile ? { width: 390, height: 844 } : { width: 1280, height: 800 } });
    const page = await context.newPage();
    const state = { calls: [], errors: [], delayed: null, started: null };
    page.on('pageerror', error => state.errors.push(error.message));
    await page.addInitScript(() => {
        window.EduOSSessionTransitionPending = () => false;
        window.EduOSVerifySessionContext = async () => {
            document.getElementById('eduosSessionShield').hidden =
                document.body.dataset.authTenantId === '12';
        };
    });
    await page.route('http://eduos.test/**', async route => {
        const request = route.request(), url = new URL(request.url());
        if (!url.pathname.startsWith('/api/'))
            return route.fulfill({ contentType: 'text/html', body: html(bn, manager) });
        state.calls.push({ url: url.pathname + url.search, method: request.method() });
        assert(url.pathname.startsWith('/api/facilities/work-orders'), 'Unexpected endpoint ' + url.pathname);
        const reply = payload => route.fulfill({ contentType: 'application/json', body: JSON.stringify(ok(payload)) });
        if (request.method() !== 'GET') return reply(record);
        if (url.pathname.endsWith('/dashboard')) return reply({});
        if (url.pathname.endsWith('/history')) return reply({ items: [], totalPages: 1 });
        if (state.delayed && url.searchParams.get('campusId') === '12') {
            state.started.resolve();
            await state.delayed.promise;
        }
        return reply({ items: [record], totalPages: 1 });
    });
    await page.goto('http://eduos.test/FacilityWorkOrders/Index');
    await page.addStyleTag({ path: bootstrap });
    await page.addScriptTag({ path: script });
    await page.locator('#wfRows').filter({ hasText: 'Broken light' }).waitFor();
    return { context, page, state };
}
async function managerAndSession(browser) {
    const { context, page, state } = await setup(browser);
    try {
        await page.locator('#wfRows button').click();
        assert.match(await page.locator('#wfAction').innerText(), /Assign schedule/);
        await page.evaluate(() => { document.body.dataset.authTenantId = '99'; });
        await page.locator('#facilityExport').click();
        await page.locator('#workflowAlert').filter({ hasText: 'Institution session changed' }).waitFor();
        assert.equal(await page.locator('#wfRows tr').count(), 0);
        assert.equal(await page.locator('#wfCreate button[type="submit"]').isDisabled(), true);
        assert.equal(await page.locator('#facilityExport').isDisabled(), true);
        assert.equal(state.calls.filter(c => c.method !== 'GET').length, 0);
        assert.deepEqual(state.errors, []);
    } finally { await context.close(); }
}
async function delayedRead(browser) {
    const { context, page, state } = await setup(browser);
    try {
        state.delayed = deferred();
        state.started = deferred();
        await page.locator('#wfCampusFilter').fill('12');
        await page.locator('#workflowSearch button[type="submit"]').click();
        await state.started.promise;
        await page.evaluate(() => { document.body.dataset.authTenantId = '99'; });
        state.delayed.resolve();
        await page.locator('#workflowAlert').filter({ hasText: 'Institution session changed' }).waitFor();
        assert.equal(await page.locator('#wfRows tr').count(), 0);
        assert.equal(await page.locator('#wfTransition').isVisible(), false);
        assert.deepEqual(state.errors, []);
    } finally { state.delayed?.resolve(); await context.close(); }
}
async function mobileBangla(browser) {
    const { context, page, state } = await setup(browser, true, false, true);
    try {
        assert.match(await page.locator('h2').innerText(), /ক্যাম্পাস/);
        await page.evaluate(() => { document.body.dataset.authTenantId = '99'; });
        await page.locator('#workflowSearch button[type="submit"]').click();
        await page.locator('#workflowAlert').filter({ hasText: 'প্রতিষ্ঠানের সেশন পরিবর্তিত' }).waitFor();
        assert.equal(await page.locator('#wfRows tr').count(), 0);
        assert.deepEqual(state.errors, []);
    } finally { await context.close(); }
}
(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        await managerAndSession(browser);
        await delayedRead(browser);
        await mobileBangla(browser);
        console.log('Facility Work Orders Chromium PASS: bilingual mobile/desktop, session switch, export and stale readback; synthetic API/session only.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
