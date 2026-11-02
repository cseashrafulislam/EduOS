'use strict';
// Actual production aging and reconciliation scripts in Chromium; synthetic GET only.
// Not authenticated SQL-backed role/tenant acceptance.
const assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path');
const {chromium}=require('playwright');
const root=path.resolve(__dirname,'../..');
const scripts=['fee-receivables-aging.js','fee-reconciliation.js','fee-payment-reconciliation.js']
 .map(name=>path.join(root,'EduOS.App/wwwroot/js/finance',name));
function html(lang){
 const inputs=['agingDate','agingSearch'];
 const buttons=['agingRun','agingExport','agingPrev','agingNext','reconcileRun','reconcilePrev','reconcileNext',
  'paymentReconcileRun','paymentReconcilePrev','paymentReconcileNext'];
 const parts=['agingAlert','agingCount','agingPage','reconcileNotice','reconcilePage',
  'paymentReconcileNotice','paymentReconcilePage'];
 return '<!doctype html><html lang="'+lang+'"><body><section id="feeAgingApp">'+
 '<form id="agingFilters">'+inputs.map(id=>'<input id="'+id+'">').join('')+
 '<select id="agingCampus"><option value="">All</option></select>'+
 '<select id="agingPageSize"><option value="25">25</option></select>'+
 '<button id="agingRun" type="submit">Run</button></form>'+
 parts.map(id=>'<p id="'+id+'"></p>').join('')+
 buttons.filter(id=>id!=='agingRun').map(id=>'<button type="button" id="'+id+'" disabled>'+id+'</button>').join('')+
 '<table><tbody id="agingTotals"></tbody></table><table><tbody id="agingRows"></tbody></table>'+
 '<section id="feeReconciliation"><table><tbody id="reconcileRows"></tbody></table></section>'+
 '<section id="feePaymentReconciliation"><table><tbody id="paymentReconcileRows"></tbody></table></section>'+
 '</section></body></html>';
}
function aging(url){
 const date=url.searchParams.get('agingDate'),search=url.searchParams.get('search');
 return {success:true,data:{agingDate:date,campuses:[{id:5,name:'Synthetic Campus'}],
 totals:[{currencyCode:'BDT',invoiceCount:1,current:'10.50',days1To30:'0.00',days31To60:'0.00',
 days61To90:'0.00',daysOver90:'0.00',outstanding:'10.50'}],
 invoices:{page:Number(url.searchParams.get('page')),pageSize:25,totalCount:1,items:[{
 reference:'00000000-0000-4000-8000-000000000001',invoiceNumber:search?'=HYPERLINK("evil")':'OLD-INV',
 studentCode:'S-1',displayName:'Synthetic Learner',campusName:'Synthetic Campus',
 invoiceDate:date,dueDate:date,currencyCode:'BDT',totalAmount:'20.50',
 paidAmount:'10.00',dueAmount:'10.50',daysOverdue:0,agingBucket:'Current'
 }]}}};
}
function recon(url,payment){
 const row=payment?{receiptNumber:'RC-1',studentCode:'S-1',studentName:'Synthetic',
 campusName:'Synthetic Campus',amount:'10.50',grossAllocated:'10.50',paidRefunds:'0.00',
 netAllocated:'10.50',status:'Successful',issues:''}:
 {invoiceNumber:'IN-1',studentCode:'S-1',studentName:'Synthetic',campusName:'Synthetic Campus',
 totalAmount:'20.50',paidAmount:'10.00',netAllocatedAmount:'10.00',creditNoteAmount:'0.00',
 reservationCreditAmount:'0.00',studentCreditPayable:'0.00',dueAmount:'10.50',
 status:'Issued',issues:''};
 return {success:true,data:{page:Number(url.searchParams.get('page')),pageSize:25,
 totalCount:1,pageExceptions:0,rows:[row]}};
}
async function verify(browser,lang){
 const context=await browser.newContext({acceptDownloads:true,viewport:{width:lang==='bn-BD'?390:1280,height:800}});
 const page=await context.newPage(),errors=[],writes=[];let releaseOld=null;
 page.on('pageerror',e=>errors.push(e.message));
 await page.route('http://localhost:4173/**',async route=>{
  const req=route.request(),url=new URL(req.url());
  if(url.pathname==='/FeeReceivablesAging/Index')
   return route.fulfill({contentType:'text/html; charset=utf-8',body:html(lang)});
  if(req.method()!=='GET'){writes.push(url.pathname);throw Error('Unexpected write');}
  if(url.pathname==='/api/finance/receivables-aging'){
   if(!url.searchParams.has('search')){
    await new Promise(resolve=>{releaseOld=resolve;});
    return route.fulfill({contentType:'application/json',body:JSON.stringify(aging(url))}).catch(()=>{});
   }
   if(url.searchParams.has('campusId'))
    return route.fulfill({status:403,contentType:'application/json',body:'{"success":false,"message":"PRIVATE-SERVER-ERROR"}'});
   return route.fulfill({contentType:'application/json',body:JSON.stringify(aging(url))});
  }
  if(url.pathname.endsWith('/reconciliation')){
   const payment=url.pathname.endsWith('/payment-reconciliation');
   return route.fulfill({contentType:'application/json',body:JSON.stringify(recon(url,payment))});
  }
  throw Error('Unexpected GET '+url.pathname);
 });
 try{
  await page.goto('http://localhost:4173/FeeReceivablesAging/Index');
  for(const script of scripts)await page.addScriptTag({path:script});
  await page.waitForFunction(()=>document.querySelector('#agingRun').disabled);
  for(let i=0;i<100&&!releaseOld;i++)await page.waitForTimeout(20);
  assert(releaseOld,'Initial aging GET must be intercepted before the filter change');
  await page.locator('#agingSearch').fill('INV');
  assert.equal(await page.locator('#agingExport').isDisabled(),true);
  if(releaseOld)releaseOld();
  await page.waitForTimeout(80);
  assert.doesNotMatch(await page.locator('#agingRows').innerText(),/OLD-INV/);
  await page.locator('#agingRun').click();
  await page.waitForFunction(()=>document.querySelector('#agingExport').disabled===false);
  assert.match(await page.locator('#agingRows').innerText(),/10.50/);
  await page.locator('#reconcileRun').click();
  await page.locator('#paymentReconcileRun').click();
  await page.waitForFunction(()=>document.querySelector('#reconcileRows').textContent.includes('IN-1') &&
   document.querySelector('#paymentReconcileRows').textContent.includes('RC-1'));
  const promise=page.waitForEvent('download');
  await page.locator('#agingExport').click();
  const download=await promise,csv=fs.readFileSync(await download.path(),'utf8');
  assert(csv.includes(lang==='bn-BD'?'শিক্ষার্থী কোড':'Student code'));
  assert(csv.includes('"'+"'"+'=HYPERLINK(""evil"")"'));
  await page.locator('#agingSearch').fill('NEW');
  assert.equal(await page.locator('#agingExport').isDisabled(),true);
  assert.doesNotMatch(await page.locator('#reconcileRows').innerText(),/IN-1/);
  assert.doesNotMatch(await page.locator('#paymentReconcileRows').innerText(),/RC-1/);
  await page.locator('#agingCampus').selectOption('5');
  await page.locator('#agingRun').click();
  await page.waitForFunction(()=>document.querySelector('#agingAlert').classList.contains('alert-danger'));
  assert.doesNotMatch(await page.locator('#agingAlert').innerText(),/PRIVATE-SERVER-ERROR/);
  assert.match(await page.locator('#agingAlert').innerText(),lang==='bn-BD'?/অনুমতি নেই/:/access denied/);
  assert.equal(await page.locator('#agingExport').isDisabled(),true);
  assert.deepEqual(errors,[]);assert.deepEqual(writes,[]);
  console.log(lang+' fee aging/reconciliation: stale isolation, localized CSV, 403 privacy PASS (synthetic GET)');
 }finally{await context.close();}
}
(async()=>{const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
 try{await verify(browser,'en-BD');await verify(browser,'bn-BD');}finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
