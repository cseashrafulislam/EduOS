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
  await check('SuperAdmin catalog metadata edit, retry, conflict, activation and restoration',async()=>{
   const c=await browser.newContext({viewport:{width:1366,height:850},serviceWorkers:'block'});
   try{
    await login(c,adminEmail,adminPassword);
    await c.addCookies([{name:'.AspNetCore.Culture',value:'c=en-BD|uic=en-BD',url:base}]);
    const page=await c.newPage(), errors=[];page.on('pageerror',e=>errors.push(e.message));
    page.on('dialog',d=>d.accept());
    const res=await page.goto(base+'/PlatformCatalogAdministration/Index',{waitUntil:'domcontentloaded'});
    assert.equal(res.status(),200);
    assert.equal(await page.locator('h1').count(),1,'only shared shell page title');
    assert.equal(await page.locator('#catalogRefresh').count(),0);
    assert.equal(await page.locator('#catalogRetry').isHidden(),true);
    const response=await request(page,'/api/platform/catalog/institution-types');
    assert.equal(response.status,200,response.preview);
    assert.match(response.cache,/no-store/);
    const types=response.payload.data;
    assert.ok(Array.isArray(types)&&types.length>0,'seeded global catalog required');
    const item=types.find(x=>x.isActive&&x.rowVersion);
    assert.ok(item,'one active institution type with optimistic version required');
    const metadataRoute='/api/platform/catalog/institution-types/'+item.id+'/metadata';
    const activationRoute='/api/platform/catalog/institution-types/'+item.id+'/activation';
    const bad=await request(page,metadataRoute,'POST',{
      requestId:crypto.randomUUID(),rowVersion:item.rowVersion,name:'',
      nameBangla:'',description:'',academicCycleType:item.academicCycleType,displayOrder:item.displayOrder
    });
    assert.equal(bad.status,400,'empty name cannot change authoritative catalog');
    const stale=await request(page,metadataRoute,'POST',{
      requestId:crypto.randomUUID(),rowVersion:'AQ==',name:item.name,
      nameBangla:item.nameBangla||'',description:item.description||'',
      academicCycleType:item.academicCycleType,displayOrder:item.displayOrder
    });
    assert.equal(stale.status,409,'stale metadata version must not be applied');
    const row=page.locator('#catalogRows tr').filter({hasText:item.code});
    await row.getByRole('button',{name:'Edit'}).click();
    assert.equal(await page.locator('#catalogEditor').isVisible(),true);
    assert.equal((await page.locator('#catalogEditCode').innerText()).trim(),item.code);
    assert.equal(await page.locator('input[id*="Code"]').count(),0,'system code cannot be editable');
    const oldDescription=item.description||'';
    const qaDescription='QA temporary catalog metadata, disposable test only.';
    await page.locator('#catalogEditDescription').fill(qaDescription);
    let recordedMetadata=null;
    await page.route('**/api/platform/catalog/institution-types/*/metadata',async route=>{
      if(route.request().method()==='POST')recordedMetadata=route.request().postDataJSON();
      await route.continue();
    });
    const saveWait=page.waitForResponse(r=>r.url().endsWith('/metadata')&&r.request().method()==='POST');
    await page.locator('#catalogSave').click();
    assert.equal((await saveWait).status(),200,'metadata edit must commit');
    assert.ok(recordedMetadata?.requestId);
    const duplicate=await request(page,metadataRoute,'POST',recordedMetadata);
    assert.equal(duplicate.status,200,'unchanged metadata replay must be idempotent');
    const saved=(await request(page,'/api/platform/catalog/institution-types')).payload.data.find(x=>x.id===item.id);
    assert.equal(saved.description,qaDescription);
    const restore=await request(page,metadataRoute,'POST',{
      requestId:crypto.randomUUID(),rowVersion:saved.rowVersion,name:item.name,
      nameBangla:item.nameBangla||'',description:oldDescription,
      academicCycleType:item.academicCycleType,displayOrder:item.displayOrder
    });
    assert.equal(restore.status,200,'original metadata restored in disposable DB');
    const next=(await request(page,'/api/platform/catalog/institution-types')).payload.data.find(x=>x.id===item.id);
    assert.equal(next.description||'',oldDescription);
    await page.locator('#catalogRows tr').filter({hasText:item.code})
       .getByRole('button',{name:'Hide from signup'}).click();
    const hiddenWait=page.waitForResponse(r=>r.url().endsWith('/activation')&&r.request().method()==='POST');
    // The click above triggers the request; waitForResponse is intentionally
    // followed by the next response check only if still pending.
    const hidden=await request(page,'/api/platform/catalog/institution-types');
    const afterHide=hidden.payload.data.find(x=>x.id===item.id);
    assert.equal(afterHide.isActive,false,'signup type must be hidden');
    const reactivate=await request(page,activationRoute,'POST',{
       requestId:crypto.randomUUID(),rowVersion:afterHide.rowVersion,isActive:true
    });
    assert.equal(reactivate.status,200,'catalog restored to originally active');
    const current=(await request(page,'/api/platform/catalog/institution-types')).payload.data.find(x=>x.id===item.id);
    assert.equal(current.isActive,true);
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
     const catalog=await request(page,'/api/platform/catalog/institution-types');
     assert.equal(catalog.status,403,'Tenant role must not list platform catalog');
     await page.goto(base+'/PlatformCatalogAdministration/Index',{waitUntil:'domcontentloaded'});
     assert.equal(await page.locator('#institutionTypeAdministration').count(),0);
     await page.goto(base+'/PlatformTenants/Index',{waitUntil:'domcontentloaded'});
     assert.equal(await page.locator('#platformTenantWorkspace').count(),0);
    }finally{await c.close()}
   });
  }
 }finally{await browser.close()}
 if(failed.length){console.error('Institution lifecycle QA failures: '+failed.join(' | '));process.exitCode=1}
 else console.log('Institution lifecycle real SQL/Chromium Final QA PASS: '+passed.length+' scenarios');
})().catch(e=>{console.error(e.stack||e);process.exitCode=1});
