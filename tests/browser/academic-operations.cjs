'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../..');
const source = p => fs.readFileSync(path.join(root, p), 'utf8');
function markup(file, startTag) {
    const razor = source(file);
    const start = razor.indexOf(startTag), end = razor.indexOf('@section Scripts', start);
    assert(start !== -1 && end > start, file + ' markup missing');
    return '<!doctype html><html><body>' +
        '<input name="__RequestVerificationToken" value="academic-ops-csrf">' +
        razor.slice(start, end).replace('data-can-manage="@canManage.ToString().ToLowerInvariant()"',
            'data-can-manage="true"') + '</body></html>';
}
const attendanceHtml = markup('EduOS.App/Views/Attendance/Index.cshtml', '<section class="admission-page"');
const routineHtml = markup('EduOS.App/Views/AcademicRoutines/Index.cshtml', '<section id="routineApp"');
const leavesHtml = markup('EduOS.App/Views/StudentLeaves/Index.cshtml', '<div id="studentLeavesApp"');
const response = (route, status, data, message = '') => route.fulfill({
    status, contentType: 'application/json', body: JSON.stringify({
        success: status < 400, data, message
    })
});
const tokenOk = request => {
    assert.equal(request.headers()['requestverificationtoken'], 'academic-ops-csrf',
        request.url() + ' missing antiforgery token');
};
async function attendance(browser) {
    const page = await browser.newPage();
    const requests = [];
    const roster = {
        attendanceSessionId: 0, sessionRowVersion: '', academicBatchId: 10, attendanceDate: '2026-10-10',
        students: [
            { id: 0, rowVersion: '', studentEnrollmentReference: '11ebaaaa-1111-4111-8111-111111111111',
                studentCode: 'S-1', studentName: 'Present Student', rollNo: '1', state: 0, hasApprovedLeave: false },
            { id: 0, rowVersion: '', studentEnrollmentReference: '11ebbbbb-2222-4222-8222-222222222222',
                studentCode: 'S-2', studentName: 'Approved Leave Student', rollNo: '2', state: 0, hasApprovedLeave: true }
        ]
    };
    await page.route('http://eduos.test/**', async route => {
        const request = route.request(), url = new URL(request.url());
        if (!url.pathname.startsWith('/api/'))
            return route.fulfill({ status: 200, contentType: 'text/html', body: attendanceHtml });
        if (url.pathname === '/api/academic-setup/catalog')
            return response(route, 200, { batches: [{
                id: 10, academicYearId: 20, academicLevelId: 30, academicYearName: '2026',
                academicLevelName: 'Grade 9', name: 'Section A', code: '9-A', isActive: true
            }] });
        if (url.pathname === '/api/student-attendance/roster') return response(route, 200, roster);
        if (url.pathname === '/api/student-attendance/sessions') {
            tokenOk(request);
            return response(route, 200, { ...roster, attendanceSessionId: 99,
                sessionRowVersion: 'AQIDBAUGBwg=' });
        }
        if (url.pathname === '/api/student-attendance' && request.method() === 'POST') {
            tokenOk(request);
            requests.push(request.postDataJSON());
            return response(route, 200, { ...roster, attendanceSessionId: 99,
                sessionRowVersion: 'AQIDBAUGBwg=' });
        }
        return response(route, 404, null, 'Unexpected endpoint');
    });
    await page.goto('http://eduos.test/Attendance/Index');
    await page.addScriptTag({ path: path.join(root, 'EduOS.App/wwwroot/js/attendance/index.js') });
    await page.locator('#academicYearId option[value="20"]').waitFor({ state: 'attached' });
    await page.locator('#academicYearId').selectOption('20');
    await page.locator('#classId').selectOption('30');
    await page.locator('#sectionId').selectOption('10');
    await page.locator('#attendanceDate').fill('2026-10-10');
    await page.locator('#attendanceFilters button[type="submit"]').click();
    await page.locator('tr[data-enrollment-reference]').first().waitFor();
    assert.match(await page.locator('#attendanceRows').innerText(), /Approved leave on this date/);
    await page.locator('#markAllPresent').click();
    assert.equal(await page.locator('tr[data-enrollment-reference] [data-state]').first().inputValue(), '1');
    assert.equal(await page.locator('tr[data-enrollment-reference] [data-state]').last().inputValue(), '');
    await page.locator('tr[data-enrollment-reference] [data-state]').last().selectOption('4');
    await page.locator('#saveAttendance').click();
    await page.waitForFunction(() => document.querySelector('#attendanceAlert')?.textContent.includes('Attendance saved'));
    assert.equal(requests.length, 1);
    assert.deepEqual(requests[0].students.map(x => x.state), [1, 4]);
    await page.close();
    console.log('Attendance browser: PASS — approved leave, safe mass marking, CSRF and posting.');
}
async function routine(browser) {
    const page = await browser.newPage();
    const posted = [];
    page.on('dialog', d => d.accept());
    await page.route('http://eduos.test/**', async route => {
        const request = route.request(), url = new URL(request.url());
        if (!url.pathname.startsWith('/api/'))
            return route.fulfill({ status: 200, contentType: 'text/html', body: routineHtml });
        if (url.pathname === '/api/academic-setup/catalog')
            return response(route, 200, { batches: [{
                id: 10, academicYearId: 20, academicProgramId: 21,
                academicLevelId: 30, academicYearName: '2026', name: 'Section A', campusId: 50,
                isActive: true
            }], rooms: [], subjects: [], curricula: [], curriculumSubjects: [] });
        if (url.pathname === '/api/academic-routines/time-slots')
            return response(route, 200, [{ id: 5, name: 'Morning', isBreak: false,
                startTime: '09:00:00', endTime: '09:45:00' }]);
        if (url.pathname === '/api/academic-routines/instructors') return response(route, 200, []);
        if (url.pathname.endsWith('/assignments')) return response(route, 200, []);
        if (url.pathname === '/api/academic-routines/batches/10')
            return response(route, 200, posted.length ? [] : [{
                id: 70, rowVersion: 'AQIDBAUGBwg=', dayOfWeek: 1, timeSlotName: 'Morning',
                subjectName: 'Math', effectiveFrom: '2026-10-01', isActive: true
            }]);
        if (url.pathname === '/api/academic-routines/entries/70/deactivate') {
            tokenOk(request);
            assert.equal(url.searchParams.get('rowVersion'), 'AQIDBAUGBwg=');
            posted.push(true);
            return response(route, 200, {});
        }
        return response(route, 404, null, url.pathname);
    });
    await page.goto('http://eduos.test/AcademicRoutines/Index');
    await page.addScriptTag({ path: path.join(root, 'EduOS.App/wwwroot/js/academic/routines.js') });
    await page.locator('#routineBatch option[value="10"]').waitFor({ state: 'attached' });
    await page.locator('#routineBatch').selectOption('10');
    await page.locator('#routineEntries button[data-deactivate-id="70"]').waitFor();
    await page.locator('#routineEntries button[data-deactivate-id="70"]').click();
    await page.waitForFunction(() => document.querySelector('#routineAlert')?.textContent.includes('Routine entry deactivated.'));
    assert.equal(posted.length, 1);
    await page.close();
    console.log('Timetable browser: PASS — rowversion deactivation, CSRF and refresh.');
}
async function leaves(browser) {
    const page = await browser.newPage();
    const studentRef = '11111111-2222-4333-8444-555555555555';
    const enrollmentRef = 'aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee';
    const posts = [];
    let state = 2;
    page.on('dialog', d => d.type === 'prompt' ? d.accept('Reviewed') : d.accept());
    await page.route('http://eduos.test/**', async route => {
        const req = route.request(), url = new URL(req.url());
        if (!url.pathname.startsWith('/api/'))
            return route.fulfill({ status: 200, contentType: 'text/html', body: leavesHtml });
        if (url.pathname === '/api/students')
            return response(route, 200, { items: [{
                reference: studentRef, fullName: 'Leave Student', studentCode: 'S-100'
            }] });
        if (url.pathname === '/api/academic-enrollments/students/' + studentRef + '/current')
            return response(route, 200, { reference: enrollmentRef, rollNo: '10' });
        if (url.pathname === '/api/student-leaves' && req.method() === 'GET')
            return response(route, 200, {
                items: posts.length < 2 ? [] : [{
                    id: 50, studentEnrollmentReference: enrollmentRef,
                    fromDate: '2026-10-10', toDate: '2026-10-11', reason: 'Illness',
                    state, rowVersion: 'AQIDBAUGBwg='
                }], page: 1, totalPages: 1, totalCount: posts.length < 2 ? 0 : 1
            });
        if (url.pathname === '/api/student-leaves' && req.method() === 'POST') {
            tokenOk(req);
            const data = req.postDataJSON(); posts.push(data);
            if (posts.length === 1) return response(route, 503, null, 'Transient error');
            return response(route, 200, { id: 50 });
        }
        if (url.pathname === '/api/student-leaves/50/review') {
            tokenOk(req);
            assert.equal(req.postDataJSON().rowVersion, 'AQIDBAUGBwg=');
            state = 3;
            return response(route, 200, { id: 50, state });
        }
        return response(route, 404, null, url.pathname);
    });
    await page.goto('http://eduos.test/StudentLeaves/Index');
    await page.addScriptTag({ path: path.join(root, 'EduOS.App/wwwroot/js/attendance/student-leaves.js') });
    await page.locator('#leaveStudentTerm').fill('Leave Student');
    await page.locator('#leaveStudentSearch button[type="submit"]').click();
    await page.locator('#leaveStudentResults button').first().click();
    await page.locator('#leaveSubmit:not([disabled])').waitFor();
    await page.locator('#leaveStart').fill('2026-10-10');
    await page.locator('#leaveEnd').fill('2026-10-11');
    await page.locator('#leaveReason').fill('Illness');
    await page.locator('#leaveSubmit').click();
    await page.waitForFunction(() => document.querySelector('#leaveNotice')?.textContent.includes('Transient error'));
    await page.locator('#leaveSubmit').click();
    await page.waitForFunction(() => document.querySelector('#leaveNotice')?.textContent.includes('submitted.'));
    assert.equal(posts.length, 2);
    assert.equal(posts[0].clientRequestId, posts[1].clientRequestId);
    await page.locator('#leaveRows button').first().click();
    await page.waitForFunction(() => document.querySelector('#leaveRows')?.textContent.includes('Approved'));
    await page.close();
    console.log('Student leave browser: PASS — submission retry idempotency, review, CSRF and listing.');
}
(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try { await attendance(browser); await routine(browser); await leaves(browser); }
    finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
