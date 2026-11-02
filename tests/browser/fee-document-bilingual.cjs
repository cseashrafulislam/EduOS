'use strict';
// Real production fee document JavaScript in Chromium with synthetic read-only Finance GETs.
// Not authenticated SQL-backed acceptance or actual PDF binary fidelity.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {chromium}=require('playwright');
const root=path.resolve(__dirname,'../..');
const script=path.join(root,'EduOS.App/wwwroot/js/finance/fee-documents.js');
const view=fs.readFileSync(path.join(root,'EduOS.App/Views/FeeDocuments/Document.cshtml'),'utf8');
assert(view.includes('finance/fee-documents.js')&&view.includes('docPrint'));
const student='11111111-1111-4111-8111-111111111111',ref='22222222-2222-4222-8222-222222222222';
function html(lang,kind){
 const ids=['docPrint','docNotice','docContent','docTitle','docStudent','docNumber','docDate','docState','docExtraLabel','docExtra','docColumns','docRows','docTotal','docNote'];
 return '<!doctype html><html lang="'+lang+'"><body><section id="feeDocument" data-kind="'+kind+'">'+
 ids.map(id=>id==='docPrint'?'<button id="'+id+'" disabled>Print</button>':
 id==='docContent'?'<article id="'+id+'" class="d-none"></article>':
 id==='docColumns'||id==='docRows'?'<table><tbody id="'+id+'"></tbody></table>':
 '<div id="'+id+'"></div>').join('')+'</section></body></html>';
}
const invoice={studentName:'<img src=x onerror=alert(1)>',studentCode:'S-001',invoiceNumber:'INV-001',
 invoiceDate:'2026-11-02',dueDate:'2026-11-30',state:3,currencyCode:'BDT',
 totalAmount:'1234567890123.45',paidAmount:'100.20',dueAmount:'1234567890023.25',
 lines:[{feeHeadName:'Tuition',description:'November',amount:'1234567890123.45',
 discountAmount:'0.00',fineAmount:'0.00',netAmount:'1234567890123.45'}]};
const receipt={studentName:'Demo Student',studentCode:'S-002',payment:{
 receiptNumber:'R-002',paymentDate:'2026-11-02',state:3,paymentMethod:1,
 currencyCode:'BDT',amount:'200.10',allocations:[{invoiceNumber:'INV-002',amount:'200.10'}]}};
async function check(browser,lang,kind,status=200,invalid=false){
 const context=await browser.newContext({viewport:{width:lang==='bn-BD'?390:1280,height:800}});
 const page=await context.newPage(),requests=[],errors=[];
 page.on('pageerror',err=>errors.push(err.message));
 await page.addInitScript(()=>{window.__printed=0;window.print=()=>{window.__printed++;};});
 await page.route('http://localhost:4173/**',route=>{
  const req=route.request(),url=new URL(req.url());
  requests.push({method:req.method(),path:url.pathname});
  if(url.pathname==='/FeeDocuments/'+(kind==='invoice'?'Invoice':'Receipt'))
   return route.fulfill({contentType:'text/html; charset=utf-8',body:html(lang,kind)});
  if(url.pathname.startsWith('/api/finance/fees/students/'))
   return route.fulfill({status,contentType:'application/json',
    body:JSON.stringify(status===200?{success:true,data:kind==='invoice'?invoice:receipt}:{success:false,message:'Sensitive backend detail'})});
  throw Error('Unexpected URL '+req.method()+' '+url.pathname);
 });
 try{
  const link='http://localhost:4173/FeeDocuments/'+(kind==='invoice'?'Invoice':'Receipt')+
   '?studentReference='+(invalid?'bad-reference':student)+'&documentReference='+ref;
  await page.goto(link);await page.addScriptTag({path:script});
  if(status!==200||invalid){
   await page.waitForFunction(()=>document.getElementById('docNotice').classList.contains('alert-danger'));
   assert.equal(await page.locator('#docPrint').isDisabled(),true);
   const notice=await page.locator('#docNotice').innerText();
   assert.match(notice,invalid?(lang==='bn-BD'?/লিংক সঠিক নয়/:/Invalid document link/):
    (lang==='bn-BD'?/নথি পাওয়া যায়নি/:/Document unavailable/));
   assert(!notice.includes('Sensitive backend detail'));
  }else{
   await page.waitForFunction(()=>!document.getElementById('docPrint').disabled);
   assert.match(await page.locator('#docTitle').innerText(),kind==='invoice'?
    (lang==='bn-BD'?/ফি ইনভয়েস/:/fee invoice/):(lang==='bn-BD'?/পরিশোধ রসিদ/:/payment receipt/));
   assert.equal(await page.locator('#docState').innerText(),kind==='invoice'?
    (lang==='bn-BD'?'আংশিক পরিশোধিত':'Partially paid'):(lang==='bn-BD'?'সফল':'Successful'));
   if(kind==='invoice'){
    assert((await page.locator('#docTotal').innerText()).includes('1,234,567,890,123.45'));
    assert.equal(await page.locator('#docStudent img').count(),0);
    assert((await page.locator('#docStudent').innerText()).includes('<img'));
   }else{
    assert((await page.locator('#docTotal').innerText()).includes('200.10'));
    assert.equal(await page.locator('#docExtra').innerText(),lang==='bn-BD'?'নগদ':'Cash');
   }
   await page.locator('#docPrint').click();
   assert.equal(await page.evaluate(()=>window.__printed),1);
  }
  assert(requests.every(r=>r.method==='GET'),'Print must not issue mutations');
  assert.deepEqual(errors,[]);
 }finally{await context.close();}
}
(async()=>{const browser=await chromium.launch({headless:true,args:['--no-sandbox']});try{
 for(const lang of ['en-BD','bn-BD']){
  await check(browser,lang,'invoice');
  await check(browser,lang,'receipt');
  await check(browser,lang,'invoice',403);
  await check(browser,lang,'receipt',200,true);
 }
 console.log('Fee invoice/receipt Chromium PASS: two locales, cents, safe text, 403/invalid, GET-only (synthetic API)');
}finally{await browser.close();}})().catch(err=>{console.error(err);process.exitCode=1;});
