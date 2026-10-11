'use strict';
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../..');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/StudentLifecycle/Index.cshtml'), 'utf8');
const start = view.indexOf('<div id="studentLifecycle"');
const end = view.indexOf('@section Scripts', start);
assert(start >= 0 && end > start, 'StudentLifecycle view missing');
const html = '<!doctype html><html><body>' + view.slice(start, end)
    .replace('@Html.AntiForgeryToken()', '<input name="__RequestVerificationToken" value="exit-csrf">') +
    '</body></html>';
const reference = 'c2b391fa-7df2-45cd-a79b-965974cbb63e';
const state = { status: 'Active', history: [], posts: [] };
const respond = (route, status, data, message = '') => route.fulfill({
    status, contentType: 'application/json',
    body: JSON.stringify({ success: status < 400, data, message })
});

async function main() {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        const page = await browser.newPage();
        const pageErrors = [];
        page.on('pageerror', e => pageErrors.push(e.message));
        page.on('dialog', dialog => dialog.accept());
        await page.route('http://eduos.test/**', async route => {
            const req = route.request(), url = new URL(req.url());
            if (!url.pathname.startsWith('/api/'))
                return route.fulfill({ status: 200, contentType: 'text/html', body: html });
            if (url.pathname === '/api/students' && req.method() === 'GET')
                return respond(route, 200, { items: [{
                    reference, fullName: 'Student Transfer Test',
                    studentCode: 'STU-TC-001', status: state.status
                }] });
            if (url.pathname === '/api/students/' + reference)
                return respond(route, 200, { reference, rowVersion: 'AQIDBAUGBwg=',
                    fullName: 'Student Transfer Test', studentCode: 'STU-TC-001',
                    status: state.status, enrollments: [{ id: 5 }] });
            if (url.pathname === '/api/students/exit/' + reference + '/history')
                return respond(route, 200, state.history);
            if (url.pathname === '/api/students/exit' && req.method() === 'POST') {
                assert.equal(req.headers()['requestverificationtoken'], 'exit-csrf');
                const data = req.postDataJSON();
                state.posts.push(data);
                if (state.posts.length === 1) return respond(route, 503, null, 'Simulated transient failure');
                state.status = 'TC';
                const item = { reference: '3c1941f4-c58d-487b-877b-d4707a59f43f',
                    studentReference: reference, studentCode: 'STU-TC-001',
                    exitType: 'Transfer', finalStatus: 'TC', certificateNo: 'TC-TEST-001',
                    processedAtUtc: '2026-10-11T12:00:00Z', dueAtExit: 0 };
                state.history = [item];
                return respond(route, 201, item);
            }
            return respond(route, 404, null, 'Unexpected endpoint: ' + url.pathname);
        });
        await page.goto('http://eduos.test/StudentLifecycle/Index');
        await page.addScriptTag({ path: path.join(root, 'EduOS.App/wwwroot/js/students/lifecycle.js') });
        await page.locator('#lifecycleSearchText').fill('Student');
        await page.locator('#lifecycleStudentSearch button[type="submit"]').click();
        await page.locator('#lifecycleMatches button').first().click();
        await page.waitForFunction(() => document.querySelector('#lifecycleStatus')?.textContent === 'Active');
        await page.locator('#lifecycleReason').fill('Moving to another school');
        await page.locator('#lifecycleSubmit').click();
        await page.waitForFunction(() => document.querySelector('#lifecycleAlert')?.textContent.includes('Simulated transient failure'));
        assert.equal(state.posts.length, 1);
        assert.equal(await page.locator('#lifecycleSubmit').isDisabled(), false);
        await page.locator('#lifecycleSubmit').click();
        await page.waitForFunction(() => document.querySelector('#lifecycleHistory')?.textContent.includes('TC-TEST-001'));
        assert.equal(state.posts.length, 2);
        assert.equal(state.posts[0].clientRequestId, state.posts[1].clientRequestId,
            'Retry must keep original idempotency key');
        assert.equal(state.posts[0].studentRowVersion, 'AQIDBAUGBwg=');
        assert.equal(state.posts[1].exitType, 'Transfer');
        assert.equal(await page.locator('#lifecycleSubmit').isDisabled(), true,
            'Exited student cannot be processed again');
        assert.deepEqual(pageErrors, []);
        console.log('Student lifecycle browser: PASS — tenant admin selection, CSRF, TC history, retry and final-exit lockout.');
    } finally { await browser.close(); }
}
main().catch(e => { console.error(e); process.exitCode = 1; });
