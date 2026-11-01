'use strict';
// Opt-in STAGING ONLY: real authenticated cookie/session + SQL-backed preference GET,
// two synthetic users in different tenants, role visibility, bilingual and mobile.
// No appearance Save/Reset/Restore, business mutations, provider sends or UAT claims.
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
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
const url = new URL(base);
if ((url.protocol !== 'https:' && !['localhost','127.0.0.1'].includes(url.hostname)) ||
    url.hostname.toLowerCase() !== allowedHost || url.username || url.password || url.search || url.hash ||
    !['','/'].includes(url.pathname) || users[0].tenant === users[1].tenant) {
    console.error('Refusing non-staging, non-HTTPS, credentialed or same-tenant browser target.');
    process.exit(2);
}
async function login(page, user) {
    await page.goto(base+'/Account/Login', {waitUntil:'domcontentloaded'});
    await page.locator('#email').fill(user.email);
    await page.locator('#password').fill(user.password);
    await page.locator('#loginBtn').click();
    await page.waitForTimeout(800);
    if (await page.locator('#institutionChoicePanel').isVisible()) {
        await page.locator('#institutionChoice').selectOption(user.tenant);
        await page.locator('#loginBtn').click();
    }
    await page.waitForTimeout(800);
    if (page.url().includes('MfaChallenge'))
        throw Error('MFA must be completed by an authorized test flow; no bypass attempted.');
    const response = await page.goto(base+'/workspace', {waitUntil:'domcontentloaded'});
    assert.equal(response.status(),200, 'Workspace access for '+user.label);
    assert.equal(await page.locator('body').getAttribute('data-auth-tenant-id'),user.tenant);
}
async function verify(browser, user, mobile) {
    const context = await browser.newContext({viewport:mobile?{width:390,height:844}:{width:1280,height:800}});
    const page = await context.newPage(), errors=[], writes=[];
    page.on('pageerror', e=>errors.push(e.message));
    page.on('request', request=>{
        const p = new URL(request.url()).pathname;
        if (p.startsWith('/api/workspace-preferences') && request.method()!=='GET')
            writes.push({path:p,method:request.method()});
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
        if (user.label==='admin') {
            const current=await page.locator('#uiCulture').inputValue();
            const alternate=current==='bn-BD'?'en-BD':'bn-BD';
            await page.locator('#uiCulture').selectOption(alternate);
            await page.waitForFunction(expected=>document.documentElement.lang===expected,alternate);
            await page.locator('#workspacePreferencesForm').waitFor();
            await page.locator('#uiCulture').selectOption(current);
            await page.waitForFunction(expected=>document.documentElement.lang===expected,current);
        }
        if (mobile) {
            const width=await page.locator('#workspacePreferencesForm').evaluate(e=>e.getBoundingClientRect().width);
            assert.ok(width>0 && width<=390,'Mobile appearance form exceeds viewport');
        }
        assert.deepEqual(writes,[],'No preference write allowed in read-only browser QA');
        assert.deepEqual(errors,[],'Browser errors: '+errors.join('; '));
    } finally { await context.close(); }
}
(async()=>{
    const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
    try {
        await verify(browser,users[0],false);
        await verify(browser,users[1],true);
        console.log('STAGING authenticated appearance read-only PASS: separate tenant identities, role visibility, GET session headers, preview/discard, help, locale and mobile.');
    } finally { await browser.close(); }
})().catch(e=>{console.error(e.message);process.exitCode=1;});
