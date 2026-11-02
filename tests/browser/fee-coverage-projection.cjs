'use strict';
// Real production finance scripts in Chromium with synthetic GET responses only.
// NOT authenticated SQL-backed tenant/role/CSRF acceptance.
const assert = require('node:assert/strict'), fs = require('node:fs'), path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname,'../..');
const scripts = {
 coverage: path.join(root,'EduOS.App/wwwroot/js/finance/fee-invoice-coverage.js'),
 projection: path.join(root,'EduOS.App/wwwroot/js/finance/fee-installment-projection.js')
};
function fixture(kind,lang) {
 const coverage = kind === 'coverage';
 const fields = coverage ?
  '<select id="feeCoverageBatch"></select><input id="feeCoverageMonth" type="month">'+
  '<select id="feeCoverageStatus"><option value="all">All</option><option value="review">Review</option></select>'+
  '<input id="feeCoverageSearch"><select id="feeCoverageSize"><option>25</option></select>' :
  '<select id="feeProjectionBatch"></select><select id="feeProjectionStructure"></select>'+
  '<input id="feeProjectionStart" type="month"><select id="feeProjectionMonths"><option>3</option><option>6</option></select>';
 const prefix = coverage ? 'feeCoverage' : 'feeProjection';
 const extras = coverage ?
  ['Eligible','Billed','Unbilled','Review','Count','Page'].map(x=>'<p id="feeCoverage'+x+'"></p>').join('') :
  '<p id="feeProjectionCohort"></p><p id="feeProjectionBasis"></p>';
 const pagination = coverage ? '<button id="feeCoveragePrev" disabled>Prev</button><button id="feeCoverageNext" disabled>Next</button>' : '';
 return '<!doctype html><html lang="'+lang+'"><body data-auth-tenant-id="41" data-auth-session-reference="s1">'+
  '<section id="'+prefix+'App"><form id="'+prefix+'Filters">'+fields+
  '<button id="'+prefix+'Run" type="submit">Run</button></form>'+
  '<p id="'+prefix+'Notice"></p><p id="'+prefix+'Scope"></p>'+extras+
  '<button id="'+prefix+'Csv" disabled>CSV</button><button id="'+prefix+'Print" disabled>Print</button>'+
  pagination+'<table><tbody id="'+prefix+'Rows"></tbody></table></section></body></html>';
}
const options = { success:true,data:{
 academicBatches:[{id:1,name:'Synthetic Batch',campusId:5,academicYearId:2026,academicLevelId:3,academicProgramId:2}],
 feeStructures:[{id:7,name:'Monthly Synthetic',campusId:5,academicYearId:2026,academicBatchId:1,
  academicLevelId:3,academicProgramId:2}]
}};
function result(kind,url) {
 if (kind === 'coverage') return {success:true,data:{
  academicBatchId:1,academicBatchName:'Synthetic Batch',campusName:'Campus Five',
  billingMonth:url.searchParams.get('billingMonth'),eligible:2,billed:1,unbilled:1,review:0,
  students:{page:Number(url.searchParams.get('page')),pageSize:25,totalCount:1,items:[{
   studentCode:'=HYPERLINK("evil")',studentName:'Synthetic Student',invoiceCount:0,
   integrityIssueCount:0,lineIssueCount:0,status:'Unbilled'
  }]}
 }};
 const start=url.searchParams.get('startMonth').slice(0,7);
 const [year,month]=start.split('-').map(Number);
 const months=Array.from({length:Number(url.searchParams.get('months'))},(_,i)=>{
  const d=new Date(Date.UTC(year,month-1+i,1));
  return {year:d.getUTCFullYear(),month:d.getUTCMonth()+1,
   earliestConfiguredDueDate:null,latestConfiguredDueDate:null,hasUnspecifiedDueDays:true,
   feeHeadCount:1,grossPerStudent:'100.25',grossForCurrentCohort:'200.50',
   observedInvoiceCount:1,observedInvoiceTotal:'50.10'};
 });
 return {success:true,data:{academicBatchId:1,feeStructureId:7,campusName:'Campus Five',
  academicBatchName:'Synthetic Batch',feeStructureName:'Monthly Synthetic',currencyCode:'BDT',
  currentEligibleEnrollments:2,months,basis:'English backend basis'}};
}
async function verify(browser,kind,lang) {
 const context=await browser.newContext({acceptDownloads:true,
  viewport:{width:lang==='bn-BD'?390:1280,height:800}});
 const page=await context.newPage(),errors=[],writes=[];
 let releaseOld=null,first=true;
 const prefix=kind==='coverage'?'feeCoverage':'feeProjection';
 const routeName=kind==='coverage'?'FeeInvoiceCoverage':'FeeInstallmentProjection';
 const apiName=kind==='coverage'?'invoice-coverage':'installment-projection';
 page.on('pageerror',e=>errors.push(e.message));
 await page.route('http://eduos.test/**',async route=>{
  const req=route.request(),url=new URL(req.url());
  if(url.pathname==='/'+routeName+'/Index')
   return route.fulfill({contentType:'text/html; charset=utf-8',body:fixture(kind,lang)});
  if(req.method()!=='GET'){writes.push(url.pathname);return route.abort();}
  if(url.pathname==='/api/finance/fees/options')
   return route.fulfill({contentType:'application/json',body:JSON.stringify(options)});
  if(url.pathname==='/api/finance/fees/'+apiName) {
   if(kind==='coverage' && url.searchParams.get('status')==='review' ||
      kind==='projection' && url.searchParams.get('startMonth')==='2027-01-01')
    return route.fulfill({status:403,contentType:'application/json',
     body:'{"success":false,"message":"PRIVATE-ROLE-DENIAL"}'});
   if(first) {
    first=false;
    await new Promise(resolve=>{releaseOld=resolve;});
    return route.fulfill({contentType:'application/json',body:JSON.stringify(result(kind,url))}).catch(()=>{});
   }
   return route.fulfill({contentType:'application/json',body:JSON.stringify(result(kind,url))});
  }
  return route.abort();
 });
 try {
  await page.goto('http://eduos.test/'+routeName+'/Index');
  await page.addScriptTag({path:scripts[kind]});
  await page.waitForFunction(prefix => document.getElementById(prefix+'Batch').options.length>1,prefix);
  await page.locator('#'+prefix+'Batch').selectOption('1');
  if(kind==='coverage'){
   await page.locator('#feeCoverageMonth').fill('2026-11');
  }else{
   await page.locator('#feeProjectionStructure').selectOption('7');
   await page.locator('#feeProjectionMonths').selectOption('3');
   await page.locator('#feeProjectionStart').fill('2026-11');
  }
  await page.locator('#'+prefix+'Run').click();
  for(let i=0;i<100&&!releaseOld;i++)await page.waitForTimeout(20);
  assert(releaseOld,'First GET must be intercepted');
  if(kind==='coverage') await page.locator('#feeCoverageSearch').fill('NEW');
  else await page.locator('#feeProjectionStart').fill('2026-12');
  assert.equal(await page.locator('#'+prefix+'Csv').isDisabled(),true);
  releaseOld();
  await page.waitForTimeout(70);
  assert.equal(await page.locator('#'+prefix+'Csv').isDisabled(),true);
  await page.locator('#'+prefix+'Run').click();
  await page.waitForFunction(prefix=>!document.getElementById(prefix+'Csv').disabled,prefix);
  assert.match(await page.locator('#'+prefix+'Rows').innerText(),
   kind==='coverage'?/Synthetic Student/:/200.50/);
  const downloaded=page.waitForEvent('download');
  await page.locator('#'+prefix+'Csv').click();
  const file=await downloaded,csv=fs.readFileSync(await file.path(),'utf8');
  assert(csv.charCodeAt(0)===0xFEFF);
  assert(csv.includes(lang==='bn-BD'?'শিক্ষার্থী কোড':'Student code') ||
   csv.includes(lang==='bn-BD'?'পর্যবেক্ষিত ইনভয়েস':'Observed invoices'));
  if(kind==='coverage') assert(csv.includes("'"+'=HYPERLINK(""evil"")'));
  if(kind==='coverage') await page.locator('#feeCoverageStatus').selectOption('review');
  else await page.locator('#feeProjectionStart').fill('2027-01');
  assert.equal(await page.locator('#'+prefix+'Csv').isDisabled(),true);
  await page.locator('#'+prefix+'Run').click();
  await page.waitForFunction(prefix=>document.getElementById(prefix+'Notice').classList.contains('alert-danger'),prefix);
  assert.doesNotMatch(await page.locator('#'+prefix+'Notice').innerText(),/PRIVATE-ROLE-DENIAL/);
  assert.equal(await page.locator('#'+prefix+'Csv').isDisabled(),true);
  assert.deepEqual(errors,[]);assert.deepEqual(writes,[]);
  console.log(lang+' '+kind+': stale filter, localized CSV, 403 privacy PASS (synthetic GET)');
 }finally{await context.close();}
}
(async()=>{
 const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
 try{for(const lang of ['en-BD','bn-BD'])for(const kind of ['coverage','projection'])
  await verify(browser,kind,lang);}
 finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
