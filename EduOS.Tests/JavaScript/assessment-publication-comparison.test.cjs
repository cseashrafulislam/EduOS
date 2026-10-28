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

test('comparison is integrated into the existing authorized results workspace', () => {
    assert.doesNotThrow(() => new Function(js));
    assert.match(view, /id="examComparisonSection"/);
    assert.match(view, /id="examComparisonRows"/);
    assert.match(view, /id="examComparisonChangedOnly"/);
    assert.match(api, /\[HttpGet\("results\/publications\/compare"\)\]/);
    assert.match(js, /results\/publications\/compare\?' \+ params/);
    assert.match(js, /pageSize: String\(state\.comparisonSize\)/);
    assert.match(js, /state\.comparison = data/);
    assert.match(js, /clearComparison\(\)/);
});

test('comparison exports only the current page with spreadsheet formula protection', () => {
    assert.match(js, /data\.items\.map\(row =>/);
    assert.match(js, /\^\[=\+@/);
    assert.match(js, /link.download = 'comparison-'/);
    assert.match(js, /'-page-'/);
    assert.match(js, /td\.textContent/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
});
