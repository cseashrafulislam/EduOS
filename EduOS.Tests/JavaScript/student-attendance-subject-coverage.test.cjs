const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('subject attendance coverage uses tenant-scoped SQL grouping and bounded paging', () => {
    const dto = read('EduOS.Core/DTOs/Attendance/StudentAttendanceReportDtos.cs');
    const service = read('EduOS.Service/Services/Attendance/StudentAttendanceReportService.cs');
    const api = read('EduOS.App/Controllers/Api/StudentAttendanceReportsController.cs');
    const view = read('EduOS.App/Views/Attendance/Index.cshtml');
    const js = read('EduOS.App/wwwroot/js/attendance/report.js');
    assert.match(dto, /class StudentAttendanceSubjectSummaryDto/);
    assert.match(service, /GetSubjectSummaryAsync/);
    assert.match(service, /session\.IsFinalized/);
    assert.match(service, /enrollment\.CampusId == campusId\.Value/);
    assert.match(service, /grouped\.CountAsync\(cancellationToken\)/);
    assert.match(service, /\.Skip\(\(query\.Page - 1\) \* query\.PageSize\)/);
    assert.match(api, /HttpGet\("subject-summary"\)/);
    assert.match(api, /Authorize\(Roles = "TenantAdmin,Principal,VicePrincipal"\)/);
    for (const id of ['attendanceCoverageRows', 'attendanceCoverageSearch', 'attendanceCoverageSort',
        'attendanceCoverageCsv', 'attendanceCoveragePrevious', 'attendanceCoverageNext'])
        assert.ok(view.includes('id="' + id + '"'));
    assert.match(js, /loadCoverage\(\)/);
    assert.match(js, /request !== coveragePending/);
    assert.match(js, /coverageItems\.map\(x =>/);
    assert.match(js, /row\.map\(csvCell\)/);
    assert.doesNotThrow(() => new Function(js));
});
