'use strict';
const test = require('node:test'), assert = require('node:assert/strict');
const fs = require('node:fs'), path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root,p),'utf8');
test('Ask EduOS institution metrics reuse canonical MIS under its original tenant-admin scope', () => {
    const svc=read('EduOS.Service/Services/AI/AiInstitutionMetricService.cs');
    const api=read('EduOS.App/Controllers/Api/AiInstitutionQueryController.cs');
    const mvc=read('EduOS.App/Controllers/AiWorkspaceController.cs');
    assert.match(svc, /ICentralInstitutionMisService/);
    assert.match(svc, /mis.GetAsync\(fromDate, toDate, ct\)/);
    assert.match(svc, /!metric.Available \|\| !metric.Value.HasValue/);
    assert.doesNotMatch(svc, /SaveChanges|ExecuteSql|HttpClient|StudentInvoices/);
    assert.match(api, /Authorize\(Roles = "TenantAdmin"\)/);
    assert.match(api, /RequireModule\("REPORTING"\)/);
    assert.match(api, /AutoValidateAntiforgeryToken/);
    assert.match(mvc, /IActionResult Institution\(\)/);
});
test('Ask EduOS UI is safe, session-scoped, anti-CSRF and model-free',()=>{
    const view=read('EduOS.App/Views/AiWorkspace/Institution.cshtml');
    const js=read('EduOS.App/wwwroot/js/ai/institution-questions.js');
    assert.match(view,/AntiForgeryToken/);
    assert.match(js,/RequestVerificationToken:token/);
    assert.match(js,/sameSession\(\)/);
    assert.match(js,/AbortController/);
    assert.match(js,/textContent/);
    assert.doesNotMatch(js,/innerHTML|localStorage|api\.openai\.com/);
});
