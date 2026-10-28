'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('substitution scheduling uses date-filtered SQL-paged routine choices instead of a truncated timetable', () => {
    const js = read('EduOS.App/wwwroot/js/academic/instruction.js');
    const view = read('EduOS.App/Views/AcademicInstruction/Index.cshtml');
    assert.doesNotThrow(() => new Function(js));
    assert.match(js, /substitution-routines\?'\+query/);
    assert.match(js, /pageSize:'50'/);
    assert.match(js, /state\.routineRequestSeq/);
    assert.match(js, /get\('substitutionDate'\)\.value!==date/);
    assert.doesNotMatch(js, /api\('\/api\/academic-routines\/batches\//);
    assert.match(view, /id="substitutionRoutineSearch"[^>]*maxlength="100"/);
    assert.match(view, /id="substitutionRoutinePrevious"/);
    assert.match(view, /id="substitutionRoutineNext"/);
    assert.match(view, /id="substitutionDate"/);
});

test('substitution mutations retain CSRF, idempotency and concurrency-safe cancellation', () => {
    const js = read('EduOS.App/wwwroot/js/academic/instruction.js');
    const controller = read('EduOS.App/Controllers/Api/AcademicInstructionController.cs');
    assert.match(js, /headers\.RequestVerificationToken = token/);
    assert.match(js, /clientRequestId: state\.substitutionRequestId/);
    assert.match(js, /rowVersion: row\.rowVersion/);
    assert.match(controller, /Authorize\(Roles = "TenantAdmin,Principal,VicePrincipal"\)/);
    assert.match(controller, /GetSubstitutionRoutineChoicesAsync/);
});
