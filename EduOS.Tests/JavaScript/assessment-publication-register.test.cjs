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

test('publication history and tabulation are integrated in the existing authorized results workspace', () => {
    assert.doesNotThrow(() => new Function(js));
    assert.match(view, /id="examPublicationRegister"/);
    assert.match(view, /id="examHistoryVersion"/);
    assert.match(view, /id="examTabulationRows"/);
    assert.match(view, /publication-register\.js/);
    assert.match(api, /\[HttpGet\("results\/publications"\)\]/);
    assert.match(api, /\[HttpGet\("results\/tabulation"\)\]/);
    assert.match(api, /Authorize\(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController"\)/);
    assert.match(js, /results\/publications\?' \+ params/);
    assert.match(js, /results\/tabulation\?' \+ params/);
});

test('withdrawal requires reason, rowversion, confirmation and a scoped server action', () => {
    assert.match(js, /rowVersion: version\.rowVersion/);
    assert.match(js, /Type WITHDRAW to confirm/);
    assert.match(js, /\/withdraw', 'POST'/);
    assert.match(js, /RequestVerificationToken: token/);
    assert.match(js, /await loadHistory\(1\)/);
    assert.match(js, /assessment-publication-withdrawn/);
    assert.match(read('EduOS.App/wwwroot/js/assessment/workflow.js'), /assessment-publication-withdrawn/);
});

test('tabulation is page-only, uses safe DOM and spreadsheet-safe CSV', () => {
    assert.match(js, /pageSize: String\(state\.tabulationSize\)/);
    assert.match(js, /td\.textContent/);
    assert.match(js, /replace\(\/"\/g, '""'\)/);
    assert.match(js, /const safe = value =>/);
    assert.match(js, /data\.items\.map\(row =>/);
    assert.match(js, /page-' \+ data\.page/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
});
