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
<form id="passwordForm"><div class="input-group"><input id="currentPassword" type="password">
<button type="button" class="password-toggle" data-target="currentPassword"
 data-show-label="${bn?'পাসওয়ার্ড দেখান':'Show password'}" data-hide-label="${bn?'পাসওয়ার্ড লুকান':'Hide password'}"
 aria-controls="currentPassword" aria-pressed="false" aria-label="${bn?'পাসওয়ার্ড দেখান':'Show password'}"><i class="bi bi-eye"></i></button>
<div class="invalid-feedback"></div></div>
<input id="newPassword"><div id="newPasswordError"></div><input id="confirmPassword">
<div id="confirmPasswordError"></div><button id="changePasswordBtn" disabled>Change</button></form>
<script>window.EduOSVerifySessionContext=async()=>{};window.EduOSSessionTransitionPending=()=>false;<\/script>
<script src="/js/account/profile.js"></script></body></html>`;
}
async function setup(browser,{bn=false,profileStatus=200,profileSuccess=true,delay=false,jsonGate=false,syntheticWrite=null,writeGate=false,writeStatus=403}={}) {
    if(syntheticWrite)assert(['/api/auth/profile','/api/auth/mfa/disable',
        '/api/auth/mfa/recovery-codes/regenerate','/api/auth/sessions/revoke-others',
        '/api/auth/sessions/revoke-all'].includes(syntheticWrite),'Unexpected synthetic write target');
    const context = await browser.newContext({viewport:bn?{width:390,height:844}:{width:1280,height:800},
        serviceWorkers:'block',acceptDownloads:false});
    const page = await context.newPage(),writes=[],syntheticWrites=[],errors=[];
    let release,releaseWrite,notifyWrite;
    const gate = new Promise(resolve=>{release=resolve;});
    const writeLatch = new Promise(resolve=>{releaseWrite=resolve;});
    const writeStarted = new Promise(resolve=>{notifyWrite=resolve;});
    page.on('pageerror',error=>errors.push(error.message));
    if(jsonGate)await page.addInitScript(() => {
        const originalFetch=window.fetch.bind(window);
        window.fetch=async (...args) => {
            const response=await originalFetch(...args);
            if(String(args[0])==='/api/auth/profile' && String(args[1]?.method||'GET').toUpperCase()==='GET'){
                const originalJson=response.json.bind(response);
                response.json=async () => {
                    const payload=await originalJson();
                    await new Promise(resolve=>{window.__releaseProfileJson=resolve;});
                    return payload;
                };
            }
            return response;
        };
    });
    await page.route('**/*',async route=>{
        const req=route.request(),url=new URL(req.url());
        if(url.origin==='http://eduos.test' && req.method()!=='GET' &&
            url.pathname===syntheticWrite && ['POST','PUT'].includes(req.method())){
            syntheticWrites.push(req.method()+' '+url.pathname);
            notifyWrite();
            if(writeGate)await writeLatch;
            return route.fulfill({status:writeStatus,contentType:'application/json',
                headers:{'X-EduOS-Session-Reference':session,'X-EduOS-Tenant-Id':'11','Cache-Control':'no-store'},
                body:JSON.stringify(writeStatus===200?{success:true}:
                    {success:false,message:'PRIVATE_FOREIGN_TENANT_RECORD'})});
        }
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
            {success:profileSuccess,data:{fullName:'Synthetic Account',email:'synthetic@example.test',
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
    return {context,page,writes,syntheticWrites,errors,release,releaseWrite,writeStarted};
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
        const toggle=s.page.locator('.password-toggle[data-target="currentPassword"]');
        assert.equal(await toggle.getAttribute('aria-pressed'),'false');
        assert.equal(await toggle.getAttribute('aria-label'),bn?'পাসওয়ার্ড দেখান':'Show password');
        await toggle.click();
        assert.equal(await s.page.locator('#currentPassword').getAttribute('type'),'text');
        assert.equal(await toggle.getAttribute('aria-pressed'),'true');
        assert.equal(await toggle.getAttribute('aria-label'),bn?'পাসওয়ার্ড লুকান':'Hide password');
        await toggle.click();
        assert.equal(await s.page.locator('#currentPassword').getAttribute('type'),'password');
        assert.equal(await toggle.getAttribute('aria-pressed'),'false');
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
async function switchedAfterLoad(browser){
    const s=await setup(browser);
    try{
        await s.page.waitForFunction(()=>document.getElementById('profileDisplayName').textContent==='Synthetic Account');
        await s.page.evaluate(()=>{document.body.dataset.authTenantId='22';});
        await s.page.locator('#fullName').fill('Should not be sent');
        await s.page.locator('#saveProfileBtn').click();
        await s.page.waitForFunction(()=>document.getElementById('saveProfileBtn').disabled);
        assert.equal(await s.page.locator('#profileDisplayName').innerText(),'',
            'Old profile must be cleared after tenant switch');
        assert.equal(await s.page.locator('#profileDisplayEmail').innerText(),'');
        assert.deepEqual(s.writes,[],'Changed institution must not dispatch a profile write');
        assert.deepEqual(s.errors,[]);
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

async function switchedDuringSuccessfulWrite(browser){
    const s=await setup(browser,{syntheticWrite:'/api/auth/profile',writeGate:true,writeStatus:200});
    try{
        await s.page.waitForFunction(()=>document.getElementById('saveProfileBtn')?.disabled===false);
        await s.page.locator('#fullName').fill('Synthetic updated account');
        await s.page.locator('#saveProfileBtn').click();
        await s.writeStarted;
        await s.page.evaluate(()=>{document.body.dataset.authTenantId='22';});
        s.releaseWrite();
        await s.page.waitForFunction(()=>document.getElementById('saveProfileBtn').disabled &&
            document.getElementById('profileDisplayName').textContent==='');
        await s.page.waitForTimeout(100);
        assert(!(await s.page.locator('#alertContainer').innerText()).includes('Profile updated successfully.'),
            'Stale successful write must not show a success toast');
        assert.equal(await s.page.locator('#profileDisplayEmail').innerText(),'');
        assert.equal(await s.page.locator('#changePasswordBtn').isDisabled(),true);
        assert.deepEqual(s.syntheticWrites,['PUT /api/auth/profile']);
        assert.deepEqual(s.writes,[]);
        assert.deepEqual(s.errors,[]);
    }finally{s.releaseWrite();await s.context.close();}
}

async function switchedWhileParsingJson(browser){
    const s=await setup(browser,{jsonGate:true});
    try{
        await s.page.waitForFunction(()=>typeof window.__releaseProfileJson==='function');
        await s.page.evaluate(()=>{document.body.dataset.authTenantId='22';window.__releaseProfileJson();});
        await s.page.waitForFunction(()=>document.getElementById('saveProfileBtn').disabled &&
            document.getElementById('profileDisplayName').textContent==='');
        assert.equal(await s.page.locator('#profileDisplayEmail').innerText(),'');
        assert.equal(await s.page.locator('#sessionList').innerText(),'');
        assert.deepEqual(s.writes,[]);
        assert.deepEqual(s.errors,[]);
    }finally{await s.context.close();}
}
async function falseSuccessRead(browser){
    const s=await setup(browser,{profileSuccess:false});
    try{
        await s.page.waitForTimeout(200);
        assert.equal(await s.page.locator('#saveProfileBtn').isDisabled(),true);
        assert.equal(await s.page.locator('#changePasswordBtn').isDisabled(),true);
        assert.equal(await s.page.locator('#profileDisplayName').innerText(),'');
        assert(!(await s.page.locator('body').innerText()).includes('PRIVATE_FOREIGN_TENANT_RECORD'));
        assert.deepEqual(s.writes,[]);
        assert.deepEqual(s.errors,[]);
    }finally{await s.context.close();}
}
async function deniedSecurityWrite(browser,id,endpoint){
    const s=await setup(browser,{syntheticWrite:endpoint,writeStatus:403});
    try{
        await s.page.waitForFunction(id=>document.getElementById(id)?.disabled===false,id);
        if(id.startsWith('mfa')){
            await s.page.locator('#mfaManagePassword').fill('synthetic-password');
            await s.page.locator('#mfaManageCode').fill('123456');
        }else await s.page.locator('#sessionRevokePassword').fill('synthetic-password');
        await s.page.locator('#'+id).click();
        await s.writeStarted;
        await s.page.waitForFunction(()=>document.getElementById('profileDisplayName').textContent==='');
        await s.page.waitForTimeout(50);
        for(const button of ['saveProfileBtn','changePasswordBtn','mfaDisableBtn','mfaRegenerateBtn',
            'sessionRevokeOthersBtn','sessionRevokeAllBtn'])
            assert.equal(await s.page.locator('#'+button).isDisabled(),true,
                'Revoked session re-enabled '+button);
        assert.equal(await s.page.locator('#sessionList').innerText(),'');
        assert(!(await s.page.locator('body').innerText()).includes('PRIVATE_FOREIGN_TENANT_RECORD'));
        assert.deepEqual(s.syntheticWrites,['POST '+endpoint]);
        assert.deepEqual(s.writes,[]);
        assert.deepEqual(s.errors,[]);
    }finally{await s.context.close();}
}
(async()=>{
    const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
    try{
        await success(browser,false);await success(browser,true);
        await denied(browser);await stale(browser);await switchedAfterLoad(browser);
        await switchedWhileParsingJson(browser);await falseSuccessRead(browser);
        await switchedDuringSuccessfulWrite(browser);
        for(const [id,endpoint] of [
            ['mfaDisableBtn','/api/auth/mfa/disable'],
            ['mfaRegenerateBtn','/api/auth/mfa/recovery-codes/regenerate'],
            ['sessionRevokeOthersBtn','/api/auth/sessions/revoke-others'],
            ['sessionRevokeAllBtn','/api/auth/sessions/revoke-all']
        ])await deniedSecurityWrite(browser,id,endpoint);
        console.log('Account Profile Chromium PASS: English/Bangla, desktop/mobile, verified read gating, validation, private denial stale tenant suppression, in-flight write isolation, post-JSON tenant race, false-success denial, accessible password toggles and denied security-action fail-closed (synthetic API).');
    }finally{await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
