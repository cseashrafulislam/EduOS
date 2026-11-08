'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const base = path.join(__dirname, '..', '..');
const read = file => fs.readFileSync(path.join(base, file), 'utf8');

test('A4 interface provides bilingual policy, safe guardian notice and independent retention review', () => {
    const view = read('EduOS.App/Views/StudentSupport/Counseling.cshtml');
    const js = read('EduOS.App/wwwroot/js/students/student-counseling.js');
    assert.match(view, /data-admin=/);
    assert.match(view, /Legal hold — block redaction/);
    assert.match(view, /Emergency guidance \(Bangla\)/);
    assert.match(view, /Pending confidential redaction approvals/);
    assert.match(view, /Notify consenting guardian \(no case details\)/);
    assert.match(js, /policy-campuses/);
    assert.match(js, /policy-requests/);
    assert.match(js, /redaction-requests/);
    assert.match(js, /notify-guardian/);
    assert.match(js, /redaction\/review/);
    assert.match(js, /casePolicyDetails'\)\.textContent = ''/);
    assert.match(js, /credentials: 'same-origin', cache: 'no-store'/);
    assert.doesNotMatch(js, /\.innerHTML\s*=/);
});

test('A4 access and approvals are live-role protected and no private case text enters generic notifications', () => {
    const privacy = read('EduOS.Service/Services/Students/CounselingPrivacyService.cs');
    const workflow = read('EduOS.Service/Services/Students/CounselingWorkflowService.cs');
    const api = read('EduOS.App/Controllers/Api/CounselingCasesController.cs');
    const migration = read('EduOS.Persistence/Migrations/20261108152000_CounselingPrivacySafeguarding.cs');
    const sql = read('.github/workflows/sqlserver-migration.yml');
    assert.match(privacy, /CentralPrivilegedAccess\.HasRoleAsync/);
    assert.match(privacy, /proposed\.ProposedByUserId == user\.UserId/);
    assert.match(privacy, /row\.RedactionRequestedByUserId == user\.UserId/);
    assert.match(privacy, /policy != null && !policy\.LegalHold/);
    assert.match(privacy, /foreach \(var item in events\) item\.ProtectedNote = ""/);
    assert.match(workflow, /row\.GuardianContactConsented/);
    assert.match(workflow, /member\.Status == MembershipStatus\.Active/);
    assert.match(workflow, /SourceType = "CounselingCaseEvent"/);
    assert.doesNotMatch(workflow.match(/db\.Notifications\.Add\([\s\S]*?\}\);/)?.[0] || '',
        /PrivateSummary|ProtectedSummary|reference|StudentId/);
    assert.match(api, /\[HttpPost\("\{reference:guid\}\/redaction\/review"\)\]/);
    assert.match(migration, /Cannot drop active counseling privacy policies or redaction evidence/);
    assert.match(sql, /FullyQualifiedName~SqlServerCounselingSafeguardingIntegrationTests/);
});
