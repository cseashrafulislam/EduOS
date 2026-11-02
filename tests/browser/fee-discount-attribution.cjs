'use strict';
// Real Chromium + production JavaScript, synthetic read-only GET responses.
// NOT authenticated SQL-backed tenant/role/CSRF or real institute acceptance.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require('playwright');
const source=path.resolve(__dirname,'../../EduOS.App/wwwroot/js/finance/fee-discount-attribution-review.js');
function fixture(lang){
 const ids=['feeAttributionNotice','feeAttributionScope','feeAttributionCount','feeAttributionPage'];
 return '<!doctype html><html lang="'+lang+'"><body data-auth-tenant-id="41" data-auth-session-reference="s1">'+
 '<section id="feeAttributionApp"><form id="feeAttributionFilters">'+
 '<select id="feeAttributionCampus" required><option value="">Select</option></select>'+
 '<input id="feeAttributionFrom" type="date" required><input id="feeAttributionTo" type="date" required>'+
 '<select id="feeAttributionSize"><option>25</option><option>50</option></select>'+
 '<button id="feeAttributionRun" type="submit">Run</button></form>'+
 ids.map(id=>'<p id="'+id+'"></p>').join('')+
 '<button id="feeAttributionCsv" disabled>CSV</button><button id="feeAttributionPrint" disabled>Print</button>'+
 '<button id="feeAttributionPrev" disabled>Prev</button><button id="feeAttributionNext" disabled>Next</button>'+
 '<table><tbody id="feeAttributionRows"></tbody></table></section></body></html>';
}
function report(url){
 const from=url.searchParams.get('fromDate'),to=url.searchParams.get('toDate');
 return {success:true,data:{
  campusId:5,campusName:'Synthetic Campus',fromDate:from,toDate:to,reviewRowsOnPage:1,
  potentialAmountOnPage:'10.25',
  invoices:{page:Number(url.searchParams.get('page')),pageSize:Number(url.searchParams.get('pageSize')),
   totalCount:1,items:[{
    invoiceReference:'12345678-1234-1234-1234-123456789abc',
    invoiceNumber:'=HYPERLINK("evil")',studentCode:'TEST-01',invoiceDate:from,currencyCode:'BDT',
    subtotal:'100.25',discountAmount:'10.25',totalAmount:'90.00',
    potentiallyMisattributedDiscount:'10.25',hasJournalLink:true,needsReview:true,
    evidenceStatus:'Possible cross-head attribution'
   }]}
 }};
}
async function verify(browser,lang){
 const context=await browser.newContext({acceptDownloads:true,viewport:{width:lang==='bn-BD'?390:1280,height:800}});
 const page=await context.newPage(),errors=[],writes=[];
 let release=null,first=true;
 page.on('pageerror',e=>errors.push(e.message));
 await page.route('http://eduos.test/**',async route=>{
  const request=route.request(),url=new URL(request.url());
  if(url.pathname==='/FeeDiscountAttributionReview/Index')
   return route.fulfill({contentType:'text/html',body:fixture(lang)});
  if(request.method()!=='GET'){writes.push(url.pathname);return route.abort();}
  if(url.pathname==='/api/finance/fees/options')
   return route.fulfill({contentType:'application/json',body:JSON.stringify({success:true,data:{
    campuses:[{id:5,name:'Synthetic Campus'}]}})});
  if(url.pathname==='/api/finance/fees/discount-attribution-review'){
   if(url.searchParams.get('toDate')==='2026-11-29')
    return route.fulfill({status:403,contentType:'application/json',
     body:'{"success":false,"message":"PRIVATE-DENIAL-REFERENCE"}'});
   if(first){first=false;await new Promise(resolve=>{release=resolve;});
    return route.fulfill({contentType:'application/json',body:JSON.stringify(report(url))}).catch(()=>{});}
   return route.fulfill({contentType:'application/json',body:JSON.stringify(report(url))});
  }
  return route.abort();
 });
 try{
  await page.goto('http://eduos.test/FeeDiscountAttributionReview/Index');
  await page.addScriptTag({path:source});
  await page.waitForFunction(()=>document.getElementById('feeAttributionCampus').options.length>1);
  await page.locator('#feeAttributionCampus').selectOption('5');
  await page.locator('#feeAttributionFrom').fill('2026-11-01');
  await page.locator('#feeAttributionTo').fill('2026-11-30');
  await page.locator('#feeAttributionRun').click();
  for(let i=0;i<100&&!release;i++)await page.waitForTimeout(20);
  assert(release,'First GET must be intercepted');
  await page.locator('#feeAttributionFrom').fill('2026-11-02');
  assert.equal(await page.locator('#feeAttributionCsv').isDisabled(),true);
  release();await page.waitForTimeout(80);
  assert.equal(await page.locator('#feeAttributionCsv').isDisabled(),true);
  await page.locator('#feeAttributionRun').click();
  await page.waitForFunction(()=>!document.getElementById('feeAttributionCsv').disabled);
  assert.match(await page.locator('#feeAttributionRows').innerText(),/TEST-01/);
  assert.match(await page.locator('#feeAttributionRows').innerText(),
   lang==='bn-BD'?/ভিন্ন ফি খাতে/:/Possible cross-head/);
  const download=page.waitForEvent('download');
  await page.locator('#feeAttributionCsv').click();
  const file=await download,csv=fs.readFileSync(await file.path(),'utf8');
  assert.equal(csv.charCodeAt(0),0xFEFF);
  assert(csv.includes(lang==='bn-BD'?'শিক্ষার্থী কোড':'Student code'));
  assert(csv.includes("'"+'=HYPERLINK(""evil"")'));
  assert(csv.includes('"10.25"'));
  await page.locator('#feeAttributionTo').fill('2026-11-29');
  assert.equal(await page.locator('#feeAttributionCsv').isDisabled(),true);
  await page.locator('#feeAttributionRun').click();
  await page.waitForFunction(()=>document.getElementById('feeAttributionNotice').classList.contains('alert-danger'));
  assert.doesNotMatch(await page.locator('#feeAttributionNotice').innerText(),/PRIVATE-DENIAL-REFERENCE/);
  assert.equal(await page.locator('#feeAttributionCsv').isDisabled(),true);
  await page.evaluate(()=>{document.body.dataset.authTenantId='42';});
  await page.waitForFunction(()=>document.getElementById('feeAttributionRun').disabled);
  assert.equal(await page.locator('#feeAttributionCsv').isDisabled(),true);
  assert.doesNotMatch(await page.locator('#feeAttributionRows').innerText(),/TEST-01/);
  assert.deepEqual(errors,[]);assert.deepEqual(writes,[]);
  console.log(lang+' discount attribution: stale, CSV, denied scope, session PASS (synthetic GET)');
 }finally{await context.close();}
}
(async()=>{
 const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
 try{for(const lang of ['en-BD','bn-BD'])await verify(browser,lang);}
 finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
