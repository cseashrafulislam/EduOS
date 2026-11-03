'use strict';
// Real production Branding Setup JS in Chromium, isolated synthetic tenant/API.
// Browser lifecycle proof only; not authenticated SQL-backed staging or actual BFCache restoration.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const source = fs.readFileSync(path.resolve(__dirname,
    '../../EduOS.App/wwwroot/js/account/branding-setup.js'), 'utf8');

function html(lang) {
    const ids = ['alertContainer','subdomainInput','availabilityMsg','saveSubdomainBtn',
        'saveBrandingBtn','retryBrandingLoadBtn','logoPreview','faviconPreview',
        'logoInput','faviconInput','uploadLogoBtn','uploadFaviconBtn',
        'removeLogoBtn','removeFaviconBtn','primaryColor','primaryColorText',
        'secondaryColor','secondaryColorText','accentColor','accentColorText'];
    const nodes = ids.map(id => {
        if (id === 'logoInput' || id === 'faviconInput') return '<input id="' + id + '" type="file" disabled>';
        if (id.endsWith('Color') || id.endsWith('ColorText') || id === 'subdomainInput')
            return '<input id="' + id + '" value="" disabled>';
        if (id === 'availabilityMsg' || id === 'alertContainer') return '<div id="' + id + '"></div>';
        return '<button id="' + id + '" type="button" disabled></button>';
    }).join('');
    const strings = JSON.stringify({
        loadFailed:'Unable to load branding', saving:'Saving', saveAndContinue:'Save and Continue',
        dashboard:'Dashboard', continueSetup:'Continue', subdomainSaved:'Saved',
        logoAlt:'Institute logo', faviconAlt:'Favicon', saveFailed:'Save failed'
    });
    return '<!doctype html><html lang="' + lang + '"><body data-auth-tenant-id="1" ' +
        'data-auth-session-reference="session-1"><div id="eduosSessionShield" hidden></div>' +
        nodes + '<form id="brandingForm"><input name="__RequestVerificationToken" value="synthetic-csrf"></form>' +
        '<script id="brandingSetupStrings" type="application/json">' + strings + '</script>' +
        '<script src="/js/account/branding-setup.js"></script></body></html>';
}

async function run(browser, lang, width) {
    const context = await browser.newContext({ viewport: {width,height:844}, serviceWorkers:'block' });
    const page = await context.newPage();
    let documents = 0, writes = 0;
    await page.addInitScript(() => {
        window.EduOSWorkspaceResponseMatchesSession = response =>
            response.headers.get('X-EduOS-Session-Reference') ===
                document.body.dataset.authSessionReference &&
            response.headers.get('X-EduOS-Tenant-Id') === document.body.dataset.authTenantId;
        window.EduOSSessionTransitionPending = () => false;
        window.EduOSVerifySessionContext = () => {};
        window.eduosOnboardingStageUrl = () => '/Account/BrandingSetup';
    });
    await page.route('http://eduos.test/**', async route => {
        const request = route.request(), url = new URL(request.url());
        if (request.method() !== 'GET') { writes++; return route.abort(); }
        if (url.pathname === '/') {
            documents++;
            return route.fulfill({status:200,contentType:'text/html',
                headers:{'Cache-Control':'no-store'},body:html(lang)});
        }
        if (url.pathname === '/js/account/branding-setup.js')
            return route.fulfill({status:200,contentType:'application/javascript',body:source});
        if (url.pathname === '/api/workspace-preferences')
            return route.fulfill({status:200,contentType:'application/json',
                headers:{'X-EduOS-Session-Reference':'session-1','X-EduOS-Tenant-Id':'1'},
                body:JSON.stringify({success:true,data:{}})});
        if (url.pathname === '/api/tenant-profile')
            return route.fulfill({status:200,contentType:'application/json',
                body:JSON.stringify({success:true,data:{subdomain:'demo-school',rowVersion:'rv1',
                    logoUrl:'/uploads/logo.png',faviconUrl:null}})});
        if (url.pathname === '/api/tenant-settings')
            return route.fulfill({status:200,contentType:'application/json',
                body:JSON.stringify({success:true,data:{items:[]}})});
        if (url.pathname === '/api/onboarding/status')
            return route.fulfill({status:200,contentType:'application/json',
                body:JSON.stringify({success:true,data:{currentStage:8,isComplete:false}})});
        return route.abort();
    });
    try {
        await page.goto('http://eduos.test/', {waitUntil:'domcontentloaded'});
        await page.waitForFunction(() => document.getElementById('subdomainInput').value === 'demo-school' &&
            !document.getElementById('saveBrandingBtn').disabled);
        assert.equal(await page.locator('#logoPreview img').count(), 1);
        await page.evaluate(() => window.dispatchEvent(new PageTransitionEvent('pagehide')));
        assert.equal(await page.locator('#subdomainInput').inputValue(), '');
        assert.equal(await page.locator('#saveBrandingBtn').isDisabled(), true);
        assert.equal(await page.locator('#uploadLogoBtn').isDisabled(), true);
        assert.equal(await page.locator('#logoPreview img').count(), 0);
        assert.equal(await page.locator('#alertContainer').innerText(), '');
        await page.locator('#brandingForm').evaluate(form =>
            form.dispatchEvent(new Event('submit', {bubbles:true,cancelable:true})));
        assert.equal(writes, 0);
        const nextDocument = page.waitForEvent('framenavigated', {timeout:10000});
        await page.evaluate(() => window.dispatchEvent(new PageTransitionEvent('pageshow', {persisted:true})));
        await nextDocument;
        await page.waitForFunction(() => document.getElementById('subdomainInput').value === 'demo-school');
        assert.equal(documents, 2, 'BFCache return must fetch a fresh document and token');
        assert.equal(writes, 0);
    } finally { await context.close(); }
}
(async () => {
    const browser = await chromium.launch({headless:true,args:['--no-sandbox']});
    try {
        await run(browser,'en-BD',1280);
        await run(browser,'bn-BD',390);
        console.log('Branding Setup Chromium PASS: desktop/mobile lifecycle clearing and fresh-document restore.');
    } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
