const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/Attendance/Index.cshtml'), 'utf8');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/attendance/correction-report.js'), 'utf8');
const api = fs.readFileSync(path.join(root, 'EduOS.App/Controllers/Api/StudentAttendanceController.cs'), 'utf8');
const exportsController = fs.readFileSync(path.join(root, 'EduOS.App/Controllers/AttendanceCorrectionExportsController.cs'), 'utf8');
const print = fs.readFileSync(path.join(root, 'EduOS.App/Views/Attendance/CorrectionAuditPrint.cshtml'), 'utf8');
test('manager correction report has scoped SQL-paged API and authorized UI', () => {
    for (const id of ['attendanceCorrectionAuditLoad','attendanceCorrectionAuditCsv',
        'attendanceCorrectionAuditPrint','attendanceCorrectionAuditRows','attendanceCorrectionAuditPrevious',
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
    // Export and print must reauthorize each request instead of using cached DOM rows.
    for (const token of ['[Route("attendance/corrections")]',
        '[Authorize(Roles = "TenantAdmin,Principal,VicePrincipal")]',
        '[RequireModule("ATTENDANCE")]', '[ResponseCache(NoStore = true',
        '[EnableRateLimiting("ApiPolicy")]', 'GetCorrectionReportAsync(',
        'ExportPageSize = 20', 'cells.Select(EscapeCsv)', 'TrimStart()'])
        assert.ok(exportsController.includes(token), token);
    assert.ok(js.includes('/attendance/corrections/export.csv?'));
    assert.ok(js.includes('/attendance/corrections/print?'));
    assert.ok(!js.includes('new Blob('));
    assert.ok(print.includes('@@page { size: A4 landscape'));
    assert.ok(print.includes('@Model.Items.Count'));
});
