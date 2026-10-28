const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('stale institution tabs verify the server session before revealing private UI', () => {
    const js = read('EduOS.App/wwwroot/js/platform/session-context-guard.js');
    const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
    const css = read('EduOS.App/wwwroot/css/site.css');
    assert.match(layout, /data-auth-session-reference="@sessionReference"/);
    assert.match(layout, /id="eduosSessionShield"/);
    assert.match(layout, /js\/platform\/session-context-guard\.js/);
    assert.match(js, /fetch\('\/api\/auth\/session-context'/);
    assert.match(js, /credentials: 'same-origin', cache: 'no-store'/);
    assert.match(js, /current !== original/);
    assert.match(js, /function redirectSafely\(url\)/);
    assert.match(js, /window\.location\.replace\(url\)/);
    assert.match(js, /redirectSafely\('\/Dashboard\/Index'\)/);
    assert.match(js, /redirectSafely\('\/Account\/Login'\)/);
    assert.match(js, /if \(navigationPending\) return;/);
    assert.match(js, /visibilitychange/);
    assert.match(js, /event\.persisted/);
    assert.match(js, /BroadcastChannel/);
    assert.match(js, /shell\?\.setAttribute\('inert'/);
    assert.match(css, /\.auth-session-shield\[hidden\]/);
    assert.doesNotMatch(js, /localStorage|sessionStorage|innerHTML/);
});

test('server-bound CSRF rejects stale form tokens after session rotation', () => {
    const provider = read('EduOS.App/Security/SessionBoundAntiforgeryDataProvider.cs');
    const program = read('EduOS.App/Program.cs');
    const switchJs = read('EduOS.App/wwwroot/js/platform/institution-switch.js');
    const site = read('EduOS.App/wwwroot/js/site.js');
    assert.match(program, /AddSingleton<IAntiforgeryAdditionalDataProvider, SessionBoundAntiforgeryDataProvider>/);
    assert.match(provider, /AuthSessionRegistry\.ToPublicReference\(sid\)/);
    assert.match(provider, /context\.User\.FindFirstValue\("TenantId"\)/);
    assert.match(provider, /ValidateAdditionalData/);
    assert.match(switchJs, /window\.EduOSNotifySessionChanged\?\.\(\)/);
    assert.match(site, /window\.EduOSNotifySessionChanged\?\.\(\)/);
});
