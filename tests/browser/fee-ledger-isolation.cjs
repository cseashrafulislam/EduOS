'use strict';
// Production fees.js in real Chromium with synthetic GET-only Finance API data.
// This is NOT authenticated SQL-backed acceptance and MUST NOT send payment writes.
const assert=require('node:assert/strict');
const path=require('node:path');
const {chromium}=require('playwright');
const root=path.resolve(__dirname,'../..');
const script=path.join(root,'EduOS.App/wwwroot/js/finance/fees.js');
function html(lang){
 const ids=['feeAlert','feeLedgerSummary','feeInvoicePage','feeReceiptPage','feeAuditSummary','feeAuditPage'];
 const buttons=['feeFindStudent','feeLoadLedger','feeInvoicePrev','feeInvoiceNext','feeReceiptPrev','feeReceiptNext','feeAuditClose','feeAuditCsv','feeAuditPrev','feeAuditNext','feeCollect'];
 return '<!doctype html><html lang="'+lang+'"><head><meta name="request-verification-token" content="synthetic-only"></head><body>'+
 '<section id="feeApp" data-active-feature="ledger">'+
 '<input id="feeStudentSearch"><select id="feeStudent"><option value="">Choose student</option><option value="student-a">A</option><option value="student-b">B</option></select>'+
 ids.map(id=>'<p id="'+id+'" tabindex="-1"></p>').join('')+
 buttons.map(id=>'<button id="'+id+'" type="button">'+id+'</button>').join('')+
 '<table><tbody id="feeInvoices"></tbody></table><table><tbody id="feeReceipts"></tbody></table>'+
 '<section id="feeAllocationAudit" class="d-none"><table><tbody id="feeAuditRows"></tbody></table></section>'+
 '<section id="feePaymentPanel" class="d-none"><form id="feePaymentForm"></form>'+
 '<input id="feePaymentDate"><input id="feePaymentInvoice"><input id="feePaymentAmount"></section></section></body></html>';
}
const response=data=>({contentType:'application/json',body:JSON.stringify({success:true,data})});
const ledger=name=>({studentName:name,studentCode:name,totalBilled:100,totalCreditNotes:0,netBilled:100,netPaid:100,totalDue:0});
const invoice=name=>({reference:'invoice-'+name,invoiceNumber:'INV-'+name,invoiceDate:'2026-11-01',dueDate:'2026-11-30',totalAmount:100,paidAmount:100,dueAmount:0,state:4});
const receipt=name=>({reference:'receipt-'+name,receiptNumber:'REC-'+name,paymentDate:'2026-11-01',amount:100,paymentMethod:1,allocations:[]});
async function waitFor(check){for(let n=0;n<100;n++){if(check())return;await new Promise(r=>setTimeout(r,20));}throw Error('Timed out waiting for synthetic request');}
async function verify(browser,lang){
 const context=await browser.newContext({viewport:{width:lang==='bn-BD'?390:1280,height:800}});
 const page=await context.newPage(),errors=[],writes=[];
 let releaseA=null,releaseAudit=null;
 page.on('pageerror',e=>errors.push(e.message));
 await page.route('http://localhost:4173/**',async route=>{
  const req=route.request(),url=new URL(req.url()),p=url.pathname;
  if(p==='/FeeOperations/Ledger')return route.fulfill({status:200,contentType:'text/html',body:html(lang)});
  if(req.method()!=='GET'){writes.push(p);throw Error('Unexpected Finance write '+p);}
  if(p.endsWith('/student-a/ledger')){
   await new Promise(resolve=>{releaseA=resolve;});
   return route.fulfill(response(ledger('A'))).catch(()=>{});
  }
  if(p.endsWith('/student-b/ledger'))return route.fulfill(response(ledger('B')));
  if(p.endsWith('/invoices')||p.endsWith('/payments')){
   const name=p.includes('student-a')?'A':'B';
   return route.fulfill(response({totalCount:1,items:p.endsWith('/invoices')?[invoice(name)]:[receipt(name)]}));
  }
  if(p.includes('/allocation-audit')){
   await new Promise(resolve=>{releaseAudit=resolve;});
   return route.fulfill(response({invoiceNumber:'INV-B',invoiceTotal:100,currentPaid:100,currentDue:0,
    grossSuccessfulAllocations:100,grossToNetDifference:0,matchesCurrentInvoice:true,totalAllocations:1,
    items:[{receiptNumber:'REC-B',paymentDate:'2026-11-01',allocationAmount:100,paymentState:3,journalId:null}]})).catch(()=>{});
  }
  if(p.endsWith('/students/search'))return route.fulfill(response([{reference:'student-a',studentCode:'A',name:'A'}]));
  throw Error('Unexpected GET '+p);
 });
 try{
  await page.goto('http://localhost:4173/FeeOperations/Ledger');
  await page.addScriptTag({path:script});
  await page.locator('#feeStudent').selectOption('student-a');
  await page.locator('#feeLoadLedger').click();
  await waitFor(()=>!!releaseA);
  await page.locator('#feeStudent').selectOption('student-b');
  await page.locator('#feeLoadLedger').click();
  await page.locator('#feeLedgerSummary').getByText('B (B)',{exact:false}).waitFor().catch(()=>{});
  await page.waitForFunction(()=>document.querySelector('#feeInvoices').textContent.includes('INV-B') &&
    document.querySelector('#feeReceipts').textContent.includes('REC-B'));
  releaseA();
  await page.waitForTimeout(60);
  assert.match(await page.locator('#feeLedgerSummary').innerText(),/B \(B\)/);
  assert.doesNotMatch(await page.locator('#feeInvoices').innerText(),/INV-A/);
  assert.doesNotMatch(await page.locator('#feeReceipts').innerText(),/REC-A/);
  await page.locator('#feeInvoices button').filter({hasText:lang==='bn-BD'?'রসিদ নিরীক্ষা':'Receipt audit'}).click();
  await waitFor(()=>!!releaseAudit);
  await page.locator('#feeStudent').selectOption('student-a');
  releaseAudit();
  await page.waitForTimeout(60);
  assert.equal(await page.locator('#feeAllocationAudit').evaluate(x=>x.classList.contains('d-none')),true);
  assert.equal(await page.locator('#feeAuditCsv').isDisabled(),true);
  assert.equal(await page.locator('#feePaymentPanel').evaluate(x=>x.classList.contains('d-none')),true);
  assert.equal(await page.locator('#feeInvoices').innerText(),lang==='bn-BD'?'একজন শিক্ষার্থী বাছুন।':'Select a student.');
  assert.deepEqual(writes,[]);
  assert.deepEqual(errors,[]);
  console.log(lang+' finance ledger: stale student/audit responses isolated in real Chromium (synthetic GET)');
 }finally{await context.close();}
}
(async()=>{const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
 try{await verify(browser,'en-BD');await verify(browser,'bn-BD');}finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
