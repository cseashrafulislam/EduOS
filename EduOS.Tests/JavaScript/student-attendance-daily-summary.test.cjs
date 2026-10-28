const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/Attendance/Index.cshtml'), 'utf8');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/attendance/report.js'), 'utf8');
const api = fs.readFileSync(path.join(root, 'EduOS.App/Controllers/Api/StudentAttendanceReportsController.cs'), 'utf8');
const service = fs.readFileSync(path.join(root, 'EduOS.Service/Services/Attendance/StudentAttendanceReportService.cs'), 'utf8');

test('daily finalized attendance report is manager scoped and limited to 31 days', () => {
    for (const id of ['attendanceDailyRows', 'attendanceDailyCsv', 'attendanceDailyStatus'])
        assert.match(view, new RegExp('id="' + id + '"'));
    assert.match(api, /\[Authorize\(Roles = "TenantAdmin,Principal,VicePrincipal"\)\]/);
    assert.match(api, /\[RequireModule\("ATTENDANCE"\)\]/);
    assert.match(api, /\[HttpGet\("daily"\)\]/);
    assert.match(js, /\/api\/student-attendance\/report\/daily\?/);
    assert.match(js, /requestId !== dailyPending/);
    assert.match(js, /dailyItems\.map\(x =>/);
    assert.match(js, /row\.map\(csvCell\)/);
    assert.match(service, /query\.ToDate\.DayNumber - query\.FromDate\.DayNumber > 30/);
    assert.match(service, /session\.IsFinalized/);
    assert.match(service, /enrollment\.CampusId == batch\.CampusId/);
    assert.match(service, /access\.IsActive && !access\.IsDeleted/);
});
