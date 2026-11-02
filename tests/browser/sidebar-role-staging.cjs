'use strict';
// Opt-in read-only authenticated disposable STAGING browser acceptance; never real UAT.
const assert=require('node:assert/strict');
const {chromium}=require('playwright');
const {isAllowedRequest, assertNoStore, assertDeniedHtml}=require('./sidebar-staging-guards.cjs');
const base=(process.env.EDUOS_BASE_URL||'').trim().replace(/\/$/,'');
const host=(process.env.EDUOS_E2E_STAGING_HOST||'').trim().toLowerCase();
const users=[
  {role:'admin',email:process.env.EDUOS_E2E_ADMIN_USER,password:process.env.EDUOS_E2E_ADMIN_PASSWORD,tenant:process.env.EDUOS_E2E_ADMIN_TENANT_ID},
  {role:'accountant',email:process.env.EDUOS_E2E_ACCOUNTANT_USER,password:process.env.EDUOS_E2E_ACCOUNTANT_PASSWORD,tenant:process.env.EDUOS_E2E_ACCOUNTANT_TENANT_ID},
  {role:'viewer',email:process.env.EDUOS_E2E_VIEWER_USER,password:process.env.EDUOS_E2E_VIEWER_PASSWORD,tenant:process.env.EDUOS_E2E_VIEWER_TENANT_ID}
];
let origin;
try{
  const u=new URL(base);
  assert(host && u.hostname.toLowerCase()===host,'Explicit staging hostname mismatch');
  assert(u.protocol==='https:'||['localhost','127.0.0.1'].includes(u.hostname),'HTTPS required');
  assert(!u.username&&!u.password&&!u.search&&!u.hash&&['','/'].includes(u.pathname),'Unsafe staging URL');
  assert(users.every(x=>x.email&&x.password&&/^[1-9]\d*$/.test(x.tenant||'')),'Three synthetic accounts required');
  assert.equal(users[0].tenant,users[1].tenant,'Admin and Accountant must share synthetic tenant');
  assert.notEqual(users[0].tenant,users[2].tenant,'Viewer must use foreign synthetic tenant');
  origin=u.origin;
}catch(e){console.error('Staging preflight rejected: '+e.message);process.exit(2);}
const routes={users:'/TenantUsers/Index',reports:'/AccountingReports/Index',aging:'/FeeReceivablesAging/Index'};
const grants={admin:['users','reports','aging'],accountant:['aging'],viewer:[]};
async function login(page,user){
  const response=await page.goto(base+'/Account/Login',{waitUntil:'domcontentloaded'});
  assert(response&&response.status()===200,'Login page unavailable');
  await page.locator('#userNameOrEmail').fill(user.email);
  await page.locator('#password').fill(user.password);
  await page.locator('#loginBtn').click();
  await page.waitForFunction(()=>location.pathname.toLowerCase()!=='/account/login'||
    !!document.querySelector('#institutionChoicePanel:not([hidden])')||
    !!document.querySelector('#alertContainer.alert-danger'),null,{timeout:20000});
  if(await page.locator('#institutionChoicePanel').isVisible()){
    const choices=await page.locator('#institutionChoice option').evaluateAll(nodes=>nodes.map(n=>n.value));
    assert(choices.includes(user.tenant),'Synthetic tenant not offered');
    await page.locator('#institutionChoice').selectOption(user.tenant);
    await page.locator('#loginBtn').click();
    await page.waitForFunction(()=>location.pathname.toLowerCase()!=='/account/login'||
      !!document.querySelector('#alertContainer.alert-danger'),null,{timeout:20000});
  }
  assert.equal(await page.locator('#alertContainer.alert-danger').isVisible(),false,'Login rejected');
  assert(!page.url().includes('MfaChallenge'),'No MFA bypass allowed');
  const home=await page.goto(base+'/workspace',{waitUntil:'domcontentloaded'});
  assert(home&&home.status()===200&&new URL(page.url()).pathname.toLowerCase()==='/workspace','Workspace unavailable');
  assert.equal(await page.locator('body').getAttribute('data-auth-tenant-id'),user.tenant,'Tenant mismatch');
  const session=await page.locator('body').getAttribute('data-auth-session-reference');
  assert(/^[0-9a-f]{64}$/i.test(session||''),'Session reference missing');
  return session;
}
async function verify(browser,user,mobile){
  const context=await browser.newContext({viewport:mobile?{width:390,height:844}:{width:1280,height:800},
    serviceWorkers:'block',ignoreHTTPSErrors:false,acceptDownloads:false});
  const page=await context.newPage(),blocked=[];
  await context.route('**/*',route=>{
    const request=route.request();
    if(isAllowedRequest(request.url(),request.method(),origin))return route.continue();
    let sameOrigin=false;
    try{sameOrigin=new URL(request.url()).origin===origin;}catch{}
    blocked.push(sameOrigin?'write':'off-origin');
    return route.abort('blockedbyclient');
  });
  try{
    // An unauthenticated context must not render any privileged page before login.
    for(const [name,path] of Object.entries(routes)){
      const response=await page.goto(base+path,{waitUntil:'domcontentloaded'});
      assert(response,'Anonymous route did not respond: '+name);
      const target=new URL(page.url());
      assert.equal(target.origin,origin,'Anonymous redirect escaped staging origin');
      assert([401,403,404].includes(response.status())||
        /^\/(?:account\/login|account\/accessdenied|error\/accessdenied)\/?$/i.test(target.pathname),
        'Anonymous route exposed: '+name);
      assertDeniedHtml(await response.text(),user.role==='viewer'?users[0].tenant:null);
      if(/^\/account\/login\/?$/i.test(target.pathname))assertNoStore(response.headers(),'Anonymous login');
    }
    const session=await login(page,user);
    assert.equal(await page.locator('meta[name="request-verification-token"]').count(),1,'CSRF shell metadata missing');
    const csrf=await page.locator('meta[name="request-verification-token"]').getAttribute('content');
    assert(csrf&&csrf.length>20,'CSRF request token empty');
    const links=await page.locator('#sidebarMenu a.nav-link').evaluateAll(nodes=>nodes.map(n=>n.getAttribute('href')));
    assert.equal(new Set(links.map(x=>x.toLowerCase())).size,links.length,'Duplicate authorized link');
    for(const [name,path] of Object.entries(routes)){
      const allowed=grants[user.role].includes(name);
      assert.equal(links.filter(x=>x.toLowerCase()===path.toLowerCase()).length,allowed?1:0,
        'Razor role navigation mismatch: '+user.role+'/'+name);
      const response=await page.goto(base+path,{waitUntil:'domcontentloaded'});
      assert(response,'No response for '+name);
      const target=new URL(page.url());
      assert.equal(target.origin,origin,'Unexpected navigation origin');
      if(allowed){
        assert.equal(response.status(),200,'Expected authorized '+name);
        assert.equal(target.pathname.toLowerCase(),path.toLowerCase(),'Unexpected authorized redirect');
        assert.equal(await page.locator('body').getAttribute('data-auth-tenant-id'),user.tenant);
        assert.equal(await page.locator('body').getAttribute('data-auth-session-reference'),session,
          'Session changed during authorized navigation');
        assertNoStore(response.headers(),'Authorized '+name);
      }else{
        assert([403,404].includes(response.status())||
          /^\/(?:account|error)\/accessdenied\/?$/i.test(target.pathname),
          'Unauthorized page exposed or redirected: '+name);
        assert.equal(await page.locator('#feeAgingApp').count(),0,'Unauthorized finance DOM exposed');
        assertDeniedHtml(await response.text(),user.role==='viewer'?users[0].tenant:null);
      }
    }
    await page.goto(base+'/workspace',{waitUntil:'domcontentloaded'});
    assert.equal(await page.locator('body').getAttribute('data-auth-session-reference'),session,'Session drift');
    if(mobile)assert((await page.evaluate(()=>document.documentElement.scrollWidth-innerWidth))<=2,
      'Mobile shell horizontal overflow');
    assert.deepEqual(blocked,[],'Off-origin or unexpected write blocked');
    return session;
  }finally{await context.close();}
}
(async()=>{
  const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
  try{
    const sessions=[];
    for(let i=0;i<users.length;i++)sessions.push(await verify(browser,users[i],i===1));
    assert.equal(new Set(sessions).size,3,'Separate authenticated sessions required');
    console.log('Authenticated disposable staging sidebar PASS: anonymous denial, three synthetic roles, scoped pages, no-store, denied-body privacy, CSRF and mobile.');
  }finally{await browser.close();}
})().catch(e=>{console.error(e.message);process.exitCode=1;});
