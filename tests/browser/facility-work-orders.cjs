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
    workOrderId: 17, workOrderState: 'Approved', workOrderRowVersion: 'AQIDBA==', rowVersion: 'AQIDBA==' };
const ok = data => ({ success: true, data });
const deferred = () => { let resolve; const promise = new Promise(r => { resolve = r; }); return { promise, resolve }; };
function html(bn, manager, approver = false) {
    const a = view.indexOf('<section class="container-fluid py-3" id="facilityWorkflow"');
    const b = view.indexOf('@section Scripts', a);
    assert(a >= 0 && b > a, 'Facility Work Orders Razor view contract changed');
    let h = view.slice(a, b);
    h = h.replace(/@\(\s*bn \? "([^"]*)" : "([^"]*)"\s*\)/g, (_, bd, en) => bn ? bd : en);
    h = h.replace(/@bn\.ToString\(\)\.ToLowerInvariant\(\)/g, String(bn));
    h = h.replace(/@manager\.ToString\(\)\.ToLowerInvariant\(\)/g, String(manager));
    // The synthetic fixture must honor Razor's manager-only vendor panel.
    h = h.replace(/@if \(manager\)\s*\{([\s\S]*?)\n\s*\}/g,
        (_, block) => manager ? block : '');
    h = h.replace(/@approver\.ToString\(\)\.ToLowerInvariant\(\)/g, String(approver));
    h = h.replace(/@technician\.ToString\(\)\.ToLowerInvariant\(\)/g, 'false');
    h = h.replace(/@\(\s*User\.FindFirst\([\s\S]*?\?\.Value\)/g, '3');
    assert(!/@\(bn \?|@manager\.|@technician\.|@approver\./.test(h), 'Unprocessed Razor expression');
    return '<!doctype html><html lang="' + (bn ? 'bn-BD' : 'en-BD') +
        '"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">' +
        '<meta name="request-verification-token" content="facility-csrf"></head>' +
        '<body data-auth-session-reference="' + 'A'.repeat(64) + '" data-auth-tenant-id="12">' +
        '<div id="eduosSessionShield" hidden></div>' + h + '</body></html>';
}
async function setup(browser, bn = false, manager = true, mobile = false,
    approver = false, visibleRecord = record) {
    const context = await browser.newContext({ viewport: mobile ? { width: 390, height: 844 } : { width: 1280, height: 800 } });
    const page = await context.newPage();
    const state = { calls: [], errors: [], delayed: null, started: null,
        revoked: false, emptyAfterPost: false, empty: false, locationDelayed: null, locationStarted: null,
        vendorDelayed: null, vendorStarted: null,
        visibleRecord };
    page.on('pageerror', error => state.errors.push(error.message));
    await page.addInitScript(() => {
        window.EduOSSessionTransitionPending = () => false;
        window.EduOSVerifySessionContext = async () => {
            document.getElementById('eduosSessionShield').hidden =
                document.body.dataset.authTenantId === '12';
        };
    });
    // localhost is a trustworthy origin: production uses HTTPS and commandId
    // requires secure-context crypto.randomUUID for real form submission.
    const scopeHeaders = { 'X-EduOS-Session-Reference': 'A'.repeat(64),
        'X-EduOS-Tenant-Id': '12' };
    await page.route('http://localhost/**', async route => {
        const request = route.request(), url = new URL(request.url());
        if (!url.pathname.startsWith('/api/'))
            return route.fulfill({ contentType: 'text/html', body: html(bn, manager, approver) });
        state.calls.push({ url: url.pathname + url.search, method: request.method(),
            body: request.method() === 'POST' ? request.postDataJSON() : null });
        assert(url.pathname.startsWith('/api/facilities/work-orders'), 'Unexpected endpoint ' + url.pathname);
        const reply = payload => route.fulfill({ contentType: 'application/json',
            headers: scopeHeaders, body: JSON.stringify(ok(payload)) });
        if (url.pathname.endsWith('/export'))
            return route.fulfill({ contentType: 'text/csv; charset=utf-8',
                headers: scopeHeaders, body: 'Request,Campus,Summary\\r\\n7,12,Broken light\\r\\n' });
        if (state.revoked) return route.fulfill({ status: 403, contentType: 'application/json',
            body: JSON.stringify({ success: false, message: 'Campus access revoked' }) });
        if (request.method() !== 'GET') {
            if (state.emptyAfterPost) state.empty = true;
            return reply(state.visibleRecord);
        }
        if (url.pathname.endsWith('/vendors')) {
            assert.equal(url.searchParams.get('campusId'), '12');
            assert.equal(url.searchParams.get('workOrderId'), '17');
            const search = url.searchParams.get('search');
            const from = Date.parse(url.searchParams.get('startUtc'));
            const to = Date.parse(url.searchParams.get('endUtc'));
            assert.ok(from > Date.now() && to > from, 'Vendor preview needs a future UTC window');
            if (state.vendorDelayed && search === 'old') {
                state.vendorStarted.resolve();
                await state.vendorDelayed.promise;
                return reply({ items: [{ supplierId: 999, supplierCode: 'OLD',
                    supplierName: 'Stale vendor' }], totalPages: 1 });
            }
            return reply({ items: search === 'none' ? [] : [
                { supplierId: 9, supplierCode: 'FAC-APP',
                    supplierName: 'Approved Facilities Vendor' } ], totalPages: 1 });
        }
        if (url.pathname.endsWith('/technicians')) {
            assert.equal(url.searchParams.get('workOrderId'), '17');
            assert.ok(url.searchParams.get('startUtc') && url.searchParams.get('endUtc'));
            return reply({ items: url.searchParams.get('startUtc').includes('T09:00')
                ? [] : [{ userId: 42, displayName: 'Technician Test' }], totalPages: 1 });
        }
        if (url.pathname.endsWith('/locations')) {
            if (state.locationDelayed && url.searchParams.get('search') === 'old') {
                state.locationStarted.resolve();
                await state.locationDelayed.promise;
                return reply({ items: [{ campusId: 99, facilitySpaceId: 999,
                    campusName: 'Old campus', campusCode: 'OLD',
                    spaceName: 'Old space', spaceCode: 'OLD' }], totalPages: 1 });
            }
            return reply({ items: [{ campusId: 12, facilitySpaceId: 5,
                campusName: 'Facilities Campus', campusCode: 'FAC',
                spaceName: 'Building A', spaceCode: 'BLDG-A' }], totalPages: 1 });
        }
        if (url.pathname.endsWith('/dashboard')) return reply({});
        if (url.pathname.endsWith('/history')) return reply({ items: [], totalPages: 1 });
        if (state.delayed && url.searchParams.get('campusId') === '12') {
            state.started.resolve();
            await state.delayed.promise;
        }
        return reply({ items: state.empty ? [] : [state.visibleRecord], totalPages: 1 });
    });
    await page.goto('http://localhost/FacilityWorkOrders/Index');
    await page.addStyleTag({ path: bootstrap });
    await page.addScriptTag({ path: script });
    await page.locator('#wfRows').filter({ hasText: 'Broken light' }).waitFor();
    return { context, page, state };
}
async function boundCsvAndStaleResponses(browser) {
    const valid = await setup(browser, false, true, false);
    try {
        const downloadPromise = valid.page.waitForEvent('download');
        await valid.page.locator('#facilityExport').click();
        const download = await downloadPromise;
        const csv = fs.readFileSync(await download.path(), 'utf8');
        assert.match(csv, /Request,Campus,Summary/);
        assert.equal(download.suggestedFilename(), 'facilities-work-orders.csv');
        assert.ok(valid.state.calls.some(c => c.url.startsWith('/api/facilities/work-orders/export?') &&
            c.method === 'GET'));
        assert.deepEqual(valid.state.errors, []);
    } finally { await valid.context.close(); }

    const staleJson = await setup(browser, true, true, true);
    try {
        await staleJson.page.route('http://localhost/api/facilities/work-orders?*', route =>
            route.fulfill({ status: 200, contentType: 'application/json',
                headers: { 'X-EduOS-Session-Reference': 'B'.repeat(64),
                    'X-EduOS-Tenant-Id': '12' },
                body: JSON.stringify(ok({ items: [record], totalPages: 1 })) }));
        await staleJson.page.locator('#workflowSearch button[type="submit"]').click();
        await staleJson.page.locator('#workflowAlert')
            .filter({ hasText: 'প্রতিষ্ঠানের সেশন পরিবর্তিত' }).waitFor();
        assert.equal(await staleJson.page.locator('#wfRows tr').count(), 0);
        assert.equal(await staleJson.page.locator('#facilityExport').isDisabled(), true);
        assert.deepEqual(staleJson.state.errors, []);
    } finally { await staleJson.context.close(); }

    const staleCsv = await setup(browser, false, true, false);
    try {
        let downloads = 0;
        staleCsv.page.on('download', () => { downloads++; });
        await staleCsv.page.route('http://localhost/api/facilities/work-orders/export?*', route =>
            route.fulfill({ status: 200, contentType: 'text/csv',
                headers: { 'X-EduOS-Session-Reference': 'A'.repeat(64),
                    'X-EduOS-Tenant-Id': '99' },
                body: 'Request,Campus,Summary\\r\\n7,99,Foreign work order\\r\\n' }));
        await staleCsv.page.locator('#facilityExport').click();
        await staleCsv.page.locator('#workflowAlert')
            .filter({ hasText: 'Institution session changed' }).waitFor();
        assert.equal(downloads, 0, 'stale cross-tenant CSV must never be released');
        assert.equal(await staleCsv.page.locator('#wfRows tr').count(), 0);
        assert.equal(await staleCsv.page.locator('#facilityExport').isDisabled(), true);
        assert.deepEqual(staleCsv.state.errors, []);
    } finally { await staleCsv.context.close(); }
}
async function managerAndSession(browser) {
    const { context, page, state } = await setup(browser);
    try {
        await page.locator('#wfRows button').click();
        assert.match(await page.locator('#facilityWorkflow header p').innerText(),
            /Start work only during the active technician schedule/);
        assert.match(await page.locator('#wfAction').innerText(), /Assign schedule/);
        await page.evaluate(() => { document.body.dataset.authTenantId = '99'; });
        // A concurrent history read may already lock the page before the click.
        // Dispatch explicitly to exercise the export handler even when disabled.
        await page.evaluate(() => document.getElementById('facilityExport')
            .dispatchEvent(new MouseEvent('click', { bubbles: true })));
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
        assert.match(await page.locator('#facilityWorkflow header p').innerText(),
            /নির্ধারিত টেকনিশিয়ান সময়ের মধ্যে/);
        assert.equal(await page.locator('#wfVendorPanel').count(), 0,
            'requester markup must not contain manager vendor discovery');
        await page.evaluate(() => { document.body.dataset.authTenantId = '99'; });
        await page.locator('#workflowSearch button[type="submit"]').click();
        await page.locator('#workflowAlert').filter({ hasText: 'প্রতিষ্ঠানের সেশন পরিবর্তিত' }).waitFor();
        assert.equal(await page.locator('#wfRows tr').count(), 0);
        assert.deepEqual(state.errors, []);
    } finally { await context.close(); }
}
async function vendorPreview(browser) {
    const { context, page, state } = await setup(browser, true, true, true);
    try {
        await page.locator('#wfRows button').click();
        await page.locator('#wfAction').selectOption('vendorpropose');
        assert.equal(await page.locator('#wfVendorPanel').isVisible(), true);
        assert.equal(await page.locator('#wfVendorSelected').inputValue(), '');
        const future = new Date(Date.now() + 24 * 60 * 60 * 1000);
        const later = new Date(future.getTime() + 60 * 60 * 1000);
        await page.locator('#wfStart').fill(future.toISOString().slice(0, 16));
        await page.locator('#wfEnd').fill(later.toISOString().slice(0, 16));
        await page.locator('#wfVendorRows').filter({ hasText: 'Approved Facilities Vendor' }).waitFor();
        assert.equal(await page.locator('#wfVendorRows li').count(), 1);
        assert.doesNotMatch(await page.locator('#wfVendorPanel').innerText(), /@example|TaxRegistration|Phone/);
        await page.locator('#wfVendorRows button').click();
        assert.equal(await page.locator('#wfVendorSelected').inputValue(), '9');
        const changedEnd = new Date(later.getTime() + 30 * 60 * 1000);
        await page.locator('#wfEnd').fill(changedEnd.toISOString().slice(0, 16));
        assert.equal(await page.locator('#wfVendorSelected').inputValue(), '',
            'editing the booking window must invalidate the selected vendor');
        await page.locator('#wfVendorRows button').click();
        assert.equal(await page.locator('#wfVendorSelected').inputValue(), '9');
        await page.locator('#wfNote').fill('Vendor booking requires independent campus approval.');
        const posted = page.waitForResponse(response =>
            response.request().method() === 'POST' &&
            response.url().endsWith('/vendorpropose'));
        await page.locator('#wfTransition button[type="submit"]').click();
        await posted;
        const proposals = state.calls.filter(c => c.method === 'POST' &&
            c.url.endsWith('/vendorpropose'));
        assert.equal(proposals.length, 1);
        assert.equal(proposals[0].body.vendorSupplierId, 9);
        assert.equal(proposals[0].body.technicianUserId, null);
        assert.ok(Date.parse(proposals[0].body.vendorStartUtc) > Date.now());
        assert.equal(state.calls.some(c => /inventory|finance/i.test(c.url)), false);
        await page.locator('#wfAction').selectOption('vendorpropose');
        await page.locator('#wfStart').fill(future.toISOString().slice(0, 16));
        await page.locator('#wfEnd').fill(later.toISOString().slice(0, 16));
        await page.locator('#wfVendorSearch').fill('none');
        await page.locator('#wfVendorFind').click();
        await page.locator('#wfVendorRows').filter({ hasText: 'উপলব্ধ অনুমোদিত সরবরাহকারী নেই' }).waitFor();
        assert.equal(state.calls.filter(c => c.method !== 'GET').length, 1,
            'the only write is a vendor proposal, never stock or Finance posting');
        await page.evaluate(() => { document.body.dataset.authTenantId = '99'; });
        await page.locator('#wfVendorFind').click();
        await page.locator('#workflowAlert').filter({ hasText: 'প্রতিষ্ঠানের সেশন পরিবর্তিত' }).waitFor();
        assert.equal(await page.locator('#wfVendorPanel').isVisible(), false);
        assert.deepEqual(state.errors, []);
    } finally { await context.close(); }
}
async function vendorStaleWindow(browser) {
    const { context, page, state } = await setup(browser);
    try {
        await page.locator('#wfRows button').click();
        await page.locator('#wfAction').selectOption('vendorpropose');
        const start = new Date(Date.now() + 86400000);
        const end = new Date(start.getTime() + 3600000);
        await page.locator('#wfStart').fill(start.toISOString().slice(0, 16));
        await page.locator('#wfEnd').fill(end.toISOString().slice(0, 16));
        await page.locator('#wfVendorRows button').waitFor();
        state.vendorDelayed = deferred();
        state.vendorStarted = deferred();
        await page.locator('#wfVendorSearch').fill('old');
        await page.locator('#wfVendorFind').click();
        await state.vendorStarted.promise;
        const staleResponse = page.waitForResponse(r =>
            r.url().includes('/vendors?') && r.url().includes('search=old'));
        await page.locator('#wfVendorSearch').fill('');
        const later = new Date(end.getTime() + 3600000);
        await page.locator('#wfEnd').fill(later.toISOString().slice(0, 16));
        await page.locator('#wfVendorRows button').waitFor();
        state.vendorDelayed.resolve();
        await staleResponse;
        const latestWindowLookup = state.calls.filter(c =>
            c.url.includes('/vendors?') && !c.url.includes('search=old')).at(-1);
        assert.ok(latestWindowLookup, 'current window must have a fresh vendor lookup');
        const actualEnd = new URL(latestWindowLookup.url, 'http://localhost').searchParams.get('endUtc');
        assert.ok(Date.parse(actualEnd) > end.getTime(), 'current preview must use the edited end time');
        assert.doesNotMatch(await page.locator('#wfVendorRows').innerText(), /Stale vendor/);
        assert.equal(await page.locator('#wfVendorSelected').inputValue(), '');
        assert.equal(state.calls.some(c => c.method === 'POST'), false);
        assert.deepEqual(state.errors, []);
    } finally { await context.close(); }
}
async function vendorIndependentApproval(browser) {
    const proposed = { ...record, vendorSupplierId: 9, vendorStateCode: 'Proposed',
        vendorProposedByUserId: 4, vendorStartUtc: new Date(Date.now() + 86400000).toISOString(),
        vendorEndUtc: new Date(Date.now() + 90000000).toISOString() };
    const { context, page, state } = await setup(browser, false, false, false, true, proposed);
    try {
        await page.locator('#wfRows button').click();
        assert.equal(await page.locator('#wfVendorPanel').count(), 0,
            'approver must not receive manager-only vendor discovery');
        await page.locator('#wfAction').selectOption('vendorapprove');
        await page.locator('#wfNote').fill('Independent approval after supplier and overlap checks.');
        const posted = page.waitForResponse(response =>
            response.request().method() === 'POST' &&
            response.url().endsWith('/vendorapprove'));
        await page.locator('#wfTransition button[type="submit"]').click();
        await posted;
        const approvals = state.calls.filter(c => c.method === 'POST' &&
            c.url.endsWith('/vendorapprove'));
        assert.equal(approvals.length, 1);
        assert.equal(approvals[0].body.vendorSupplierId, null);
        assert.equal(approvals[0].body.vendorStartUtc, null);
        assert.equal(approvals[0].body.vendorEndUtc, null);
        assert.equal(state.calls.some(c => /inventory|finance/i.test(c.url)), false);
        assert.deepEqual(state.errors, []);
    } finally { await context.close(); }

    const selfProposed = { ...proposed, vendorProposedByUserId: 3 };
    const own = await setup(browser, true, false, true, true, selfProposed);
    try {
        await own.page.locator('#wfRows button').click();
        assert.doesNotMatch(await own.page.locator('#wfAction').innerText(),
            /Approve vendor booking|সরবরাহকারী বুকিং অনুমোদন/,
            'the proposal maker must not receive an approval control');
        assert.deepEqual(own.state.errors, []);
    } finally { await own.context.close(); }
}
async function requesterLocationPicker(browser) {
    const { context, page, state } = await setup(browser, true, false, true);
    try {
        await page.locator('#wfLocationSelect option[value="12:5"]').waitFor({ state: 'attached' });
        assert.equal(await page.locator('#wfCreate button[type="submit"]').isDisabled(), true);
        state.locationDelayed = deferred();
        state.locationStarted = deferred();
        await page.locator('#wfLocationSearch').fill('old');
        await page.locator('#wfLocationFind').click();
        await state.locationStarted.promise;
        await page.locator('#wfLocationSearch').fill('Building');
        await page.locator('#wfLocationFind').click();
        await page.locator('#wfLocationSelect option[value="12:5"]').waitFor({ state: 'attached' });
        state.locationDelayed.resolve();
        await page.waitForTimeout(50);
        assert.equal(await page.locator('#wfLocationSelect option[value="99:999"]').count(), 0);
        await page.locator('#wfLocationSelect').selectOption('12:5');
        assert.equal(await page.locator('#wfCampus').inputValue(), '12');
        assert.equal(await page.locator('#wfSpace').inputValue(), '5');
        await page.locator('#wfCategory').fill('ELECTRICAL');
        await page.locator('#wfSummary').fill('Broken corridor light');
        await page.locator('#wfCreate button[type="submit"]').click();
        await page.locator('#workflowAlert').filter({ hasText: 'অনুরোধ জমা হয়েছে' }).waitFor();
        const posted = state.calls.find(x => x.method === 'POST');
        assert.ok(posted, 'requester POST must reach the existing workflow');
        assert.equal(posted.body.campusId, 12);
        assert.equal(posted.body.facilitySpaceId, 5);
        assert.ok(posted.body.clientRequestId, 'existing idempotency key preserved');
        assert.equal(await page.locator('#wfCreate button[type="submit"]').isDisabled(), true);
        assert.deepEqual(state.errors, []);
    } finally { state.locationDelayed?.resolve(); await context.close(); }
}
async function fillSchedule(page) {
    await page.locator('#wfRows button').click();
    // Empty history still renders an attached <ol>, but it has no visible box.
    await page.locator('#wfHistory ol').waitFor({ state: 'attached' });
    await page.locator('#wfAction').selectOption('schedule');
    await page.locator('#wfStart').fill('2026-11-03T10:00');
    await page.locator('#wfEnd').fill('2026-11-03T11:00');
    await page.locator('#wfTechSelect option[value="42"]').waitFor({ state: 'attached' });
    await page.locator('#wfTechSelect').selectOption('42');
    assert.equal(await page.locator('#wfTech').inputValue(), '42');
}
async function availabilityRecheck(browser) {
    const { context, page, state } = await setup(browser);
    try {
        await page.locator('#wfRows button').click();
        await page.locator('#wfAction').selectOption('schedule');
        assert.equal(state.calls.filter(x => x.url.includes('/technicians?')).length, 0);
        await page.locator('#wfStart').fill('2026-11-03T10:00');
        await page.locator('#wfEnd').fill('2026-11-03T11:00');
        await page.locator('#wfTechSelect option[value="42"]').waitFor({ state: 'attached' });
        await page.locator('#wfTechSelect').selectOption('42');
        await page.locator('#wfStart').fill('2026-11-03T09:00');
        await page.locator('#wfTechPage').filter({ hasText: 'no available technicians' }).waitFor();
        assert.equal(await page.locator('#wfTech').inputValue(), '');
        assert.equal(await page.locator('#wfTechSelect').isDisabled(), true);
        await page.locator('#wfTransition button[type="submit"]').click();
        assert.equal(state.calls.filter(x => x.method === 'POST').length, 0);
        await page.locator('#wfStart').fill('2026-11-03T10:00');
        await page.locator('#wfTechSelect option[value="42"]').waitFor({ state: 'attached' });
        await page.locator('#wfTechSelect').selectOption('42');
        await page.locator('#wfTransition button[type="submit"]').click();
        await page.locator('#workflowAlert').filter({ hasText: 'Transition recorded' }).waitFor();
        assert.equal(state.calls.filter(x => x.method === 'POST').length, 1);
        assert.deepEqual(state.errors, []);
    } finally { await context.close(); }
}
async function incompatibleWindows(browser) {
    // Real Chromium renders the bilingual page and executes production JS.
    // The API is synthetic; SQL-backed authoritative rejection is tested separately.
    const base = Date.now() + 48 * 60 * 60 * 1000;
    const at = hours => new Date(base + hours * 3600000).toISOString();
    const local = iso => iso.slice(0, 16);
    const scheduledRecord = { ...record, workOrderState: 'Scheduled',
        vendorStateCode: 'Released', scheduledStartUtc: at(1), scheduledEndUtc: at(2) };
    const first = await setup(browser, true, true, true, false, scheduledRecord);
    try {
        const { page, state } = first;
        await page.locator('#wfRows button').click();
        await page.locator('#wfAction').selectOption('vendorpropose');
        await page.locator('#wfStart').fill(local(at(4)));
        await page.locator('#wfEnd').fill(local(at(5)));
        await page.locator('#wfVendorPage')
            .filter({ hasText: 'সরবরাহকারী বুকিং টেকনিশিয়ানের সময়সূচির সঙ্গে মিলতে হবে' }).waitFor();
        const reads = state.calls.filter(c => c.url.includes('/vendors?')).length;
        await page.locator('#wfVendorFind').click();
        assert.equal(state.calls.filter(c => c.url.includes('/vendors?')).length, reads,
            'disjoint vendor window must not trigger an advisory lookup');
        assert.equal(await page.locator('#wfVendorSelected').inputValue(), '');
        await page.locator('#wfTransition button[type="submit"]').click();
        assert.equal(state.calls.filter(c => c.method === 'POST').length, 0);
        await page.locator('#wfStart').fill(local(at(1)));
        await page.locator('#wfEnd').fill(local(at(2)));
        await page.locator('#wfVendorRows button').waitFor();
        assert.deepEqual(state.errors, []);
    } finally { await first.context.close(); }

    const approvedRecord = { ...record, vendorStateCode: 'Approved',
        vendorSupplierId: 9, vendorStartUtc: at(1), vendorEndUtc: at(2) };
    const second = await setup(browser, false, true, false, false, approvedRecord);
    try {
        const { page, state } = second;
        await page.locator('#wfRows button').click();
        await page.locator('#wfAction').selectOption('schedule');
        await page.locator('#wfStart').fill(local(at(4)));
        await page.locator('#wfEnd').fill(local(at(5)));
        await page.locator('#wfTechPage')
            .filter({ hasText: 'Schedule must overlap the approved vendor booking' }).waitFor();
        const reads = state.calls.filter(c => c.url.includes('/technicians?')).length;
        await page.locator('#wfTechFind').click();
        assert.equal(state.calls.filter(c => c.url.includes('/technicians?')).length, reads);
        await page.locator('#wfTransition button[type="submit"]').click();
        assert.equal(state.calls.filter(c => c.method === 'POST').length, 0);
        await page.locator('#wfStart').fill(local(at(1)));
        await page.locator('#wfEnd').fill(local(at(2)));
        // <option> elements are not visible surfaces in Chromium; assert
        // attachment and the actual enabled picker instead of visibility.
        await page.locator('#wfTechSelect option[value="42"]').waitFor({ state: 'attached' });
        assert.equal(await page.locator('#wfTechSelect').isDisabled(), false);
        assert.deepEqual(state.errors, []);
    } finally { await second.context.close(); }
}
async function revokedCampus(browser) {
    const { context, page, state } = await setup(browser, true, true, true);
    try {
        await fillSchedule(page);
        state.revoked = true;
        await page.locator('#wfTransition button[type="submit"]').click();
        await page.locator('#workflowAlert')
            .filter({ hasText: 'প্রতিষ্ঠানের সেশন পরিবর্তিত' }).waitFor();
        assert.equal(await page.locator('#wfRows tr').count(), 0);
        assert.equal(await page.locator('#wfTransition').isVisible(), false);
        assert.equal(await page.locator('#facilityExport').isDisabled(), true);
        assert.equal(state.calls.filter(x => x.method === 'POST').length, 1);
        assert.deepEqual(state.errors, []);
    } finally { await context.close(); }
}
async function selectionAfterTransition(browser) {
    const { context, page, state } = await setup(browser);
    try {
        await fillSchedule(page);
        assert.equal(await page.locator('#wfTech').getAttribute('readonly'), '');
        state.emptyAfterPost = true;
        await page.locator('#wfTransition button[type="submit"]').click();
        await page.locator('#wfTransition').waitFor({ state: 'hidden' });
        assert.match(await page.locator('#wfSelected').innerText(), /Select a request/);
        assert.equal(await page.locator('#wfHistory').innerText(), '');
        assert.doesNotMatch(await page.locator('#wfRows').innerText(), /Broken light/);
        assert.equal(state.calls.filter(x => x.method === 'POST').length, 1);
        assert.deepEqual(state.errors, []);
    } finally { await context.close(); }
}
(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        await boundCsvAndStaleResponses(browser);
        await managerAndSession(browser);
        await delayedRead(browser);
        await mobileBangla(browser);
        await requesterLocationPicker(browser);
        await vendorPreview(browser);
        await vendorStaleWindow(browser);
        await vendorIndependentApproval(browser);
        await availabilityRecheck(browser);
        await incompatibleWindows(browser);
        await revokedCampus(browser);
        await selectionAfterTransition(browser);
        console.log('Facility Work Orders Chromium PASS: session-bound JSON/CSV and stale 200 denial, bilingual mobile/desktop, conflict-aware availability and vendor/technician window compatibility, authorized location picker, approved vendor proposal and independent approval, stale vendor window/search, revocation and stale selection; synthetic API/session only.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
