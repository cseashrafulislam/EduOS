// Run: node --test EduOS.Tests/JavaScript/tenant-modules-management.test.cjs
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('post-onboarding modules management reuses canonical API and module UI', () => {
    const view = read('EduOS.App/Views/TenantModules/Index.cshtml');
    const layout = read('EduOS.App/Views/Shared/_Layout.cshtml');
    const controller = read('EduOS.App/Controllers/TenantModulesController.cs');
    const js = read('EduOS.App/wwwroot/js/account/module-setup.js');
    const api = read('EduOS.App/Controllers/Api/TenantModuleController.cs');
    const service = read('EduOS.Service/Services/SaaS/TenantModuleService.cs');
    assert.match(controller, /Authorize\(Roles = "TenantAdmin"\)/);
    assert.match(controller, /NoStore = true/);
    assert.equal(layout.split('href="/TenantModules/Index"').length - 1, 1);
    assert.match(view, /id="moduleList"/);
    assert.match(view, /id="moduleSetupStrings"/);
    assert.match(view, /js\/account\/module-setup\.js/);
    assert.doesNotMatch(view, /id="continueBtn"/);
    assert.match(js, /\/api\/tenant-modules/);
    assert.match(js, /RequestVerificationToken/);
    assert.match(js, /rowVersion: module\.rowVersion/);
    assert.match(api, /AutoValidateAntiforgeryToken/);
    assert.match(service, /x\.TenantId == tenantId/);
    assert.match(service, /Required modules cannot be disabled/);
    assert.match(service, /Module is not included in the current subscription/);
});
