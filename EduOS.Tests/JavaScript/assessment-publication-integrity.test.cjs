'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const js = read('EduOS.App/wwwroot/js/assessment/publication-register.js');
const view = read('EduOS.App/Views/AssessmentAdministration/Index.cshtml');
const api = read('EduOS.App/Controllers/Api/AssessmentAdministrationApiController.cs');

test('historical snapshot diagnostics are wired to authorized, bounded API and shared workspace', () => {
    assert.doesNotThrow(() => new Function(js));
    assert.match(api, /HttpGet\("results\/publications\/\{publicationId:long\}\/integrity"\)/);
    assert.match(api, /SearchPublishedSnapshotIssuesAsync/);
    assert.match(view, /id="examIntegrityRows"/);
    assert.match(view, /id="examIntegrityLoad"/);
    assert.match(view, /id="examIntegrityCsv"/);
    assert.match(js, /pageSize: String\(state\.integritySize\)/);
    assert.match(js, /encodeURIComponent\(version\.id\) \+ '\/integrity\?'/);
    assert.match(js, /!finalized\(version\)/);
});

test('integrity rows are DOM-encoded and CSV is limited to authorized page', () => {
    assert.match(js, /tr\.append\(cell\(row\.summaryId\), cell\(row\.studentEnrollmentId\), cell\(row\.issueCode\)\)/);
    assert.match(js, /data\.items\.map\(row => \[data\.publicationId/);
    assert.match(js, /replace\(\/"\/g, '""'\)/);
    assert.match(js, /'-page-' \+ data\.page/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
});
