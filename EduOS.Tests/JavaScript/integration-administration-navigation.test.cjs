const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
test('integration administration is discoverable only to TenantAdmin',()=>{
 const layout=read('EduOS.App/Views/Shared/_Layout.cshtml');
 const controller=read('EduOS.App/Controllers/IntegrationAdministrationController.cs');
 const api=read('EduOS.App/Controllers/Api/IntegrationAdministrationApiController.cs');
 const matches=[...layout.matchAll(/href="\/IntegrationAdministration\/Index"/g)];
 assert.equal(matches.length,1,'navigation should have one entry');
 const preceding=layout.slice(0,matches[0].index).split('@if').at(-1);
 assert.match(preceding,/User\?\.IsInRole\("TenantAdmin"\) == true/);
 assert.match(controller,/Authorize\(Roles = "TenantAdmin"\)/);
 assert.match(api,/Authorize\(Roles = "TenantAdmin"\)/);
 assert.match(api,/AutoValidateAntiforgeryToken/);
});
