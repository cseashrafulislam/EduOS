const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const ui = read('EduOS.App/wwwroot/js/library/overdue-notices.js');
const api = read('EduOS.App/Controllers/Api/LibraryOverdueNoticesController.cs');
const service = read('EduOS.Service/Services/Library/LibraryOverdueNoticeService.cs');

test('overdue notice endpoint is role/module protected and campus validated on the server', () => {
    assert.match(api, /Authorize\(Roles = "TenantAdmin,Principal,Librarian"\)/);
    assert.match(api, /RequireModule\("LIBRARY"\)/);
    assert.match(api, /AutoValidateAntiforgeryToken/);
    assert.match(service, /CanManageCampusAsync/);
    assert.match(service, /EmployeeCampusAssignments/);
    assert.match(service, /UserCampusAccesses/);
    assert.match(service, /IsolationLevel\.Serializable/);
    assert.match(service, /NotificationPreferences\.Any/);
});

test('overdue notice UI rejects stale campus responses and aborts private requests on session switch', () => {
    assert.match(ui, /new AbortController\(\)/);
    assert.match(ui, /controller\.abort\(\)/);
    assert.match(ui, /dataset\.authTenantId/);
    assert.match(ui, /dataset\.authSessionReference/);
    assert.match(ui, /libraryOverdueCampus'\)\.value\.trim\(\) !== String\(id\)/);
    assert.match(ui, /RequestVerificationToken/);
    assert.match(ui, /credentials: 'same-origin'/);
    assert.match(ui, /crypto\.randomUUID\(\)/);
    assert.doesNotMatch(ui, /innerHTML\s*=/);
});


test('preview refresh retains unacknowledged send key and rejects stale responses', () => {
    assert.match(ui, /previewEpoch\s*=\s*0/);
    assert.match(ui, /epoch\s*!==\s*previewEpoch/);
    assert.match(ui, /preview\.campusId\s*!==\s*data\.campusId/);
    assert.match(ui, /preview\.asOfDate\s*!==\s*data\.asOfDate/);
    assert.doesNotMatch(ui, /preview\s*=\s*data;\s*requestId\s*=\s*null/);
});
