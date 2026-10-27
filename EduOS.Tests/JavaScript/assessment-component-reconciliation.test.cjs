'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('component reconciliation is visible and exported from the existing scoped roster', () => {
    const js = read('EduOS.App/wwwroot/js/assessment/component-marks.js');
    const view = read('EduOS.App/Views/AssessmentAdministration/ComponentMarks.cshtml');
    assert.doesNotThrow(() => new Function(js));
    assert.match(js, /student\.reconciliationStatus/);
    assert.match(js, /student\.expectedSubjectMarks/);
    assert.match(js, /student\.subjectObtainedMarks/);
    assert.match(js, /td\.textContent/);
    assert.match(js, /s\.reconciliationStatus/);
    assert.match(view, /<th>Reconciliation<\/th>/);
    assert.match(view, /@Html\.AntiForgeryToken\(\)/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
});
