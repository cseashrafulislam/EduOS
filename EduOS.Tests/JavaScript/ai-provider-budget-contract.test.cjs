'use strict';
const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const root=path.resolve(__dirname,'../..'),read=p=>fs.readFileSync(path.join(root,p),'utf8');

test('external AI requires tenant allowlist, explicit per-request consent and durable SQL reserve',()=>{
    const svc=read('EduOS.Service/Services/AI/EduOsAiWorkspaceService.cs');
    const quota=read('EduOS.Service/Services/AI/AiProviderDailyQuotaService.cs');
    const migration=read('EduOS.Persistence/Migrations/20261121080000_AiDailyProviderCallBudget.cs');
    assert.match(svc,/EduOsAi:ApprovedTenantIds/);
    assert.match(svc,/EduOsAi:StudentTutorEnabled/);
    assert.match(svc,/request\?\.ConsentToExternalAi != true/);
    assert.match(svc,/if \(!consentToExternalAi\)/);
    assert.match(svc,/quota.TryReserveAsync\(current.TenantId, dailyLimit, ct\)/);
    assert.match(svc,/DailyCallsPerTenant/);
    assert.match(quota,/db\.CurrentTenantId != tenantId/);
    assert.match(quota,/sp_getapplock/);
    assert.match(quota,/BEGIN TRANSACTION/);
    assert.match(quota,/COMMIT TRANSACTION/);
    assert.match(quota,/ReservedCalls < @Limit/);
    assert.match(migration,/PK_AiProviderDailyUsage/);
    assert.match(migration,/TenantId.*UsageDateUtc/);
    assert.doesNotMatch(quota,/HttpClient|api\.openai\.com|SaveChangesAsync/);
});

test('institution AI returns invariant decimal strings and never recreates GL or fees',()=>{
    const service=read('EduOS.Service/Services/AI/AiInstitutionMetricService.cs');
    const ui=read('EduOS.App/wwwroot/js/ai/institution-questions.js');
    assert.match(service,/ICentralInstitutionMisService/);
    assert.match(service,/metric\.Value\.Value\.ToString\(CultureInfo.InvariantCulture\)/);
    assert.match(ui,/typeof m\?\.value !== 'string'/);
    assert.doesNotMatch(ui,/parseFloat|parseInt|Number\(m\.value\)|m\.value\.toLocaleString/);
});
