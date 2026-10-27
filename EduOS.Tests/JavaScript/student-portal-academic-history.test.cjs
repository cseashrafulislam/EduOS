'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/student-portal/index.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/StudentPortal/Index.cshtml'), 'utf8');
const api = fs.readFileSync(path.join(root, 'EduOS.App/Controllers/Api/SelfServicePortalController.cs'), 'utf8');

test('linked learner portal exposes enrollment and subject history through authorized paged endpoints', () => {
    for (const section of ['enrollments', 'subjects']) {
        assert.ok(view.includes('data-portal-tab="' + section + '"'));
        assert.ok(js.includes("'" + section + "'"));
        assert.ok(api.includes('students/{reference:guid}/' + section));
    }
    assert.match(api, /\[Authorize\(Roles = "Student,Guardian,Parent"\)\]/);
    assert.match(api, /\[RequireModule\("ACADEMIC"\)\]/);
    assert.match(js, /data\.items/);
    assert.match(js, /token !== latestRequest/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
});

test('history renders original enrollment and subject credit snapshots, not current roll aliases', () => {
    for (const name of ['rollNo', 'registrationNo', 'enrollmentDate', 'isCurrentEnrollment',
        'creditHoursSnapshot', 'registrationState', 'registeredAt']) assert.ok(js.includes(name), name);
    assert.match(js, /Original roll/);
    assert.match(js, /subject labels reflect the current catalog/);
});
