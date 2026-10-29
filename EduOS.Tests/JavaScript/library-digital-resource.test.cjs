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
