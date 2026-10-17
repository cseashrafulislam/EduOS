const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');

test('usage history export uses native GET and only measured counts',()=>{
 const view=read('EduOS.App/Views/UsageHistory/Index.cshtml');
 const api=read('EduOS.App/Controllers/Api/UsageHistoryApiController.cs');
 const service=read('EduOS.Service/Services/SaaS/UsageHistoryService.cs');
 assert.match(view,/method="get" action="\/api\/usage-history\/export"/);
 assert.match(view,/name="fromDate"/);
 assert.match(view,/name="toDate"/);
 assert.match(api,/HttpGet\("export"\)/);
 assert.match(api,/text\/csv/);
 assert.match(service,/x\.TenantId == user\.TenantId/);
 assert.match(service,/Take\(1001\)/);
 assert.match(service,/LocalDay,ActiveStudents,ActiveEmployees,ActiveCampuses/);
 assert.doesNotMatch(service,/csv\.Append\(.*StorageBytes/);
});
