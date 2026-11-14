'use strict';
// UX D static source contracts. Final browser/SQL/tenant/security/financial QA is independent.
const test=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const base=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(base,p),'utf8');
const view=(module,file='Index')=>read('EduOS.App/Views/'+module+'/'+file+'.cshtml');
const financeReports=['FeeDueCalendar','FeeInstallmentProjection','FeeInstallmentPlans','FeeInvoiceCoverage','FeeDiscountAttributionReview'];
const adjustments=['Policies','StudentAdjustments','FineRegister','Refunds','HostelClearances','TransportClearances','ReservationPreflight','DiscountAwards'];
const inventories=['InventoryCatalog','AssetLifecycle','InventoryMovements'];
const communications=['CommunicationWorkspace','CommunicationAdministration','CustomFields','DocumentAdministration'];
const governance=['AuditLogs','ImportHistory'];
test('remaining Finance reports use the common page title and retain printable report titles',()=>{
 for(const name of financeReports){
  const src=view(name);
  assert.match(src,/ViewData\["Title"\]/,name);
  assert.doesNotMatch(src.slice(0,850),/<header\b|<h1\b/i,name);
  assert.equal((src.match(/<form\b/g)||[]).length,(src.match(/<\/form>/g)||[]).length,name);
 }
 for(const name of ['FeeDueCalendar','FeeInstallmentProjection','FeeInvoiceCoverage'])
  assert.match(view(name),/d-print-block/,name);
 const instalments=view('FeeInstallmentPlans');
 assert.doesNotMatch(instalments,/Instalment plan help/);
 assert.match(instalments,/id="feePlanRefresh"[^>]*>Apply filters/);
});
test('Finance Adjustments eight routes share an established feature view or authorized independent view',()=>{
 const shared=view('FinanceAdjustments');
 for(const name of ['policies','students','fines','refunds'])
  assert.match(shared,new RegExp('feature == "'+name+'"'),name);
 assert.doesNotMatch(shared.slice(0,950),/<header\b/i);
 for(const name of adjustments.slice(4)){
  const src=view('FinanceAdjustments',name);
  assert.match(src,/ViewData\["Title"\]/,name);
  assert.doesNotMatch(src.slice(0,950),/<header\b|<h1\b|<h2\b/i,name);
  assert.equal((src.match(/<form\b/g)||[]).length,(src.match(/<\/form>/g)||[]).length,name);
 }
 assert.match(shared,/id="adjustRefundRequestForm"/);
 assert.match(view('FinanceAdjustments','HostelClearances'),/HOSTEL-CLEARANCE-/);
 assert.match(view('FinanceAdjustments','TransportClearances'),/TRANSPORT-CLEARANCE-/);
});
test('Inventory routes reuse transaction and report partials, preserve stock count and GL separation',()=>{
 const movements=view('InventoryMovements');
 assert.match(movements,/data-task-page=/);
 for(const name of ['_Create','_Adjustments','_PhysicalCounts','_StockPosition','_Reservations','_ItemHistory'])
  assert.match(movements,new RegExp('"'+name+'"'),name);
 assert.match(view('InventoryMovements','_PhysicalCounts'),/id="countReload"/);
 assert.match(view('InventoryMovements','_PhysicalCounts'),/Blind counts conceal book quantities/);
 assert.match(view('InventoryMovements','FacilityFinance'),/id="fpfPage"/);
 for(const name of inventories)assert.match(view(name),/ViewData\["Title"\]/,name);
 const catalog=view('InventoryCatalog'),category=view('AssetLifecycle','_Categories');
 for(const id of ['itemCode','locationCode'])assert.match(catalog,new RegExp('id="'+id+'" type="hidden"'));
 assert.match(category,/id="assetCategoryCode" type="hidden"/);
});
test('Code-like internal identifiers are generated on the server for new master records',()=>{
 const cases=[
  ['EduOS.Service/Services/Inventory/InventoryCatalogItems.cs','ITM-'],
  ['EduOS.Service/Services/Inventory/InventoryCatalogLocations.cs','LOC-'],
  ['EduOS.Service/Services/Inventory/AssetLifecycleCategories.cs','CAT-'],
  ['EduOS.Service/Services/Communication/CommunicationAdministrationService.Categories.cs','NTC-'],
  ['EduOS.Service/Services/Communication/CommunicationAdministrationService.Templates.cs','MSG-'],
  ['EduOS.Service/Services/Documents/DocumentService.cs','DCT-'],
  ['EduOS.Service/Services/Documents/DocumentService.cs','DTP-'],
  ['EduOS.Service/Services/System/CustomFieldService.cs','CF_']
 ];
 for(const [p,prefix] of cases){
  const src=read(p);
  assert.ok(src.includes('"'+prefix+'"'),p+' generates '+prefix);
  assert.match(src,/ClientRequestId/,p);
 }
 assert.match(view('CommunicationAdministration','_Categories'),/id="categoryCode" type="hidden"/);
 assert.match(view('CommunicationAdministration','_Templates'),/id="templateCode" type="hidden"/);
 assert.match(view('DocumentAdministration'),/id="typeCode" type="hidden"/);
 assert.match(view('DocumentAdministration'),/id="templateCode" type="hidden"/);
 assert.match(view('CustomFields'),/id="cfKey" type="hidden"/);
});
test('Communication, Document and Audit direct views use shared title without an in-content duplicate',()=>{
 for(const name of [...communications,...governance]){
  const src=view(name);
  assert.match(src,/ViewData\["Title"\]/,name);
  assert.doesNotMatch(src.slice(0,1050),/<h1\b|<header\b|content-header-card/i,name);
 }
 for(const name of ['CommunicationWorkspace','CustomFields','DocumentAdministration','AuditLogs','ImportHistory']){
  const src=view(name);
  assert.equal((src.match(/<form\b/g)||[]).length,(src.match(/<\/form>/g)||[]).length,name);
 }
 const docs=view('DocumentAdministration');
 assert.match(docs,/docPendingRefresh/);
 assert.match(docs,/Protected document reviews/);
 assert.match(read('EduOS.Service/Services/Documents/DocumentService.cs'),/DocumentType\.Created/);
});
test('touched legacy front-end scripts parse, use retry identifiers and retain tenant checks',()=>{
 const scripts=[
  'EduOS.App/wwwroot/js/inventory/catalog.js',
  'EduOS.App/wwwroot/js/communication/admin.js',
  'EduOS.App/wwwroot/js/document/admin.js',
  'EduOS.App/wwwroot/js/custom-fields/admin.js'
 ];
 for(const p of scripts)assert.doesNotThrow(()=>new vm.Script(read(p)),p);
 assert.match(read(scripts[0]),/crypto\.randomUUID\(\)/);
 assert.match(read(scripts[1]),/clientRequestId/);
 assert.match(read(scripts[2]),/clientRequestId/);
 assert.match(read(scripts[3]),/clientRequestId/);
 assert.match(read(scripts[0]),/authTenantId/);
 assert.match(read(scripts[3]),/authTenantId/);
});
test('lane D has exactly 39 source-completed routes, with Final QA separately deferred',()=>{
 const doc=read('docs/ux/lanes/UX_D.md');
 const routes=[...doc.matchAll(/^- \[([ x])\] \x60(\/[^\x60]+)\x60/gm)];
 assert.equal(routes.length,39);
 assert.equal(routes.filter(x=>x[1]==='x').length,39);
 assert.match(doc,/39\/39 UX D routes/);
 assert.match(doc,/Final QA/);
 assert.match(doc,/NOT CERTIFIED/);
});
