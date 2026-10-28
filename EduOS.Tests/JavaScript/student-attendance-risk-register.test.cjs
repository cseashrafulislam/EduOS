const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('attendance risk register is SQL-filtered before count and paging with tenant/campus guard', () => {
    const dto = read('EduOS.Core/DTOs/Attendance/StudentAttendanceReportDtos.cs');
    const service = read('EduOS.Service/Services/Attendance/StudentAttendanceReportService.cs');
    const view = read('EduOS.App/Views/Attendance/Index.cshtml');
    const js = read('EduOS.App/wwwroot/js/attendance/report.js');
    for (const name of ['MaxAttendancePercent', 'MinMarkedSessions', 'Sort'])
        assert.ok(dto.includes(name));
    assert.match(service, /grouped = grouped\.Where\(x => x\.MarkedSessions >= query\.MinMarkedSessions\)/);
    assert.match(service, /100m \* \(x\.Present \+ x\.Late\) < threshold \* x\.MarkedSessions/);
    assert.ok(service.indexOf('var total = await grouped.CountAsync') > service.indexOf('grouped = grouped.Where'));
    assert.match(service, /session\.IsFinalized/);
    assert.match(service, /enrollment\.CampusId == batch\.CampusId/);
    assert.match(service, /access\.IsActive && !access\.IsDeleted/);
    for (const id of ['attendanceReportRisk', 'attendanceReportMinSessions', 'attendanceReportSort'])
        assert.ok(view.includes('id="' + id + '"'));
    assert.match(js, /maxAttendancePercent: el\('attendanceReportRisk'\)\.value/);
    assert.match(js, /minMarkedSessions: el\('attendanceReportMinSessions'\)\.value/);
    assert.match(js, /sort: el\('attendanceReportSort'\)\.value/);
    assert.match(js, /visibleItems = \[\]; csvButton\.disabled = true/);
    assert.doesNotThrow(() => new Function(js));
});
