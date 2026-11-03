const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..', '..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const grants = read('EduOS.App/wwwroot/js/research/research-grants.js');
const accreditation = read('EduOS.App/wwwroot/js/research/accreditation.js');
const grantView = read('EduOS.App/Views/ResearchGrants/Index.cshtml');
const accreditationView = read('EduOS.App/Views/Accreditation/Index.cshtml');
const help = read('EduOS.App/wwwroot/js/platform/page-context-help.js');
const evidenceService = read('EduOS.Service/Services/Research/ResearchEvidenceFileService.cs');
const accreditationApi = read('EduOS.App/Controllers/Api/AccreditationApiController.cs');
const outputService = read('EduOS.Service/Services/Research/ResearchGrantOutputService.cs');
const grantApi = read('EduOS.App/Controllers/Api/ResearchGrantsApiController.cs');

test('research grant UI scopes session, clears stale details and retains retry keys', () => {
    assert.match(grants, /sessionValid\(\)/);
    assert.match(grants, /detailGeneration\+\+/);
    assert.match(grants, /generation !== detailGeneration/);
    assert.match(grants, /clientRequestId: actionRetry \|\|= crypto\.randomUUID\(\)/);
    assert.match(grants, /'X-EduOS-Tenant-Id'/);
    assert.doesNotMatch(grants, /\.innerHTML\s*=/);
    assert.match(grantView, /Html\.AntiForgeryToken\(\)/);
});

test('research output claims require independent review and stay tenant/campus scoped', () => {
    assert.match(grantApi, /HttpPost\("\{reference:guid\}\/outputs"\)/);
    assert.match(grantApi, /HttpPost\("\{reference:guid\}\/outputs\/\{outputReference:guid\}\/review"\)/);
    assert.match(outputService, /IsolationLevel\.Serializable/);
    assert.match(outputService, /access\.CampusAsync\(grant\.CampusId, ct\)/);
    assert.match(outputService, /DecisionByUserId = \{access\.UserId\}/);
    assert.match(outputService, /CryptographicOperations\.FixedTimeEquals/);
    assert.match(outputService, /ResearchWorkflowAccess\.ResearchCoordinator\(roles\)/);
    assert.match(outputService, /p\.CampusId = \{1\}/);
    assert.match(grants, /outputRetry \|\|= crypto\.randomUUID\(\)/);
    assert.match(grants, /reviewRetry \|\|= crypto\.randomUUID\(\)/);
    assert.match(grantView, /id="grantOutputReviewForm"/);
    assert.match(grantView, /id="grantOutcomeReportRows"/);
    assert.doesNotMatch(grants, /\.innerHTML\s*=/);
    assert.match(help, /অপেক্ষমাণ ফলাফল থাকলে সমাপ্তি বন্ধ থাকে/);
});

test('accreditation UI retains version, reviewer and report scope without HTML injection', () => {
    assert.match(accreditation, /framework\.rowVersion/);
    assert.match(accreditation, /declaresNoConflict/);
    assert.match(accreditation, /reportGeneration\+\+/);
    assert.match(accreditation, /accCsv.*removeAttribute\('href'\)/);
    assert.match(accreditation, /X-EduOS-Session-Reference/);
    assert.doesNotMatch(accreditation, /\.innerHTML\s*=/);
    assert.match(accreditationView, /Html\.AntiForgeryToken\(\)/);
    assert.match(help, /'\/accreditation\/index':/);
    assert.match(help, /'\/researchgrants\/index':/);
});

test('accreditation private evidence is guarded by reviewer assignment and immutable version', () => {
    assert.match(accreditationApi, /RequireModule\("ACCREDITATION"\)/);
    assert.match(accreditationApi, /AutoValidateAntiforgeryToken/);
    assert.match(accreditationApi, /HttpGet\("assessments\/\{reference:guid\}\/files\/\{fileReference:guid\}\/download"\)/);
    assert.match(evidenceService, /p\.ReviewerUserId == actor/);
    assert.match(evidenceService, /e\.RevisionNo <= \{4\}/);
    assert.match(evidenceService, /e\.ProposalId IS NULL/);
    assert.match(accreditationView, /id="accFileForm"/);
    assert.match(accreditation, /fileRetry \|\|= crypto\.randomUUID\(\)/);
    assert.match(accreditation, /accFileRows/);
    assert.doesNotMatch(accreditation, /innerHTML\s*=/);
});
