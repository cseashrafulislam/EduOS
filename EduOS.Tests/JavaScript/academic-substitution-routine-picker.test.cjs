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
    assert.doesNotMatch(js, /\/api\/academic-routines\/batches\/'\s*\+\s*batchId\s*\+\s*'\/page/);
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

test('substitution teacher selection uses batch-authorized SQL pagination with safe navigation', () => {
    const js = read('EduOS.App/wwwroot/js/academic/instruction.js');
    const view = read('EduOS.App/Views/AcademicInstruction/Index.cshtml');
    const service = read('EduOS.Service/Services/Academic/AcademicInstructionService.cs');
    assert.match(js, /\/instructors\/page\?' \+ query/);
    assert.match(js, /teacherRequestSeq/);
    assert.match(js, /state\.teacherPage/);
    assert.match(js, /pageSize: '50'/);
    assert.doesNotMatch(js, /academic-routines\/instructors\?/);
    assert.match(view, /id="substitutionTeacherPrevious"/);
    assert.match(view, /id="substitutionTeacherNext"/);
    assert.match(view, /id="substitutionTeacherPage"/);
    assert.match(js, /get\('substitutionTeacherSearch'\)\?\.addEventListener\('keydown'/);
    assert.match(service, /EmployeeCampusAssignment/);
    assert.match(service, /campusAssignment\.EffectiveFrom <= request\.Date/);
    assert.match(service, /AllowedManagerBatchIds\(tenant\)\.Contains\(batch\.Id\)/);
});
