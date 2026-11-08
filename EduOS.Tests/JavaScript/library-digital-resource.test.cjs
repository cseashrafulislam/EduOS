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

test('catalogue CSV export uses identical authorized advanced filters as list', () => {
    const list = api.match(/public async Task<IActionResult> List\([\s\S]*?\n    }/);
    const exportAction = api.match(/public async Task<IActionResult> Export\([\s\S]*?\n    }/);
    assert.ok(list && exportAction, 'list and export endpoints must exist');
    const call = /service\.ListAsync\(bookReference, search, page, pageSize, includeInactive, ct,\s*category, author, publisher, edition, curriculumId, subjectId, chapter, (?:contentLanguage|language)\)/;
    assert.match(list[0], call);
    assert.match(exportAction[0], call);
    for (const name of ['category', 'author', 'publisher', 'edition']) {
        assert.match(list[0], new RegExp('\\[FromQuery\\] string\\? ' + name));
        assert.match(exportAction[0], new RegExp('\\[FromQuery\\] string\\? ' + name));
    }
    assert.match(list[0], /curriculumId, subjectId, chapter, language/);
    assert.match(exportAction[0], /curriculumId, subjectId, chapter, contentLanguage/);
});

test('education content requires rights provenance and human review before reading', () => {
    const service = read('EduOS.Service/Services/Library/LibraryDigitalResourceService.cs');
    const dto = read('EduOS.Core/DTOs/Library/LibraryDigitalResourceDtos.cs');
    assert.match(service, /VerificationStatusCode = "Pending"/);
    assert.match(service, /VerificationStatusCode == "Approved"/);
    assert.match(service, /ReviewAsync\(/);
    assert.match(service, /ValidCurriculumAsync\(/);
    assert.match(service, /SetRights\(row, request\.Rights\)/);
    assert.match(api, /\[HttpPost\("\{reference:guid\}\/review"\)\]/);
    assert.match(api, /\[HttpGet\("\{reference:guid\}\/read"\)\]/);
    assert.match(dto, /LibraryDigitalRightsRequestDto/);
    for (const id of ['digitalSourceUrl', 'digitalRightsHolder', 'digitalLicenceCode',
        'digitalLicenceEvidence', 'digitalCurriculum', 'digitalChapter', 'digitalTopic'])
        assert.match(view, new RegExp('id="' + id + '"'));
    assert.match(ui, /appendRights\(data, 'digital'\)/);
    assert.match(ui, /review\(resource, approve, button\)/);
});

test('LMS reuses approved subject library instead of duplicating files or publication state', () => {
    const lms = read('EduOS.App/wwwroot/js/learning/course-content.js');
    const lmsView = read('EduOS.App/Views/Learning/CourseContent.cshtml');
    assert.match(lms, /selected\?\.subjectId/);
    assert.match(lms, /api\/library\/digital\?/);
    assert.match(lms, /sessionValid\(\)/);
    assert.match(lms, /clearPrivateLibrary\(\)/);
    assert.match(lmsView, /id="contentLibraryRows"/);
    assert.doesNotMatch(lms, /localStorage|sessionStorage|indexedDB/i);
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


test('licensed catalogue searches canonical Book bibliographic fields and renders authorized metadata safely', () => {
    const dto = read('EduOS.Core/DTOs/Library/LibraryDigitalResourceDtos.cs');
    for (const field of ['BookAuthor', 'BookPublisher', 'BookEdition', 'BookIsbn']) {
        assert.match(dto, new RegExp(field));
        assert.match(service, new RegExp(field));
    }
    for (const field of ['Author', 'Publisher', 'Edition', 'ISBN'])
        assert.match(service, new RegExp('x\\.Book\\.' + field + '\\s*!= null'));
    assert.match(service, /ResourceQuery\(\)/);
    assert.match(service, /MemberCampusIds\(\)/);
    assert.match(ui, /function bookDetails\(resource\)/);
    assert.match(ui, /small\.textContent = details/);
    assert.match(view, /ISBN or resource/);
    assert.doesNotMatch(ui, /innerHTML\s*=/);
});


test('licensed catalogue category discovery uses canonical tenant-scoped BookCategory and safe bilingual export', () => {
    const dto = read('EduOS.Core/DTOs/Library/LibraryDigitalResourceDtos.cs');
    const writer = read('EduOS.App/Controllers/Api/LibraryDigitalCsvWriter.cs');
    assert.match(dto, /BookCategory/);
    assert.match(service, /db\.BookCategories\.AsNoTracking\(\)\.Any\(category =>/);
    assert.match(service, /category\.TenantId == Tenant && !category\.IsDeleted && category\.IsActive/);
    assert.match(service, /category\.Id == x\.Book\.BookCategoryId && category\.Name\.Contains\(term\)/);
    assert.match(service, /BookCategory = db\.BookCategories\.AsNoTracking\(\)/);
    assert.match(service, /BookCategory = x\.BookCategory/);
    assert.match(ui, /resource\.bookCategory/);
    assert.match(view, /বিভাগ/);
    assert.match(writer, /Cell\(row\.BookCategory\)/);
    assert.match(writer, /Book,Category,Resource/);
    assert.doesNotMatch(writer, /StorageKey|StudentId|PersonId|Sha256/);
});


test('catalogue advanced facets compose with authorized paging, bilingual responsive search and matching CSV export', () => {
    const iface = read('EduOS.Core/Interfaces/IServices/ILibraryDigitalResourceService.cs');
    for (const name of ['category', 'author', 'publisher', 'edition']) {
        assert.match(iface, new RegExp('string\\? ' + name));
        assert.match(api, new RegExp('\\[FromQuery\\] string\\? ' + name));
        assert.match(service, new RegExp('if \\(' + name + ' != null\\)'));
        assert.match(ui, new RegExp("\\['" + name + "', 'digital"));
    }
    assert.match(service, /ValidCatalogueFilter/);
    assert.match(service, /c\.TenantId == Tenant && !c\.IsDeleted && c\.IsActive/);
    assert.match(service, /var total = await query\.CountAsync\(ct\)/);
    assert.match(ui, /const query = params\(\); query\.set\('language'/);
    assert.match(view, /id="digitalClearFilters"/);
    assert.match(ui, /page = 1; clearAlert\(\); load\(\)/);
    assert.doesNotMatch(ui, /innerHTML\s*=/);
});
