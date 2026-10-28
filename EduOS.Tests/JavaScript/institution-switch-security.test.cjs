const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('institution switch uses authorized session API and discards runtime cache on success', () => {
    const js = read('EduOS.App/wwwroot/js/platform/institution-switch.js');
    const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
    assert.match(layout, /id="institutionSwitchModal"/);
    assert.match(layout, /id="institutionSwitchSelect"/);
    assert.match(layout, /js\/platform\/institution-switch\.js/);
    assert.match(js, /fetch\('\/api\/auth\/institutions',/);
    assert.match(js, /fetch\('\/api\/auth\/institutions\/switch',/);
    assert.match(js, /credentials: 'same-origin'/);
    assert.match(js, /cache: 'no-store'/);
    assert.match(js, /available\.has\(tenantId\)/);
    assert.match(js, /EDUOS_CLEAR_RUNTIME_STATIC/);
    assert.match(js, /window\.location\.replace\('\/Dashboard\/Index'\)/);
    assert.doesNotMatch(js, /localStorage|sessionStorage|innerHTML/);
    assert.match(read('EduOS.App/wwwroot/js/site.js'), /RequestVerificationToken/);
});
