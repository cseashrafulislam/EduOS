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

test('F10 current-page export and print are fail-closed and reuse displayed authorized rows', () => {
    const js = read('EduOS.App/wwwroot/js/students/student-360.js');
    const view = read('EduOS.App/Views/Students/Student360.cshtml');
    for (const id of ['s360Retry', 's360Export', 's360Print']) assert.ok(view.includes('id="' + id + '"'));
    assert.match(js, /currentTab === tab/);
    assert.match(js, /profileReady && historyReady/);
    assert.match(js, /historyReady = false; currentRows = \[\]/);
    assert.match(js, /const rows = authorizedPage\(\)/);
    assert.match(js, /csvCell/);
    assert.match(js, /replace\(\/"\/g, '""'\)/);
    assert.match(js, /textContent = value/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
    assert.doesNotMatch(js, /method:\s*['"](?:POST|PUT|PATCH|DELETE)['"]/);
});

test('Student 360 page controls fail closed on failed requests, session changes and malformed counts', () => {
    const js = read('EduOS.App/wwwroot/js/students/student-360.js');
    assert.match(js, /!historyReady \|\| !sameSession\(\) \|\| page <= 1/);
    assert.match(js, /!historyReady \|\| !sameSession\(\) \|\| page >= totalPages/);
    assert.match(js, /historyReady && sameSession\(\) && page > 1/);
    assert.match(js, /historyReady && sameSession\(\) && page < totalPages/);
    assert.match(js, /profileController\?\.abort\(\)/);
    assert.match(js, /typeof data\.hasMore !== 'boolean'/);
    assert.match(js, /items\.some\(item => !item/);
    assert.match(js, /items\.length > data\.totalCount/);
    assert.match(js, /\+\+token; busy = false; historyReady = false/);
});

test('F10 subject registration history reuses tenant-authorized bounded canonical query', () => {
    const page = read('EduOS.App/Views/Students/Student360.cshtml');
    const js = read('EduOS.App/wwwroot/js/students/student-360.js');
    const api = read('EduOS.App/Controllers/Api/StudentSubjectHistoryController.cs');
    const query = read('EduOS.Service/Services/Academic/StudentSubjectHistoryQueryService.cs');
    assert.match(page, /data-s360-tab="subjects"/);
    assert.match(js, /\/api\/student-subjects\/students\//);
    for (const field of ['registeredAt', 'subjectCode', 'subjectName', 'rollNo',
        'registrationState', 'creditHoursSnapshot', 'approvedAt'])
        assert.ok(js.includes("'" + field + "'"), field);
    assert.match(api, /Authorize\(Roles = "TenantAdmin"\)/);
    assert.match(api, /RequireModule\("ACADEMIC"\)/);
    assert.match(query, /user\.TenantId/);
    assert.match(query, /\.Skip\(\(page - 1\) \* pageSize\)\.Take\(pageSize\)/);
    assert.match(js, /pageSize: '20'/);
    assert.match(js, /currentTab === tab/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
});

test('F10 CSV export neutralizes spreadsheet formulas after invisible or whitespace prefixes', () => {
    const js = read('EduOS.App/wwwroot/js/students/student-360.js');
    const match = js.match(/    function csvCell\\(value\\) \\{([\\s\\S]*?)\\n    \\}/);
    assert.ok(match, 'CSV serializer must exist');
    const csvCell = new Function('value', match[1]);
    const payloads = ['=1+1', '  =1+1', String.fromCharCode(9) + '=1+1',
        String.fromCharCode(10) + '=1+1', String.fromCharCode(0xFEFF) + '=1+1',
        String.fromCharCode(0x200B) + '=1+1',
        String.fromCharCode(0x2060) + '@SUM(1,2)', '-1+2'];
    for (const payload of payloads)
        assert.ok(csvCell(payload).startsWith(`"'`), JSON.stringify(payload));
    assert.equal(csvCell('Normal student'), '"Normal student"');
    assert.equal(csvCell('Student "A"'), '"Student ""A"""');
});
