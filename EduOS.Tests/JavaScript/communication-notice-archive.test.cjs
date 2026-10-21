const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const listing = read('EduOS.App/wwwroot/js/communication/workspace-notice-list.js');
const core = read('EduOS.App/wwwroot/js/communication/workspace-core.js');
const api = read('EduOS.App/Controllers/Api/CommunicationWorkspaceApiController.cs');
const service = read('EduOS.Service/Services/Communication/CommunicationService.cs');
test('notice archive is admin-only, confirmed, and uses existing CSRF-aware API helper', () => {
    assert.match(listing, /if \(admin\) \{/);
    assert.match(listing, /action\('Archive'/);
    assert.match(listing, /window\.confirm/);
    assert.match(listing, /encodeURIComponent\(row\.rowVersion \|\| ''\)/);
    assert.match(listing, /await api\(path, 'DELETE'\)/);
    assert.match(core, /headers\.RequestVerificationToken = csrf/);
    assert.doesNotMatch(listing, /innerHTML|localStorage|sessionStorage/);
});
test('notice archive API and service enforce tenant, concurrency, and filtered soft deletion', () => {
    assert.match(api, /\[AutoValidateAntiforgeryToken\]/);
    assert.match(api, /\[HttpDelete\("notices\/\{reference:guid\}"\)\]/);
    assert.match(api, /service\.ArchiveNoticeAsync\(reference, rowVersion, ct\)/);
    assert.match(service, /x\.TenantId == tenant && x\.PublicId == noticeReference/);
    assert.match(service, /SameVersion\(row\.RowVersion, rowVersion\)/);
    assert.match(service, /catch \(DbUpdateConcurrencyException\)/);
    assert.match(service, /row\.IsDeleted = true/);
    const archive = service.split('public async Task<ApiResponse<bool>> ArchiveNoticeAsync')[1]
        .split('public async Task<ApiResponse<bool>> MarkNoticeReadAsync')[0];
    assert.doesNotMatch(archive, /\.IgnoreQueryFilters\(/);
});
