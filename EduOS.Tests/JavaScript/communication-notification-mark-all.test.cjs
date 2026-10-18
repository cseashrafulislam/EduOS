const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const view = read('EduOS.App/Views/CommunicationWorkspace/Index.cshtml');
const entry = read('EduOS.App/wwwroot/js/communication/workspace.js');
const inbox = read('EduOS.App/wwwroot/js/communication/workspace-inbox.js');
const service = read('EduOS.Service/Services/Communication/CommunicationService.cs');
const repository = read('EduOS.Persistence/Repositories/NotificationRepository.cs');
const api = read('EduOS.App/Controllers/Api/CommunicationWorkspaceApiController.cs');
test('bulk notification read is reachable in existing authenticated workspace', () => {
    assert.match(view, /id="cwMarkAllNotificationsRead"/);
    assert.match(entry, /initNotificationInbox\(\)/);
    assert.match(inbox, /window\.confirm/);
    assert.match(inbox, /button\.disabled = true/);
    assert.match(inbox, /finally \{ button\.disabled = false/);
    assert.match(inbox, /api\('\/api\/communication\/notifications\/read-all', 'POST'\)/);
    assert.doesNotMatch(inbox, /innerHTML|localStorage|sessionStorage/);
});
test('bulk mutation retains antiforgery and tenant/user ownership', () => {
    assert.match(api, /\[AutoValidateAntiforgeryToken\]/);
    assert.match(api, /\[Authorize\]/);
    assert.match(api, /\[HttpPost\("notifications\/read-all"\)\]/);
    assert.match(service, /notifications\.MarkAllAsReadAsync\(user\.TenantId, user\.UserId, DateTime\.UtcNow, ct\)/);
    assert.match(repository, /x\.TenantId == tenantId && x\.UserId == userId && !x\.IsRead/);
    assert.match(repository, /ExecuteUpdateAsync/);
});
