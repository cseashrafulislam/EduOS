const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
test('feature audit export is discoverable without a client-side mutation',()=>{
 const view=read('EduOS.App/Views/TenantFeatureAudit/Index.cshtml');
 const api=read('EduOS.App/Controllers/Api/TenantFeatureAuditApiController.cs');
 const service=read('EduOS.Service/Services/SaaS/TenantFeatureAuditService.Export.cs');
 assert.match(view,/method="get" action="\/api\/tenant-feature-audit\/export"/);
 assert.match(api,/HttpGet\("export"\)/);
 assert.match(api,/text\/csv/);
 assert.match(service,/TotalCount > 1000/);
 assert.match(service,/CsvCell\(row.Reason\)/);
 assert.doesNotMatch(view,/method="post" action="\/api\/tenant-feature-audit\/export"/);
});
