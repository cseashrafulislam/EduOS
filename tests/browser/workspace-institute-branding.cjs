'use strict';
// Real production script in Chromium; synthetic API/session only. No authenticated SQL claims.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const source = fs.readFileSync(path.resolve(__dirname,
    '../../EduOS.App/wwwroot/js/platform/workspace-institute-branding.js'), 'utf8');

function html(lang) {
    const rows = ['primary','secondary','accent'].map(k =>
        '<div data-branding-color="' + k + '"><span data-branding-value></span>' +
        '<span data-branding-swatch></span></div>').join('');
    return '<!doctype html><html lang="' + lang + '"><body data-auth-tenant-id="1" ' +
        'data-auth-session-reference="session-1"><div id="eduosSessionShield" hidden></div>' +
        '<section id="workspaceInstituteBranding" data-loading="Loading" data-error="Unable to load">' +
        '<span data-branding-status role="status"></span><button data-branding-retry hidden>Retry</button>' +
        '<div data-branding-content hidden><span data-branding-name></span><img data-branding-logo hidden>' +
        rows + '</div></section></body></html>';
}
async function run(browser, options) {
    const {lang,width,mismatched=false,switched=false} = options;
    const context = await browser.newContext({viewport:{width,height:844},serviceWorkers:'block'});
    const page = await context.newPage(), calls = [];
    await page.route('http://eduos.test/**', async route => {
        const url = new URL(route.request().url());
        if (url.pathname === '/') return route.fulfill({status:200,contentType:'text/html',body:html(lang)});
        calls.push(url.pathname);
        if (url.pathname === '/api/workspace-preferences')
            return route.fulfill({status:200,contentType:'application/json',
                headers:{'X-EduOS-Session-Reference':mismatched?'other':'session-1'},
                body:JSON.stringify({success:true,data:{}})});
        if (switched && url.pathname === '/api/tenant-profile')
            await page.evaluate(() => { document.body.dataset.authTenantId = '2'; });
        if (url.pathname === '/api/tenant-profile')
            return route.fulfill({status:200,contentType:'application/json',
                body:JSON.stringify({success:true,data:{name:'Synthetic Institute',logoUrl:'/uploads/logo.png'}})});
        if (url.pathname === '/api/tenant-settings')
            return route.fulfill({status:200,contentType:'application/json',
                body:JSON.stringify({success:true,data:{items:[{key:'Branding.PrimaryColor',value:'#123456'}]}})});
        return route.abort();
    });
    try {
        await page.goto('http://eduos.test/',{waitUntil:'domcontentloaded'});
        await page.evaluate(() => {
            window.EduOSWorkspaceResponseMatchesSession = response =>
                response.headers.get('X-EduOS-Session-Reference') ===
                document.body.dataset.authSessionReference;
            window.EduOSVerifySessionContext = () => { window.sessionChecks = (window.sessionChecks||0)+1; };
            window.EduOSSessionTransitionPending = () => false;
        });
        await page.addScriptTag({content:source});
        await page.waitForFunction(() => {
            const status = document.querySelector('[data-branding-status]');
            return (!document.querySelector('[data-branding-content]').hidden && status.textContent === '') ||
                status.getAttribute('role') === 'alert';
        });
        const hidden = await page.locator('[data-branding-content]').evaluate(e=>e.hidden);
        if (mismatched || switched) {
            assert.equal(hidden,true);
            assert.equal(await page.locator('[data-branding-retry]').evaluate(e=>e.hidden),true);
            assert.equal(await page.evaluate(()=>window.sessionChecks),1);
        } else {
            assert.equal(hidden,false);
            assert.equal(await page.locator('[data-branding-name]').innerText(),'Synthetic Institute');
            assert.equal(await page.locator('[data-branding-value]').first().innerText(),'#123456');
            assert.equal(await page.locator('[data-branding-value]').nth(1).innerText(),'#64748B');
            assert.equal(calls.filter(p=>p==='/api/workspace-preferences').length,2);
        }
        if (mismatched) assert.equal(calls.includes('/api/tenant-profile'),false);
    } finally { await context.close(); }
}
(async()=>{
    const browser = await chromium.launch({headless:true,args:['--no-sandbox']});
    try {
        await run(browser,{lang:'en-BD',width:1280});
        await run(browser,{lang:'bn-BD',width:390});
        await run(browser,{lang:'en-BD',width:1280,mismatched:true});
        await run(browser,{lang:'bn-BD',width:390,switched:true});
        console.log('Branding summary Chromium PASS: bilingual desktop/mobile, verified reads, stale tenant rejection.');
    } finally { await browser.close(); }
})().catch(error=>{console.error(error);process.exitCode=1;});
