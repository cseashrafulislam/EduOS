const test=require('node:test');
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const read=p=>fs.readFileSync(path.join(__dirname,'../..',p),'utf8');
test('Aging uses canonical posted AP with tenant/campus authorization, not shadow balances',()=>{
 const s=read('EduOS.Service/Services/Inventory/ProcurementSupplierInvoiceAging.cs');
 for(const v of ['Visible()','JournalState.Posted','AccountType.Liability','CreditAmount==x.TotalAmount',
  'SourceType=="ProcurementSupplierInvoice"','Skip((page-1)*pageSize)','overdueOnly'])
  assert.ok(s.includes(v),v);
 assert.doesNotMatch(s,/db\\.Journals\\.(Add|Update)|db\\.InventoryMovements\\.(Add|Update)/);
});
test('Aging has protected bounded export and bilingual XSS-safe UI',()=>{
 const c=read('EduOS.App/Controllers/Api/ProcurementSupplierInvoicesController.cs');
 const v=read('EduOS.App/Views/ProcurementSupplierInvoiceMatching/Index.cshtml');
 const js=read('EduOS.App/wwwroot/js/procurement/supplier-payable-aging.js');
 const csvGuard=read('EduOS.App/Controllers/Api/ProcurementCsvExportGuard.cs');
 for(const x of ['HttpGet("aging")','HttpGet("aging/export")','TotalCount>500','ProcurementCsvExportGuard.Cell'])
  assert.ok(c.includes(x),x);
 for(const x of ['"=+-@"','char.GetUnicodeCategory','\\u200B','\\u202E'])
  assert.ok(csvGuard.includes(x), 'missing shared CSV formula/invisible-prefix defense: '+x);
 assert.match(c,/ExportFailure\(response\)/);
 for(const x of ['supplierPayableAging','data-bn','paRows','supplier-payable-aging.js'])
  assert.ok(v.includes(x),x);
 for(const x of ['textContent','replaceChildren','n!==seq',"credentials:'same-origin'", "cache:'no-store'"])
  assert.ok(js.includes(x),x);
 assert.doesNotMatch(js,/innerHTML|localStorage|sessionStorage/);
});
