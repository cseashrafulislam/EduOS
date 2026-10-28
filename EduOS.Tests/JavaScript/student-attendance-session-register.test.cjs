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
