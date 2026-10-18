const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
test('credential CSV export reuses scoped usage endpoint and documents 100-row bound',()=>{
 const api=read('EduOS.App/Controllers/Api/ApiCredentialUsageApiController.cs');
 const view=read('EduOS.App/Views/IntegrationAdministration/Index.cshtml');
 assert.match(api,/HttpGet\("export"\)/);
 assert.match(api,/GetUsageAsync\(values\[0\], 1, 100, ct\)/);
 assert.match(api,/IsAuthenticated == true/);
 assert.match(api,/Request.Headers.Authorization/);
 assert.match(api,/X-Content-Type-Options/);
 assert.match(view,/api\/integration\/v1\/usage\/export/);
 assert.match(view,/latest 100/);
});
