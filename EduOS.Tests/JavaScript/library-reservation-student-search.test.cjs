const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = file => fs.readFileSync(path.join(__dirname, '../..', file), 'utf8');

test('staff reservation search binds active student selection to an authorized paged endpoint', () => {
    const view = read('EduOS.App/Views/Library/Reservations.cshtml');
    const js = read('EduOS.App/wwwroot/js/library/reservations.js');
    const controller = read('EduOS.App/Controllers/Api/LibraryReservationsController.cs');
    const service = read('EduOS.Service/Services/Library/LibraryReservationService.StudentLookup.cs');
    assert.match(view, /id="reservationStudentSearchForm"/);
    assert.match(view, /id="reservationStudent"[^>]*><option/);
    assert.doesNotMatch(view, /placeholder="Student GUID"/);
    assert.match(controller, /\[HttpGet\("eligible-students"\)\]/);
    assert.match(controller, /\[Authorize\(Roles = "TenantAdmin,Principal,Librarian"\)\]/);
    assert.match(js, /\/api\/library\/reservations\/eligible-students\?/);
    assert.match(js, /pageSize: 20/);
    assert.match(js, /sequence !== state\.studentRequest/);
    assert.match(js, /Select a student from the search results/);
    assert.doesNotMatch(js, /innerHTML/);
    assert.match(service, /if \(!CanManage\(\)\)/);
    for (const field of ['student.TenantId == tenant', '!student.IsDeleted', 'student.StatusCode == "Active"',
        'enrollment.TenantId == tenant', 'enrollment.IsCurrent', 'EnrollmentState.Active'])
        assert.ok(service.includes(field), `missing student lookup guard: ${field}`);
    new Function(js);
});
