'use strict';
// Production account/profile.js in pinned Chromium with synthetic HTML and API only.
// This is NOT authenticated SQL-backed staging, full Razor rendering or institute UAT.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {chromium} = require('playwright');
const root = path.resolve(__dirname,'../..');
const script = fs.readFileSync(path.join(root,'EduOS.App/wwwroot/js/account/profile.js'),'utf8');
const session = 'a'.repeat(64);
function fixture(bn) {
    return `<!doctype html><html lang="${bn?'bn-BD':'en-BD'}"><head><meta charset="utf-8"></head>
<body data-auth-tenant-id="11" data-auth-session-reference="${session}">
<div id="eduosSessionShield" hidden></div><div id="alertContainer"></div>
<div id="profileAvatar">U</div><div id="profileDisplayName"></div><div id="profileDisplayEmail"></div><div id="profileLanguage"></div>
<form id="profileForm"><input id="fullName"><div class="invalid-feedback"></div><input id="email">
<input id="phoneNumber"><textarea id="address"></textarea><button id="saveProfileBtn" disabled>Save</button></form>
<div id="mfaStatus"></div><div id="mfaStatusHelp"></div><a id="mfaSetupLink" class="d-none"></a>
<div id="mfaManagement" class="d-none"></div><input id="mfaManagePassword"><input id="mfaManageCode">
<input id="mfaManageRecovery" type="checkbox"><button id="mfaDisableBtn" disabled>Disable</button>
<button id="mfaRegenerateBtn" disabled>Regenerate</button>
<div id="mfaNewCodesPanel" class="d-none"><ul id="mfaNewCodes"></ul></div>
<div id="sessionList" data-current="This device" data-other="Other session" data-revoke="Revoke"></div>
<input id="sessionRevokePassword"><button id="sessionRevokeOthersBtn" disabled>Others</button>
<button id="sessionRevokeAllBtn" disabled>All</button>
<form id="passwordForm"><div class="input-group"><input id="currentPassword"><div class="invalid-feedback"></div></div>
<input id="newPassword"><div id="newPasswordError"></div><input id="confirmPassword">
<div id="confirmPasswordError"></div><button id="changePasswordBtn" disabled>Change</button></form>
<script>window.EduOSVerifySessionContext=async()=>{};window.EduOSSessionTransitionPending=()=>false;<\/script>
<script src="/js/account/profile.js"></script></body></html>`;
}
async function setup(browser,{bn=false,profileStatus=200,delay=false}={}) {
    const context = await browser.newContext({viewport:bn?{width:390,height:844}:{width:1280,height:800},
        serviceWorkers:'block',acceptDownloads:false});
    const page = await context.newPage(),writes=[],errors=[];
    let release;
    const gate = new Promise(resolve=>{release=resolve;});
    page.on('pageerror',error=>errors.push(error.message));
    await page.route('**/*',async route=>{
        const req=route.request(),url=new URL(req.url());
        if(url.origin!=='http://eduos.test' || req.method()!=='GET'){
            writes.push(req.method()+' '+req.url());
            return route.abort('blockedbyclient');
        }
        if(url.pathname==='/Account/Profile')
            return route.fulfill({status:200,contentType:'text/html',body:fixture(bn)});
        if(url.pathname==='/js/account/profile.js')
            return route.fulfill({status:200,contentType:'application/javascript',body:script});
        if(url.pathname==='/api/auth/profile' && delay) await gate;
        const headers={'X-EduOS-Session-Reference':session,'X-EduOS-Tenant-Id':'11','Cache-Control':'no-store'};
        if(url.pathname==='/api/auth/profile') return route.fulfill({status:profileStatus,headers,
            contentType:'application/json',body:JSON.stringify(profileStatus===200?
            {success:true,data:{fullName:'Synthetic Account',email:'synthetic@example.test',
                phoneNumber:'0123456789',address:'Synthetic address',preferredLanguage:'bn-BD'}}:
            {success:false,message:'PRIVATE_FOREIGN_TENANT_RECORD'})});
        if(url.pathname==='/api/auth/mfa/status')return route.fulfill({status:200,headers,
            contentType:'application/json',body:JSON.stringify({success:true,data:{enabled:true}})});
        if(url.pathname==='/api/auth/sessions')return route.fulfill({status:200,headers,
            contentType:'application/json',body:JSON.stringify({success:true,data:[
                {sessionId:'synthetic-session',isCurrent:true,createdAtUtc:'2026-11-03T00:00:00Z'}]})});
        return route.abort('blockedbyclient');
    });
    await page.goto('http://eduos.test/Account/Profile',{waitUntil:'domcontentloaded'});
    return {context,page,writes,errors,release};
}
async function success(browser,bn){
    const s=await setup(browser,{bn,delay:true});
    try{
        assert.equal(await s.page.locator('#saveProfileBtn').isDisabled(),true,'Profile must start read-gated');
        s.release();
        await s.page.waitForFunction(()=>document.getElementById('profileDisplayName').textContent==='Synthetic Account');
        assert.equal(await s.page.locator('#saveProfileBtn').isEnabled(),true);
        assert.equal(await s.page.locator('#changePasswordBtn').isEnabled(),true);
        await s.page.waitForFunction(()=>document.getElementById('mfaStatus').textContent.length>0);
        assert.equal(await s.page.locator('#mfaStatus').innerText(),bn?'চালু':'Enabled');
        assert.equal(await s.page.locator('#mfaDisableBtn').isEnabled(),true);
        assert.equal(await s.page.locator('#sessionRevokeOthersBtn').isEnabled(),true);
        await s.page.locator('#currentPassword').fill('synthetic-only');
        await s.page.locator('#newPassword').fill('abc');
        await s.page.locator('#confirmPassword').fill('abc');
        await s.page.locator('#changePasswordBtn').click();
        assert.equal(await s.page.locator('#newPasswordError').innerText(),
            bn?'পাসওয়ার্ড অন্তত ৬ অক্ষরের হতে হবে।':'Password must be at least 6 characters.');
        assert.deepEqual(s.writes,[],'Validation must not send a write');
        assert.deepEqual(s.errors,[],'Browser errors');
        if(bn)assert((await s.page.evaluate(()=>document.documentElement.scrollWidth-innerWidth))<=2,
            'Mobile overflow');
    }finally{await s.context.close();}
}
async function denied(browser){
    const s=await setup(browser,{profileStatus:403});
    try{
        await s.page.waitForFunction(()=>document.getElementById('saveProfileBtn').disabled);
        await s.page.waitForTimeout(200);
        const body=await s.page.locator('body').innerText();
        assert(!body.includes('PRIVATE_FOREIGN_TENANT_RECORD'),'Private denial message exposed');
        assert(!body.includes('Synthetic Account'),'Foreign profile exposed');
        assert.equal(await s.page.locator('#changePasswordBtn').isDisabled(),true);
        assert.deepEqual(s.writes,[]);
        assert.deepEqual(s.errors,[]);
    }finally{await s.context.close();}
}
async function stale(browser){
    const s=await setup(browser,{delay:true});
    try{
        await s.page.evaluate(()=>{document.body.dataset.authTenantId='22';});
        s.release();
        await s.page.waitForTimeout(250);
        assert.equal(await s.page.locator('#profileDisplayName').innerText(),'');
        assert.equal(await s.page.locator('#profileDisplayEmail').innerText(),'');
        assert.equal(await s.page.locator('#saveProfileBtn').isDisabled(),true);
        assert.equal(await s.page.locator('#sessionList').innerText(),'');
        assert.deepEqual(s.writes,[]);
        assert.deepEqual(s.errors,[]);
    }finally{await s.context.close();}
}
(async()=>{
    const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
    try{
        await success(browser,false);await success(browser,true);
        await denied(browser);await stale(browser);
        console.log('Account Profile Chromium PASS: English/Bangla, desktop/mobile, verified read gating, validation, private denial and stale tenant suppression (synthetic API).');
    }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
