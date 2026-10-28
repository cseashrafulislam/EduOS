const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../');
const script = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/account/profile.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/Account/Profile.cshtml'), 'utf8');
test('other-device signout uses password and refreshes the list without redirecting current session', () => {
    const begin = script.indexOf("document.getElementById('sessionRevokeOthersBtn')");
    const end = script.indexOf("document.getElementById('sessionRevokeAllBtn')", begin);
    assert.ok(begin >= 0 && end > begin);
    const handler = script.slice(begin, end);
    assert.match(handler, /if \(!currentPassword\)/);
    assert.match(handler, /authSecurityPost\('\/api\/auth\/sessions\/revoke-others', \{ currentPassword \}\)/);
    assert.match(handler, /await loadActiveSessions\(\)/);
    assert.doesNotMatch(handler, /window\.location\.assign/);
    assert.match(view, /id="sessionRevokeOthersBtn"/);
    assert.match(view, /data-success="@T\["OtherSessionsRevoked"\]"/);
});
