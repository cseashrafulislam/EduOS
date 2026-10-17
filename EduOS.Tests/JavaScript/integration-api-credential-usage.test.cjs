const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
test('API credential usage is documented and the example script is loaded',()=>{
 const view=read('EduOS.App/Views/IntegrationAdministration/Index.cshtml');
 const script=read('EduOS.App/wwwroot/js/integration-admin/credential-usage-example.js');
 const api=read('EduOS.App/Controllers/Api/ApiCredentialUsageApiController.cs');
 assert.match(view,/api\/integration\/v1\/usage/);
 assert.match(view,/integration:read/);
 assert.match(view,/credential-usage-example\.js/);
 assert.match(script,/iaCopyApiExample/);
 assert.match(api,/AllowAnonymous/);
 assert.match(api,/IsAuthenticated == true/);
 assert.match(api,/Request.Headers.Authorization/);
});
