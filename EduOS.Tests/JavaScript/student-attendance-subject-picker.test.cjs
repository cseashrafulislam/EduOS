const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/Attendance/Index.cshtml'), 'utf8');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/attendance/index.js'), 'utf8');
const api = fs.readFileSync(path.join(root, 'EduOS.App/Controllers/Api/StudentAttendanceController.cs'), 'utf8');
test('subject attendance has scoped paginated selection and server route', () => {
    for (const id of ['attendanceSubject', 'attendanceSubjectSearch',
        'attendanceSubjectPrevious', 'attendanceSubjectNext', 'attendanceSubjectPage'])
        assert.match(view, new RegExp('id="' + id + '"'));
    assert.match(js, /\/api\/student-attendance\/subjects\?/);
    assert.match(js, /subjectOfferingId: selected \? Number\(selected\) : null/);
    assert.match(js, /if \(scope\.subjectOfferingId\) params\.set\('subjectOfferingId'/);
    assert.match(js, /request !== subjectRequest/);
    assert.match(api, /\[HttpGet\("subjects"\)\]/);
});
