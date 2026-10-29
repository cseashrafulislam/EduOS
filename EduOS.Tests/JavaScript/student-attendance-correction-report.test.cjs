const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/Attendance/Index.cshtml'), 'utf8');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/attendance/correction-report.js'), 'utf8');
const api = fs.readFileSync(path.join(root, 'EduOS.App/Controllers/Api/StudentAttendanceController.cs'), 'utf8');
test('manager correction report has scoped SQL-paged API and authorized UI', () => {
    for (const id of ['attendanceCorrectionAuditLoad','attendanceCorrectionAuditCsv',
        'attendanceCorrectionAuditRows','attendanceCorrectionAuditPrevious',
        'attendanceCorrectionAuditNext','attendanceCorrectionAuditPage',
        'attendanceCorrectionAuditStudent','attendanceCorrectionAuditActor',
        'attendanceCorrectionAuditPreviousState','attendanceCorrectionAuditNewState'])
        assert.match(view, new RegExp('id="' + id + '"'));
    assert.match(api, /\[HttpGet\("corrections\/report"\)\]/);
    assert.match(api, /GetCorrectionReportAsync\(query, page, pageSize, cancellationToken\)/);
    assert.match(js, /\/api\/student-attendance\/corrections\/report\?/);
    assert.match(js, /pageSize: '20'/);
    for (const filter of ['studentSearch','changedByUserId','previousState','newState'])
        assert.match(js, new RegExp(filter));
    assert.match(js, /credentials: 'same-origin'/);
    assert.match(js, /cache: 'no-store'/);
    assert.match(js, /\^\\s\*\[=\+\\-@\\t\\r\]/);
});
