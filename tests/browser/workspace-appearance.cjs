'use strict';
// Production site.js + Appearance Studio editor in real Chromium with synthetic,
// isolated preference API/session fixtures. No authenticated server, SQL, or UAT.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright');
const root = path.resolve(__dirname, '../..');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/WorkspacePreferences/Index.cshtml'), 'utf8');
const editor = path.join(root, 'EduOS.App/wwwroot/js/platform/workspace-preferences.js');
const shell = path.join(root, 'EduOS.App/wwwroot/js/site.js');
for (const id of ['workspacePreferencesForm', 'workspaceSave', 'workspaceDiscard',
    'workspaceReset', 'workspaceHistoryList', 'workspaceDashboardPanels', 'workspaceStatus'])
    assert(view.includes('id="' + id + '"'), 'Razor appearance contract missing ' + id);
const session = 'A'.repeat(64);
const panels = ['capacity', 'quick-actions', 'setup-checklist'];
const colors = ['sidebar', 'header', 'page', 'card', 'button', 'form', 'table'];
const tables = {
    'institution-users': ['user', 'membership', 'roles', 'action'],
    'institution-invitations': ['email', 'role', 'campus', 'expires', 'status', 'action'],
    'institution-feature-audit': ['feature', 'code', 'plan', 'override', 'restriction', 'reason'],
    'institution-activity-audit': ['time', 'user', 'action', 'entity', 'record', 'ip', 'outcome'],
    'institution-campuses': ['campus', 'code', 'contact', 'type', 'action'],
    'institution-change-history': ['when', 'action', 'fields', 'actor'],
    'institution-settings': ['key', 'category', 'value', 'protection', 'action'],
    'institution-permission-exceptions': ['permission', 'mode', 'reason', 'expires', 'action']
};
const defaultTables = () => Object.entries(tables).map(([id, ids]) => ({
    id, columns: ids.map(id => ({ id, visible: true }))
}));
function preferences() {
    return { theme: 'light', palette: 'blue', navigation: 'left', density: 'comfortable',
        fontScale: 'normal', customColors: {sidebar:'#123456'},
        dashboardPanels: panels.map(id => ({id, visible:true})),
        tablePreferences: defaultTables(), rowVersion:'v1',
        history: [{revision:1,savedAtUtc:'2026-10-31T10:00:00Z'}] };
}
function markup(lang, tenant) {
    const bn = lang === 'bn-BD';
    const select = (name, options) => '<select name="' + name + '" id="workspace' +
        name[0].toUpperCase() + name.slice(1) + '">' +
        options.map(x => '<option value="' + x + '">' + x + '</option>').join('') + '</select>';
    const colorRows = colors.map(token => '<div data-custom-color="' + token + '">' +
        '<input data-custom-color-text aria-label="' + token + ' color">' +
        '<input data-custom-color-picker type="color" value="#123456" aria-label="' + token + ' picker">' +
        '<button type="button" data-custom-color-clear>Clear</button>' +
        '<small data-custom-color-feedback></small></div>').join('');
    const panelRows = panels.map(id => '<div data-panel-row="' + id + '">' +
        '<input type="checkbox" checked><button type="button" data-panel-move="up">Up</button>' +
        '<button type="button" data-panel-move="down">Down</button></div>').join('');
    return '<!doctype html><html lang="' + lang + '"><head><meta charset="utf-8">' +
        '<meta name="request-verification-token" content="fixture-csrf"></head>' +
        '<body data-auth-tenant-id="' + tenant + '" data-auth-session-reference="' + session +
        '" data-workspace-preferences="enabled">' +
        '<form id="workspacePreferencesForm"><input type="hidden" name="__RequestVerificationToken" value="fixture-csrf">' +
        select('theme',['system','light','dark']) + select('palette',['blue','teal','violet']) +
        select('navigation',['left','right','top']) + select('density',['comfortable','compact']) +
        select('fontScale',['normal','large']) + colorRows +
        '<div id="workspaceDashboardPanels">' + panelRows + '</div>' +
        '<button id="workspaceSave" type="submit">' + (bn?'ডিজাইন সংরক্ষণ':'Save appearance') + '</button>' +
        '<button id="workspaceDiscard" type="button" disabled>' + (bn?'প্রিভিউ বাতিল':'Discard preview') + '</button>' +
        '<button id="workspaceReset" type="button">Reset</button>' +
        '<ol id="workspaceHistoryList"></ol><p id="workspaceStatus" role="status" aria-live="polite"></p>' +
        '</form></body></html>';
}
async function setup(browser, lang='en-BD', mobile=false, wrongTenant=false) {
    const context = await browser.newContext({ viewport: mobile ? {width:390,height:844} : {width:1280,height:800} });
    const page = await context.newPage(), errors = [], requests = [];
    let current = preferences(), nextConflict = false;
    page.on('pageerror', e => errors.push(e.message));
    await page.addInitScript(() => {
        window.EduOSSessionTransitionPending = () => Boolean(window.transitioning);
        window.EduOSVerifySessionContext = () => { window.verifyCount = (window.verifyCount || 0) + 1; };
    });
    await page.route('http://eduos.test/**', async route => {
        const req = route.request(), url = new URL(req.url());
        if (url.pathname !== '/api/workspace-preferences' &&
            url.pathname !== '/api/workspace-preferences/reset' &&
            url.pathname !== '/api/workspace-preferences/restore') {
            await route.fulfill({contentType:'text/html; charset=utf-8', body:markup(lang,'22')});
            return;
        }
        const method = req.method();
        const csrf = req.headers()['requestverificationtoken'];
        requests.push({method, path:url.pathname, csrf});
        const headers = {'X-EduOS-Session-Reference':session,
            'X-EduOS-Tenant-Id':wrongTenant?'99':'22'};
        if (method !== 'GET' && csrf !== 'fixture-csrf') {
            await route.fulfill({status:400,headers,contentType:'application/json',
                body:JSON.stringify({success:false,message:'CSRF missing'})});
            return;
        }
        if (nextConflict) {
            nextConflict = false;
            await route.fulfill({status:409,headers,contentType:'application/json',
                body:JSON.stringify({success:false,message:'Stale rowversion'})});
            return;
        }
        if (method !== 'GET') {
            const data = JSON.parse(req.postData() || '{}');
            if (data.rowVersion !== current.rowVersion) {
                await route.fulfill({status:409,headers,contentType:'application/json',
                    body:JSON.stringify({success:false,message:'Stale rowversion'})});
                return;
            }
            if (url.pathname.endsWith('/restore')) {
                current = {...current, theme:'light', customColors:{sidebar:'#123456'},
                    rowVersion:'v3', history:[...current.history,{revision:3,savedAtUtc:'2026-11-01T10:00:00Z'}]};
            } else if (url.pathname.endsWith('/reset')) {
                current = {...current, theme:'system', customColors:{}, rowVersion:'v4'};
            } else {
                current = {...current, ...data, rowVersion:'v2',
                    history:[...current.history,{revision:2,savedAtUtc:'2026-11-01T09:00:00Z'}]};
            }
        }
        await route.fulfill({status:200,headers,contentType:'application/json',
            body:JSON.stringify({success:true,data:current})});
    });
    await page.goto('http://eduos.test/workspace');
    await page.addScriptTag({path:shell});
    await page.addScriptTag({path:editor});
    await page.waitForFunction(() => document.getElementById('workspaceStatus').textContent.length > 0);
    return {context,page,errors,requests,conflict:()=>{nextConflict=true;}};
}
async function editorFlow(browser) {
    const {context,page,errors,requests,conflict} = await setup(browser);
    try {
        assert.equal(await page.locator('#workspaceTheme').inputValue(),'light');
        assert.equal(await page.evaluate(()=>document.documentElement.style.getPropertyValue('--edu-custom-sidebar-bg')),'#123456');
        assert.equal(await page.locator('[data-appearance-restore]').count(),1);
        await page.locator('#workspaceTheme').selectOption('dark');
        assert.equal(await page.evaluate(()=>document.documentElement.dataset.workspaceTheme),'dark');
        assert.equal(await page.locator('#workspaceDiscard').isEnabled(),true);
        assert.equal(await page.locator('[data-appearance-restore]').first().isDisabled(),true);
        await page.locator('#workspaceDiscard').click();
        assert.equal(await page.locator('#workspaceTheme').inputValue(),'light');
        assert.equal(await page.locator('#workspaceDiscard').isDisabled(),true);
        const color = page.locator('[data-custom-color="button"] [data-custom-color-text]');
        await color.fill('url(javascript:alert(1))');
        assert.equal(await color.getAttribute('aria-invalid'),'true');
        assert.equal(await page.evaluate(()=>document.documentElement.style.getPropertyValue('--edu-custom-button-bg')),'');
        await color.fill('rgb(18,52,86)');
        assert.equal(await page.evaluate(()=>document.documentElement.style.getPropertyValue('--edu-custom-button-bg')),'#123456');
        await page.locator('#workspaceSave').click();
        await page.waitForFunction(() => document.getElementById('workspaceStatus').textContent.includes('Appearance saved'));
        assert.ok(requests.some(r=>r.method==='PUT' && r.csrf==='fixture-csrf'));
        assert.equal(await page.locator('[data-appearance-restore]').count(),2);
        await page.locator('#workspaceTheme').selectOption('dark');
        assert.equal(await page.locator('[data-appearance-restore]').first().isDisabled(),true);
        await page.locator('#workspaceDiscard').click();
        page.once('dialog', dialog=>dialog.accept());
        await page.locator('[data-appearance-restore]').first().click();
        await page.waitForFunction(() => document.getElementById('workspaceTheme').value==='light');
        assert.ok(requests.some(r=>r.path.endsWith('/restore') && r.csrf==='fixture-csrf'));
        conflict();
        await page.locator('#workspaceTheme').selectOption('dark');
        await page.locator('#workspaceSave').click();
        await page.waitForFunction(() => document.getElementById('workspaceTheme').value==='light');
        assert.equal(await page.locator('#workspaceSave').isEnabled(),true);
        assert.deepEqual(errors,[]);
    } finally { await context.close(); }
}
async function mobileAndIsolation(browser) {
    const {context,page,errors,requests} = await setup(browser,'bn-BD',true);
    try {
        assert.match(await page.locator('#workspaceStatus').innerText(),/ডিজাইন/);
        await page.locator('#workspaceTheme').selectOption('dark');
        assert.equal(await page.locator('#workspaceDiscard').isEnabled(),true);
        await page.locator('#workspaceDiscard').click();
        assert.equal(await page.locator('#workspaceTheme').inputValue(),'light');
        await page.evaluate(()=>{document.body.dataset.authTenantId='23';});
        await page.locator('#workspaceReset').click();
        await page.waitForFunction(() => document.getElementById('workspaceSave').disabled);
        assert.equal(await page.evaluate(()=>document.documentElement.style.getPropertyValue('--edu-custom-sidebar-bg')),'');
        assert.equal(await page.locator('[data-appearance-restore]').count(),0);
        assert.ok(await page.evaluate(()=>window.verifyCount)>=1);
        assert.equal(await page.locator('#workspaceSave').isDisabled(),true);
        assert.deepEqual(errors,[]);
        assert.ok(requests.some(r=>r.path.endsWith('/reset')));
    } finally { await context.close(); }
    const denied = await setup(browser,'en-BD',true,true);
    try {
        assert.equal(await denied.page.locator('#workspaceSave').isDisabled(),true);
        assert.equal(await denied.page.locator('#workspaceHistoryList').innerText(),'No previous saved versions.');
        assert.equal(denied.requests.filter(r=>r.method!=='GET').length,0);
        assert.deepEqual(denied.errors,[]);
    } finally { await denied.context.close(); }
}
(async () => {
    const browser = await chromium.launch({headless:true,args:['--no-sandbox']});
    try {
        await editorFlow(browser);
        await mobileAndIsolation(browser);
        console.log('Appearance Chromium PASS: real editor/shell JS, bilingual mobile, color preview/discard, CSRF, version restore, 409 reload and tenant isolation (synthetic API/session).');
    } finally { await browser.close(); }
})().catch(e => { console.error(e); process.exitCode=1; });
