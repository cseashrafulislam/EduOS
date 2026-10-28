const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('MFA setup and enable refresh the cookie-bound CSRF token and adopt only the same institution', () => {
    const mfa = read('EduOS.App/wwwroot/js/account/mfa-setup.js');
    const site = read('EduOS.App/wwwroot/js/site.js');
    const guard = read('EduOS.App/wwwroot/js/platform/session-context-guard.js');
    const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
    assert.match(mfa, /fetch\('\/api\/auth\/antiforgery'/);
    assert.match(mfa, /await refreshRotatedSession\(\)/);
    assert.match(mfa, /EduOSAdoptRotatedSession\?\.\(data\.sessionReference, data\.tenantId\)/);
    assert.match(mfa, /meta\.content = data\.requestToken/);
    assert.match(mfa, /EduOSNotifySessionChanged\?\.\(\)/);
    const wrapper = site.slice(site.indexOf('window.fetch ='), site.indexOf('// Clear only public'));
    assert.match(wrapper, /const verificationToken = document\.querySelector/);
    assert.match(wrapper, /headers\.set\('RequestVerificationToken', verificationToken\)/);
    assert.match(guard, /tenant !== originalTenant/);
    assert.match(guard, /original = reference/);
    assert.match(layout, /data-auth-tenant-id=/);
    assert.doesNotMatch(mfa, /localStorage|sessionStorage|innerHTML/);
});
