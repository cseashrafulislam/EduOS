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
        razor.slice(start, end)
            .replace('@Html.AntiForgeryToken()',
                '<input name="__RequestVerificationToken" value="academic-ops-csrf">')
            .replace('data-can-manage="@canManage.ToString().ToLowerInvariant()"',
                'data-can-manage="true"')
            .replace('data-can-correct="@((User.IsInRole("TenantAdmin") || User.IsInRole("Principal") || User.IsInRole("VicePrincipal")).ToString().ToLowerInvariant())"',
                'data-can-correct="true"') + '</body></html>';
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
    const finalized = [], corrections = [];
    let historicalCsvRequests = 0, failHistoricalRefresh = false;
    let isFinalized = false, saved = false, correctedState = null;
    page.on('dialog', dialog => dialog.type() === 'prompt'
        ? dialog.accept('Reviewed correction reason') : dialog.accept());
    const roster = {
        attendanceSessionId: 0, sessionRowVersion: '', academicBatchId: 10, attendanceDate: '2026-10-10',
        page: 1, pageSize: 50, totalPages: 1,
        summary: { totalStudents: 2, marked: 0, unmarked: 2 },
        students: [
            { id: 0, rowVersion: '', studentEnrollmentReference: '11ebaaaa-1111-4111-8111-111111111111',
                studentCode: 'S-1', studentName: 'Present Student', rollNo: '1', state: 0, hasApprovedLeave: false },
            { id: 0, rowVersion: '', studentEnrollmentReference: '11ebbbbb-2222-4222-8222-222222222222',
                studentCode: 'S-2', studentName: 'Approved Leave Student', rollNo: '2', state: 0, hasApprovedLeave: true }
        ]
    };
    await page.route('http://eduos.test/**', async route => {
        const request = route.request(), url = new URL(request.url());
        if (url.pathname === '/attendance/sessions/99/export.csv') {
            assert.equal(url.searchParams.get('page'), '1');
            historicalCsvRequests++;
            return route.fulfill({ status: 200, contentType: 'text/csv; charset=utf-8',
                body: 'Session ID,Roll,Student Name\r\n99,1,Historical Learner\r\n' });
        }
        if (!url.pathname.startsWith('/api/'))
            return route.fulfill({ status: 200, contentType: 'text/html', body: attendanceHtml });
        if (url.pathname === '/api/student-attendance/batches') {
            assert.equal(url.searchParams.get('page'), '1');
            assert.equal(url.searchParams.get('pageSize'), '25');
            return response(route, 200, { page: 1, pageSize: 25, totalCount: 1,
                totalPages: 1, items: [{
                    id: 10, academicYearName: '2026', academicLevelName: 'Grade 9',
                    name: 'Section A', code: '9-A'
                }] });
        }
        if (url.pathname === '/api/student-attendance/roster-page') {
            assert.equal(url.searchParams.get('page'), '1');
            assert.equal(url.searchParams.get('pageSize'), '50');
            return response(route, 200, !saved ? roster : {
                ...roster, isFinalized, attendanceSessionId: 99,
                sessionRowVersion: isFinalized ? 'AgIDBAUGBwg=' : 'AQIDBAUGBwg=',
                summary: { totalStudents: 2, marked: 2, unmarked: 0 },
                students: roster.students.map((x, i) => ({
                    ...x, id: 901 + i, rowVersion: 'AQIDBAUGBwg=',
                    state: i === 0 ? correctedState || 1 : 4
                }))
            });
        }
        if (url.pathname === '/api/student-attendance/report/sessions/99/roster' ||
            url.pathname === '/api/student-attendance/report/sessions/98/roster') {
            if (failHistoricalRefresh) {
                failHistoricalRefresh = false;
                return response(route, 500, null, 'Roster refresh unavailable.');
            }
            assert.equal(url.searchParams.get('page'), '1');
            assert.equal(url.searchParams.get('pageSize'), '50');
            return response(route, 200, {
                ...roster, attendanceSessionId: 99, isFinalized: true,
                isHistoricalReadOnly: true, sessionRowVersion: '',
                summary: { totalStudents: 2, marked: 2, unmarked: 0,
                    present: 1, absent: 1, late: 0, leave: 0, excused: 0 },
                students: roster.students.map((x, i) => ({
                    ...x, id: 901 + i, rowVersion: 'AQIDBAUGBwg=',
                    state: i === 0 ? correctedState || 1 : 4
                }))
            });
        }
        if (url.pathname === '/api/student-attendance/report/sessions/summary') {
            return response(route, 200, { totalSessions: 2, draftSessions: 0,
                finalizedSessions: 2, emptyDraftSessions: 0, emptyFinalizedSessions: 0 });
        }
        if (url.pathname === '/api/student-attendance/report/sessions') {
            return response(route, 200, { page: 1, pageSize: 20, totalPages: 1,
                totalCount: 2, items: [
                    { sessionId: 99, attendanceDate: '2026-10-10',
                        subjectOfferingId: null, subjectOfferingCode: 'General',
                        isFinalized: true, marked: 2, present: 1, absent: 1, late: 0, leave: 0, excused: 0 },
                    { sessionId: 98, attendanceDate: '2026-10-10',
                        subjectOfferingId: null, subjectOfferingCode: 'General',
                        isFinalized: true, marked: 2, present: 1, absent: 1, late: 0, leave: 0, excused: 0 }
                ] });
        }
        if (url.pathname === '/api/student-attendance/sessions') {
            tokenOk(request);
            return response(route, 200, { ...roster, attendanceSessionId: 99,
                sessionRowVersion: 'AQIDBAUGBwg=' });
        }
        if (url.pathname === '/api/student-attendance' && request.method() === 'POST') {
            tokenOk(request);
            requests.push(request.postDataJSON());
            saved = true;
            return response(route, 200, { ...roster, attendanceSessionId: 99,
                sessionRowVersion: 'AQIDBAUGBwg=',
                summary: { totalStudents: 2, marked: 2, unmarked: 0 },
                students: roster.students.map((x, i) => ({
                    ...x, id: 901 + i, rowVersion: 'AQIDBAUGBwg=',
                    state: i === 0 ? 1 : 4
                }))
            }, 'Attendance saved.');
        }
        if (url.pathname === '/api/student-attendance/sessions/99/finalize') {
            isFinalized = true;
            tokenOk(request);
            assert.equal(url.searchParams.get('rowVersion'), 'AQIDBAUGBwg=');
            finalized.push(true);
            return response(route, 200, { ...roster, attendanceSessionId: 99,
                sessionRowVersion: 'AgIDBAUGBwg=', isFinalized: true,
                summary: { totalStudents: 2, marked: 2, unmarked: 0 },
                students: roster.students.map((x, i) => ({
                    ...x, id: 901 + i, rowVersion: 'AQIDBAUGBwg=',
                    state: i === 0 ? 1 : 4
                }))
            }, 'Attendance finalized.');
        }
        if (url.pathname === '/api/student-attendance/901/correct') {
            tokenOk(request);
            const data = request.postDataJSON();
            assert.equal(data.rowVersion, 'AQIDBAUGBwg=');
            corrections.push(data); correctedState = data.newState;
            return response(route, 200, { id: 901, state: correctedState });
        }
        return response(route, 404, null, 'Unexpected endpoint');
    });
    await page.goto('http://eduos.test/Attendance/Index');
    // The harness renders Razor without the production Bootstrap bundle.
    await page.evaluate(() => {
        window.bootstrap = { Modal: { getOrCreateInstance: element => ({
            show() { element.style.display = 'block'; element.classList.add('show'); },
            hide() { element.style.display = 'none'; element.classList.remove('show');
                element.dispatchEvent(new Event('hidden.bs.modal')); }
        }) } };
    });
    await page.addScriptTag({ path: path.join(root, 'EduOS.App/wwwroot/js/attendance/index.js') });
    await page.locator('#attendanceDate').fill('2026-10-10');
    await page.locator('#sectionId option[value="10"]').waitFor({ state: 'attached' });
    await page.locator('#sectionId').selectOption('10');
    await page.locator('#attendanceFilters button[type="submit"]').click();
    await page.locator('tr[data-enrollment-reference]').first().waitFor();
    assert.match(await page.locator('#attendanceRows').innerText(), /Approved leave on this date/);
    assert.match(await page.locator('#attendanceRosterPage').innerText(), /Page 1 of 1/);
    await page.locator('#markAllPresent').click();
    assert.equal(await page.locator('tr[data-enrollment-reference] [data-state]').first().inputValue(), '1');
    assert.equal(await page.locator('tr[data-enrollment-reference] [data-state]').last().inputValue(), '');
    await page.locator('tr[data-enrollment-reference] [data-state]').last().selectOption('4');
    await page.locator('#saveAttendance').click();
    await page.waitForFunction(() => document.querySelector('#attendanceAlert')?.textContent.includes('Attendance saved'));
    assert.equal(requests.length, 1);
    assert.deepEqual(requests[0].students.map(x => x.state), [1, 4]);
    await page.locator('#finalizeAttendance').click();
    await page.waitForFunction(() => document.querySelector('#attendanceAlert')?.textContent.includes('Attendance finalized.'));
    assert.equal(finalized.length, 1);
    assert.equal(await page.locator('#saveAttendance').isDisabled(), true);
    assert.equal(await page.locator('#finalizeAttendance').isDisabled(), true);
    assert.equal(await page.locator('#attendanceRows [data-state]').first().isDisabled(), true);
    await page.locator('#attendanceRows button').first().click();
    await page.locator('#attendanceCorrectionModal').waitFor({ state: 'visible' });
    await page.locator('#attendanceCorrectionState').selectOption('2');
    await page.locator('#attendanceCorrectionReason').fill('Reviewed correction reason');
    await page.locator('#attendanceCorrectionConfirm').click();
    await page.waitForFunction(() => document.querySelector('#attendanceAlert')?.textContent.includes('Attendance corrected'));
    assert.equal(corrections.length, 1);
    assert.equal(corrections[0].newState, 2);
    assert.equal(corrections[0].reason, 'Reviewed correction reason');
    await page.addScriptTag({ path: path.join(root, 'EduOS.App/wwwroot/js/attendance/session-register.js') });
    await page.locator('#attendanceReportBatch').evaluate(select => {
        const option = document.createElement('option');
        option.value = '10'; option.textContent = '2026 / Grade 9 / Section A';
        select.append(option); select.disabled = false; select.value = '10';
    });
    await page.locator('#attendanceReportFrom').fill('2026-10-10');
    await page.locator('#attendanceReportTo').fill('2026-10-10');
    await page.locator('#attendanceSessionsLoad').click();
    await page.locator('#attendanceSessionsRows button').first().waitFor();
    await page.locator('#attendanceSessionsRows button').first().click();
    await page.waitForFunction(() => document.querySelector('#attendanceRosterPage')?.textContent.includes('Page 1 of 1'));
    assert.equal(await page.locator('#sectionId').inputValue(), '10');
    assert.equal(await page.locator('#attendanceDate').inputValue(), '2026-10-10');
    assert.equal(await page.locator('#attendanceRows [data-state]').first().isDisabled(), true);
    assert.equal(await page.locator('#saveAttendance').isDisabled(), true);
    assert.match(await page.locator('#attendanceRosterPage').innerText(), /historical marks \(read-only\)/);
    // Historical posted marks stay read-only in the roster but support an explicit
    // manager correction with rowversion, reason, CSRF and refreshed posted status.
    await page.locator('#attendanceRows button').first().click();
    await page.locator('#attendanceCorrectionModal').waitFor({ state: 'visible' });
    await page.locator('#attendanceCorrectionState').selectOption('3');
    await page.locator('#attendanceCorrectionReason').fill('Archived session audit review');
    await page.locator('#attendanceCorrectionConfirm').click();
    await page.waitForFunction(() => document.querySelector('#attendanceAlert')?.textContent.includes('Attendance corrected'));
    assert.equal(corrections.length, 2);
    assert.equal(corrections[1].newState, 3);
    assert.equal(corrections[1].reason, 'Archived session audit review');
    assert.equal(await page.locator('#attendanceRows [data-state]').first().inputValue(), '3');
    assert.equal(await page.locator('#attendanceRows [data-state]').first().isDisabled(), true);
    await page.evaluate(() => {
        document.addEventListener('attendance:corrections-open', e => {
            window.lastHistoryAttendanceId = e.detail.attendanceId;
        }, { once: true });
    });
    await page.locator('#attendanceRows button').nth(1).click();
    assert.equal(await page.evaluate(() => window.lastHistoryAttendanceId), 901);
    // A committed correction followed by a failed readback must NOT show success.
    await page.locator('#attendanceRows button').first().click();
    await page.locator('#attendanceCorrectionModal').waitFor({ state: 'visible' });
    await page.locator('#attendanceCorrectionState').selectOption('4');
    await page.locator('#attendanceCorrectionReason').fill('Reconcile archived leave');
    failHistoricalRefresh = true;
    await page.locator('#attendanceCorrectionConfirm').click();
    await page.waitForFunction(() => document.querySelector('#attendanceAlert')?.textContent.includes('Roster refresh unavailable.'));
    assert.match(await page.locator('#attendanceAlert').getAttribute('class'), /alert-danger/);
    assert.equal(corrections.length, 3);
    await page.locator('#attendanceSessionsRows button').first().click();
    await page.waitForFunction(() => document.querySelector('#attendanceRosterPage')?.textContent.includes('Page 1 of 1'));
    assert.equal(await page.locator('#attendanceRows [data-state]').first().inputValue(), '4');
    assert.equal(await page.locator('#attendanceHistoricalCsv').isDisabled(), false);
    assert.equal(await page.locator('#attendanceHistoricalPrint').isDisabled(), false);
    const csvDownload = page.waitForEvent('download');
    await page.locator('#attendanceHistoricalCsv').click();
    assert.equal((await csvDownload).suggestedFilename(), 'attendance-session-99-page-1.csv');
    assert.equal(historicalCsvRequests, 1);
    await page.locator('#attendanceSessionsRows button').last().click();
    await page.waitForFunction(() => document.querySelector('#attendanceAlert')?.textContent.includes('selected attendance session changed'));
    assert.equal(await page.locator('#saveAttendance').isDisabled(), true);
    assert.equal(await page.locator('#attendanceHistoricalCsv').isDisabled(), true);
    assert.equal(await page.locator('#attendanceHistoricalPrint').isDisabled(), true);
    await page.close();
    console.log('Attendance browser: PASS — approved leave, mass marking, CSRF, save and finalization.');
}
async function routine(browser) {
    const page = await browser.newPage();
    const posted = [], assignmentPosts = [], entryPosts = [];
    const offeringReference = '11111111-2222-4333-8444-555555555555';
    const teacherReference = '22222222-3333-4444-8555-666666666666';
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
        if (url.pathname === '/api/academic-instruction/batches/10/lesson-offerings')
            return response(route, 200, { page: 1, pageSize: 50, totalCount: 1, items: [{
                id: 99, reference: '11111111-2222-4333-8444-555555555555',
                subjectName: 'Math', subjectCode: 'MATH', code: 'MATH-A', isActive: true
            }] });
        if (url.pathname === '/api/academic-routines/batches/10/instructors/page') {
            assert.equal(url.searchParams.get('pageSize'), '50');
            const pageNumber = Number(url.searchParams.get('page'));
            assert.ok(pageNumber === 1 || pageNumber === 2);
            const items = pageNumber === 1
                ? Array.from({ length: 50 }, (_, i) => ({
                    employeeReference: 'aaaaaaaa-aaaa-4aaa-8aaa-' + String(i + 1).padStart(12, '0'),
                    employeeCode: 'T-' + String(i + 100).padStart(3, '0'),
                    name: 'Instructor ' + String(i + 1).padStart(3, '0')
                }))
                : [{ employeeReference: teacherReference, employeeCode: 'T-001', name: 'Teacher One' }];
            return response(route, 200, { page: pageNumber, pageSize: 50, totalCount: 51, items });
        }
        if (url.pathname === '/api/academic-routines/assignments' && request.method() === 'POST') {
            tokenOk(request);
            const data = request.postDataJSON();
            assert.equal(data.subjectOfferingReference, offeringReference);
            assert.equal(data.employeeReference, teacherReference);
            assert.equal(data.effectiveFrom, '2026-10-01');
            assert.match(data.clientRequestId, /^[0-9a-f-]{36}$/i);
            assignmentPosts.push(data);
            return response(route, 200, { id: 80, subjectOfferingId: 99,
                subjectOfferingReference: offeringReference, employeeId: 5,
                employeeName: 'Teacher One', employeeCode: 'T-001', isActive: true });
        }
        if (url.pathname === '/api/academic-routines/batches/10/assignments/page') {
            assert.equal(url.searchParams.get('page'), '1');
            assert.equal(url.searchParams.get('pageSize'), '50');
            const items = assignmentPosts.length ? [{ id: 80, subjectOfferingId: 99,
                subjectOfferingReference: offeringReference, employeeId: 5,
                employeeName: 'Teacher One', employeeCode: 'T-001', isActive: true }] : [];
            return response(route, 200, { page: 1, pageSize: 50, totalCount: items.length, items });
        }
        if (url.pathname === '/api/academic-routines/entries' && request.method() === 'POST') {
            tokenOk(request);
            const data = request.postDataJSON();
            assert.equal(data.subjectOfferingReference, offeringReference);
            assert.equal(data.instructorAssignmentId, 80);
            assert.equal(data.effectiveFrom, '2026-10-01');
            assert.match(data.clientRequestId, /^[0-9a-f-]{36}$/i);
            entryPosts.push(data);
            return response(route, 200, { id: 71, rowVersion: 'AQIDBAUGBwg=' });
        }
        if (url.pathname === '/api/academic-routines/batches/10/page') {
            assert.equal(url.searchParams.get('page'), '1');
            assert.equal(url.searchParams.get('pageSize'), '50');
            const items = entryPosts.length ? [{
                id: 71, rowVersion: 'AQIDBAUGBwg=', dayOfWeek: 1, timeSlotName: 'Morning',
                subjectName: 'Math', effectiveFrom: '2026-10-01', isActive: true
            }] : posted.length ? [] : [{
                id: 70, rowVersion: 'AQIDBAUGBwg=', dayOfWeek: 1, timeSlotName: 'Morning',
                subjectName: 'Math', effectiveFrom: '2026-10-01', isActive: true
            }];
            return response(route, 200, { page: 1, pageSize: 50, totalCount: items.length, items });
        }
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
    await page.locator('#routineSubject option[value="11111111-2222-4333-8444-555555555555"]').waitFor({ state: 'attached' });
    await page.locator('#routineEntries button[data-deactivate-id="70"]').click();
    await page.waitForFunction(() => document.querySelector('#routineAlert')?.textContent.includes('Routine entry deactivated.'));
    assert.equal(posted.length, 1);
    await page.locator('#routineTeachersNext').click();
    await page.locator('#routineTeachersPage').getByText('Page 2 of 2').waitFor();
    await page.locator('#routineTeacher option[value="' + teacherReference + '"]').waitFor({ state: 'attached' });
    await page.locator('#routineTeacher').selectOption(teacherReference);
    await page.locator('#routineSubject').selectOption(offeringReference);
    await page.locator('#routineAssignFrom').fill('2026-10-01');
    await page.locator('#routineAssignmentForm button[type="submit"]').click();
    await page.waitForFunction(() => document.querySelector('#routineAlert')?.textContent.includes('Instructor assigned.'));
    assert.equal(assignmentPosts.length, 1);
    await page.locator('#routineAssignment option[value="80"]').waitFor({ state: 'attached' });
    await page.locator('#routineAssignment').selectOption('80');
    await page.locator('#routineSlot').selectOption('5');
    // The browser fixture extracts raw Razor; populate the @foreach-generated day option.
    await page.locator('#routineDay').evaluate(select => {
        const monday = document.createElement('option');
        monday.value = '1'; monday.textContent = 'Monday';
        select.append(monday); select.value = '1';
    });
    await page.locator('#routineEntryFrom').fill('2026-10-01');
    await page.locator('#routineEntryForm button[type="submit"]').click();
    await page.waitForFunction(() => document.querySelector('#routineAlert')?.textContent.includes('Class routine saved.'));
    assert.equal(entryPosts.length, 1);
    await page.close();
    console.log('Timetable browser: PASS — campus-scoped offering selector, canonical writes, CSRF, rowversion and refresh.');
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
        if (url.pathname === '/api/student-leaves/eligible-enrollments') {
            assert.equal(url.searchParams.get('search'), 'Leave Student');
            assert.equal(url.searchParams.get('page'), '1');
            return response(route, 200, { page: 1, pageSize: 20,
                totalCount: 1, totalPages: 1, items: [{
                    reference: enrollmentRef, fullName: 'Leave Student',
                    studentCode: 'S-100', rollNo: '10'
                }] });
        }
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
async function correctionAudit(browser) {
    const page = await browser.newPage({ acceptDownloads: true });
    let csvRequests = 0, printRequests = 0, denyCsv = false;
    await page.route('http://eduos.test/**', async route => {
        const request = route.request(), url = new URL(request.url());
        if (url.pathname === '/api/student-attendance/corrections/report') {
            assert.equal(url.searchParams.get('academicBatchId'), '10');
            assert.equal(url.searchParams.get('studentSearch'), 'ST');
            assert.equal(url.searchParams.get('previousState'), '2');
            assert.equal(url.searchParams.get('newState'), '1');
            assert.equal(url.searchParams.get('pageSize'), '20');
            return response(route, 200, {
                page: 1, pageSize: 20, totalPages: 1, totalCount: 1,
                items: [{ attendanceDate: '2026-10-15', rollNo: '01',
                    studentCode: 'ST-1', studentName: 'Historical Learner',
                    subjectOfferingId: null, previousState: 2, newState: 1,
                    reason: 'Reviewed', changedAt: '2026-10-16T10:30:00Z',
                    changedByUserId: 7 }]
            });
        }
        if (url.pathname === '/attendance/corrections/export.csv') {
            csvRequests++;
            assert.equal(url.searchParams.get('academicBatchId'), '10');
            assert.equal(url.searchParams.get('studentSearch'), 'ST');
            assert.equal(url.searchParams.get('previousState'), '2');
            assert.equal(url.searchParams.get('newState'), '1');
            assert.equal(url.searchParams.get('page'), '1');
            if (denyCsv) return route.fulfill({
                status: 403, contentType: 'application/json', body: '{"message":"Denied"}'
            });
            return route.fulfill({
                status: 200, contentType: 'text/csv; charset=utf-8',
                body: 'Attendance date,Student\r\n2026-10-15,Historical Learner\r\n'
            });
        }
        if (url.pathname === '/attendance/corrections/print') {
            printRequests++;
            assert.equal(url.searchParams.get('academicBatchId'), '10');
            assert.equal(url.searchParams.get('studentSearch'), 'ST');
            assert.equal(url.searchParams.get('page'), '1');
            return route.fulfill({ status: 200, contentType: 'text/html',
                body: '<!doctype html><html><body>Authorized print</body></html>' });
        }
        if (!url.pathname.startsWith('/api/'))
            return route.fulfill({ status: 200, contentType: 'text/html', body: attendanceHtml });
        return response(route, 404, null, 'Unexpected endpoint');
    });
    await page.goto('http://eduos.test/Attendance/Index');
    await page.addScriptTag({ path: path.join(root, 'EduOS.App/wwwroot/js/attendance/correction-report.js') });
    await page.locator('#attendanceReportBatch').evaluate(select => {
        select.add(new Option('Batch', '10')); select.disabled = false; select.value = '10';
    });
    await page.locator('#attendanceReportFrom').fill('2026-10-01');
    await page.locator('#attendanceReportTo').fill('2026-10-20');
    await page.locator('#attendanceCorrectionAuditStudent').fill('ST');
    await page.locator('#attendanceCorrectionAuditPreviousState').selectOption('2');
    await page.locator('#attendanceCorrectionAuditNewState').selectOption('1');
    await page.locator('#attendanceCorrectionAuditLoad').click();
    await page.locator('#attendanceCorrectionAuditRows tr').first().waitFor();
    const [download] = await Promise.all([
        page.waitForEvent('download'),
        page.locator('#attendanceCorrectionAuditCsv').click()
    ]);
    assert.equal(download.suggestedFilename(), 'attendance-corrections-batch-10-page-1.csv');
    assert.equal(csvRequests, 1);
    await Promise.all([
        page.context().waitForEvent('request', {
            predicate: req => req.url().includes('/attendance/corrections/print?')
        }),
        page.locator('#attendanceCorrectionAuditPrint').click()
    ]);
    assert.equal(printRequests, 1);
    denyCsv = true;
    await page.locator('#attendanceCorrectionAuditCsv').click();
    await page.waitForFunction(() => document.querySelector('#attendanceCorrectionAuditStatus')
        ?.textContent.includes('Authorized correction export unavailable.'));
    assert.equal(csvRequests, 2);
    await page.close();
    console.log('Correction audit browser: PASS — scoped CSV, print and denied download.');
}
(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try { await attendance(browser); await routine(browser); await leaves(browser); await correctionAudit(browser); }
    finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode = 1; });
