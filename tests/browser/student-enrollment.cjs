'use strict';
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../..');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/EnrollmentManagement/Index.cshtml'), 'utf8');
const start = view.indexOf('<div id="enrollmentManagement"');
const end = view.indexOf('@section Scripts', start);
assert(start >= 0 && end > start, 'Enrollment view not found');
const html = '<!doctype html><html><body>' + view.slice(start, end)
    .replace('@Html.AntiForgeryToken()',
        '<input name="__RequestVerificationToken" value="enrollment-browser-csrf">') + '</body></html>';
async function run() {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        const page = await browser.newPage();
        const jsErrors = [];
        page.on('pageerror', error => jsErrors.push(error.message));
        const posts = [];
        await page.route('http://eduos.test/**', async route => {
            const request = route.request();
            const url = new URL(request.url());
            if (!url.pathname.startsWith('/api/'))
                return route.fulfill({ status: 200, contentType: 'text/html', body: html });
            const respond = (status, data, message = '') => route.fulfill({
                status, contentType: 'application/json',
                body: JSON.stringify({ success: status < 400, data, message })
            });
            if (url.pathname === '/api/students' && request.method() === 'GET') return respond(200, {
                items: [{ reference: 'd4d211e0-fcee-4f20-a5eb-001a203ebcaa',
                    fullName: 'Test Student', studentCode: 'STU-2026-001', status: 'Active' }],
                totalPages: 1
            });
            if (url.pathname === '/api/academic-setup/catalog') return respond(200, {
                batches: [{ id: 80, name: 'Grade Nine A', code: 'G9-A',
                    academicProgramId: 25, academicLevelId: 9,
                    startDate: '2026-01-01', endDate: '2026-12-31',
                    capacity: 40, isActive: true }]
            });
            if (url.pathname.includes('/students/') && url.pathname.endsWith('/current'))
                return respond(404, null, 'No enrollment');
            if (url.pathname === '/api/academic-enrollments/batches/80/curricula')
                return respond(200, [{ id: 70, name: 'Secondary Curriculum', code: 'SEC-2026',
                    academicProgramId: 25, isActive: true, isCurrent: true }]);
            if (url.pathname === '/api/academic-enrollments' && request.method() === 'POST') {
                assert.equal(request.headers()['requestverificationtoken'], 'enrollment-browser-csrf');
                const data = request.postDataJSON();
                posts.push(data);
                if (posts.length === 1) return respond(503, null, 'Temporary failure');
                return respond(200, { rollNo: data.rollNo });
            }
            return respond(404, null, 'Unexpected endpoint');
        });
        await page.goto('http://eduos.test/EnrollmentManagement/Index');
        await page.addScriptTag({ path: path.join(root, 'EduOS.App/wwwroot/js/students/enrollment-management.js') });
        await page.locator('#enrollmentStudentSearch').fill('Test');
        await page.locator('#enrollmentStudentSearchForm button[type="submit"]').click();
        await page.locator('#enrollmentStudentResults button').first().click();
        await page.waitForFunction(() =>
            document.querySelector('#enrollmentCurrent')?.textContent.includes('No current enrollment'));
        await page.locator('#enrollmentBatch option[value="80"]').waitFor({ state: 'attached' });
        await page.locator('#enrollmentBatch').selectOption('80');
        await page.locator('#enrollmentDate').fill('2026-09-21');
        await page.locator('#enrollmentRoll').fill('09');
        await page.locator('#enrollmentCurriculum option[value="70"]').waitFor({ state: 'attached' });
        await page.locator('#enrollmentCurriculum').selectOption('70');
        await page.locator('#enrollmentSubmit').click();
        await page.waitForFunction(() => document.querySelector('#enrollmentNotice')?.textContent.includes('Temporary failure'));
        assert.equal(posts.length, 1);
        assert.equal(await page.locator('#enrollmentSubmit').isDisabled(), false);
        await page.locator('#enrollmentSubmit').click();
        await page.waitForFunction(() => document.querySelector('#enrollmentNotice')?.textContent.includes('successfully'));
        assert.equal(posts.length, 2);
        assert.equal(posts[0].clientRequestId, posts[1].clientRequestId,
            'Retry must preserve original idempotency token.');
        assert.equal(posts[1].academicBatchId, 80);
        assert.equal(posts[1].academicCurriculumId, 70);
        assert.equal(posts[1].rollNo, '09');
        assert.equal(await page.locator('#enrollmentSubmit').isDisabled(), true);
        assert.deepEqual(jsErrors, []);
        console.log('Student enrollment browser: PASS — lookup, curriculum, CSRF, retry idempotency, duplicate prevention.');
    } finally { await browser.close(); }
}
run().catch(error => { console.error(error); process.exitCode = 1; });
