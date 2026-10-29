'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const service = read('EduOS.Service/Services/Students/StudentImportExecutionService.AcademicRepair.cs');
const api = read('EduOS.App/Controllers/Api/StudentImportsController.cs');
const ui = read('EduOS.App/Views/StudentImports/Index.cshtml');
const js = read('EduOS.App/wwwroot/js/students/import-preview.js');

test('historical repair is scoped to a tenant admin, one committed import, and unchanged enrollments', () => {
    assert.match(service, /if \(!CanManage\(\)\)/);
    assert.match(service, /GetIntegrityAsync\(importReference, token\)/);
    assert.match(service, /history\.Length != 1/);
    assert.match(service, /StudentResultSummaries/);
    assert.match(service, /IsCurrent/);
    assert.match(service, /AcademicRepairScope/);
    assert.match(service, /PlanHash != expectedPlanHash/);
});
test('repair serializes with regular admissions and never edits historical results or students', () => {
    assert.match(service, /OrderBy\(x => x\.Id\)\.ToListAsync\(token\)/);
    assert.match(service, /batch\.UpdatedAt = now/);
    assert.match(service, /IsolationLevel\.ReadCommitted/);
    assert.match(service, /occupied\.GetValueOrDefault\(offering\.Id\).*newByOffering\[offering\.Id\]/);
    assert.match(service, /StudentSubjectRegistration\s*\{/);
    assert.doesNotMatch(service, /StudentResultSummaries\.(Update|Remove)|StudentEnrollments\.(Update|Remove)/);
});
test('UI requires explicit preview, confirmation and anti-forgery-protected idempotent POST', () => {
    assert.match(api, /\[AutoValidateAntiforgeryToken\]/);
    assert.match(api, /\[Authorize\(Roles = "TenantAdmin"\)\]/);
    assert.match(api, /\[HttpPost\("\{reference:guid\}\/academic-repair"\)\]/);
    assert.match(ui, /id="studentImportRepairAcademics"/);
    assert.match(js, /academicRepairPlan\.canApply/);
    assert.match(js, /confirm\('Reconcile '/);
    assert.match(js, /academicRepairRequestId = crypto\.randomUUID\(\)/);
    assert.match(js, /formData\.append\('expectedPlanHash', academicRepairPlan\.planHash\)/);
    assert.match(js, /send\('\/api\/student-imports\/'/);
});
