const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/CommunicationWorkspace/Index.cshtml'), 'utf8');
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/communication/workspace-preferences.js'), 'utf8');
const entry = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/communication/workspace.js'), 'utf8');
test('preference editor is wired to existing authenticated workspace', () => {
    for (const id of ['cwPrefForm','cwPrefChannel','cwPrefEvent','cwPrefEnabled','cwPrefSave','cwPrefCancel','cwPrefRefresh','cwPrefRows','cwPrefEditing']) {
        assert.match(view, new RegExp('id="' + id + '"'));
    }
    assert.match(entry, /initPreferences\(\)/);
    assert.match(entry, /workspace-preferences\.js/);
});
test('preference mutation reuses canonical antiforgery API and row version', () => {
    assert.match(js, /api\('\/api\/communication\/preferences', 'PUT', payload\)/);
    assert.match(js, /rowVersion: row\.rowVersion/);
    assert.match(js, /rowVersion: editing\?\.rowVersion/);
    assert.match(js, /error\.status === 409/);
    assert.doesNotMatch(js, /innerHTML|localStorage|sessionStorage/);
});
