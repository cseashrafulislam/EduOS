'use strict';
// Real Chromium + production scripts, Razor-derived markup, synthetic GET-only API.
// This is NOT authenticated SQL-backed acceptance; no Finance or bank writes.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require('playwright');
const base=path.resolve(__dirname,'../..'),read=p=>fs.readFileSync(path.join(base,p),'utf8');
const supplier=read('EduOS.App/Views/ProcurementSupplierInvoiceMatching/Index.cshtml');
const asset=read('EduOS.App/Views/AssetAccounting/Index.cshtml');
function part(source,start,end){const a=source.indexOf(start),b=source.indexOf(end,a);
 assert(a>=0&&b>a,'Razor markup changed');return source.slice(a,b);}
const supplierHtml=part(supplier,'<section id="supplierSettlement"','<section id="supplierPayableAging"')
 .split('data-maker="@maker.ToString().ToLowerInvariant()"').join('data-maker="true"')
 .split('data-reviewer="@reviewer.ToString().ToLowerInvariant()"').join('data-reviewer="true"');
const assetHtml=part(asset,'<section id="assetAccountingWorkspace"','@section Scripts')
 .split('data-maker="@maker.ToString().ToLowerInvariant()"').join('data-maker="true"')
 .split('data-checker="@checker.ToString().ToLowerInvariant()"').join('data-checker="true"');
const shell=(lang,body,scripts='')=>'<!doctype html><html lang="'+lang+'"><head>'+
 '<meta name="request-verification-token" content="synthetic-only"><style>.d-none{display:none!important}</style>'+
 '</head><body>'+body+scripts+'</body></html>';
const json=(data,status=200)=>({status,contentType:'application/json',
 body:JSON.stringify(status===200?{success:true,data}:{success:false,message:data})});
const A='aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa',B='bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';
async function supplierCheck(browser,lang,mobile){
 const context=await browser.newContext({viewport:mobile?{width:390,height:844}:{width:1280,height:800}});
 const page=await context.newPage(),errors=[],writes=[],blocked=new Set();
 page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/api/procurement/supplier-invoices/**',route=>{
  const request=route.request(),url=new URL(request.url()),parts=url.pathname.split('/');
  const ref=parts[4],kind=url.pathname.endsWith('/options')?'options':'list';
  if(request.method()!=='GET'){writes.push(url.pathname);return route.fulfill(json('No writes',403));}
  if(blocked.has(ref+'-'+kind))return route.fulfill(json('Campus access revoked',403));
  if(kind==='options')return route.fulfill(json({availableAmount:18,postedSupplierCreditAmount:1,
   banks:[{id:7,name:'Bank'}],periods:[{id:3,name:'Open period'}]}));
  return route.fulfill(json({totalPages:1,items:[{reference:ref+'-instruction',
   state:'Submitted',amount:6,plannedDate:'2026-11-02',makerId:200,rowVersion:'rv'}]}));
 });
 try{
  await page.setContent(shell(lang,'<section id="supplierInvoiceMatch" data-user-id="100">'+supplierHtml+'</section>'));
  await page.addScriptTag({path:path.join(base,'EduOS.App/wwwroot/js/procurement/supplier-settlement-instructions.js')});
  const select=ref=>page.evaluate(reference=>window.dispatchEvent(
   new CustomEvent('procurement-settlement-select',{detail:{reference,invoiceNumber:'INV-'+reference,rowVersion:'rv'}})),ref);
  await select('A');await page.locator('#ssRows button').first().waitFor();
  assert.match(await page.locator('#ssSelected').innerText(),/INV-A/);
  assert.equal(await page.locator('#ssForm').evaluate(x=>x.classList.contains('d-none')),false);
  await page.locator('#ssRows button').first().click();
  assert.equal(await page.locator('#ssReviewDialog').evaluate(x=>x.open),true);
  blocked.add('A-list');await select('A');
  await page.locator('#ssStatus').filter({hasText:'Campus access revoked'}).waitFor();
  assert.equal(await page.locator('#ssReviewDialog').evaluate(x=>x.open),false);
  assert.equal(await page.locator('#ssRows tr').count(),0);
  blocked.add('B-options');await select('B');
  await page.locator('#ssStatus').filter({hasText:'Campus access revoked'}).waitFor();
  assert.doesNotMatch(await page.locator('#ssSelected').innerText(),/INV-A|INV-B/);
  assert.equal(await page.locator('#ssRows tr').count(),0);
  assert.equal(await page.locator('#ssForm').evaluate(x=>x.classList.contains('d-none')),true);
  assert.equal(await page.locator('#ssNext').isDisabled(),true);
  if(lang.startsWith('bn'))assert.match(await page.locator('#ssSelected').innerText(),/ইনভয়েস/);
  assert.deepEqual(writes,[]);assert.deepEqual(errors,[]);
 }finally{await context.close();}
}
async function assetCheck(browser,lang,mobile){
 const context=await browser.newContext({viewport:mobile?{width:390,height:844}:{width:1280,height:800}});
 const page=await context.newPage(),errors=[],writes=[];
 page.on('pageerror',e=>errors.push(e.message));
 await page.route('https://eduos.test/**',route=>{
  const request=route.request(),p=new URL(request.url()).pathname;
  if(p==='/asset')return route.fulfill({status:200,contentType:'text/html',body:shell(lang,assetHtml,
   '<script type="module" src="/js/inventory/asset-accounting.js"></script>')});
  if(p==='/js/inventory/asset-accounting.js')return route.fulfill({status:200,contentType:'text/javascript',
   body:read('EduOS.App/wwwroot/js/inventory/asset-accounting.js')});
  if(p==='/js/inventory/movements-api.js')return route.fulfill({status:200,contentType:'text/javascript',
   body:read('EduOS.App/wwwroot/js/inventory/movements-api.js')});
  if(request.method()!=='GET'){writes.push(p);return route.fulfill(json('No writes',403));}
  if(p.endsWith('/options'))return route.fulfill(json({accounts:[],periods:[]}));
  if(p.endsWith('/capitalization-readiness'))return route.fulfill(json({
   assetTag:'ASSET-A',statusCode:'Active',candidateLineCount:0,candidates:[]}));
  if(p.endsWith('/statement')){
   if(p.includes(B))return route.fulfill(json('Campus access revoked',403));
   return route.fulfill(json({assetBookId:42,costBasis:10,postedDepreciation:0,
    postedImpairment:0,carryingValue:10,policyVersion:1,policyMode:'Automatic',
    remainingUsefulLifeMonths:36,events:[],policyHistory:[]}));
  }
  if(p.endsWith('/capitalization'))return route.fulfill(json('No capitalization command',404));
  return route.fulfill(json('Unexpected route',403));
 });
 try{
  await page.goto('https://eduos.test/asset');
  await page.locator('#aaAsset').fill(A);await page.locator('#aaLoad').click();
  await page.locator('#aaBook').filter({hasText:'Book #42'}).waitFor();
  assert.equal(await page.locator('#aaResults').evaluate(x=>x.classList.contains('d-none')),false);
  await page.locator('#aaAsset').fill(B);
  assert.equal(await page.locator('#aaResults').evaluate(x=>x.classList.contains('d-none')),true,
   'Asset input edit must immediately clear previous financial evidence');
  assert.equal(await page.locator('#aaBook').innerText(),'');
  await page.locator('#aaLoad').click();
  await page.locator('#aaStatus').filter({hasText:'Campus access revoked'}).waitFor();
  assert.equal(await page.locator('#aaEvents tr').count(),0);
  assert.equal(await page.locator('#aaCapReview button').count(),0);
  assert.equal(await page.locator('#aaResults').evaluate(x=>x.classList.contains('d-none')),true);
  if(lang.startsWith('bn'))assert.match(await page.locator('h2').first().innerText(),/সম্পদ/);
  assert.deepEqual(writes,[]);assert.deepEqual(errors,[]);
 }finally{await context.close();}
}
(async()=>{const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
 try{await supplierCheck(browser,'en-BD',false);await supplierCheck(browser,'bn-BD',true);
 await assetCheck(browser,'en-BD',false);await assetCheck(browser,'bn-BD',true);
 console.log('Procurement/Asset Chromium PASS: real production JS, Razor-derived bilingual desktop/mobile, synthetic 403, zero writes.');}
 finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
