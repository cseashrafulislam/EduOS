const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const service = read('EduOS.Service/Services/Library/LibraryService.CopyIntegrity.cs');
const api = read('EduOS.App/Controllers/Api/LibraryController.cs');
const view = read('EduOS.App/Views/Library/Integrity.cshtml');
const js = read('EduOS.App/wwwroot/js/library/integrity.js');

test('copy integrity SQL filters tenant and campus before exposing anomalies', () => {
    assert.match(service, /x\.TenantId == tenant && !x\.IsDeleted/);
    assert.match(service, /permitted\.Contains\(x\.LibraryBranchId\.Value\)/);
    assert.match(service, /GroupBy\(x => x\.BookCopyId\)/);
    assert.match(service, /anomalies\.CountAsync\(ct\)/);
    assert.match(service, /Take\(pageSize\)/);
    assert.match(api, /HttpGet\("reports\/copy-integrity\/export"\)/);
    assert.match(api, /GetCopyIntegrityAsync\(accession, campusId, page, pageSize, ct\)/);
    assert.match(api, /CacheControl = "no-store, private"/);
});

test('copy integrity UI is bounded and renders untrusted values as text', () => {
    assert.match(view, /aria-label="Copy integrity help"/);
    assert.match(view, /id="libraryIntegrityCsv"/);
    assert.match(js, /credentials: 'same-origin'/);
    assert.match(js, /cache: 'no-store'/);
    assert.match(js, /td\.textContent =/);
    assert.match(js, /\^\[1-9\]\\d\*\$/);
    assert.doesNotMatch(js, /innerHTML\s*=/);
    new Function(js);
});
