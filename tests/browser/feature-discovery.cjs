'use strict';
// Actual production feature-discovery.js in Chromium; synthetic authorized menu/session,
// not a logged-in SQL-backed browser test or institutional UAT.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../..');
const scriptPath = path.join(root, 'EduOS.App/wwwroot/js/platform/feature-discovery.js');
const sidebarScriptPath = path.join(root, 'EduOS.App/wwwroot/js/sidebar-navigation.js');
const layout = fs.readFileSync(path.join(root, 'EduOS.App/Views/Shared/_Layout.cshtml'), 'utf8');
for (const id of ['sidebarMenu','featureSearchOpen','featureSearchDialog','featureSearchInput','featureSearchResults','featureSearchClose'])
    assert(layout.includes('id="' + id + '"'), 'Shared Razor layout missing ' + id);
const session = 'A'.repeat(64);
function html(bn, tenant) {
    return '<!doctype html><html lang="' + (bn?'bn-BD':'en-BD') + '"><head><meta charset="utf-8"></head>' +
        '<body data-auth-tenant-id="' + tenant + '" data-auth-session-reference="' + session + '">' +
        '<div id="eduosSessionShield" hidden></div>' +
        '<aside id="sidebarMenu"><div class="menu-title">Academics</div>' +
        '<nav data-sidebar-section="academics"><a class="nav-link" href="/Students/Index" aria-current="page"><span>Students</span></a>' +
        '<a class="nav-link" href="/FeeOperations/Index"><span>'+(bn?'ফি ও বিলিং':'Fee collection')+'</span></a>' +
        '<a class="nav-link" href="/Admissions/Index"><span>Admissions</span></a></nav>' +
        '<div class="menu-title">Management</div><nav data-sidebar-section="management">' +
        '<a class="nav-link" href="/PayrollOperations/Index"><span>Payroll</span></a>' +
        '<a class="nav-link" href="/AccountingReports/Index"><span>Accounting Reports</span></a>' +
        '<a class="nav-link" href="/AssetLifecycle/Index"><span>Fixed Assets</span></a>' +
        '<a class="nav-link" href="/AuditLogs/Index" aria-label="Audit trail"><svg aria-hidden="true"></svg></a>' +
        '</nav></aside>' +
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
    // Match production _Layout script order: sidebar translation/grouping first.
    await page.addScriptTag({path:sidebarScriptPath});
    await page.addScriptTag({path:scriptPath});
    return {context,page,errors};
}
async function english(browser) {
    const {context,page,errors}=await setup(browser);
    try {
        await page.keyboard.press('Control+k');
        await page.locator('#featureSearchDialog[open]').waitFor();
        assert.equal(await page.locator('#featureSearchInput').evaluate(e=>document.activeElement===e),true);
        await page.locator('#featureSearchInput').fill('Audit trail');
        assert.match(await page.locator('#featureSearchResults').innerText(),/Audit trail/);
        await page.locator('#featureSearchInput').fill('Payroll');
        assert.match(await page.locator('#featureSearchResults').innerText(),/Payroll/);
        await page.locator('#featureSearchInput').fill('fee');
        await page.getByRole('button',{name:'Add to favorites: Fee collection'}).click();
        assert.equal(await page.getByRole('button',{name:'Remove from favorites: Fee collection'}).getAttribute('aria-pressed'),'true');
        await page.locator('#featureSearchInput').fill('');
        assert.match(await page.locator('#featureSearchResults').innerText(),/Favorites/);
        await page.locator('#featureSearchInput').fill('student');
        await page.locator('#featureSearchResults a.feature-search-link[href="/Students/Index"]').click();
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
        assert.equal((await page.locator('#sidebarMenu a[href="/Students/Index"]').textContent()).trim(),'শিক্ষার্থী তালিকা');
        for (const [query, expected] of [
            ['বেতন', 'বেতন ব্যবস্থাপনা'],
            ['স্থায়ী সম্পদ', 'স্থায়ী সম্পদ ও রক্ষণাবেক্ষণ'],
            ['নিরীক্ষার লগ', 'নিরীক্ষার লগ'],
            ['ফি', 'ফি ও বিলিং']
        ]) {
            await page.locator('#featureSearchInput').fill(query);
            assert.match(await page.locator('#featureSearchResults').innerText(),new RegExp(expected));
        }
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

async function verificationFailure(browser) {
    // Missing verifier, rejected verifier, and absent tenant context are all
    // fail-closed without navigating or leaving stale authorized links visible.
    const first=await setup(browser,false,'22',false);
    try {
        await first.page.locator('#featureSearchOpen').click();
        await first.page.locator('#featureSearchDialog[open]').waitFor();
        await first.page.evaluate(()=>{window.EduOSVerifySessionContext=undefined;});
        await first.page.locator('#featureSearchInput').fill('fee');
        assert.equal(await first.page.locator('#featureSearchDialog').evaluate(e=>e.open),false);
        assert.equal(await first.page.locator('#featureSearchOpen').isDisabled(),true);
        assert.equal(await first.page.locator('#featureSearchResults').innerText(),'');
        assert.deepEqual(first.errors,[]);
    } finally { await first.context.close(); }

    const second=await setup(browser,false,'22',false);
    try {
        await second.page.locator('#featureSearchOpen').click();
        await second.page.locator('#featureSearchDialog[open]').waitFor();
        await second.page.locator('#featureSearchInput').fill('student');
        await second.page.evaluate(()=>{
            window.EduOSVerifySessionContext=async()=>{throw Error('Session revoked');};
        });
        await second.page.locator('#featureSearchResults a.feature-search-link[href="/Students/Index"]').click();
        assert.equal(new URL(second.page.url()).pathname,'/Dashboard/Index');
        assert.equal(await second.page.locator('#featureSearchDialog').evaluate(e=>e.open),false);
        assert.equal(await second.page.locator('#featureSearchOpen').isDisabled(),true);
        assert.equal(await second.page.locator('#featureSearchResults').innerText(),'');
        assert.deepEqual(second.errors,[]);
    } finally { await second.context.close(); }

    const third=await setup(browser,true,'',true);
    try {
        await third.page.locator('#featureSearchOpen').click();
        assert.equal(await third.page.locator('#featureSearchDialog').evaluate(e=>e.open),false);
        assert.equal(await third.page.locator('#featureSearchOpen').isDisabled(),true);
        assert.deepEqual(third.errors,[]);
    } finally { await third.context.close(); }
}

(async()=>{
    const browser=await chromium.launch({headless:true,args:['--no-sandbox']});
    try { await english(browser); await isolation(browser); await verificationFailure(browser);
        console.log('Feature discovery Chromium PASS: canonical sidebar labels across Academics/Finance/Payroll/Assets/Audit, icon-only accessible links, keyboard, bilingual mobile, favorites, verified navigation and tenant isolation (synthetic session/menu).');
    } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exitCode=1;});
