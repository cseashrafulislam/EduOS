'use strict';
// Actual production Login and shared Help JavaScript in Chromium, synthetic API.
// No real credentials, SQL-backed login, or institutional UAT.
const assert=require('node:assert/strict');
const fs=require('node:fs'),path=require('node:path');
const {chromium}=require('playwright');
const root=path.resolve(__dirname,'../..');
const read=p=>fs.readFileSync(path.join(root,p),'utf8');
const razor=read('EduOS.App/Views/Account/Login.cshtml');
for(const key of ['SelectInstitutionForLogin','InstitutionChoiceHelp','InstitutionChoicePrompt','UsernameEmailPlaceholder','FullPageManual'])
    assert(razor.includes('@T["'+key+'"]'),'Missing localized Razor key '+key);
for(const id of ['pageContextHelp','pageHelpDrawer','pageHelpClose','pageHelpContent'])
    assert(razor.includes('id="'+id+'"'),'Missing shared help element '+id);
const loginJs=path.join(root,'EduOS.App/wwwroot/js/account/login.js');
const helpJs=path.join(root,'EduOS.App/wwwroot/js/platform/page-context-help.js');
function html(bn){
    const message=bn?'প্রতিষ্ঠান বেছে আবার লগ ইন করুন।':'Select your institution, then sign in.';
    const invalid=bn?'ইমেইল অথবা পাসওয়ার্ড সঠিক নয়।':'The email or password is not correct.';
    return '<!doctype html><html lang="'+(bn?'bn-BD':'en-BD')+'"><head><meta charset="utf-8"></head><body>'+
        '<form id="loginForm" data-select-institution-message="'+message+'" data-select-institution-required="Select institution" data-institution-label="Institution" data-invalid-message="'+invalid+'">'+
        '<input id="userNameOrEmail"><input id="password" type="password"><input id="rememberMe" type="checkbox">'+
        '<div id="institutionChoicePanel" hidden><select id="institutionChoice"></select></div>'+
        '<button id="loginBtn" type="submit" data-idle-label="Sign in">Sign in</button></form>'+
        '<div id="alertContainer"></div><button id="pageContextHelp">Info</button>'+
        '<dialog id="pageHelpDrawer" aria-labelledby="pageHelpTitle"><h2 id="pageHelpTitle"></h2>'+
        '<button id="pageHelpClose">Close</button><div id="pageHelpContent"></div></dialog></body></html>';
}
async function setup(browser,bn,responses){
    const context=await browser.newContext({viewport:bn?{width:390,height:844}:{width:1280,height:800}});
    const page=await context.newPage(),requests=[],errors=[];
    page.on('pageerror',e=>errors.push(e.message));
    await page.addInitScript({path:loginJs});
    await page.route('http://eduos.test/**',async route=>{
        const req=route.request(),p=new URL(req.url()).pathname;
        if(p==='/api/auth/login'){
            requests.push(JSON.parse(req.postData()));
            const next=responses.shift();
            assert(next,'Unexpected login');
            if (next.delayMs) await new Promise(resolve=>setTimeout(resolve,next.delayMs));
            return route.fulfill({status:next.status,contentType:'application/json',body:JSON.stringify(next.body)});
        }
        return route.fulfill({status:200,contentType:'text/html',body:p==='/Account/Login'?html(bn):'<h1>Redirected</h1>'});
    });
    await page.goto('http://eduos.test/Account/Login');
    await page.addScriptTag({path:helpJs});
    return {context,page,requests,errors};
}
const choice=tenants=>({status:409,body:{success:true,data:{requiresTenantSelection:true,tenants}}});
async function bilingual(browser,bn){
    const s=await setup(browser,bn,[choice([{id:22,name:'Demo School'},{id:33,name:'Demo College'}]),
        {status:200,body:{success:true,data:{redirectUrl:'/Dashboard'}}}]);
    try{
        await s.page.locator('#pageContextHelp').click();
        await s.page.locator('#pageHelpDrawer[open]').waitFor();
        assert.equal(await s.page.locator('#pageHelpTitle').innerText(),bn?'সাইন ইন ও প্রতিষ্ঠান নির্বাচন':'Sign in and institution selection');
        assert.equal(await s.page.locator('#pageHelpContent ol li').count(),3);
        await s.page.keyboard.press('Escape');
        await s.page.locator('#userNameOrEmail').fill('synthetic@example.test');
        await s.page.locator('#password').fill('synthetic-password');
        await s.page.locator('#loginBtn').click();
        await s.page.locator('#institutionChoicePanel:visible').waitFor();
        assert.equal(await s.page.locator('#institutionChoice option').count(),3);
        assert.equal(await s.page.locator('#alertContainer').innerText(),bn?'প্রতিষ্ঠান বেছে আবার লগ ইন করুন।':'Select your institution, then sign in.');
        assert.equal(await s.page.locator('#institutionChoice').inputValue(),'');
        assert.equal(await s.page.locator('#institutionChoice option:disabled').count(),1);
        await s.page.locator('#loginBtn').click();
        assert.equal(s.requests.length,1,'Institution must be explicitly selected');
        await s.page.locator('#institutionChoice').selectOption('33');
        await s.page.locator('#loginBtn').click();
        await s.page.waitForURL('**/Dashboard');
        assert.equal(s.requests.length,2);
        assert.equal(s.requests[0].tenantId,null);
        assert.equal(s.requests[1].tenantId,33);
        assert.deepEqual(s.errors,[]);
    }finally{await s.context.close();}
}
async function invalid(browser){
    const s=await setup(browser,true,[choice([{id:-1,name:'Bad'},{id:'oops',name:'Bad'}])]);
    try{
        await s.page.locator('#userNameOrEmail').fill('synthetic@example.test');
        await s.page.locator('#password').fill('synthetic-password');
        await s.page.locator('#loginBtn').click();
        await s.page.waitForFunction(()=>document.getElementById('alertContainer').classList.contains('alert-danger'));
        assert.equal(await s.page.locator('#institutionChoicePanel').isVisible(),false);
        assert.equal(await s.page.locator('#alertContainer').innerText(),'ইমেইল অথবা পাসওয়ার্ড সঠিক নয়।');
        assert.equal(s.requests.length,1);
        assert.deepEqual(s.errors,[]);
    }finally{await s.context.close();}
}
async function changedCredentials(browser){
    const s=await setup(browser,false,[choice([{id:22,name:'School A'},{id:33,name:'School B'}]),
        choice([{id:44,name:'School C'}])]);
    try{
        await s.page.locator('#userNameOrEmail').fill('first@example.test');
        await s.page.locator('#password').fill('synthetic-password');
        await s.page.locator('#loginBtn').click();
        await s.page.locator('#institutionChoicePanel:visible').waitFor();
        await s.page.locator('#institutionChoice').selectOption('33');
        await s.page.locator('#userNameOrEmail').fill('second@example.test');
        assert.equal(await s.page.locator('#institutionChoicePanel').isVisible(),false);
        await s.page.locator('#loginBtn').click();
        await s.page.locator('#institutionChoicePanel:visible').waitFor();
        assert.equal(await s.page.locator('#institutionChoice option').count(),2);
        assert.equal(await s.page.locator('#institutionChoice').inputValue(),'');
        assert.deepEqual(s.requests.map(x=>x.tenantId),[null,null]);
        assert.deepEqual(s.errors,[]);
    }finally{await s.context.close();}
}
async function staleAndDuplicate(browser){
    const delayed={...choice([{id:22,name:'Stale school'}]),delayMs:450};
    const s=await setup(browser,false,[delayed,choice([{id:44,name:'Fresh school'}])]);
    try{
        await s.page.locator('#userNameOrEmail').fill('first@example.test');
        await s.page.locator('#password').fill('synthetic-password');
        await s.page.locator('#loginBtn').click();
        await s.page.evaluate(()=>document.getElementById('loginForm')
            .dispatchEvent(new Event('submit',{bubbles:true,cancelable:true})));
        assert.equal(s.requests.length,1,'Duplicate pending submission must not send another login');
        await s.page.locator('#userNameOrEmail').fill('second@example.test');
        await s.page.locator('#loginBtn').click();
        await s.page.locator('#institutionChoicePanel:visible').waitFor();
        assert.equal(await s.page.locator('#institutionChoice option').last().getAttribute('value'),'44');
        assert.deepEqual(s.requests.map(x=>x.tenantId),[null,null]);
        assert.deepEqual(s.errors,[]);
    }finally{await s.context.close();}
}
async function mfa(browser){
    const s=await setup(browser,false,[{status:202,body:{success:true,data:{requiresTwoFactor:true,challengeToken:'synthetic-only'}}}]);
    try{
        await s.page.locator('#userNameOrEmail').fill('synthetic@example.test');
        await s.page.locator('#password').fill('synthetic-password');
        await s.page.locator('#loginBtn').click();
        await s.page.waitForURL('**/Account/MfaChallenge');
        assert.equal(await s.page.evaluate(()=>sessionStorage.getItem('eduos.mfaChallenge')),'synthetic-only');
        assert.deepEqual(s.errors,[]);
    }finally{await s.context.close();}
}
(async()=>{
    const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
    try{await bilingual(browser,false);await bilingual(browser,true);await invalid(browser);
        await changedCredentials(browser);await staleAndDuplicate(browser);await mfa(browser);
        console.log('Login Chromium PASS: explicit bilingual tenant choice, stale-identity cancellation, duplicate-submit protection, invalid tenant and MFA (synthetic API).');
    }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
