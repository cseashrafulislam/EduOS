'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const js = read('EduOS.App/wwwroot/js/assessment/result-rescrutiny.js');
const view = read('EduOS.App/Views/AssessmentAdministration/ResultRescrutiny.cshtml');
const api = read('EduOS.App/Controllers/Api/AssessmentAdministrationApiController.cs');
const service = read('EduOS.Service/Services/Assessment/AssessmentAdministrationService.ResultCorrections.cs');
const db = read('EduOS.Persistence/Context/EduOSDbContext.cs');
const migration = read('EduOS.Persistence/Migrations/20261029150000_ResultCorrectionEvents.cs');

test('accepted re-scrutiny has bounded accessible mark and component correction UI', () => {
    assert.doesNotThrow(() => new Function(js));
    assert.match(view, /id="rescrutinyCorrectionForm"/);
    assert.match(view, /id="rescrutinyCorrectionSubjects"/);
    assert.match(view, /id="rescrutinyCorrectionApply"/);
    assert.match(js, /results\/rescrutiny\/' \+\s*encodeURIComponent\(state\.correctionCase\) \+ '\/correction'/);
    assert.match(js, /publicationRowVersion: choice\.evidence\.publicationRowVersion/);
    assert.match(js, /clientRequestId: state\.correctionKey/);
    assert.match(js, /state\.correctionBusy/);
    assert.match(js, /data\.items \|\| \[\]/);
    assert.match(js, /document\.createElement/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
});

test('server correction is independently authorized, single-transaction and version preserving', () => {
    assert.match(api, /HttpPost\("results\/rescrutiny\/\{caseReference:guid\}\/correction"\)/);
    assert.match(api, /Authorize\(Roles = "TenantAdmin,Principal,VicePrincipal,ExamController"\)/);
    assert.match(service, /HasReconciledRescrutinyLedgerAsync/);
    assert.match(service, /HasValidRescrutinyEvidenceAsync/);
    assert.match(service, /AcceptedForCorrection/);
    assert.match(service, /ActorUserId == _user\.UserId/);
    assert.match(service, /AssessmentComponentMarkPolicy\.Evaluate/);
    assert.match(service, /new ResultCorrectionRollbackException/);
    assert.match(service, /ResultPublicationState\.Withdrawn/);
    assert.match(service, /VersionNo = version/);
    assert.match(service, /NewPublicationId = replacement\.Id/);
    assert.match(service, /StudentResultSummary/);
    assert.match(db, /or ResultCorrectionEvent/);
    assert.match(migration, /TenantId", "PreviousPublicationId/);
    assert.match(migration, /TenantId", "NewPublicationId/);
    assert.match(migration, /CK_ResultCorrectionEvents_Version/);
});
