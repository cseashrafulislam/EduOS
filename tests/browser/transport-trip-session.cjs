'use strict';
const assert = require('node:assert/strict');
const path = require('node:path');
const { chromium } = require('playwright');
const scripts = path.resolve(__dirname, '../../EduOS.App/wwwroot/js/transport-operations');
const trip = '11111111-2222-4333-8444-555555555555';
const code = '<img src=x onerror=alert(1)> STUDENT-PRIVATE';
const date = new Date().toISOString().slice(0, 10);
const btn = id => '<button id="' + id + '" type="button"></button>';
const inp = id => '<input type="date" id="' + id + '">';
const body = (role, lang) => {
    let inner = '';
    if (role === 'driver') inner =
        '<section id="driverTrips"><input name="__RequestVerificationToken" value="csrf">' +
        '<p id="driverTripStatus"></p>' +
        ['driverTripReload','driverTripPrev','driverTripNext','driverTripPrint','driverTripExport',
         'driverRiderPrev','driverRiderNext'].map(btn).join('') +
        '<span id="driverTripPage"></span><span id="driverRiderPage"></span>' +
        '<table><tbody id="driverTripRows"></tbody></table>' +
        '<section id="driverManifest" hidden><table><tbody id="driverRiderRows"></tbody></table></section></section>';
    else if (role === 'dispatcher') inner =
        '<section id="transportTrips" data-can-correct="true"><p id="tripStatus"></p>' +
        '<form id="tripPlanForm"><input name="__RequestVerificationToken" value="csrf">' +
        '<input id="tripDate" type="date" required><select id="tripRoute" required></select>' +
        '<select id="tripVehicle" required></select><select id="tripDirection">' +
        '<option value="Pickup">Pickup</option></select>' + btn('tripPlan') + '</form>' +
        '<input id="tripScheduleEnd" type="date">' +
        '<input id="tripScheduleWeekend" type="checkbox">' +
        '<input id="tripScheduleAcademicCalendar" type="checkbox">' +
        '<span id="tripCalendarStatus"></span><span id="tripPage"></span>' +
        '<span id="tripRiderPage"></span>' +
        ['tripSchedule','tripReload','tripPrev','tripNext','tripPrint','tripExport',
         'tripRiderPrev','tripRiderNext'].map(btn).join('') +
        '<table><tbody id="tripRows"></tbody></table><section id="tripManifestPanel" hidden>' +
        '<table><tbody id="tripManifestRows"></tbody></table></section>' +
        '<dialog id="tripCorrectionDialog"><form id="tripCorrectionForm">' +
        '<span id="tripCorrectionStudent"></span><span id="tripCorrectionState"></span>' +
        '<textarea id="tripCorrectionReason" required></textarea>' +
        btn('tripCorrectionCancel') + '<button type="submit">Save</button></form></dialog></section>';
    else inner = '<section id="transportFamilyPanel">' +
        '<form id="transportFamilyFilters">' + inp('transportFamilyDate') +
        '<button type="submit">Apply</button></form><p id="transportFamilyStatus"></p>' +
        '<span id="transportFamilyPage"></span><table><tbody id="transportFamilyRows"></tbody></table>' +
        ['transportFamilyExport','transportFamilyPrev','transportFamilyNext'].map(btn).join('') +
        '<form id="familyAttendanceFilters">' + inp('familyAttendanceDate') +
        '<button type="submit">Apply</button></form><p id="familyAttendanceStatus"></p>' +
        '<span id="familyAttendancePage"></span><table><tbody id="familyAttendanceRows"></tbody></table>' +
        ['familyAttendanceExport','familyAttendancePrev','familyAttendanceNext'].map(btn).join('') +
        '<dialog id="familyAttendanceHistoryDialog"><span id="familyAttendanceHistoryStudent"></span>' +
        '<ol id="familyAttendanceHistoryRows"></ol>' + btn('familyAttendanceHistoryClose') +
        '</dialog></section>';
    return '<!doctype html><html lang="' + lang + '"><head>' +
        '<meta name="request-verification-token" content="csrf"></head>' +
        '<body data-auth-session-reference="' + 'A'.repeat(64) +
        '" data-auth-tenant-id="101">' + inner + '</body></html>';
};
const ok = data => ({ contentType: 'application/json',
    body: JSON.stringify({ success: true, data }) });
async function scenario(browser, role, lang) {
    const page = await browser.newPage({ viewport: { width: 390, height: 800 },
        acceptDownloads: true });
    const errors = [], writes = [], downloads = [];
    let started, release;
    const pending = new Promise(resolve => { started = resolve; });
    const held = new Promise(resolve => { release = resolve; });
    page.on('pageerror', e => errors.push(e.message));
    page.on('dialog', d => d.accept());
    page.on('download', d => downloads.push(d.suggestedFilename()));
    await page.route('http://localhost/**', async route => {
        const req = route.request(), url = new URL(req.url());
        if (!url.pathname.startsWith('/api/'))
            return route.fulfill({ contentType: 'text/html', body: body(role, lang) });
        if (req.method() !== 'GET') {
            writes.push({ csrf: req.headers().requestverificationtoken, data: req.postDataJSON() });
            return route.fulfill(ok({ eventType: 'Boarded' }));
        }
        if (url.pathname.endsWith('/export')) {
            started();
            await held;
            try { await route.fulfill({ contentType: 'text/csv', body: 'private csv' }); }
            catch (e) { if (!/abort|cancel|closed|intercept|target/i.test(String(e))) throw e; }
            return;
        }
        if (url.pathname === '/api/transport/routes')
            return route.fulfill(ok([{ reference: trip, name: 'Private route' }]));
        if (url.pathname === '/api/transport/vehicles')
            return route.fulfill(ok([{ reference: trip, vehicleNumber: 'PRIVATE', capacity: 30 }]));
        if (url.pathname === '/api/transport/trips' || url.pathname === '/api/transport/driver-trips')
            return route.fulfill(ok({ items: [{ reference: trip, campusName: 'Private campus',
                routeName: 'Private route', vehicleNumber: 'PRIVATE', operatingDate: date,
                direction: 'Pickup', state: 'Dispatched', riderCount: 1 }], totalPages: 1 }));
        if (url.pathname.endsWith('/riders'))
            return route.fulfill(ok({ items: [{ studentCode: code, pickupStop: 'Secret pickup',
                dropStop: 'Secret drop', attendanceState: 'Awaiting' }], totalPages: 1 }));
        if (url.pathname === '/api/transport/family-schedule')
            return route.fulfill(ok({ items: [{ studentName: 'Private student', studentCode: code,
                campusName: 'Private campus', routeName: 'Private route', vehicleNumber: 'PRIVATE',
                pickupStop: 'Secret pickup', dropStop: 'Secret drop', startDate: date }], totalCount: 1 }));
        if (url.pathname === '/api/transport/family-attendance')
            return route.fulfill(ok({ items: [{ studentCode: code, routeName: 'Private route',
                operatingDate: date, direction: 'Pickup', attendanceState: 'Boarded',
                tripReference: trip, lastEventAt: date + 'T08:00:00' }], totalCount: 1 }));
        if (url.pathname.endsWith('/history'))
            return route.fulfill(ok([{ eventType: 'Boarded', occurredAt: date + 'T08:00:00Z' }]));
        return route.fulfill({ status: 404, body: 'Not found' });
    });
    try {
        await page.goto('http://localhost/transport/' + role);
        await page.addScriptTag({ path: path.join(scripts, 'session-guard.js') });
        await page.addScriptTag({ path: path.join(scripts, role === 'dispatcher' ?
            'trips.js' : role === 'driver' ? 'driver-trips.js' : 'family-schedule.js') });
        const table = role === 'dispatcher' ? '#tripRows' :
            role === 'driver' ? '#driverTripRows' : '#familyAttendanceRows';
        await page.locator(table + ' tr').first().waitFor({ timeout: 10000 })
            .catch(async error => {
                const diagnostic = {
                    role, language: lang, errors,
                    scheduleStatus: await page.locator('#transportFamilyStatus').count() ?
                        await page.locator('#transportFamilyStatus').innerText() : '',
                    attendanceStatus: await page.locator('#familyAttendanceStatus').count() ?
                        await page.locator('#familyAttendanceStatus').innerText() : '',
                    attendanceDate: await page.locator('#familyAttendanceDate').count() ?
                        await page.locator('#familyAttendanceDate').inputValue() : '',
                    rows: await page.locator(table).innerText()
                };
                throw new Error('Transport browser initial rows missing: ' +
                    JSON.stringify(diagnostic), { cause: error });
            });
        if (role === 'dispatcher') {
            await page.locator('#tripRows button').first().click();
            await page.locator('#tripManifestRows').getByText(code).waitFor();
            assert.equal(await page.locator('#tripManifestRows img').count(), 0);
            await page.locator('#tripExport').click();
        } else if (role === 'driver') {
            await page.locator('#driverTripRows button').first().click();
            await page.locator('#driverRiderRows').getByText(code).waitFor();
            assert.equal(await page.locator('#driverRiderRows img').count(), 0);
            await page.locator('#driverRiderRows button').first().click();
            await page.waitForFunction(() => document.getElementById('driverTripStatus')
                .textContent.includes('Attendance saved'));
            assert.equal(writes.length, 1);
            assert.equal(writes[0].csrf, 'csrf');
            assert.equal(writes[0].data.studentCode, code);
            await page.locator('#driverTripExport').click();
        } else {
            await page.locator('#familyAttendanceRows').getByText(code).waitFor();
            assert.equal(await page.locator('#familyAttendanceRows img').count(), 0);
            await page.locator('#familyAttendanceRows button').first().click();
            await page.waitForFunction(() =>
                document.getElementById('familyAttendanceHistoryDialog').open);
            await page.locator('#familyAttendanceHistoryClose').click();
            await page.locator('#familyAttendanceExport').click();
        }
        await pending;
        if (role === 'family') {
            await page.locator('#familyAttendanceRows button').first().click();
            await page.waitForFunction(() =>
                document.getElementById('familyAttendanceHistoryDialog').open);
        }
        await page.evaluate(() => { document.body.dataset.authTenantId = '202'; });
        const privateSelectors = role === 'dispatcher' ? ['#tripRows','#tripManifestRows'] :
            role === 'driver' ? ['#driverTripRows','#driverRiderRows'] :
            ['#transportFamilyRows','#familyAttendanceRows','#familyAttendanceHistoryRows'];
        await page.waitForFunction(selectors => selectors.every(selector =>
            document.querySelector(selector).children.length === 0), privateSelectors);
        release();
        await page.waitForTimeout(200);
        for (const selector of privateSelectors)
            assert.equal(await page.locator(selector).innerText(), '');
        assert.deepEqual(downloads, [], 'Old-tenant CSV must never download');
        assert.equal(writes.length, role === 'driver' ? 1 : 0);
        if (role === 'family')
            assert.equal(await page.locator('#familyAttendanceHistoryDialog').evaluate(el => el.open), false);
        assert.deepEqual(errors, []);
        console.log('Transport ' + role + ' ' + lang + ': mobile, CSRF, private DOM and export boundary PASS');
    } finally { release(); await page.close(); }
}
(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        for (const role of ['dispatcher','driver','family'])
            for (const lang of ['en-BD','bn-BD'])
                await scenario(browser, role, lang);
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
