const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const ui = read('EduOS.App/wwwroot/js/library/digital.js');
const api = read('EduOS.App/Controllers/Api/LibraryDigitalResourcesController.cs');
const service = read('EduOS.Service/Services/Library/LibraryDigitalResourceService.cs');
const view = read('EduOS.App/Views/Library/Digital.cshtml');

test('digital library uses real scanned private upload, authorized download and server-side list', () => {
    assert.match(api, /\[RequireModule\("LIBRARY"\)\]/);
    assert.match(api, /\[AutoValidateAntiforgeryToken\]/);
    assert.match(api, /\[RequestSizeLimit\(6291456\)\]/);
    assert.match(api, /service\.UploadAsync\(/);
    assert.match(api, /service\.DownloadAsync\(/);
    assert.match(api, /service\.UpdateMetadataAsync\(/);
    assert.match(service, /LibraryDigitalResource\.Update/);
    assert.match(service, /documentFiles\.UploadAsync\(/);
    assert.match(service, /DocumentFileService\.IsTenantDocumentKey/);
    assert.match(service, /SHA256\.HashData\(stored\.Content\)/);
    assert.match(service, /x\.TenantId == Tenant/);
    assert.match(service, /MemberCampusIds\(/);
    assert.match(service, /EnrollmentState\.Active/);
    assert.match(service, /EmployeeState\.Active/);
    assert.match(service, /CryptographicOperations\.FixedTimeEquals/);
    assert.match(service, /IsolationLevel\.Serializable/);
    assert.match(service, /LibraryDigitalResource\.Download/);
    assert.match(service, /!definition\.RequiresVerification && !definition\.IsSensitive/);
});

test('digital resource UI is interactive, bounded, bilingual and avoids browser-held authorization', () => {
    assert.match(view, /id="digitalUploadForm"/);
    assert.match(view, /id="digitalRows"/);
    assert.match(view, /aria-labelledby="digitalLibraryTitle"/);
    assert.match(view, /asp-action="Index"/);
    assert.match(view, /CurrentUICulture/);
    assert.match(ui, /new FormData\(\)/);
    assert.match(ui, /crypto\.randomUUID\(\)/);
    assert.match(ui, /RequestVerificationToken/);
    assert.match(ui, /5242880/);
    assert.match(ui, /credentials: 'same-origin'/);
    assert.match(ui, /requestSequence/);
    assert.match(ui, /download\(resource, button\)/);
    assert.match(ui, /resource\.fileName/);
    assert.match(ui, /digitalEditDialog/);
    assert.match(ui, /expiresOn/);
    assert.match(view, /id="digitalEditForm"/);
    assert.match(view, /id="digitalExpiresOn"/);
    assert.match(ui, /revoke\(resource, button\)/);
    assert.doesNotMatch(ui, /innerHTML\s*=/);
});

test('digital resource export never exposes storage keys or student identity', () => {
    const writer = read('EduOS.App/Controllers/Api/LibraryDigitalCsvWriter.cs');
    assert.match(api, /\[Authorize\(Roles = "TenantAdmin"\)\][\s\S]*?Export/);
    assert.match(api, /service\.ListAsync\(/);
    assert.match(writer, /UTF8Encoding\(true\)/);
    assert.match(writer, /TrimStart/);
    assert.doesNotMatch(writer, /StorageKey|StudentId|PersonId|Sha256/);
});

test('digital library scrubs private rows, form state and pending bytes on tenant/session changes', () => {
    assert.match(ui, /data(?:set)?\.authSessionReference|dataset\.authSessionReference/);
    assert.match(ui, /dataset\.authTenantId/);
    assert.match(ui, /EduOSSessionTransitionPending/);
    assert.match(ui, /new AbortController\(\)/);
    assert.match(ui, /activeRequests\.add\(controller\)/);
    assert.match(ui, /activeRequests\.clear\(\)/);
    assert.match(ui, /if \(!currentSession\(\)\) throw sessionError\(\)/);
    assert.match(ui, /sessionFetch\('\/api\/library\/digital\/'/);
    assert.match(ui, /function clearSensitive\(\)/);
    assert.match(ui, /rows\.replaceChildren\(\)/);
    assert.match(ui, /digitalUploadForm'\)\.reset\(\)/);
    assert.match(ui, /digitalEditDialog'\)\.close\(\)/);
    assert.match(ui, /sessionObserver\.observe\(document\.body/);
    assert.match(ui, /window\.addEventListener\('pagehide', clearSensitive\)/);
    assert.match(ui, /event\.persisted/);
    assert.doesNotMatch(ui, /innerHTML\s*=/);
});

test('digital access audit is tenant-admin-only, bounded, session-safe and not raw audit JSON', () => {
    assert.match(api, /\[HttpGet\("audit"\)\]/);
    assert.match(api, /\[HttpGet\("audit\/export"\)\]/);
    assert.match(service, /GetAuditTrailAsync\(/);
    assert.match(service, /db\.CurrentTenantId != Tenant/);
    assert.match(service, /last\.DayNumber - first\.DayNumber > 31/);
    assert.match(view, /id="digitalAuditRows"/);
    assert.match(ui, /function loadAudit\(/);
    assert.match(ui, /function exportAudit\(/);
    assert.match(ui, /auditSequence\+\+/);
    assert.doesNotMatch(ui, /innerHTML\s*=/);
});


test('campus licences are tenant-bound, inherited by replacement and controlled through bilingual admin UI', () => {
    const model = read('EduOS.Core/Entities/Files/FileEntities.cs');
    const context = read('EduOS.Persistence/Context/EduOSDbContext.cs');
    assert.match(model, /LibraryCampusId/);
    assert.match(context, /TenantReference<Document, Campus>/);
    assert.match(service, /campuses\.Contains\(x\.Document\.LibraryCampusId\.Value\)/);
    assert.match(service, /LibraryCampusId = previous\.LibraryCampusId/);
    assert.match(service, /ValidCampusScopeAsync/);
    assert.match(service, /ConfirmTenantWideAccess/);
    assert.match(api, /HttpGet\("campuses"\)/);
    assert.match(view, /id="digitalCampus"/);
    assert.match(view, /id="digitalEditCampus"/);
    assert.match(ui, /campusOptionsReady/);
    assert.match(ui, /data\.append\('CampusId'/);
    assert.match(ui, /confirmTenantWideAccess: widening/);
});


test('digital publication history resolves one bounded tenant-owned lineage and never exposes old file bytes', () => {
    assert.match(api, /HttpGet\("{reference:guid}\/versions"\)/);
    assert.match(api, /HttpGet\("{reference:guid}\/versions\/export"\)/);
    assert.match(service, /GetVersionHistoryAsync\(/);
    assert.match(service, /SupersedesDocumentId/);
    assert.match(service, /Take\(251\)/);
    assert.match(service, /Conflicting publication successors/);
    assert.match(view, /id="digitalHistoryDialog"/);
    assert.match(view, /id="digitalHistoryRows"/);
    assert.match(ui, /function loadHistory\(/);
    assert.match(ui, /historySequence\+\+/);
    assert.match(ui, /openHistory\(resource\)/);
    assert.match(ui, /digitalHistoryDialog'\)\.close\(\)/);
    assert.doesNotMatch(ui, /innerHTML\s*=/);
    const writer = read('EduOS.App/Controllers/Api/LibraryDigitalCsvWriter.cs');
    assert.match(writer, /WriteVersions/);
    assert.doesNotMatch(writer, /StorageKey|Sha256|PersonId/);
});
