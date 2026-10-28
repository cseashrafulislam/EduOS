'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const js = read('EduOS.App/wwwroot/js/assessment/grade-distribution.js');
const view = read('EduOS.App/Views/AssessmentAdministration/GradeDistribution.cshtml');

test('grade distribution loads authorized finalized versions and no private learner roster', () => {
    assert.doesNotThrow(() => new Function(js));
    assert.match(js, /scopes\/page\?' \+ params/);
    assert.match(js, /results\/publications\?' \+ params/);
    assert.match(js, /results\/grade-distribution\?publicationId=/);
    assert.match(js, /state\.report = report/);
    assert.match(view, /id="gradeTotals"/);
    assert.doesNotMatch(view, /Student name|Guardian name/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
});

test('CSV uses only server-authorized aggregate groups and prevents formulas', () => {
    assert.match(js, /report\.grades \|\| \[\]/);
    assert.match(js, /\^\[=\+@/);
    assert.match(js, /link\.download = 'grade-distribution-version-'/);
    assert.match(js, /td\.textContent/);
});
