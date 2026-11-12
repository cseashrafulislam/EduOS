const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('F10 Student 360 is read-only and independently role/module protected', () => {
    const controller = read('EduOS.App/Controllers/StudentsController.cs');
    assert.match(controller, /\[Authorize\(Roles = "TenantAdmin,AdmissionOfficer"\)\]/);
    assert.match(controller, /\[RequireModule\("STUDENT"\)\]/);
    assert.match(controller, /\[Authorize\(Roles = "TenantAdmin"\)\][\s\S]*?IActionResult Student360\(Guid reference\)/);
    assert.match(controller, /reference == Guid.Empty/);
    const page = read('EduOS.App/Views/Students/Student360.cshtml');
    assert.match(page, /student-360\.js/);
    assert.match(page, /data-s360-tab="enrollment"/);
    assert.match(page, /data-s360-tab="lifecycle"/);
    assert.match(page, /data-s360-tab="guardian"/);
    const directory = read('EduOS.App/Views/Students/Index.cshtml');
    assert.match(directory, /data-can-student360="@\(User.IsInRole\("TenantAdmin"\)/);
    const list = read('EduOS.App/wwwroot/js/students/index.js');
    assert.match(list, /dataset\.canStudent360 === 'true'/);
    assert.match(list, /Students\/Student360\?reference=/);
});
test('F10 tabs reuse canonical paged authorized APIs and fail closed on session change', () => {
    const js = read('EduOS.App/wwwroot/js/students/student-360.js');
    for (const endpoint of ['/api/academic-enrollments/students/', '/api/students/',
        '/api/student-guardians/students/']) assert.ok(js.includes(endpoint));
    assert.match(js, /pageSize: '20'/);
    assert.match(js, /sameSession\(\)/);
    assert.match(js, /controller\?\.abort\(\)/);
    assert.match(js, /cache: 'no-store'/);
    assert.match(js, /items\.length > 20/);
    assert.match(js, /textContent =/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
    assert.doesNotMatch(js, /method:\s*['"](?:POST|PUT|DELETE|PATCH)['"]/);
    assert.match(js, /Unavailable \(not zero\)/);
    for (const [p,role] of [
        ['EduOS.App/Controllers/Api/StudentsController.cs', 'TenantAdmin,AdmissionOfficer'],
        ['EduOS.App/Controllers/Api/StudentEnrollmentHistoryController.cs', 'TenantAdmin'],
        ['EduOS.App/Controllers/Api/StudentLifecycleTimelineController.cs', 'TenantAdmin'],
        ['EduOS.App/Controllers/Api/StudentGuardiansController.cs', 'TenantAdmin']
    ]) assert.ok(read(p).includes('Authorize(Roles = "' + role + '")'), p);
});
