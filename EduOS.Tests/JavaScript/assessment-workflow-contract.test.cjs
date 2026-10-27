'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/assessment/workflow.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/AssessmentAdministration/Index.cshtml'), 'utf8');
const dto = fs.readFileSync(path.join(root, 'EduOS.Core/DTOs/Assessment/AssessmentWorkflowDtos.cs'), 'utf8');
const service = fs.readFileSync(path.join(root, 'EduOS.Service/Services/Assessment/AssessmentAdministrationService.cs'), 'utf8');

test('assessment scope uses canonical IDs and exposes publication reference', () => {
    assert.match(js, /assessmentId: String\(s\.assessmentId\)/);
    assert.match(js, /academicBatchId: String\(s\.academicBatchId\)/);
    assert.match(js, /assessmentSubjectId: String\(s\.assessmentSubjectId\)/);
    assert.match(js, /'assessmentSubjectId', 'subjectName'/);
    assert.match(dto, /public Guid AssessmentReference \{ get; set; \}/);
    assert.match(service, /AssessmentReference = x\.exam\.PublicId/);
    assert.doesNotMatch(js, /classId: match\.classId|sectionId: match\.sectionId/);
});

test('mark writes match canonical DTO, preserve rowversion and carry antiforgery', () => {
    assert.match(view, /@Html\.AntiForgeryToken\(\)/);
    assert.match(js, /RequestVerificationToken: token/);
    assert.match(js, /assessmentSubjectId: scope\(\)\.assessmentSubjectId, marks: changedMarks\(\)/);
    assert.match(js, /studentSubjectRegistrationId: Number\(tr\.dataset\.registrationId\)/);
    assert.match(js, /obtainedMarks: value, isAbsent: absent\.checked, isWithheld: withheld\.checked/);
    assert.match(js, /rowVersion: tr\.dataset\.rowVersion \|\| null/);
    assert.match(js, /await loadRoster\(\)/);
    assert.doesNotMatch(js, /items: changes\(\)|studentReference: tr\.dataset/);
});

test('draft preview is distinct from published snapshot and publication is idempotent', () => {
    assert.match(js, /results\/preview', 'POST', body/);
    assert.match(js, /results\?' \+ new URLSearchParams\(body\)/);
    assert.match(js, /results\/publish', 'POST', body/);
    assert.match(js, /clientRequestId: state\.publishKey, assessmentReference: s\.assessmentReference/);
    assert.match(js, /state\.publishKey \|\|= crypto\.randomUUID\(\)/);
    assert.match(js, /mode !== 'preview' \|\| !locked\(selected\(\)\)/);
    assert.match(js, /renderResults\(snapshot, 'published'\)/);
    assert.match(view, /Preview draft/);
    assert.match(view, /View published results/);
    assert.doesNotMatch(js, /results\/generate/);
});

test('reports include withheld status and guard CSV spreadsheet injection', () => {
    assert.match(view, /id="examMarksCsv"/);
    assert.match(view, /id="examResultsCsv"/);
    assert.match(view, /id="examPrint"/);
    assert.match(view, /id="examVisibleStudent"/);
    assert.match(view, /id="examVisibleGuardian"/);
    assert.match(view, /id="examPublishNote"/);
    assert.match(js, /isWithheld: withheld\.checked/);
    assert.match(js, /'Draft preview \(not saved\)'/);
    assert.match(js, /'Published snapshot \(immutable\)'/);
    assert.match(js, /const safe = value => \/\^\[=\+@/);
    assert.match(js, /'assessment-' \+ kind/);
});

test('assessment selector is server-paged, searchable and campus-filtered before counting', () => {
    assert.match(view, /id="examScopeSearch"/);
    assert.match(view, /id="examScopeTerm"/);
    assert.match(view, /id="examScopePrev"/);
    assert.match(view, /id="examScopeNext"/);
    assert.match(view, /id="examScopePageInfo"/);
    assert.match(js, /scopes\/page\?' \+ params/);
    assert.match(js, /state\.scopes = data\.items \|\| \[\]/);
    assert.match(js, /state\.scopeTotal = data\.totalCount/);
    assert.match(js, /pageSize: String\(state\.scopeSize\)/);
    assert.match(js, /state\.scopePage \* state\.scopeSize >= state\.scopeTotal/);
    assert.match(service, /AllowedCampusIds\(tenant\)\.Contains\(x\.exam\.CampusId\)/);
    assert.match(service, /var total = await q\.CountAsync\(ct\)/);
    assert.match(service, /Skip\(\(int\)skip\)\.Take\(pageSize\)/);
});

test('readiness review is paged, scoped and has safe page-only CSV export', () => {
    assert.match(view, /id="examReadinessRows"/);
    assert.match(view, /id="examReadinessCsv"/);
    assert.match(view, /page is not a global publication approval/);
    assert.match(js, /results\/readiness\?' \+ params/);
    assert.match(js, /pageSize: String\(state\.readinessSize\)/);
    assert.match(js, /state\.readiness = data/);
    assert.match(js, /kind === 'readiness'/);
    assert.match(js, /examReadinessPrev/);
    assert.match(js, /examReadinessNext/);
});
