const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const view = read('EduOS.App/Views/InstitutionManagement/Index.cshtml');
const js = read('EduOS.App/wwwroot/js/institution-management/index.js');
const service = read('EduOS.Service/Services/Tenants/TenantProfileService.cs');
const api = read('EduOS.App/Controllers/Api/TenantProfileController.cs');
test('post-onboarding regional editor supports every canonical date format', () => {
    assert.match(view, /id="institutionDateFormat"/);
    for (const value of ['dd-MM-yyyy', 'dd/MM/yyyy', 'yyyy-MM-dd', 'MM-dd-yyyy']) {
        assert.ok(view.includes('value="' + value + '"'), value);
        assert.ok(service.includes('"' + value + '"'), value);
    }
    assert.match(js, /set\('institutionDateFormat', data\.dateFormat\)/);
    assert.match(js, /dateFormat: value\('institutionDateFormat'\)/);
    assert.match(api, /HttpPut\("regional-settings"\)/);
});
test('regional save retains rowversion and antiforgery security', () => {
    assert.match(view, /@Html\.AntiForgeryToken\(\)/);
    assert.match(js, /rowVersion: state\.profile\.rowVersion/);
    assert.match(js, /RequestVerificationToken: token\(\)/);
    assert.match(api, /AutoValidateAntiforgeryToken/);
    assert.match(service, /MatchesVersion\(tenant\.RowVersion, request\.RowVersion\)/);
});
