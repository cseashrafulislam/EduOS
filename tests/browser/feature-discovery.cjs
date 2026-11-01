'use strict';
// Actual production feature-discovery.js in Chromium; synthetic authorized menu/session,
// not a logged-in SQL-backed browser test or institutional UAT.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../..');
const scriptPath = path.join(root, 'EduOS.App/wwwroot/js/platform/feature-discovery.js');
const layout = fs.readFileSync(path.join(root, 'EduOS.App/Views/Shared/_Layout.cshtml'), 'utf8');
for (const id of ['sidebarMenu','featureSearchOpen','featureSearchDialog','featureSearchInput','featureSearchResults','featureSearchClose'])
    assert(layout.includes('id="' + id + '"'), 'Shared Razor layout missing ' + id);
const session = 'A'.repeat(64);
function html(bn, tenant) {
    return '<!doctype html><html lang="' + (bn?'bn-BD':'en-BD') + '"><head><meta charset="utf-8"></head>' +
        '<body data-auth-tenant-id="' + tenant + '" data-auth-session-reference="' + session + '">' +
        '<div id="eduosSessionShield" hidden></div>' +
        '<aside id="sidebarMenu"><div class="menu-title">Academics</div>' +
        '<nav data-sidebar-section="academics"><a class="nav-link" href="/Students/Index" aria-current="page">Students</a>' +
        '<a class="nav-link" href="/FeeOperations/Index">Fee collection</a></nav></aside>' +
        '<button id="featureSearchOpen" type="button">Search features</button>' +
        '<dialog id="featureSearchDialog"><button id="featureSearchClose" type="button">Close</button>' +
        '<input id="featureSearchInput" type="search"><div id="featureSearchResults"></div></dialog>' +
        '</body></html>';
}
async function setup(browser, bn=false, tenant='22', mobile=false) {
    const context = await browser.newContext({viewport:mobile?{width:390,height:844}:{width:1280,height:800}});
    const page = await context.newPage(), errors=[];
    page.on('pageerror', e => errors.push(e.message));
    await page.addInitScript(() => {
        window.verifyCount=0;
        window.EduOSSessionTransitionPending=()=>Boolean(window.transitioning);
        window.EduOSVerifySessionContext=async()=>{
            window.verifyCount++;
            const shield=document.getElementById('eduosSessionShield');
            shield.hidden=false;
            await new Promise(resolve=>setTimeout(resolve,5));
            if(!window.keepShield)shield.hidden=true;
        };
    });
    await page.route('http://eduos.test/**', route => route.fulfill({contentType:'text/html; charset=utf-8',body:html(bn,tenant)}));
    await page.goto('http://eduos.test/Dashboard/Index');
    await page.addScriptTag({path:scriptPath});
    return {context,page,errors};
}
async function english(browser) {
    const {context,page,errors}=await setup(browser);
    try {
        await page.keyboard.press('Control+k');
        await page.locator('#featureSearchDialog[open]').waitFor();
        assert.equal(await page.locator('#featureSearchInput').evaluate(e=>document.activeElement===e),true);
        await page.locator('#featureSearchInput').fill('fee');
        await page.getByRole('button',{name:'Add to favorites: Fee collection'}).click();
        assert.equal(await page.getByRole('button',{name:'Remove from favorites: Fee collection'}).getAttribute('aria-pressed'),'true');
        await page.locator('#featureSearchInput').fill('');
        assert.match(await page.locator('#featureSearchResults').innerText(),/Favorites/);
        await page.locator('#featureSearchInput').fill('student');
        await page.getByRole('link',{name:/Students/}).click();
        await page.waitForURL('**/Students/Index');
        assert.equal(await page.evaluate(()=>window.verifyCount),0); // fresh document after navigation
        assert.deepEqual(errors,[]);
    } finally { await context.close(); }
}
async function isolation(browser) {
    const {context,page,errors}=await setup(browser,true,'22',true);
    try {
        await page.locator('#featureSearchOpen').click();
        await page.locator('#featureSearchDialog[open]').waitFor();
        await page.locator('#featureSearchInput').fill('ফি');
        assert.match(await page.locator('#featureSearchResults').innerText(),/ফি ও বিলিং/);
        await page.evaluate(()=>{document.getElementById('eduosSessionShield').hidden=false;});
        await page.locator('#featureSearchInput').fill('শিক্ষার্থী');
        assert.equal(await page.locator('#featureSearchDialog').evaluate(e=>e.open),false);
        assert.equal(await page.locator('#featureSearchOpen').isDisabled(),false);
        await page.evaluate(()=>{document.getElementById('eduosSessionShield').hidden=true;});
        await page.locator('#featureSearchOpen').click();
        await page.locator('#featureSearchDialog[open]').waitFor();
        await page.evaluate(()=>{document.body.dataset.authTenantId='23';});
        await page.locator('#featureSearchInput').fill('ফি');
        assert.equal(await page.locator('#featureSearchDialog').evaluate(e=>e.open),false);
        assert.equal(await page.locator('#featureSearchOpen').isDisabled(),true);
        assert.equal(await page.locator('#featureSearchResults').innerText(),'');
        assert.deepEqual(errors,[]);
    } finally { await context.close(); }
}
(async()=>{
    const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
    try { await english(browser); await isolation(browser);
        console.log('Feature discovery Chromium PASS: keyboard, bilingual mobile, favorites, verified navigation, session shield and tenant change (synthetic session/menu).');
    } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1;});
