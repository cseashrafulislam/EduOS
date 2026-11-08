'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const base = path.join(__dirname, '..', '..');
const file = name => fs.readFileSync(path.join(base, name), 'utf8');

test('A3 aid form keeps antiforgery, server-enrollment selection and tenant-bound session checks', () => {
    const view = file('EduOS.App/Views/StudentSupport/Scholarships.cshtml');
    const js = file('EduOS.App/wwwroot/js/students/student-scholarships.js');
    assert.match(view, /AntiForgeryToken/);
    assert.match(view, /@L\("Scholarships & Fee Assistance"/);
    assert.match(js, /student-support-directory\/enrollments/);
    assert.match(js, /student-support-directory\/aid-rules/);
    assert.match(js, /RequestVerificationToken/);
    assert.match(js, /safeSession\(\)/);
    assert.match(js, /visibilitychange/);
    assert.match(js, /querySelectorAll\('button,input,select,textarea'\)/);
    assert.match(js, /credentials: 'same-origin', cache: 'no-store'/);
    assert.match(js, /evidence-download/);
    assert.match(js, /evidence-upload/);
    assert.match(js, /new FormData\(\)/);
    assert.match(js, /student-scholarships\/reconciliation/);
    assert.match(js, /\['Submitted', 'Approved', 'Awarded'\]\.includes\(state\.selected\?\.state\)/);
    assert.match(view, /Review private evidence \(audited\)/);
    assert.match(view, /Financial award reconciliation/);
    assert.match(view, /Invoice credit history/);
    assert.match(js, /invoice-credits/);
    assert.doesNotMatch(js, /\.innerHTML\s*=/);
});

test('A4 keeps confidential narratives outside GET and never builds HTML from notes', () => {
    const view = file('EduOS.App/Views/StudentSupport/Counseling.cshtml');
    const js = file('EduOS.App/wwwroot/js/students/student-counseling.js');
    const api = file('EduOS.App/Controllers/Api/CounselingCasesController.cs');
    const svc = file('EduOS.Service/Services/Students/CounselingWorkflowService.cs');
    assert.match(view, /AntiForgeryToken/);
    assert.match(view, /@L\("Student Counseling & Safeguarding"/);
    assert.match(js, /\/private-view', 'POST'/);
    assert.match(js, /casePrivateSummary'\)\.textContent/);
    assert.match(js, /visibilitychange/);
    assert.match(js, /casePrivatePanel'\)\.classList\.add\('d-none'\)/);
    assert.doesNotMatch(js, /\.innerHTML\s*=/);
    assert.match(api, /\[HttpPost\("\{reference:guid\}\/private-view"\)\]/);
    assert.match(svc, /data protection|IDataProtectionProvider/i);
    assert.match(svc, /row\.OpenedByUserId != user\.UserId/);
    assert.match(svc, /CampusAccess\(row\.CampusId/);
    assert.match(svc, /Safeguarding intake is not staffed for this campus/);
    assert.match(svc, /row\.State == "Requested" \|\| row\.State == "Escalated"/);
    assert.match(svc, /row\.State = "Escalated"; row\.IsUrgent = true;/);
    assert.match(js, /student-support-directory\/counselors/);
    assert.match(js, /student-counseling\/aggregates/);
    assert.match(js, /guardian-contact-consent/);
    assert.match(js, /rowVersion: item\.rowVersion, consent: false/);
    assert.match(view, /Privacy-safe monthly safeguarding summary/);
    assert.match(view, /Eligible campus counselor/);
    assert.doesNotMatch(view, /Assigned counselor user ID/);
    const directory = file('EduOS.Service/Services/Students/StudentSupportDirectoryService.cs');
    assert.match(directory, /CounselorsAsync/);
    assert.match(directory, /TenantMemberships\.Any/);
    assert.match(directory, /UserCampusAccesses\.Any/);
    assert.match(directory, /IsActive && !x\.IsDeleted/);
});

test('A3 cancellation must reconcile finance assignment even before linkage and on retry', () => {
    const svc = file('EduOS.Service/Services/Finance/ScholarshipWorkflowService.cs');
    assert.match(svc, /"Submitted" or "Approved" or "Awarded" or "Cancelled"/);
    assert.match(svc, /x\.ClientRequestId == row\.PublicId/);
    assert.match(svc, /candidates\.Count > 1/);
    assert.match(svc, /assigned\.StudentEnrollmentId != row\.StudentEnrollmentId/);
    assert.match(svc, /finance\.DeactivateStudentDiscountAsync/);
    assert.match(svc, /currentState == "Cancelled"/);
    assert.match(svc, /Award must have its own active, verified financial lineage/);
    assert.match(svc, /canonical == null \|\| canonical\.IsActive/);
    assert.match(svc, /current\.StudentDiscountId = canonical\.Id/);
});

test('A3 financial attribution gets a real disposable SQL Server acceptance gate', () => {
    const workflow = file('.github/workflows/sqlserver-migration.yml');
    assert.match(workflow, /FullyQualifiedName~SqlServerStudentFeeInstallmentPlanIntegrationTests/);
    assert.match(workflow, /FullyQualifiedName~SqlServerFeeDiscountAttributionReviewIntegrationTests/);
    const proof = file('EduOS.Tests/App/SqlServerFeeDiscountAttributionReviewIntegrationTests.cs');
    assert.match(proof, /StudentInvoiceDiscountAttributions/);
    assert.match(proof, /GetDiscountAttributionReviewAsync/);
});
