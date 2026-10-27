const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('workspace appearance is a connected authorized user/tenant feature', () => {
    const controller = read('EduOS.App/Controllers/WorkspacePreferencesController.cs');
    const service = read('EduOS.Service/Services/Tenants/WorkspacePreferencesService.cs');
    const admin = read('EduOS.Service/Services/Tenants/TenantSettingService.cs');
    const view = read('EduOS.App/Views/WorkspacePreferences/Index.cshtml');
    const script = read('EduOS.App/wwwroot/js/platform/workspace-preferences.js');
    const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
    assert.match(controller, /\[Authorize\]/);
    assert.match(controller, /\[AutoValidateAntiforgeryToken\]/);
    assert.match(controller, /\/api\/workspace-preferences/);
    assert.match(service, /_user\.TenantId/);
    assert.match(service, /_user\.UserId/);
    assert.match(service, /RowVersion/);
    assert.match(service, /SaveChangesAsync/);
    assert.match(admin, /WorkspacePreferencesService\.KeyPrefix/);
    assert.match(view, /workspacePreferencesForm/);
    assert.match(script, /credentials: 'same-origin'/);
    assert.match(script, /rowVersion/);
    assert.doesNotMatch(script, /\.innerHTML/);
    assert.match(layout, /\/workspace/);
});

test('shell applies only whitelisted server preferences without cross-tenant browser storage', () => {
    const site = read('EduOS.App/wwwroot/js/site.js');
    assert.match(site, /EduOSApplyWorkspacePreferences/);
    assert.match(site, /\/api\/workspace-preferences/);
    assert.doesNotMatch(site, /(?:window\.)?localStorage\s*[.\[]/);
    assert.doesNotMatch(site, /(?:window\.)?sessionStorage\s*[.\[]/);
    const css = read('EduOS.App/wwwroot/css/site.css');
    assert.match(css, /data-workspace-navigation="right"/);
    assert.match(css, /data-workspace-navigation="top"/);
    assert.match(css, /data-workspace-theme="dark"/);
});
