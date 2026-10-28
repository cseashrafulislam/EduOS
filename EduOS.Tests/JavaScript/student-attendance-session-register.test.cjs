const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('session register is manager-authorized, bounded and includes unfinalized sessions', () => {
    const dto = read('EduOS.Core/DTOs/Attendance/StudentAttendanceReportDtos.cs');
    const service = read('EduOS.Service/Services/Attendance/StudentAttendanceReportService.cs');
    const api = read('EduOS.App/Controllers/Api/StudentAttendanceReportsController.cs');
    const view = read('EduOS.App/Views/Attendance/Index.cshtml');
    const js = read('EduOS.App/wwwroot/js/attendance/session-register.js');
    assert.match(dto, /class StudentAttendanceSessionRegisterDto/);
    assert.match(dto, /class StudentAttendanceSessionOverviewDto/);
    assert.match(service, /GetSessionRegisterAsync/);
    assert.match(service, /GetSessionOverviewAsync/);
    assert.match(service, /ValidMarkedSessionIds/);
    assert.match(service, /"empty-draft" or "empty-finalized"/);
    assert.match(service, /GetAuthorizedCampusIdAsync/);
    assert.match(service, /session\.AcademicBatchId == query\.AcademicBatchId/);
    assert.match(service, /sessionIds\.Contains\(mark\.AttendanceSessionId\)/);
    assert.match(service, /bySession\.TryGetValue/);
    assert.match(service, /\.Skip\(\(query\.Page - 1\) \* query\.PageSize\)/);
    assert.match(api, /HttpGet\("sessions"\)/);
    assert.match(api, /HttpGet\("sessions\/summary"\)/);
    assert.match(api, /Authorize\(Roles = "TenantAdmin,Principal,VicePrincipal"\)/);
    for (const id of ['attendanceSessionsOverview', 'attendanceSessionsRows', 'attendanceSessionsStatusFilter',
        'attendanceSessionsCsv', 'attendanceSessionsPrevious', 'attendanceSessionsNext'])
        assert.ok(view.includes('id="' + id + '"'));
    assert.match(js, /token !== request/);
    assert.match(js, /token !== summaryVersion/);
    assert.match(js, /loadSummary\(\+\+summaryVersion, params\)/);
    assert.match(js, /sessions\/summary/);
    const roster = read('EduOS.App/wwwroot/js/attendance/index.js');
    assert.ok(js.includes('attendance:session-open'));
    assert.ok(js.includes('sessionId: item.sessionId'));
    assert.ok(js.includes('isFinalized: item.isFinalized === true'));
    assert.ok(roster.includes('isHistoricalReadOnly'));
    for (const id of ['attendanceHistoricalCsv', 'attendanceHistoricalPrint'])
        assert.ok(view.includes('id="' + id + '"'));
    assert.match(roster, /function historicalPageReady\(\)/);
    for (const id of ['attendanceHistoricalFilters', 'attendanceHistoricalSearch',
        'attendanceHistoricalState', 'attendanceHistoricalFind', 'attendanceHistoricalClear'])
        assert.ok(view.includes('id="' + id + '"'));
    assert.match(roster, /historicalParams\.set\('search', historicalQuery\.search\)/);
    assert.match(roster, /historicalParams\.set\('state', historicalQuery\.state\)/);
    assert.match(roster, /function applyHistoricalFilters\(\)/);
    assert.match(roster, /resetHistoricalQuery\(\)/);
    assert.match(api, /GetFilteredFinalizedSessionRosterAsync/);
    assert.match(service, /posted = posted\.Where/);
    assert.match(roster, /function updateHistoricalActions\(\)/);
    assert.match(roster, /historicalPageUrl\('export\.csv'\)/);
    assert.match(roster, /historicalPageUrl\('print'\)/);
    assert.match(roster, /token !== rosterRequest/);
    assert.match(roster, /response\.headers\.get\('content-type'\)/);
    assert.match(roster, /window\.open\(historicalPageUrl\('print'\)/);
    assert.match(roster, /credentials: 'same-origin', cache: 'no-store'/);
    assert.ok(roster.includes('/api/student-attendance/report/sessions/'));
    assert.ok(roster.includes('historicalSessionId = detail.isFinalized === true ? sessionId : null'));
    assert.ok(js.includes('load(true)'));
    assert.ok(roster.includes('attendance:session-open'));
    assert.ok(roster.includes('expectedSessionId !== null'));
    assert.ok(roster.includes('Number(result.data?.attendanceSessionId) !== expectedSessionId'));
    assert.ok(roster.includes('Number.isSafeInteger(batchId)'));
    assert.ok(view.includes('<th>Action</th>'));
    assert.match(js, /visible\.map\(x =>/);
    assert.match(js, /row\.map\(csvCell\)/);
    assert.doesNotThrow(() => new Function(js));
});
