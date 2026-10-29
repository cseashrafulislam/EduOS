const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const read = p => fs.readFileSync(path.join(__dirname, '../../', p), 'utf8');

const ui = read('EduOS.App/wwwroot/js/assessment/transcripts.js');
const view = read('EduOS.App/Views/AssessmentAdministration/Transcripts.cshtml');
const service = read('EduOS.Service/Services/Assessment/AssessmentAdministrationService.TranscriptAcademicRecord.cs');

test('academic record preview is a separate read-only published course ledger', () => {
    assert.match(view, /transcriptPreviewRecord/);
    assert.match(view, /not a signed transcript/);
    assert.match(ui, /transcripts\/academic-record\//);
    assert.match(ui, /state\.recordRequest/);
    assert.match(ui, /state\.record = null/);
    assert.doesNotMatch(ui, /innerHTML|insertAdjacentHTML|localStorage|sessionStorage/);
});

test('published transcript course history enforces tenant, campus and version gates', () => {
    assert.match(service, /CanPublish\(\)/);
    assert.match(service, /IsRestrictedCampusManager\(\)/);
    assert.match(service, /CanAccessCampusAsync/);
    assert.match(service, /publication\.State == ResultPublicationState\.Published/);
    assert.match(service, /GetPublicationEvidenceAsync/);
    assert.match(service, /HasCourseGradeSnapshots/);
    assert.match(service, /_courseSnapshots\.GetQueryable\(\)/);
    assert.match(service, /Take\(501\)/);
});
