'use strict';
// Real production shared Help Drawer JavaScript in Chromium with synthetic
// menu/session responses. NOT an authenticated server/SQL or institute UAT.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../..');
const js = path.join(root, 'EduOS.App/wwwroot/js/platform/page-context-help.js');
const css = path.join(root, 'EduOS.App/wwwroot/css/site.css');
const layout = fs.readFileSync(path.join(root, 'EduOS.App/Views/Shared/_Layout.cshtml'), 'utf8');
for (const id of ['pageContextHelp', 'pageHelpDrawer', 'pageHelpTitle', 'pageHelpContent', 'pageHelpClose'])
    assert(layout.includes('id="' + id + '"'), 'Missing shared Razor element ' + id);
const session = 'A'.repeat(64);
function html(bn, tenant) {
    return '<!doctype html><html lang="' + (bn ? 'bn-BD' : 'en-BD') + '"><head><meta charset="utf-8"></head>' +
        '<body data-auth-tenant-id="' + tenant + '" data-auth-session-reference="' + session + '">' +
        '<nav data-sidebar-section="students"><a class="nav-link" href="/Admissions/Index">Admissions</a></nav>' +
        '<button id="pageContextHelp" type="button">Info</button>' +
        '<dialog class="page-help-drawer" id="pageHelpDrawer" aria-labelledby="pageHelpTitle" aria-modal="true">' +
        '<h2 id="pageHelpTitle"></h2><button id="pageHelpClose" type="button">Close</button>' +
        '<div id="pageHelpContent"></div></dialog>' +
        '<section id="eduosSessionShield" hidden></section></body></html>';
}
async function setup(browser, bn=false, mobile=false) {
    const context = await browser.newContext({viewport: mobile ? {width:390,height:844} : {width:1280,height:800}});
    const page = await context.newPage(), errors = [];
    page.on('pageerror', e => errors.push(e.message));
    await page.addInitScript(() => {
        window.verifyCount = 0;
        window.EduOSSessionTransitionPending = () => Boolean(window.transitioning);
        window.EduOSVerifySessionContext = async () => {
            window.verifyCount++;
            if (window.rejectVerification) throw Error('revoked');
            await new Promise(resolve => setTimeout(resolve, 5));
        };
    });
    await page.route('http://eduos.test/**', route => route.fulfill({
        contentType:'text/html; charset=utf-8', body:html(bn,'22')
    }));
    await page.goto('http://eduos.test/Students/Index');
    await page.addStyleTag({path:css});
    await page.addScriptTag({path:js});
    return {context,page,errors};
}
async function normal(browser) {
    const {context,page,errors} = await setup(browser);
    try {
        await page.locator('#pageContextHelp').click();
        await page.locator('#pageHelpDrawer[open]').waitFor();
        assert.equal(await page.locator('#pageHelpTitle').innerText(), 'Student Directory');
        for (const label of ['Page overview','Before you start','How to use','Worked example (fictional)',
            'Expected result','Important rules','Errors and recovery','Related authorized workflows'])
            assert.equal(await page.getByRole('heading',{name:label}).count(), 1, label);
        assert.equal(await page.locator('#pageHelpContent a[href="/Admissions/Index"]').count(),1);
        assert.equal(await page.locator('#pageHelpContent a[href="/StudentLifecycle/Index"]').count(),0);
        assert.equal(await page.locator('#pageHelpContent script').count(),0);
        assert.equal(await page.locator('#pageHelpClose').evaluate(e=>document.activeElement===e),true);
        await page.keyboard.press('Escape');
        await page.waitForFunction(() => !document.getElementById('pageHelpDrawer').open);
        assert.equal(await page.locator('#pageContextHelp').evaluate(e=>document.activeElement===e),true);
        assert.ok(await page.evaluate(()=>window.verifyCount)>=1);
        await page.evaluate(()=>{document.documentElement.lang='bn-BD';});
        await page.locator('#pageContextHelp').click();
        await page.locator('#pageHelpDrawer[open]').waitFor();
        assert.equal(await page.locator('#pageHelpTitle').innerText(), 'শিক্ষার্থী তালিকা');
        assert.equal(await page.getByRole('heading',{name:'কীভাবে করবেন'}).count(),1);
        await page.evaluate(()=>{document.documentElement.lang='en-BD';});
        await page.getByRole('heading',{name:'Page overview'}).waitFor();
        assert.equal(await page.locator('#pageHelpTitle').innerText(),'Student Directory');
        await page.evaluate(()=>{document.getElementById('eduosSessionShield').hidden=false;});
        await page.waitForFunction(() => !document.getElementById('pageHelpDrawer').open);
        assert.equal(await page.locator('#pageHelpContent').innerText(),'');
        await page.evaluate(()=>{document.getElementById('eduosSessionShield').hidden=true;});
        await page.locator('#pageContextHelp').click();
        await page.locator('#pageHelpDrawer[open]').waitFor();
        await page.evaluate(()=>{document.body.dataset.authTenantId='23';});
        await page.waitForFunction(() => !document.getElementById('pageHelpDrawer').open);
        assert.equal(await page.locator('#pageContextHelp').isDisabled(),true);
        assert.equal(await page.locator('#pageHelpContent').innerText(),'');
        assert.deepEqual(errors,[]);
    } finally { await context.close(); }
}
async function mobileDenied(browser) {
    const {context,page,errors}=await setup(browser,true,true);
    try {
        await page.evaluate(()=>{window.rejectVerification=true;});
        await page.locator('#pageContextHelp').click();
        await page.waitForTimeout(25);
        assert.equal(await page.locator('#pageHelpDrawer').evaluate(e=>e.open),false);
        assert.equal(await page.locator('#pageHelpContent').innerText(),'');
        await page.evaluate(()=>{window.rejectVerification=false;});
        await page.locator('#pageContextHelp').click();
        await page.locator('#pageHelpDrawer[open]').waitFor();
        assert.equal(await page.locator('#pageHelpTitle').innerText(),'শিক্ষার্থী তালিকা');
        const box=await page.locator('#pageHelpDrawer').boundingBox();
        assert.ok(box && box.x>=0 && box.x+box.width<=391, 'mobile drawer exceeds viewport');
        await page.evaluate(()=>{window.transitioning=true;});
        await page.evaluate(()=>{window.dispatchEvent(new Event('focus'));});
        await page.waitForFunction(() => !document.getElementById('pageHelpDrawer').open);
        assert.equal(await page.locator('#pageContextHelp').isDisabled(),true);
        assert.deepEqual(errors,[]);
    } finally { await context.close(); }
}
(async()=>{
    const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
    try { await normal(browser); await mobileDenied(browser);
        console.log('Page Help Chromium PASS: bilingual sections, authorized related links, keyboard focus, locale switch, session shield, tenant change, denied verification and mobile (synthetic session).');
    } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1;});
