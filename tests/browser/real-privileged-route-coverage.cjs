'use strict';
// Read-only authenticated real-browser acceptance for the 15 routes excluded
// from the ordinary 13 tenant-role sidebar inventory. Isolated SQL ONLY.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const {chromium}=require('playwright');
const origin='https://qa.eduos.test:7243';
const sha=process.env.GITHUB_SHA||'';
const tenantPassword=process.env.QA_DEMO_PASSWORD||'';
const platformPassword=process.env.QA_PLATFORM_PASSWORD||'';
assert.equal(process.env.EDUOS_QA_ORIGIN,origin);
assert(/^[0-9a-f]{40}$/i.test(sha),'Exact SHA required');
assert(tenantPassword.length>=16&&platformPassword.length>=24&&
    platformPassword!==tenantPassword,'Two distinct ephemeral QA secrets required');
const platformRoutes=[
    '/Dashboard/Admin','/PlatformTenants/Index',
    '/PlatformCatalogAdministration/Index','/PlatformFeatureAdministration/Index',
    '/PlatformProductModuleAdministration/Index','/PlatformInstitutionModulePresets/Index',
    '/PlatformModuleFeatures/Index','/SubscriptionPlanAdministration/Index',
    '/LegalDocuments/Index','/SubscriptionPaymentHistory/Index',
    '/Account/PaymentVerification'
];
const optionalRoutes=['/ResearchProposals/Index','/ResearchGrants/Index',
    '/Accreditation/Index','/Accreditation/Obe'];
const expected=[...platformRoutes,...optionalRoutes];
const normalize=s=>new URL(s,origin).pathname.toLowerCase();
const manifest=JSON.parse(fs.readFileSync('docs/ux/UX_ROUTE_OWNERSHIP_20261114.json','utf8'));
const canonical=new Set(manifest.lanes.flatMap(l=>l.routes.map(r=>normalize(r.path))));
assert.equal(canonical.size,204,'Canonical route manifest drifted');
assert.equal(new Set(expected.map(normalize)).size,15,'Privileged route inventory drifted');
assert(expected.every(p=>canonical.has(normalize(p))),'Unrecognized privileged route');

async function signIn(browser,role,pwd){
    const context=await browser.newContext({ignoreHTTPSErrors:true,serviceWorkers:'block',
        acceptDownloads:false,viewport:{width:1366,height:768}});
    const page=await context.newPage();
    const initial=await page.goto(origin+'/Account/Login',{waitUntil:'domcontentloaded'});
    assert(initial&&initial.status()===200,'Actual staging login page unavailable');
    await page.locator('#userNameOrEmail').fill(role);
    await page.locator('#password').fill(pwd);
    await page.locator('#loginBtn').click();
    await page.waitForURL(u=>u.pathname.toLowerCase()!=='/account/login',
        {waitUntil:'domcontentloaded',timeout:25000});
    return {context,page};
}
async function checkRoutes(browser,role,password,routes){
    const {context,page}=await signIn(browser,role,password);
    let active='', errors=[],mutations=[];
    await context.route('**/*',route=>{
        const request=route.request(),url=new URL(request.url());
        if(url.origin!==origin)return route.abort('blockedbyclient');
        if(!['GET','HEAD','OPTIONS'].includes(request.method())){
            mutations.push({route:active,path:url.pathname,method:request.method()});
            return route.abort('blockedbyclient');
        }
        return route.continue();
    });
    page.on('pageerror',e=>errors.push({route:active,message:String(e.message).slice(0,160)}));
    const result=[];
    for(const path of routes){
        active=path;
        const prevErrors=errors.length,prevWrites=mutations.length;
        let status=0,desktop=false,mobile=false,main=false,redirect='',reason='';
        try{
            await page.setViewportSize({width:1366,height:768});
            const response=await page.goto(origin+path,{waitUntil:'domcontentloaded',timeout:20000});
            status=response?.status()||0;
            redirect=new URL(page.url()).pathname;
            main=await page.locator('main#main-content').count()===1;
            if(main)desktop=await page.locator('main#main-content').isVisible();
            await page.setViewportSize({width:390,height:844});
            if(main)mobile=await page.locator('main#main-content').isVisible();
        }catch(e){reason=String(e.message).slice(0,160);}
        const valid=status===200&&normalize(redirect)===normalize(path)&&main&&
            desktop&&mobile&&!reason&&errors.length===prevErrors&&
            mutations.length===prevWrites;
        result.push({path,role,status,desktop,mobile,passed:valid,reason:reason||undefined,
            errors:errors.slice(prevErrors),blockedMutations:mutations.slice(prevWrites)});
        if(!valid)console.error('[QA privileged route FAIL]',role,path,'HTTP',status,
            'route',redirect,'JS',errors.length-prevErrors,'writes',mutations.length-prevWrites);
    }
    await context.close();
    return result;
}
async function negativeAccess(browser,email,pwd,paths){
    const {context}=await signIn(browser,email,pwd);
    const checked=[];
    for(const path of paths){
        const response=await context.request.get(origin+path,{
            maxRedirects:0,failOnStatusCode:false,timeout:15000});
        const status=response.status();
        const passed=status===403 || status===302;
        checked.push({role:email.split('@')[0],path,status,passed});
        if(!passed)console.error('[QA denied route FAIL]',path,'HTTP',status);
    }
    await context.close();
    return checked;
}
async function main(){
    const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
    try{
        const platform=await checkRoutes(browser,
            'demo.platform-qa@eduos.example.invalid',platformPassword,platformRoutes);
        const optional=await checkRoutes(browser,
            'demo.tenantadmin@eduos.example.invalid',tenantPassword,optionalRoutes);
        const tenantDenied=await negativeAccess(browser,
            'demo.tenantadmin@eduos.example.invalid',tenantPassword,platformRoutes);
        const studentDenied=await negativeAccess(browser,
            'demo.student@eduos.example.invalid',tenantPassword,optionalRoutes);
        const pages=[...platform,...optional];
        const denied=[...tenantDenied,...studentDenied];
        fs.mkdirSync('qa-artifacts',{recursive:true});
        fs.writeFileSync('qa-artifacts/real-privileged-route-coverage.json',
            JSON.stringify({sha,scope:'isolated demo-only role-privileged GET + Chromium desktop/mobile; NOT CRUD/UAT',
                checked:pages.length,passed:pages.filter(x=>x.passed).length,
                failed:pages.filter(x=>!x.passed).length,
                deniedChecks:denied.length,deniedPassed:denied.filter(x=>x.passed).length,
                pages,denied,productionReleaseCertified:false},null,2));
        assert.equal(pages.length,15);
        assert(pages.every(x=>x.passed),'Missing authorized platform/optional route checks');
        assert(denied.every(x=>x.passed),'Unauthorized platform or optional role access');
        console.log('PASS real isolated privileged routes',pages.length+'/15, negative role denials',
            denied.length+'/'+denied.length);
    }finally{await browser.close();}
}
main().catch(e=>{console.error('Isolated privileged route QA failed:',String(e.message).slice(0,170));process.exitCode=1;});
