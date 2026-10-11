'use strict';
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../..');
const source = fs.readFileSync(path.join(root, 'EduOS.App/Views/MyStudentLeaves/Index.cshtml'), 'utf8');
const from = source.indexOf('<div class="container-fluid py-4" id="myStudentLeave"');
const to = source.indexOf('@section Scripts', from);
assert(from >= 0 && to > from, 'Student self-service leave markup missing');
const html = '<!doctype html><html><body>' + source.slice(from, to)
    .replace('@Html.AntiForgeryToken()',
        '<input name="__RequestVerificationToken" value="portal-csrf">') + '</body></html>';
const studentRef = '11111111-2222-4333-8444-555555555555';
const enrollmentRef = 'aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee';
const reply = (route, status, data, message = '') => route.fulfill({
    status, contentType: 'application/json',
    body: JSON.stringify({ success: status < 400, data, message })
});
async function main() {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        const page = await browser.newPage();
        const posts = [];
        await page.route('http://eduos.test/**', async route => {
            const req = route.request(), url = new URL(req.url());
            if (!url.pathname.startsWith('/api/'))
                return route.fulfill({ status: 200, contentType: 'text/html', body: html });
            if (url.pathname === '/api/portal/students')
                return reply(route, 200, [{
                    reference: studentRef, studentCode: 'STU-001', name: 'Linked Student'
                }]);
            if (url.pathname === '/api/academic-enrollments/students/' + studentRef + '/current')
                return reply(route, 200, { reference: enrollmentRef, rollNo: '11' });
            if (url.pathname === '/api/student-leaves' && req.method() === 'GET')
                return reply(route, 200, { items: posts.length < 2 ? [] : [{
                    id: 9, fromDate: '2026-10-10', toDate: '2026-10-11',
                    reason: 'Medical appointment', state: 2, reviewNote: null
                }], totalCount: posts.length < 2 ? 0 : 1, totalPages: 1 });
            if (url.pathname === '/api/student-leaves' && req.method() === 'POST') {
                assert.equal(req.headers()['requestverificationtoken'], 'portal-csrf');
                posts.push(req.postDataJSON());
                if (posts.length === 1) return reply(route, 503, null, 'Temporary error');
                return reply(route, 200, { id: 9 });
            }
            return reply(route, 404, null, 'Unexpected endpoint');
        });
        await page.goto('http://eduos.test/MyStudentLeaves/Index');
        await page.addScriptTag({ path: path.join(root, 'EduOS.App/wwwroot/js/student-portal/leaves.js') });
        await page.locator('#myLeaveSubmit:not([disabled])').waitFor();
        await page.locator('#myLeaveFrom').fill('2026-10-10');
        await page.locator('#myLeaveTo').fill('2026-10-11');
        await page.locator('#myLeaveReason').fill('Medical appointment');
        await page.locator('#myLeaveSubmit').click();
        await page.waitForFunction(() => document.querySelector('#myLeaveNotice')?.textContent.includes('Temporary error'));
        await page.locator('#myLeaveSubmit').click();
        await page.waitForFunction(() => document.querySelector('#myLeaveRows')?.textContent.includes('Medical appointment'));
        assert.equal(posts.length, 2);
        assert.equal(posts[0].clientRequestId, posts[1].clientRequestId);
        assert.equal(posts[1].studentEnrollmentReference, enrollmentRef);
        assert.match(await page.locator('#myLeaveRows').innerText(), /Submitted/);
        console.log('Student leave self-service browser: PASS — linked student, CSRF, retry idempotency and history.');
    } finally { await browser.close(); }
}
main().catch(e => { console.error(e); process.exitCode = 1; });
