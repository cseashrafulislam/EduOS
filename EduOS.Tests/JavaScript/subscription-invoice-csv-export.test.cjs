'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');

test('subscription invoice CSV reuses tenant-owned persisted detail and protects formula cells',()=>{
 const service=read('EduOS.Service/Services/SaaS/SubscriptionInvoiceService.cs');
 const iface=read('EduOS.Core/Interfaces/IServices/ISubscriptionInvoiceService.cs');
 const api=read('EduOS.App/Controllers/Api/SubscriptionController.cs');
 assert.match(service,/ExportCsvAsync\(long invoiceId\)/);
 assert.match(service,/await GetByIdAsync\(invoiceId\)/);
 assert.match(service,/_currentUser\.IsSuperAdmin/);
 assert.match(service,/GetLinesForInvoiceAsync\(invoice\.TenantId, invoice\.Id\)/);
 assert.match(service,/text\.TrimStart/);
 assert.match(service,/CsvText\(line\.Description\)/);
 assert.match(iface,/Task<ApiResponse<string>> ExportCsvAsync/);
 assert.match(api,/HttpGet\("invoices\/\{id:long\}\/export"\)/);
 assert.match(api,/ResponseCache\(NoStore = true/);
 assert.match(api,/text\/csv; charset=utf-8/);
});
test('invoice history downloads CSV without injecting invoice fields into HTML',()=>{
 const view=read('EduOS.App/Views/SubscriptionBilling/Index.cshtml');
 const js=read('EduOS.App/wwwroot/js/subscription-billing/index.js');
 assert.match(view,/Download a tenant-scoped CSV/);
 assert.match(js,/button\("Download CSV"/);
 assert.match(js,/credentials:"same-origin"/);
 assert.match(js,/cache:"no-store"/);
 assert.match(js,/response\.blob\(\)/);
 assert.match(js,/URL\.revokeObjectURL\(url\)/);
 assert.ok(!js.includes('innerHTML'));
 new Function(js);
});
