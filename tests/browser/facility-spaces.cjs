'use strict';
// Chromium with actual Razor-derived markup and production JS; synthetic API/session, NOT authenticated SQL/UAT.
const assert=require('node:assert/strict'), fs=require('node:fs'), path=require('node:path');
const {chromium}=require('playwright');
const root=path.resolve(__dirname,'../..'), read=p=>fs.readFileSync(path.join(root,p),'utf8');
const view=read('EduOS.App/Views/FacilitySpaces/Index.cshtml');
const script=path.join(root,'EduOS.App/wwwroot/js/facilities/spaces.js');
const bootstrap=path.join(root,'EduOS.App/wwwroot/lib/bootstrap/dist/css/bootstrap.min.css');
const initial={id:5,campusId:12,code:'LAB-A',name:'Lab A',address:'North wing',isActive:true,rowVersion:'AQIDBAUGBwg='};
const ok=data=>({success:true,data}), bad=message=>({success:false,data:null,message});
function html(bn,manage){
 const a=view.indexOf('<section class="container-fluid py-3" id="facilitySpaces"'),b=view.indexOf('@section Scripts',a);
 assert(a>=0&&b>a,'FacilitySpaces view contract changed');
 let h=view.slice(a,b);
 h=h.replace(/@\(\s*bn \? "([^"]*)" : "([^"]*)"\s*\)/g,(_,bd,en)=>bn?bd:en);
 h=h.replace(/@if\(manage\)\{(<th>[\s\S]*?<\/th>)\}/g,(_,s)=>manage?s:'');
 h=h.replace(/@if\(manage\)\s*\{([\s\S]*?)\n    \}/g,(_,s)=>manage?s:'');
 h=h.replace('@bn.ToString().ToLowerInvariant()',String(bn)).replace('@manage.ToString().ToLowerInvariant()',String(manage));
 assert(!/@if\(manage\)|@\(\s*bn \?/.test(h),'Unprocessed Razor branch');
 return '<!doctype html><html lang="'+(bn?'bn-BD':'en-BD')+'"><head><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="request-verification-token" content="facility-csrf"></head><body data-auth-session-reference="'+ 'A'.repeat(64)+'" data-auth-tenant-id="12"><div id="eduosSessionShield" hidden></div>'+h+'</body></html>';
}
const deferred=()=>{let resolve;const promise=new Promise(r=>resolve=r);return {promise,resolve};};
async function setup(browser,bn=false,manage=true,mobile=false){
 const context=await browser.newContext({viewport:mobile?{width:390,height:844}:{width:1280,height:800}});
 const page=await context.newPage(),state={item:{...initial},writes:[],errors:[],conflict:false,slow:null,started:null};
 page.on('pageerror',e=>state.errors.push(e.message));page.on('dialog',d=>d.accept());
 await page.addInitScript(()=>{window.EduOSSessionTransitionPending=()=>false;window.EduOSVerifySessionContext=async()=>{document.getElementById('eduosSessionShield').hidden=document.body.dataset.authTenantId==='12';};});
 await page.route('http://eduos.test/**',async route=>{
  const req=route.request(),url=new URL(req.url());
  if(!url.pathname.startsWith('/api/'))return route.fulfill({contentType:'text/html',body:html(bn,manage)});
  assert(url.pathname.startsWith('/api/facilities/spaces'),'Unexpected API '+url.pathname);
  const respond=(status,payload)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(payload)});
  if(req.method()==='GET'){
   assert.equal(url.searchParams.get('pageSize'),'25');
   const search=url.searchParams.get('search')||'';
   if(search==='slow'){state.started.resolve();await state.slow.promise;try{return await respond(200,ok({items:[{...state.item,code:'STALE'}],totalPages:1}));}catch(e){if(!/abort|cancel|closed|intercept|target/i.test(String(e)))throw e;return;}}
   if(search==='forbidden')return respond(403,bad('Facilities access denied.'));
   const items=search==='empty'?[]:search==='fast'?[{...state.item,code:'FAST'}]:[state.item];
   return respond(200,ok({items,totalPages:1}));
  }
  state.writes.push({method:req.method(),csrf:req.headers().requestverificationtoken,body:req.postDataJSON()});
  assert.equal(req.headers().requestverificationtoken,'facility-csrf');
  if(!manage)return respond(403,bad('Facilities access denied.'));
  if(state.conflict){state.conflict=false;return respond(409,bad('Space changed; reload.'));}
  state.item={...state.item,...req.postDataJSON(),rowVersion:'AgMEBQYHCAk='};
  return respond(200,ok(state.item));
 });
 await page.goto('http://eduos.test/FacilitySpaces/Index');
 await page.addStyleTag({path:bootstrap});await page.addScriptTag({path:script});
 await page.locator('#facilityPage').filter({hasText:'1 / 1'}).waitFor();
 return {context,page,state};
}
async function manager(browser){
 const {context,page,state}=await setup(browser);
 try{
  await page.getByRole('button',{name:'Edit space LAB-A'}).click();
  assert.equal(await page.locator('#facilityName').evaluate(e=>document.activeElement===e),true);
  await page.locator('#facilityName').fill('Lab A Updated');state.conflict=true;
  await page.locator('#facilityForm button[type="submit"]').click();
  await page.locator('#facilityAlert').filter({hasText:'Space changed'}).waitFor();
  assert.equal(await page.locator('#facilityVersion').inputValue(),initial.rowVersion);
  await page.locator('#facilityForm button[type="submit"]').click();
  await page.locator('#facilityAlert').filter({hasText:'Space saved.'}).waitFor();
  assert.equal(state.writes.length,2);
  assert(state.writes.every(x=>x.method==='PUT'&&x.csrf==='facility-csrf'));
  assert.equal(state.writes[1].body.rowVersion,initial.rowVersion);
  await page.reload();await page.addScriptTag({path:script});
  await page.locator('#facilityRows').filter({hasText:'Lab A Updated'}).waitFor();
  assert.deepEqual(state.errors,[]);
 }finally{await context.close();}
}
async function sessionAndRace(browser){
 const {context,page,state}=await setup(browser);
 try{
  state.slow=deferred();state.started=deferred();
  await page.locator('#facilitySearch').fill('slow');await page.locator('#facilitySearchForm button').click();
  await state.started.promise;
  await page.locator('#facilitySearch').fill('fast');await page.locator('#facilitySearchForm button').click();
  await page.locator('#facilityRows').filter({hasText:'FAST'}).waitFor();
  state.slow.resolve();await page.waitForTimeout(100);
  assert.doesNotMatch(await page.locator('#facilityRows').innerText(),/STALE/);
  await page.evaluate(()=>{document.body.dataset.authTenantId='999';});
  await page.locator('#facilitySearchForm button').click();
  await page.locator('#facilityAlert').filter({hasText:'Institution session changed'}).waitFor();
  assert.equal(await page.locator('#facilityRows tr').count(),0);
  assert.equal(await page.locator('#facilityForm button[type="submit"]').isDisabled(),true);
  assert.equal(state.writes.length,0);assert.deepEqual(state.errors,[]);
 }finally{state.slow?.resolve();await context.close();}
}
async function mobileViewer(browser){
 const {context,page,state}=await setup(browser,true,false,true);
 try{
  assert.match(await page.locator('h2').innerText(),/ক্যাম্পাস/);
  assert.equal(await page.locator('#facilityForm').count(),0);
  assert.equal(await page.locator('#facilityRows button').count(),0);
  assert.match(await page.locator('#facilityRows').innerText(),/সক্রিয়/);
  await page.locator('#facilitySearch').fill('forbidden');await page.locator('#facilitySearchForm button').click();
  await page.locator('#facilityAlert').filter({hasText:'অনুমতি নেই'}).waitFor();
  assert.doesNotMatch(await page.locator('#facilityRows').innerText(),/LAB-A/);
  await page.locator('#facilitySearch').fill('empty');await page.locator('#facilitySearchForm button').click();
  await page.locator('#facilityRows').filter({hasText:'কোনো স্থান পাওয়া যায়নি'}).waitFor();
  assert.equal(state.writes.length,0);assert.deepEqual(state.errors,[]);
 }finally{await context.close();}
}
(async()=>{const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
 try{await manager(browser);await sessionAndRace(browser);await mobileViewer(browser);
 console.log('Facility Spaces Chromium PASS: Razor-derived bilingual desktop/mobile, CSRF, 409 retry, reload, focus, stale response, tenant shield, read-only UI. Synthetic API/session only.');}
 finally{await browser.close();}})().catch(e=>{console.error(e);process.exitCode=1;});
