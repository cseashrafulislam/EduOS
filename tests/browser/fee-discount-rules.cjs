'use strict';
// Actual Chromium and production discount-rules.js, synthetic GET/PUT/POST only.
// NOT authenticated SQL-backed multi-tenant or real institute UAT.
const assert=require('node:assert/strict'),path=require('node:path');
const {chromium}=require('playwright');
const source=path.resolve(__dirname,'../../EduOS.App/wwwroot/js/finance/discount-rules.js');
const ref='AQIDBA==';
function fixture(lang){
 return '<!doctype html><html lang="'+lang+'"><head><meta name="request-verification-token" content="synthetic-csrf"></head>'+
 '<body data-auth-tenant-id="41" data-auth-session-reference="s1"><section id="discountRulesApp">'+
 '<p id="discountRuleAlert" tabindex="-1"></p><h2 id="discountRuleFormTitle"></h2>'+
 '<form id="discountRuleForm"><input id="discountRuleName" required maxlength="150">'+
 '<select id="discountRuleHead"></select><select id="discountRuleType"><option value="percentage">%</option><option value="fixed">BDT</option></select>'+
 '<input id="discountRuleValue" type="number" min="0.0001" max="100" step="0.0001" required>'+
 '<input id="discountRuleFrom" type="date"><input id="discountRuleTo" type="date">'+
 '<input id="discountRuleActive" type="checkbox" checked><button id="discountRuleSave" type="submit"></button>'+
 '<button id="discountRuleReset" type="button"></button></form>'+
 '<button id="discountRuleRefresh" type="button"></button><button id="discountRulePrev" type="button"></button>'+
 '<button id="discountRuleNext" type="button"></button><span id="discountRulePage"></span>'+
 '<table><tbody id="discountRuleRows"></tbody></table></section></body></html>';
}
function rule(name='Tuition'){
 return {id:11,name,feeHeadId:5,feeHeadName:'Tuition',isPercentage:true,value:12.5,
  effectiveFrom:'2026-11-01',effectiveTo:null,isActive:true,rowVersion:ref};
}
async function verify(browser,lang){
 const context=await browser.newContext({viewport:{width:lang==='bn-BD'?390:1280,height:800}});
 const page=await context.newPage(),errors=[],writes=[];
 let first=true,release=null,created=false;
 page.on('pageerror',e=>errors.push(e.message));
 await page.route('http://eduos.test/**',async route=>{
  const req=route.request(),url=new URL(req.url());
  if(url.pathname==='/FeeDiscountRules/Index')
   return route.fulfill({contentType:'text/html',body:fixture(lang)});
  if(url.pathname==='/api/finance/fees/options')
   return route.fulfill({contentType:'application/json',body:JSON.stringify({success:true,data:{feeHeads:[{id:5,name:'Tuition'}]}})});
  if(url.pathname==='/api/finance/fees/discount-rules'&&req.method()==='GET'){
   if(first){first=false;await new Promise(resolve=>{release=resolve;});}
   return route.fulfill({contentType:'application/json',body:JSON.stringify({success:true,data:{
    page:Number(url.searchParams.get('page')),pageSize:25,totalCount:1,items:[rule(created?'New tuition':'Tuition')]
   }})}).catch(()=>{});
  }
  if(url.pathname==='/api/finance/fees/discount-rules/11'&&req.method()==='PUT'){
   writes.push({method:'PUT',csrf:req.headers()['requestverificationtoken'],body:req.postDataJSON()});
   return route.fulfill({status:409,contentType:'application/json',body:'{"success":false,"message":"PRIVATE-CONFLICT-REFERENCE"}'});
  }
  if(url.pathname==='/api/finance/fees/discount-rules'&&req.method()==='POST'){
   writes.push({method:'POST',csrf:req.headers()['requestverificationtoken'],body:req.postDataJSON()});
   created=true;
   return route.fulfill({contentType:'application/json',body:JSON.stringify({success:true,data:rule('New tuition')})});
  }
  return route.abort();
 });
 try{
  await page.goto('http://eduos.test/FeeDiscountRules/Index');
  await page.evaluate(()=>{window.confirm=()=>true;});
  await page.addScriptTag({path:source});
  for(let i=0;i<100&&!release;i++)await page.waitForTimeout(20);
  assert(release,'first register GET intercepted');
  await page.locator('#discountRuleRefresh').click();
  await page.waitForFunction(()=>document.getElementById('discountRuleRows').innerText.includes('Tuition'));
  release();await page.waitForTimeout(80);
  assert.match(await page.locator('#discountRuleRows').innerText(),/Tuition/);
  await page.locator('#discountRuleRows button').click();
  assert.match(await page.locator('#discountRuleFormTitle').innerText(),lang==='bn-BD'?/সম্পাদনা/:/Edit/);
  await page.locator('#discountRuleValue').fill('120');
  await page.locator('#discountRuleForm').evaluate(form=>form.requestSubmit());
  assert.equal(writes.length,0,'invalid percentage cannot write');
  await page.locator('#discountRuleValue').fill('12.5');
  await page.locator('#discountRuleForm').evaluate(form=>form.requestSubmit());
  await page.waitForFunction(()=>document.getElementById('discountRuleAlert').classList.contains('alert-danger'));
  assert.equal(writes[0].method,'PUT');
  assert.equal(writes[0].csrf,'synthetic-csrf');
  assert.equal(writes[0].body.rowVersion,ref);
  assert.doesNotMatch(await page.locator('#discountRuleAlert').innerText(),/PRIVATE-CONFLICT-REFERENCE/);
  await page.locator('#discountRuleReset').click();
  await page.locator('#discountRuleName').fill('New tuition');
  await page.locator('#discountRuleValue').fill('10.125');
  await page.locator('#discountRuleForm').evaluate(form=>form.requestSubmit());
  await page.waitForFunction(()=>document.getElementById('discountRuleRows').innerText.includes('New tuition'));
  assert.equal(writes[1].method,'POST');assert.equal(writes[1].csrf,'synthetic-csrf');
  assert.equal(writes[1].body.rowVersion,null);
  assert.equal(writes[1].body.value,10.125);
  await page.evaluate(()=>{document.body.dataset.authTenantId='42';});
  await page.waitForFunction(()=>document.getElementById('discountRuleSave').disabled);
  assert.doesNotMatch(await page.locator('#discountRuleRows').innerText(),/New tuition/);
  assert.equal(await page.locator('#discountRuleRefresh').isDisabled(),true);
  assert.deepEqual(errors,[]);
  console.log(lang+' discount rules: bilingual, stale GET, CSRF, rowversion, conflict redaction, session PASS (synthetic API)');
 }finally{await context.close();}
}
(async()=>{const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
 try{for(const lang of ['en-BD','bn-BD'])await verify(browser,lang);}
 finally{await browser.close();}
})().catch(err=>{console.error(err);process.exitCode=1;});
