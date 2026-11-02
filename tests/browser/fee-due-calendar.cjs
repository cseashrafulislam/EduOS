'use strict';
// Actual production due-calendar.js in Chromium, synthetic read-only GET responses.
// Not an authenticated SQL-backed tenant acceptance test.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const {chromium}=require('playwright');
const root=path.resolve(__dirname,'../..');
const script=path.join(root,'EduOS.App/wwwroot/js/finance/fee-due-calendar.js');
const view=fs.readFileSync(path.join(root,'EduOS.App/Views/FeeDueCalendar/Index.cshtml'),'utf8');
assert(view.includes('CultureInfo.CurrentUICulture.Name == "bn-BD"'));
function html(lang){
 const inputs=['feeDueFrom','feeDueTo','feeDueSearch'];
 const buttons=['feeDueRun','feeDuePrev','feeDueNext','feeDueCsv','feeDuePrint'];
 return '<!doctype html><html lang="'+lang+'"><body><section id="feeDueCalendarApp">'+
 '<form id="feeDueFilters">'+inputs.map(id=>'<input id="'+id+'">').join('')+
 '<select id="feeDueCampus"><option value="">All accessible</option><option value="5">Demo Campus</option></select>'+
 '<select id="feeDueSize"><option value="25">25</option></select>'+
 '<button id="feeDueRun" type="submit">Run</button></form>'+
 '<div id="feeDueNotice"></div><p id="feeDueScope"></p><p id="feeDueCount"></p><p id="feeDuePage"></p>'+
 buttons.filter(id=>id!=='feeDueRun').map(id=>'<button id="'+id+'" disabled>'+id+'</button>').join('')+
 '<table><tbody id="feeDueMonths"></tbody></table><table><tbody id="feeDueRows"></tbody></table></section></body></html>';
}
function payload(url){
 const fromDate=url.searchParams.get('fromDate'),toDate=url.searchParams.get('toDate');
 return {success:true,data:{fromDate,toDate,campuses:[{id:5,name:'Demo Campus'}],
 months:[{year:Number(fromDate.slice(0,4)),month:Number(fromDate.slice(5,7)),currencyCode:'BDT',invoiceCount:1,outstanding:'10.50'}],
 invoices:{page:Number(url.searchParams.get('page')),pageSize:25,totalCount:1,items:[{
 dueDate:fromDate,invoiceNumber:'=HYPERLINK("evil")',studentCode:'A-1',studentName:'Synthetic Student',
 campusName:'Demo Campus',currencyCode:'BDT',totalAmount:'20.50',paidAmount:'10.00',dueAmount:'10.50'
 }]}}};
}
async function waitFor(check){
 for(let n=0;n<100;n++){if(check())return;await new Promise(r=>setTimeout(r,20));}
 throw Error('Synthetic request timeout');
}
async function verify(browser,lang){
 const context=await browser.newContext({acceptDownloads:true,viewport:{width:lang==='bn-BD'?390:1280,height:800}});
 const page=await context.newPage(),errors=[],writes=[];
 let releaseFirst=null;
 page.on('pageerror',e=>errors.push(e.message));
 await page.route('http://localhost:4173/**',async route=>{
  const req=route.request(),url=new URL(req.url());
  if(url.pathname==='/FeeDueCalendar/Index')
   return route.fulfill({status:200,contentType:'text/html; charset=utf-8',body:html(lang)});
  if(req.method()!=='GET'){writes.push(url.pathname);throw Error('Unexpected write '+url.pathname);}
  if(url.pathname!=='/api/finance/receivables-aging/due-calendar')throw Error('Unexpected GET '+url.pathname);
  if(!url.searchParams.has('search')&&!url.searchParams.has('campusId')){
   await new Promise(resolve=>{releaseFirst=resolve;});
   return route.fulfill({contentType:'application/json',body:JSON.stringify(payload(url))}).catch(()=>{});
  }
  if(url.searchParams.has('campusId'))return route.fulfill({status:403,contentType:'application/json',body:'{"success":false,"message":"private"}'});
  return route.fulfill({contentType:'application/json',body:JSON.stringify(payload(url))});
 });
 try{
  await page.goto('http://localhost:4173/FeeDueCalendar/Index');
  await page.addScriptTag({path:script});
  await page.locator('#feeDueRun').click();
  await waitFor(()=>!!releaseFirst);
  await page.locator('#feeDueSearch').fill('INV');
  assert.equal(await page.locator('#feeDueCsv').isDisabled(),true);
  releaseFirst();
  await page.waitForTimeout(80);
  assert.doesNotMatch(await page.locator('#feeDueRows').innerText(),/HYPERLINK/);
  await page.locator('#feeDueRun').click();
  await page.waitForFunction(()=>document.querySelector('#feeDueCsv').disabled===false);
  assert.match(await page.locator('#feeDueRows').innerText(),/10.50/);
  const downloadPromise=page.waitForEvent('download');
  await page.locator('#feeDueCsv').click();
  const download=await downloadPromise,csv=fs.readFileSync(await download.path(),'utf8');
  assert(csv.includes(lang==='bn-BD'?'শেষ তারিখ':'Due date'));
  assert(csv.includes('"'+"'"+'=HYPERLINK(""evil"")"'),'Formula injection must be neutralized');
  await page.locator('#feeDueCampus').selectOption('5');
  assert.equal(await page.locator('#feeDueCsv').isDisabled(),true);
  assert.doesNotMatch(await page.locator('#feeDueRows').innerText(),/HYPERLINK/);
  await page.locator('#feeDueRun').click();
  await page.waitForFunction(()=>document.querySelector('#feeDueNotice').classList.contains('alert-danger'));
  assert.equal(await page.locator('#feeDuePrint').isDisabled(),true);
  assert.doesNotMatch(await page.locator('#feeDueNotice').innerText(),/private/);
  assert.match(await page.locator('#feeDueNotice').innerText(),lang==='bn-BD'?/অনুমতি নেই/:/access denied/);
  assert.deepEqual(writes,[]);
  assert.deepEqual(errors,[]);
  console.log(lang+' due-calendar: stale read, export, denied campus, bilingual Chromium PASS (synthetic GET)');
 }finally{await context.close();}
}
(async()=>{const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
 try{await verify(browser,'en-BD');await verify(browser,'bn-BD');}finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});