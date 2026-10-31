const {test}=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const app=path.resolve(__dirname,'../../EduOS.App');
const script=fs.readFileSync(path.join(app,'wwwroot/js/procurement/commitments.js'),'utf8');
const view=fs.readFileSync(path.join(app,'Views/ProcurementPurchaseOrders/Commitments.cshtml'),'utf8');
const api=fs.readFileSync(path.join(app,'Controllers/Api/ProcurementPurchaseOrdersController.cs'),'utf8');
test('approved commitments use a bilingual bounded read-only register, not stock balance',()=>{
 assert.match(script,/অনুমোদিত সরবরাহ প্রতিশ্রুতি/);
 assert.match(script,/Goods Receipt integration/);
 assert.match(script,/credentials:'same-origin'/);
 assert.match(script,/cache:'no-store'/);
 assert.match(script,/textContent/);
 assert.match(script,/pagehide/);
 assert.match(script,/overdueOnly/);
 assert.match(view,/poCommitHelp/);
 assert.match(view,/poCommitPrint/);
 assert.match(view,/poCommitExport/);
 assert.match(view,/data-i18n/);
 assert.match(api,/HttpGet\("commitments"\)/);
 assert.match(api,/HttpGet\("commitments\/export"\)/);
 assert.match(api,/TotalCount > 500/);
 assert.match(api,/=\+-@/);
});

test('cancellation requests remain visible commitments with explicit status',()=>{
 assert.match(script,/CancelRequested/);
 assert.match(script,/t\(x.stateCode\)/);
 assert.match(view,/approvalStatus/);
 assert.match(api,/Csv\(x.StateCode\)/);
});

test('confirmed supplier evidence is displayed separately from original PO aging',()=>{
 assert.match(script,/supplierPromisedDeliveryDate/);
 assert.match(script,/supplierConfirmationReference/);
 assert.match(view,/supplierPromised/);
 assert.match(view,/supplierReference/);
 assert.match(api,/Supplier Confirmed UTC/);
});
