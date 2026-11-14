'use strict';
const assert=require('node:assert/strict');
const {chromium}=require('playwright');
const base=process.env.EDUOS_DASHBOARD_BASE_URL||'http://127.0.0.1:5089';
const adminEmail=process.env.EDUOS_DASHBOARD_SUPERADMIN_EMAIL;
const adminPassword=process.env.EDUOS_DASHBOARD_SUPERADMIN_PASSWORD;
const tenantPassword=process.env.EDUOS_DASHBOARD_TENANT_PASSWORD;
if(!/^http:\/\/127\.0\.0\.1:\d+$/.test(base)||!adminEmail||!adminPassword||!tenantPassword)process.exit(2);
const passed=[],failed=[];
async function check(name,fn){try{await fn();passed.push(name);console.log('PASS:',name)}
catch(e){failed.push(name);console.error('FAIL:',name,e.stack||e)}}
async function login(context,email,password){
 const page=await context.newPage();
 try{
  const res=await page.goto(base+'/Account/Login',{waitUntil:'domcontentloaded'});
  assert.equal(res.status(),200);
  await page.locator('#userNameOrEmail').fill(email);
  await page.locator('#password').fill(password);
  await page.locator('#loginBtn').click();
  await page.waitForURL(url=>/^\/Dashboard(?:\/|$)/i.test(url.pathname),{timeout:15000});
 }finally{await page.close()}
}
async function request(page,route,method='GET',body){
 return page.evaluate(async({route,method,body})=>{
  const token=document.querySelector('meta[name="request-verification-token"]')?.content ||
     document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
  const headers={Accept:'application/json'};
  if(method!=='GET'){headers['Content-Type']='application/json';headers.RequestVerificationToken=token}
  const r=await fetch(route,{method,headers,cache:'no-store',credentials:'same-origin',
      ...(body===undefined?{}:{body:JSON.stringify(body)})});
  const raw=await r.text();let payload;
  try{payload=JSON.parse(raw)}catch{}
  return{status:r.status,payload,preview:raw.slice(0,160),cache:r.headers.get('cache-control')||''};
 },{route,method,body});
}
function stateCode(row){return typeof row.state==='number'?row.state:
 ({PendingVerification:1,Active:2,Suspended:3,Closed:4}[row.state]||Number(row.state))}
(async()=>{
 const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
 try{
  await check('anonymous cannot access platform institution lifecycle',async()=>{
   const c=await browser.newContext({serviceWorkers:'block'});
   try{
    const page=await c.newPage();
    await page.goto(base+'/PlatformTenants/Index',{waitUntil:'domcontentloaded'});
    assert.equal(await page.locator('#platformTenantWorkspace').count(),0);
    const api=await page.request.get(base+'/api/platform/tenants',{maxRedirects:0,failOnStatusCode:false});
    assert.notEqual(api.status(),200);
   }finally{await c.close()}
  });
  await check('SuperAdmin English desktop: directory, suspend, replay, stale version, history, reactivate',async()=>{
   const c=await browser.newContext({viewport:{width:1366,height:850},serviceWorkers:'block'});
   try{
    await login(c,adminEmail,adminPassword);
    await c.addCookies([{name:'.AspNetCore.Culture',value:'c=en-BD|uic=en-BD',url:base}]);
    const page=await c.newPage(),errors=[];page.on('pageerror',e=>errors.push(e.message));
    const res=await page.goto(base+'/PlatformTenants/Index',{waitUntil:'domcontentloaded'});
    assert.equal(res.status(),200);
    assert.equal(await page.locator('h1').count(),1,'single shell title');
    assert.equal(await page.locator('#platformTenantRefresh').count(),0);
    assert.ok((await page.evaluate(()=>document.documentElement.scrollWidth-innerWidth))<=5);
    const list=await request(page,'/api/platform/tenants?search=EDUOS-DEMO&page=1&pageSize=20');
    assert.equal(list.status,200,list.preview);
    assert.match(list.cache,/no-store/);
    assert.equal(list.payload.data.items.length,1);
    const initial=list.payload.data.items[0];
    assert.equal(initial.code,'EDUOS-DEMO');
    assert.equal(stateCode(initial),2);
    const systemSearch=await request(page,'/api/platform/tenants?search=EDUOS-SYSTEM&page=1&pageSize=20');
    assert.equal(systemSearch.status,200);
    const system=systemSearch.payload.data.items.find(x=>x.code==='EDUOS-SYSTEM');
    assert.ok(system);
    const attempt=await request(page,'/api/platform/tenants/'+system.id+'/state','POST',{
      targetState:3,reason:'Never suspend reserved platform institution',
      rowVersion:system.rowVersion,requestId:crypto.randomUUID()
    });
    assert.equal(attempt.status,403,'Host system tenant must never be suspended');
    await page.locator('#platformTenantSearch').fill('EDUOS-DEMO');
    await page.locator('#platformTenantFilter button[type="submit"]').click();
    const row=page.locator('#platformTenantRows tr').filter({hasText:'EduOS Demonstration Institute'});
    await row.getByRole('button',{name:'Suspend'}).click();
    assert.equal(await page.locator('#platformTenantDecision').isVisible(),true);
    await page.locator('#platformTenantReason').fill('QA suspension of disposable test institution only.');
    let body=null;
    await page.route('**/api/platform/tenants/*/state',async route=>{
      if(route.request().method()==='POST')body=route.request().postDataJSON();
      await route.continue();
    });
    const saved=page.waitForResponse(r=>/\/api\/platform\/tenants\/\d+\/state$/.test(r.url())&&r.request().method()==='POST');
    await page.locator('#platformTenantSubmit').click();
    assert.equal((await saved).status(),200);
    assert.ok(body?.requestId,'Mutation must have stable idempotency ID');
    const after=await request(page,'/api/platform/tenants?search=EDUOS-DEMO');
    assert.equal(stateCode(after.payload.data.items[0]),3);
    const replay=await request(page,'/api/platform/tenants/'+initial.id+'/state','POST',body);
    assert.equal(replay.status,200,'Exact duplicate request must be idempotent');
    const stale=await request(page,'/api/platform/tenants/'+initial.id+'/state','POST',{
      targetState:2,reason:'Stale version must be rejected safely',
      rowVersion:initial.rowVersion,requestId:crypto.randomUUID()
    });
    assert.equal(stale.status,409);
    await row.getByRole('button',{name:'History'}).click();
    await page.locator('#platformTenantHistoryRows tr').first().waitFor();
    assert.match(await page.locator('#platformTenantHistoryRows').innerText(),/QA suspension/);
    await page.locator('#platformTenantHistoryClose').click();
    await page.locator('#platformTenantRows tr').filter({hasText:'EduOS Demonstration Institute'})
      .getByRole('button',{name:'Reactivate'}).click();
    await page.locator('#platformTenantReason').fill('Restore disposable demo tenant after successful QA.');
    const restore=page.waitForResponse(r=>/\/api\/platform\/tenants\/\d+\/state$/.test(r.url())&&r.request().method()==='POST');
    await page.locator('#platformTenantSubmit').click();
    assert.equal((await restore).status(),200);
    const restored=await request(page,'/api/platform/tenants?search=EDUOS-DEMO');
    assert.equal(stateCode(restored.payload.data.items[0]),2);
    const audit=await request(page,'/api/platform/tenants/'+initial.id+'/history?page=1&pageSize=20');
    assert.equal(audit.status,200);
    assert.equal(audit.payload.data.totalCount,2,'Retry must not create a duplicate audit entry');
    assert.equal(errors.length,0,errors.join(' | '));
   }finally{await c.close()}
  });
  await check('SuperAdmin Bangla mobile with contextual manual',async()=>{
   const c=await browser.newContext({viewport:{width:390,height:844},serviceWorkers:'block'});
   try{
    await login(c,adminEmail,adminPassword);
    await c.addCookies([{name:'.AspNetCore.Culture',value:'c=bn-BD|uic=bn-BD',url:base}]);
    const page=await c.newPage(),r=await c.newPage();await r.close();
    const res=await page.goto(base+'/PlatformTenants/Index',{waitUntil:'domcontentloaded'});
    assert.equal(res.status(),200);
    assert.equal(await page.locator('#platformTenantWorkspace').getAttribute('data-bangla'),'true');
    assert.match(await page.locator('#platformTenantFilter').innerText(),/প্রতিষ্ঠান/);
    assert.ok((await page.evaluate(()=>document.documentElement.scrollWidth-innerWidth))<=5);
    await page.locator('#pageContextHelp').click();
    await page.locator('#pageHelpDrawer[open]').waitFor({timeout:7000});
    assert.match(await page.locator('#pageHelpContent').innerText(),/স্থগিত/);
    assert.equal(await page.locator('#pageAssistantOpen').count(),1);
   }finally{await c.close()}
  });
  for(const role of ['TenantAdmin','Teacher']){
   await check(role+' cannot access host institution directory or API',async()=>{
    const c=await browser.newContext({serviceWorkers:'block'});
    try{
     await login(c,'demo.'+role.toLowerCase()+'@eduos.example.invalid',tenantPassword);
     const page=await c.newPage();
     await page.goto(base+'/Dashboard/Index',{waitUntil:'domcontentloaded'});
     const api=await request(page,'/api/platform/tenants?page=1&pageSize=20');
     assert.equal(api.status,403);
     await page.goto(base+'/PlatformTenants/Index',{waitUntil:'domcontentloaded'});
     assert.equal(await page.locator('#platformTenantWorkspace').count(),0);
    }finally{await c.close()}
   });
  }
 }finally{await browser.close()}
 if(failed.length){console.error('Institution lifecycle QA failures: '+failed.join(' | '));process.exitCode=1}
 else console.log('Institution lifecycle real SQL/Chromium Final QA PASS: '+passed.length+' scenarios');
})().catch(e=>{console.error(e.stack||e);process.exitCode=1});
