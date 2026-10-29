'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = name => fs.readFileSync(path.join(__dirname, '../../', name), 'utf8');
const script = read('EduOS.App/wwwroot/js/assessment/workflow.js');
const view = read('EduOS.App/Views/AssessmentAdministration/Index.cshtml');
const service = read('EduOS.Service/Services/Assessment/AssessmentAdministrationService.Setup.cs');

test('credit inclusion is a permission-gated and draft-only explicit action', () => {
    assert.match(view, /@if \(canPublish\)/);
    assert.match(view, /id="examCountsTowardCgpa"[^>]*disabled/);
    assert.match(view, /id="examSaveCreditPolicy"[^>]*disabled/);
    assert.match(script, /if \(!canPublish\) return;\s*const request = \+\+state\.creditRequest/);
    assert.match(script, /const draft = Number\(detail\.state\) === 1/);
    assert.match(script, /el\('examSaveCreditPolicy'\)\?\.addEventListener\('click', saveCreditPolicy\)/);
});

test('credit inclusion preserves assessment definition, optimistic lock and anti-forgery', () => {
    assert.match(script, /detail\.reference\), 'PUT', \{/);
    assert.match(script, /clientRequestId: crypto\.randomUUID\(\), campusId: detail\.campusId/);
    assert.match(script, /gradeSchemeId: detail\.gradeSchemeId, name: detail\.name, code: detail\.code/);
    assert.match(script, /startDate: detail\.startDate, endDate: detail\.endDate/);
    assert.match(script, /remarks: detail\.remarks, rowVersion: detail\.rowVersion/);
    assert.match(script, /RequestVerificationToken: token/);
    assert.match(service, /row\.State != AssessmentState\.Draft/);
    assert.match(service, /request\.CountsTowardCgpa && !await _gradeSchemes/);
});
