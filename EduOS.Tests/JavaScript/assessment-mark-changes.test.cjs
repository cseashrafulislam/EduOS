'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');
const js = read('EduOS.App/wwwroot/js/assessment/mark-changes.js');
const view = read('EduOS.App/Views/AssessmentAdministration/Index.cshtml');

test('mark change register is scoped, bounded, explicit and page-only CSV', () => {
    assert.match(js, /assessmentId, academicBatchId, assessmentSubjectId/);
    assert.match(js, /pageSize: state\.size/);
    assert.match(js, /credentials: 'same-origin'/);
    assert.match(js, /state\.items\.map/);
    assert.match(js, /\^\[=\+@/);
    assert.match(js, /addEventListener\('click', \(\) => load\(1\)\)/);
    assert.match(view, /id="examMarkChangesRows"/);
    assert.match(view, /assessment\/mark-changes\.js/);
});

test('mark history does not interpolate untrusted audit data as HTML', () => {
    assert.doesNotMatch(js, /innerHTML|insertAdjacentHTML/);
    assert.match(js, /td\.textContent/);
    assert.match(js, /state\.request/);
});
