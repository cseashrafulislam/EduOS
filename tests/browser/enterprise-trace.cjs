'use strict';
// Actual production Enterprise Trace JavaScript in Chromium with Razor-derived markup.
// API/session are synthetic; this is NOT authenticated SQL-backed browser UAT.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const view = read('EduOS.App/Views/V3EnterpriseTrace/Index.cshtml');
const script = path.join(root, 'EduOS.App/wwwroot/js/inventory/v3-enterprise-trace.js');
const A = '11111111-2222-4333-8444-555555555555';
const B = 'aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee';
const deferred = () => {
    let resolve;
    const promise = new Promise(r => { resolve = r; });
    return { promise, resolve };
};
function html(bn) {
    const start = view.indexOf('<section class="container-fluid py-3" id="v3LedgerTrace"');
    const end = view.indexOf('@section Scripts', start);
    assert(start >= 0 && end > start, 'Enterprise Trace view contract changed');
    const body = view.slice(start, end)
        .replace(/@\(\s*bn \? "([^"]*)" : "([^"]*)"\s*\)/g,
            (_, bd, en) => bn ? bd : en)
        .replace(/@ViewData\["Title"\]/g, bn ? 'এন্টারপ্রাইজ লেজার ট্রেস' : 'Enterprise Ledger Trace')
        .replace(/@DateTime\.UtcNow\.AddDays\(-30\)\.ToString\("yyyy-MM-dd"\)/g, '2026-10-01')
        .replace(/@DateTime\.UtcNow\.ToString\("yyyy-MM-dd"\)/g, '2026-11-01');
    assert(!/@\(bn \?|@DateTime\.UtcNow/.test(body), 'Unprocessed Razor expression');
    return '<!doctype html><html lang="' + (bn ? 'bn-BD' : 'en-BD') +
        '"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>' +
        '<body data-auth-session-reference="' + 'A'.repeat(64) + '" data-auth-tenant-id="12">' +
        '<div id="eduosSessionShield" hidden></div>' + body + '</body></html>';
}
const detail = number => ({ success: true, data: {
    movementNumber: number, state: 'Posted', recordedMovementValue: 25,
    financeEvidenceStatus: 'ExplicitBalancedReference', journals: []
} });
const register = reference => ({ success: true, data: {
    kind: 'stock', page: 1, pageSize: 25, totalCount: 1,
    items: [{ reference, number: 'STOCK-' + reference.slice(0, 4),
        date: '2026-11-01', state: 'Posted', recordedValue: 25,
        lineCount: 1, evidenceStatus: 'ExplicitBalancedReference' }]
} });
async function setup(browser, bn = false, mobile = false) {
    const context = await browser.newContext({
        viewport: mobile ? { width: 390, height: 844 } : { width: 1280, height: 800 }
    });
    const page = await context.newPage();
    const state = { calls: [], errors: [], delayedDetail: null, detailStarted: null,
        delayedRegister: null, registerStarted: null, revoked: false, printCount: 0 };
    page.on('pageerror', e => state.errors.push(e.message));
    await page.addInitScript(() => {
        window.EduOSSessionTransitionPending = () => false;
        window.EduOSVerifySessionContext = async () => {
            document.getElementById('eduosSessionShield').hidden = true;
        };
        window.print = () => { window.__printed = (window.__printed || 0) + 1; };
    });
    await page.route('http://localhost/**', async route => {
        const req = route.request(), url = new URL(req.url());
        if (!url.pathname.startsWith('/api/'))
            return route.fulfill({ contentType: 'text/html', body: html(bn) });
        state.calls.push(url.pathname + url.search);
        if (url.pathname === '/api/institution-onboarding/campus-list')
            return route.fulfill({ contentType: 'application/json',
                body: JSON.stringify({ success: true, data: [{ id: 12, name: 'Main Campus' }] }) });
        if (state.revoked)
            return route.fulfill({ status: 403, contentType: 'application/json',
                body: JSON.stringify({ success: false, message: 'Access revoked' }) });
        if (url.pathname.endsWith('/register')) {
            if (state.delayedRegister) {
                state.registerStarted.resolve();
                await state.delayedRegister.promise;
            }
            return route.fulfill({ contentType: 'application/json', body: JSON.stringify(register(B)) })
                .catch(() => {});
        }
        if (url.pathname.endsWith('/' + A)) {
            if (state.delayedDetail) {
                state.detailStarted.resolve();
                await state.delayedDetail.promise;
            }
            return route.fulfill({ contentType: 'application/json', body: JSON.stringify(detail('MOV-A')) })
                .catch(() => {});
        }
        if (url.pathname.endsWith('/' + B))
            return route.fulfill({ contentType: 'application/json', body: JSON.stringify(detail('MOV-B')) });
        return route.fulfill({ status: 404, body: 'Unexpected endpoint ' + url.pathname });
    });
    await page.goto('http://localhost/V3EnterpriseTrace/Index');
    await page.addScriptTag({ path: script });
    return { context, page, state };
}
async function submit(page, reference) {
    await page.locator('#v3TraceReference').fill(reference);
    await page.evaluate(() => document.getElementById('v3TraceForm').requestSubmit());
}
async function staleDetail(browser) {
    const { context, page, state } = await setup(browser);
    try {
        state.delayedDetail = deferred();
        state.detailStarted = deferred();
        await submit(page, A);
        await state.detailStarted.promise;
        await submit(page, B);
        await page.locator('#v3TraceRows').getByText('MOV-B').waitFor();
        state.delayedDetail.resolve();
        await page.waitForTimeout(120);
        assert.doesNotMatch(await page.locator('#v3TraceRows').innerText(), /MOV-A/);
        assert.match(await page.locator('#v3TraceCsv').getAttribute('href'), new RegExp(B));
        assert.equal(await page.locator('#v3TraceResult').isVisible(), true);
        assert.deepEqual(state.errors, []);
    } finally { state.delayedDetail?.resolve(); await context.close(); }
}
async function tenantSwitchRegister(browser) {
    const { context, page, state } = await setup(browser);
    try {
        await submit(page, B);
        await page.locator('#v3TraceRows').getByText('MOV-B').waitFor();
        state.delayedRegister = deferred();
        state.registerStarted = deferred();
        await page.locator('#v3RegisterForm button[type="submit"]').click();
        await state.registerStarted.promise;
        await page.evaluate(() => { document.body.dataset.authTenantId = '99'; });
        state.delayedRegister.resolve();
        await page.locator('#v3TraceMessage').filter({ hasText: 'Institution session changed' }).waitFor();
        assert.equal(await page.locator('#v3TraceResult').isVisible(), false);
        assert.equal(await page.locator('#v3RegisterResult').isVisible(), false);
        assert.equal(await page.locator('#v3TraceRows tr').count(), 0);
        assert.equal(await page.locator('#v3RegisterRows tr').count(), 0);
        assert.equal(await page.locator('#v3TraceCsv').getAttribute('href'), null);
        assert.equal(await page.locator('#v3RegisterCsv').getAttribute('href'), null);
        assert.equal(await page.locator('#v3TraceForm button').isDisabled(), true);
        assert.deepEqual(state.errors, []);
    } finally { state.delayedRegister?.resolve(); await context.close(); }
}
async function revokedDetail(browser) {
    const { context, page, state } = await setup(browser);
    try {
        await submit(page, B);
        await page.locator('#v3TraceRows').getByText('MOV-B').waitFor();
        state.revoked = true;
        await submit(page, A);
        await page.locator('#v3TraceMessage').filter({ hasText: 'Institution session changed' }).waitFor();
        assert.equal(await page.locator('#v3TraceRows tr').count(), 0);
        assert.equal(await page.locator('#v3TraceCsv').getAttribute('href'), null);
        assert.deepEqual(state.errors, []);
    } finally { await context.close(); }
}
async function banglaMobile(browser) {
    const { context, page, state } = await setup(browser, true, true);
    try {
        await submit(page, 'bad');
        assert.match(await page.locator('#v3TraceMessage').innerText(), /সঠিক UUID/);
        await submit(page, B);
        await page.locator('#v3TraceRows').getByText('MOV-B').waitFor();
        assert.match(await page.locator('#v3TraceStatus').innerText(), /ফাইন্যান্স লিংক/);
        await page.evaluate(() => { document.body.dataset.authTenantId = '99'; });
        await page.locator('#v3TracePrint').click();
        await page.locator('#v3TraceMessage').filter({ hasText: 'প্রতিষ্ঠানের সেশন পরিবর্তিত' }).waitFor();
        assert.equal(await page.evaluate(() => window.__printed || 0), 0);
        assert.equal(await page.locator('#v3TraceResult').isVisible(), false);
        assert.deepEqual(state.errors, []);
    } finally { await context.close(); }
}
(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        await staleDetail(browser);
        await tenantSwitchRegister(browser);
        await revokedDetail(browser);
        await banglaMobile(browser);
        console.log('Enterprise Trace Chromium PASS: stale readback, revoked/tenant session, bilingual mobile; synthetic API only.');
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
