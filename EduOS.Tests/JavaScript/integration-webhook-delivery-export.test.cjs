const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');

test('delivery CSV export is wired to the selected tenant webhook',()=>{
 const view=read('EduOS.App/Views/IntegrationAdministration/Index.cshtml');
 const js=read('EduOS.App/wwwroot/js/integration-admin/delivery-export.js');
 const api=read('EduOS.App/Controllers/Api/IntegrationAdministrationApiController.cs');
 const service=read('EduOS.Service/Services/Platform/IntegrationAdministrationService.WebhookExport.cs');
 assert.match(view,/id="iaDeliveryExportEndpoint"/);
 assert.match(view,/id="iaDeliveryExport"[^>]*disabled/);
 assert.match(view,/delivery-export\.js/);
 assert.match(js,/select\.value/);
 assert.match(js,/\/deliveries\/export/);
 assert.match(js,/credentials:"same-origin",cache:"no-store"/);
 assert.match(api,/HttpGet\("webhooks\/\{id:long\}\/deliveries\/export"\)/);
 assert.match(api,/text\/csv/);
 assert.match(service,/x\.TenantId == user\.TenantId && x\.WebhookEndpointId == endpointId/);
 assert.match(service,/Take\(MaxWebhookDeliveryExportRows \+ 1\)/);
 assert.doesNotMatch(service,/x\.PayloadJson|x\.ProtectedSecret/);
});
