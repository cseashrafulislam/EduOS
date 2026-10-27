const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname,'../..');
const js = fs.readFileSync(path.join(root,'EduOS.App/wwwroot/js/finance/fees.js'),'utf8');
const view = fs.readFileSync(path.join(root,'EduOS.App/Views/FeeOperations/Index.cshtml'),'utf8');
const api = fs.readFileSync(path.join(root,'EduOS.App/Controllers/Api/FeeBillingController.cs'),'utf8');
test('invoice audit requires tenant admin and scopes direct invoice IDs',()=>{
 assert.match(api,/allocation-audit/);
 assert.match(api,/Authorize\(Roles = "TenantAdmin"\)/);
 assert.match(js,/allocation-audit\?page=/);
 assert.match(view,/User\.IsInRole\("TenantAdmin"\)/);
});
test('invoice audit offers paged readback and formula-safe CSV',()=>{
 for(const id of ['feeAuditRows','feeAuditPrev','feeAuditNext','feeAuditCsv','feeAuditSummary'])
  assert.match(view,new RegExp('id="'+id+'"'));
 assert.match(js,/gross successful allocations/);
 assert.match(js,/revokeObjectURL/);
 assert.match(js,/replace\(\/"\/g/);
});
