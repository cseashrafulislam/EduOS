'use strict';
// Opt-in STAGING ONLY: real authenticated cookie/session + SQL-backed preference GET,
// two synthetic users in different tenants, role visibility, bilingual and mobile.
// No appearance Save/Reset/Restore, business mutations, provider sends or UAT claims.
const assert = require('node:assert/strict');
const base = (process.env.EDUOS_BASE_URL || '').trim().replace(/\/$/, '');
const allowedHost = (process.env.EDUOS_E2E_STAGING_HOST || '').trim().toLowerCase();
const users = [
    {label:'admin', email:process.env.EDUOS_E2E_ADMIN_USER, password:process.env.EDUOS_E2E_ADMIN_PASSWORD,
        tenant:process.env.EDUOS_E2E_ADMIN_TENANT_ID, branding:true},
    {label:'viewer', email:process.env.EDUOS_E2E_VIEWER_USER, password:process.env.EDUOS_E2E_VIEWER_PASSWORD,
        tenant:process.env.EDUOS_E2E_VIEWER_TENANT_ID, branding:false}
];
if (!base || !allowedHost || users.some(u=>!u.email || !u.password || !/^\d+$/.test(u.tenant || ''))) {
    console.error('Staging URL, explicit staging hostname and two synthetic users/tenant IDs are required.');
    process.exit(2);
}
let url;
try { url = new URL(base); } catch { console.error('Invalid staging URL.'); process.exit(2); }
if ((url.protocol !== 'https:' && !['localhost','127.0.0.1'].includes(url.hostname)) ||
    url.hostname.toLowerCase() !== allowedHost || url.username || url.password || url.search || url.hash ||
    !['','/'].includes(url.pathname) || users[0].tenant === users[1].tenant) {
    console.error('Refusing non-staging, non-HTTPS, credentialed or same-tenant browser target.');
    process.exit(2);
}
const { chromium } = require('playwright');
async function login(page, user) {
    await page.goto(base+'/Account/Login', {waitUntil:'domcontentloaded'});
    await page.locator('#userNameOrEmail').fill(user.email);
    await page.locator('#password').fill(user.password);
    await page.locator('#loginBtn').click();
    await page.waitForFunction(() => location.pathname.toLowerCase() !== '/account/login' ||
        !!document.querySelector('#institutionChoicePanel:not([hidden])') ||
        !!document.querySelector('#alertContainer.alert-danger'), null, {timeout:20000});
    if (await page.locator('#institutionChoicePanel').isVisible()) {
        const options = await page.locator('#institutionChoice option').evaluateAll(
            nodes => nodes.map(node => node.value));
        assert(options.includes(user.tenant), 'Expected synthetic tenant not offered');
        await page.locator('#institutionChoice').selectOption(user.tenant);
        await page.locator('#loginBtn').click();
        await page.waitForFunction(() => location.pathname.toLowerCase() !== '/account/login' ||
            !!document.querySelector('#alertContainer.alert-danger'), null, {timeout:20000});
    }
    if (await page.locator('#alertContainer.alert-danger').isVisible())
        throw Error('Synthetic login rejected; check staging test account.');
    if (page.url().includes('MfaChallenge'))
        throw Error('MFA must be completed by an authorized test flow; no bypass attempted.');
    const response = await page.goto(base+'/workspace', {waitUntil:'domcontentloaded'});
    assert.equal(response.status(),200, 'Workspace access for '+user.label);
    assert.equal(new URL(page.url()).pathname.toLowerCase(),'/workspace',
        'Workspace must not redirect to login/access denied');
    assert.equal(await page.locator('body').getAttribute('data-auth-tenant-id'),user.tenant);
}
async function verify(browser, user, mobile) {
    const context = await browser.newContext({viewport:{width:1280,height:800},
        serviceWorkers:'block',ignoreHTTPSErrors:false,acceptDownloads:false});
    const page = await context.newPage(), errors=[], blockedWrites=[], allowedWrites=[];
    let originalCulture=null, localeChangeAttempted=false, cleanupFailure=null;
    page.on('pageerror', e=>errors.push(e.message));
    // Block every unexpected mutation before it reaches staging, not only preference writes.
    await context.route('**/*', route=>{
        const request=route.request(), method=request.method().toUpperCase();
        if (['GET','HEAD','OPTIONS'].includes(method)) return route.continue();
        const u=new URL(request.url()), p=u.pathname.toLowerCase();
        if (u.origin===url.origin && method==='POST' &&
            ['/api/auth/login','/localization/set-language'].includes(p)) {
            allowedWrites.push(method+' '+p);
            return route.continue();
        }
        blockedWrites.push(method+' '+p);
        return route.abort('blockedbyclient');
    });
    try {
        await login(page,user);
        await page.locator('#workspacePreferencesForm').waitFor();
        await page.waitForFunction(() => document.getElementById('workspaceStatus').textContent.trim().length>0);
        assert.equal(await page.locator('#workspaceSave').isEnabled(),true,'Preference GET must load');
        assert.equal(await page.locator('#workspaceInstituteBranding').count(),user.branding?1:0);
        const result=await page.evaluate(async()=>{
            const response=await fetch('/api/workspace-preferences',{credentials:'same-origin',cache:'no-store'});
            return {status:response.status, tenant:response.headers.get('X-EduOS-Tenant-Id'),
                session:response.headers.get('X-EduOS-Session-Reference')};
        });
        assert.equal(result.status,200);
        assert.equal(result.tenant,user.tenant);
        assert.equal(result.session,await page.locator('body').getAttribute('data-auth-session-reference'));
        const theme=page.locator('#workspaceTheme'), before=await theme.inputValue();
        const preview=before==='dark'?'light':'dark';
        await theme.selectOption(preview);
        assert.equal(await page.evaluate(()=>document.documentElement.dataset.workspaceTheme),preview);
        assert.equal(await page.locator('#workspaceDiscard').isEnabled(),true);
        await page.locator('#workspaceDiscard').click();
        assert.equal(await theme.inputValue(),before);
        assert.equal(await page.locator('#workspaceDiscard').isDisabled(),true);
        await page.locator('#pageContextHelp').click();
        await page.locator('#pageHelpDrawer[open]').waitFor();
        assert.ok((await page.locator('#pageHelpContent').innerText()).length>100);
        await page.keyboard.press('Escape');
        await page.waitForFunction(()=>!document.getElementById('pageHelpDrawer').open);
        const brandingResponse=await page.goto(base+'/Account/BrandingSetup',{waitUntil:'domcontentloaded'});
        if (user.branding) {
            assert.equal(brandingResponse.status(),200,'TenantAdmin branding GET');
            await page.locator('#brandingForm').waitFor();
        } else {
            assert(new URL(page.url()).pathname.toLowerCase()!=='/account/brandingsetup' ||
                brandingResponse.status()===403,'Viewer must be denied direct BrandingSetup GET');
            assert.equal(await page.locator('#brandingForm').count(),0);
        }
        await page.goBack({waitUntil:'domcontentloaded'});
        assert.equal(new URL(page.url()).pathname.toLowerCase(),'/workspace');
        await page.reload({waitUntil:'domcontentloaded'});
        await page.locator('#workspacePreferencesForm').waitFor();
        assert.equal(await page.locator('body').getAttribute('data-auth-tenant-id'),user.tenant);
        await page.setViewportSize({width:390,height:844});
        const mobileWidth=await page.locator('#workspacePreferencesForm').evaluate(
            e=>e.getBoundingClientRect().width);
        assert(mobileWidth>0 && mobileWidth<=390,'Mobile appearance form exceeds viewport');
        await page.locator('#workspaceTheme').focus();
        await page.keyboard.press('Tab');
        assert.equal(await page.evaluate(()=>document.activeElement?.id),'workspacePalette',
            'Keyboard focus order');
        if (user.label==='admin') {
            const current=await page.locator('#uiCulture').inputValue();
            originalCulture=current;
            const alternate=current==='bn-BD'?'en-BD':'bn-BD';
            localeChangeAttempted=true;
            await page.locator('#uiCulture').selectOption(alternate);
            await page.waitForFunction(expected=>document.documentElement.lang===expected,alternate);
            await page.reload({waitUntil:'domcontentloaded'});
            assert.equal(await page.locator('#uiCulture').inputValue(),alternate,
                'Language preference must survive refresh');
            await page.locator('#workspacePreferencesForm').waitFor();
            await page.locator('#uiCulture').selectOption(current);
            await page.waitForFunction(expected=>document.documentElement.lang===expected,current);
        }
        if (mobile) {
            const width=await page.locator('#workspacePreferencesForm').evaluate(e=>e.getBoundingClientRect().width);
            assert.ok(width>0 && width<=390,'Mobile appearance form exceeds viewport');
        }
        assert.deepEqual(blockedWrites,[],'Unexpected mutation was blocked');
        assert(allowedWrites.includes('POST /api/auth/login'),'Real login request expected');
        assert.deepEqual(errors,[],'Browser errors: '+errors.join('; '));
    } finally {
        // Locale is the only persisted synthetic-user preference this test may change.
        // Restore it even after a failed assertion, and surface cleanup failures.
        if (localeChangeAttempted && originalCulture) {
            try {
                await page.goto(base+'/workspace',{waitUntil:'domcontentloaded'});
                if (await page.locator('#uiCulture').inputValue()!==originalCulture) {
                    await page.locator('#uiCulture').selectOption(originalCulture);
                    await page.waitForFunction(expected=>document.documentElement.lang===expected,
                        originalCulture,{timeout:15000});
                }
                assert.equal(await page.locator('#uiCulture').inputValue(),originalCulture);
            } catch (error) { cleanupFailure=error; }
        }
        await context.close();
        if (cleanupFailure) throw Error('Staging language restoration failed: '+cleanupFailure.message);
    }
}
(async()=>{
    const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
    try {
        await verify(browser,users[0],false);
        await verify(browser,users[1],true);
        console.log('STAGING authenticated appearance PASS: two tenant identities, direct role denial, session GET, preview/discard, Help, locale refresh, mobile/keyboard and blocked unsafe writes.');
    } finally { await browser.close(); }
})().catch(e=>{console.error(e.message);process.exitCode=1;});
