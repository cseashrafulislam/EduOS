'use strict';
const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');

test('receipt is limited to confirmed tenant-owned payment and matching invoice',()=>{
 const service=read('EduOS.Service/Services/SaaS/SubscriptionPaymentService.Receipts.cs');
 const api=read('EduOS.App/Controllers/Api/SubscriptionPaymentController.cs');
 const mvc=read('EduOS.App/Controllers/SubscriptionBillingController.cs');
 assert.match(service,/_user\.IsTenantAdmin/);
 assert.match(service,/payment\.TenantId == tenantId/);
 assert.match(service,/invoice\.TenantId/);
 assert.match(service,/payment\.State == PaymentState\.Successful/);
 assert.match(service,/payment\.CompletedAt != null/);
 assert.match(service,/!payment\.IsDeleted && !invoice\.IsDeleted/);
 assert.doesNotMatch(service,/IgnoreQueryFilters\(/);
 assert.doesNotMatch(service,/SaveChangesAsync|\.Update\(/);
 assert.match(api,/HttpGet\("receipts\/\{paymentId:long\}"\)/);
 assert.match(mvc,/GetReceiptAsync\(id, cancellationToken\)/);
});
test('receipt view encodes fields and payment history links only settled transactions',()=>{
 const view=read('EduOS.App/Views/SubscriptionBilling/Receipt.cshtml');
 const js=read('EduOS.App/wwwroot/js/subscription-billing/index.js');
 assert.match(view,/@Model\.InstitutionName/);
 assert.match(view,/@Model\.Amount/);
 assert.doesNotMatch(view,/@Html\.Raw|<script/i);
 assert.match(js,/Number\(pay\.state\)===3 && pay\.completedAt/);
 assert.match(js,/link\.textContent="View receipt"/);
 assert.match(js,/link\.rel="noopener noreferrer"/);
 assert.doesNotMatch(js,/innerHTML/);
 new Function(js);
});
