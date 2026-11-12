const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('institution setup does not mutate masters on menu GET and keeps confirmation', () => {
    const controller = read('EduOS.App/Controllers/Api/SmartSetupApiController.cs');
    const banner = read('EduOS.App/wwwroot/js/tenant/setup-readiness-banner.js');
    const wizard = read('EduOS.App/wwwroot/js/tenant/smart-setup.js');
    const service = read('EduOS.Service/Services/Tenants/SmartSetupService.cs');
    assert.match(controller, /\[HttpGet\("readiness"\)\]/);
    assert.match(controller, /\[HttpPost\("apply"\)\]/);
    assert.match(controller, /\[Authorize\(Roles = "TenantAdmin"\)\]/);
    assert.match(controller, /\[AutoValidateAntiforgeryToken\]/);
    assert.match(banner, /fetch\('\/api\/smart-setup\/readiness\?module='/);
    assert.doesNotMatch(banner, /method:\s*['"]POST['"]/);
    assert.match(banner, /link\.target = '_blank'/);
    assert.match(banner, /session === body\.dataset\.authSessionReference/);
    assert.match(wizard, /smartSetupConfirm/);
    assert.match(wizard, /RequestVerificationToken/);
    assert.match(wizard, /state\.busy/);
    assert.match(wizard, /Promise\.all/);
    assert.match(service, /IsolationLevel\.Serializable/);
    assert.match(service, /sp_getapplock/);
    assert.match(service, /db\.CurrentTenantId == user\.TenantId/);
    assert.match(service, /receipt\.Fingerprint != fingerprint/);
    assert.match(service, /db\.AuditLogs\.Add/);
});

test('module entitlement mapping matches actual platform catalog codes', () => {
    const catalog = read('EduOS.Service/Services/Tenants/SmartSetupCatalog.cs');
    const service = read('EduOS.Service/Services/Tenants/SmartSetupService.cs');
    const readiness = read('EduOS.Service/Services/Tenants/SmartSetupReadinessService.cs');
    assert.match(catalog, /"FEES" => "FINANCE"/);
    assert.match(catalog, /"ASSESSMENT" => "EXAM"/);
    assert.match(catalog, /"REPORTS" => "REPORTING"/);
    assert.match(catalog, /new\("PAYROLL"/);
    assert.match(service, /SmartSetupCatalog\.AccessModule\(section\.Code\)/);
    assert.match(readiness, /SmartSetupCatalog\.AccessModule\(section\.Code\)/);
    assert.match(readiness, /configuration\.GetValue<bool>\("PilotRelease:Enabled"\)/);
    assert.match(readiness, /IsCurrentTenantModuleAvailableAsync/);
});

test('Smart Setup row-level write contract does not trust browser names', () => {
    const dto = read('EduOS.Core/DTOs/SaaS/SmartSetupDtos.cs');
    const service = read('EduOS.Service/Services/Tenants/SmartSetupService.cs');
    const wizard = read('EduOS.App/wwwroot/js/tenant/smart-setup.js');
    assert.match(dto, /List<SmartSetupSelectedEntryDto>\? Entries/);
    assert.match(service, /Selected setup row is not in this institution template/);
    assert.match(service, /r\.Kind == x\.Kind && r\.Code == x\.Code/);
    assert.match(service, /\|rows:/);
    assert.match(wizard, /Save Checked Records|smartSetupApply/);
    assert.match(wizard, /entries: /);
});
