'use strict';
// Actual production fee task scripts in Chromium with synthetic, never posted, Finance API data.
// This is UI/CSV acceptance, not an authenticated SQL-backed financial transaction.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {chromium}=require('playwright');
const root=path.resolve(__dirname,'../..');
const localeScript=path.join(root,'EduOS.App/wwwroot/js/platform/task-workspace-locales.js');
const financeScript=path.join(root,'EduOS.App/wwwroot/js/finance/fees.js');
const view=fs.readFileSync(path.join(root,'EduOS.App/Views/FeeOperations/Index.cshtml'),'utf8');
assert(view.includes('task-workspace-locales.js') && view.includes('finance/fees.js'));
function html(lang) {
 const ids=['feeAlert','feePreviewSummary','feeGenerationInfo'];
 const buttons=['feePreview','feePreviewCsv','feeGenerate'];
 const inputs=['feeMonth','feeBillingYear','feeDueDate'];
 return '<!doctype html><html lang="'+lang+'"><head><meta name="request-verification-token" content="synthetic-csrf"></head><body>'+
 '<section id="feeApp" data-active-feature="billing">'+
 ids.map(id=>'<p id="'+id+'" tabindex="-1"></p>').join('')+
 '<form id="feeInvoiceForm"></form>'+
 '<select id="feeBatch"></select><select id="feeInvoiceStructure"></select>'+
 inputs.map(id=>'<input id="'+id+'">').join('')+
 buttons.map(id=>'<button id="'+id+'" type="button"'+(id==='feePreviewCsv'?' disabled':'')+'>'+id+'</button>').join('')+
 '<table><tbody id="feePreviewRows"></tbody></table></section></body></html>';
}
async function verify(browser,lang) {
 const context=await browser.newContext({acceptDownloads:true,viewport:{width:lang==='bn-BD'?390:1280,height:800}});
 const page=await context.newPage(),errors=[],writes=[];
 page.on('pageerror',e=>errors.push(e.message));
 await page.route('http://localhost:4173/**',async route=>{
  const req=route.request(),url=new URL(req.url());
  if(url.pathname==='/FeeOperations/Billing')return route.fulfill({status:200,contentType:'text/html; charset=utf-8',body:html(lang)});
  if(url.pathname==='/api/finance/fees/options' && req.method()==='GET')
   return route.fulfill({contentType:'application/json',body:JSON.stringify({success:true,data:{
    academicBatches:[{id:7,name:'Synthetic Batch',campusId:1,academicYearId:2,academicLevelId:3,academicProgramId:null}],
    feeStructures:[{id:8,name:'Synthetic Fees',campusId:1,academicYearId:2,academicLevelId:3,academicProgramId:null,academicBatchId:7,effectiveFrom:'2020-01-01',effectiveTo:null}]
   }})});
  if(url.pathname==='/api/finance/fees/invoices/preflight' && req.method()==='POST'){
   writes.push({method:req.method(),url:url.pathname,csrf:req.headers()['requestverificationtoken']});
   return route.fulfill({contentType:'application/json',body:JSON.stringify({success:true,data:{
    eligibleCount:1,newCount:1,existingCount:0,conflictCount:0,newTotal:125,canGenerate:true,
    rows:[{studentCode:'=HYPERLINK("evil")',studentName:'Synthetic Student',status:'New',
      invoiceNumber:'',subtotal:150,discount:25,total:125}]
   }})});
  }
  throw Error('Unexpected request '+req.method()+' '+url.pathname);
 });
 try {
  await page.goto('http://localhost:4173/FeeOperations/Billing');
  await page.addScriptTag({path:localeScript});
  await page.addScriptTag({path:financeScript});
  await page.locator('#feeBatch option[value="7"]').waitFor({state:'attached'});
  await page.locator('#feePreview').click();
  assert.match(await page.locator('#feeAlert').innerText(),lang==='bn-BD'?/ব্যাচ, ফি কাঠামো/:/Choose a batch/);
  await page.locator('#feeBatch').selectOption('7');
  await page.locator('#feeInvoiceStructure option[value="8"]').waitFor({state:'attached'});
  await page.locator('#feeInvoiceStructure').selectOption('8');
  const now=new Date(),year=now.getFullYear(),month=now.getMonth()+1;
  const due=year+'-'+String(month).padStart(2,'0')+'-'+String(new Date(year,month,0).getDate()).padStart(2,'0');
  await page.locator('#feeDueDate').fill(due);
  await page.locator('#feePreview').click();
  await page.waitForFunction(()=>document.getElementById('feePreviewCsv').disabled===false);
  assert.match(await page.locator('#feePreviewSummary').innerText(),lang==='bn-BD'?/যোগ্য: 1; নতুন: 1/:/Eligible: 1; new: 1/);
  assert.equal(writes.length,1);
  assert.equal(writes[0].csrf,'synthetic-csrf');
  const downloadPromise=page.waitForEvent('download');
  await page.locator('#feePreviewCsv').click();
  const download=await downloadPromise;
  const csv=fs.readFileSync(await download.path(),'utf8');
  assert(csv.includes(lang==='bn-BD'?'শিক্ষার্থী কোড':'Student code'));
  assert(csv.includes('"\'=HYPERLINK(""evil"")"'),'Formula-like student code must be neutralized');
  assert.deepEqual(errors,[]);
  console.log(lang+' fee billing Chromium: dynamic preview/validation, CSV heading, formula escaping and CSRF preflight PASS (synthetic API)');
 } finally {await context.close();}
}
(async()=>{const browser=await chromium.launch({headless:true,args:['--no-sandbox']});try{
 await verify(browser,'en-BD');await verify(browser,'bn-BD');
}finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
