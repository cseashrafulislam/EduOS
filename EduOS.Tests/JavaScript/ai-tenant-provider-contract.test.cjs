'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'../..'),read=p=>fs.readFileSync(path.join(root,p),'utf8');
test('institute setup renders its own AI key without exposing stored secret',()=>{
    const v=read('EduOS.App/Views/Account/GatewaySetup.cshtml');
    const js=read('EduOS.App/wwwroot/js/account/gateway-setup.js');
    for(const id of ['smsApiKey','smtpPassword','aiProviderApiKey','aiProviderEnabled',
        'aiStudentTutorEnabled','saveAiGatewayBtn','aiClearApiKey'])assert.ok(v.includes('id="'+id+'"'),id);
    assert.match(v,/type="password" id="aiProviderApiKey"/);
    assert.match(js,/aiApiRoot = '\/api\/ai\/provider-settings'/);
    assert.match(js,/rowVersion: aiSettings\?\.rowVersion/);
    assert.match(js,/config.hasApiKey/);
    assert.doesNotMatch(js,/localStorage|sessionStorage/);
    new Function(js);
});
test('tenant key is encrypted, versioned, tenant-scoped and never sent back in API status',()=>{
    const svc=read('EduOS.Service/Services/AI/TenantAiProviderSettingsService.cs');
    const migration=read('EduOS.Persistence/Migrations/20261121100000_TenantAiProviderCredentials.cs');
    const controller=read('EduOS.App/Controllers/Api/TenantAiProviderSettingsController.cs');
    const generator=read('EduOS.Service/Services/AI/EduOsAiWorkspaceService.cs');
    assert.match(svc,/db.CurrentTenantId == current.TenantId/);
    assert.match(svc,/CentralPrivilegedAccess.HasRoleAsync/);
    assert.match(svc,/CreateProtector/);
    assert.match(svc,/Unprotect\(row.ProtectedApiKey\)/);
    assert.match(svc,/Protect\(request.ApiKey\)/);
    assert.match(svc,/WHERE \[TenantId\] = \{current.TenantId\} AND \[RowVersion\] = \{expected\}/);
    assert.match(svc,/HasApiKey/);
    assert.match(migration,/PK_TenantAiProviderSettings/);
    assert.match(migration,/FK_TenantAiProviderSettings_Tenants/);
    assert.match(migration,/rowVersion: true/);
    assert.match(controller,/Authorize\(Roles = "TenantAdmin"\)/);
    assert.match(controller,/AutoValidateAntiforgeryToken/);
    assert.match(controller,/ResponseCache\(NoStore = true/);
    assert.match(generator,/providerSettings.ApiKey/);
    assert.doesNotMatch(generator,/EduOsAi:ApiKey|ApprovedTenantIds/);
});