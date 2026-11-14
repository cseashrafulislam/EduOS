'use strict';
// Real ASP.NET MVC + cookie-authenticated Playwright QA on disposable SQL Server.
// Not a mocked page. Never run against a production URL or account.
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const base = process.env.EDUOS_DASHBOARD_BASE_URL || 'http://127.0.0.1:5089';
const adminEmail = process.env.EDUOS_DASHBOARD_SUPERADMIN_EMAIL;
const adminPassword = process.env.EDUOS_DASHBOARD_SUPERADMIN_PASSWORD;
const tenantPassword = process.env.EDUOS_DASHBOARD_TENANT_PASSWORD;
if (!/^http:\/\/127\.0\.0\.1:\d+$/.test(base) || !adminEmail || !adminPassword || !tenantPassword) {
    console.error('Dashboard QA requires loopback-only real app and disposable credentials.');
    process.exit(2);
}
const failures = [];
const browserErrors = [];
async function login(context, email, password) {
    // Use the actual Login Razor page and site.js anti-forgery fetch wrapper.
    // Calling the API without a page-issued CSRF token correctly returns 400.
    const page = await context.newPage();
    try {
        const response = await page.goto(base + '/Account/Login', {waitUntil:'domcontentloaded'});
        assert.equal(response?.status(), 200, 'Public login page did not render');
        assert.ok(await page.locator('meta[name="request-verification-token"]').getAttribute('content'),
            'CSRF token must come from the real public layout');
        await page.locator('#userNameOrEmail').fill(email);
        await page.locator('#password').fill(password);
        await page.locator('#loginBtn').click();
        try {
            await page.waitForURL(url => /^\/Dashboard(?:\/|$)/i.test(url.pathname), {timeout:15000});
        } catch {
            const notice=await page.locator('#alertContainer').innerText().catch(()=>'(no alert)');
            throw new Error('Login UI did not establish dashboard session: ' + notice.slice(0,240));
        }
        return {redirectUrl:new URL(page.url()).pathname};
    } finally { await page.close(); }
}
async function visit(page, route) {
    const response = await page.goto(base + route, {waitUntil:'domcontentloaded',timeout:30000});
    assert.ok(response, 'Missing HTTP response for ' + route);
    return response;
}
async function run(name, fn) {
    try { await fn(); console.log('PASS:', name); }
    catch (e) { failures.push(name); console.error('FAIL:', name, e.stack || e); }
}
(async () => {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        await run('anonymous cannot access SuperAdmin metrics', async () => {
            const ctx = await browser.newContext();
            try {
                const page = await ctx.newPage();
                const res = await visit(page, '/Dashboard/Admin');
                if (res.status() === 200)
                    assert.match(new URL(page.url()).pathname, /^\/(Account\/Login|Error)/,
                        'Anonymous requests may render only a login/error page');
                assert.equal(await page.locator('#platformDashboard').count(), 0);
            } finally { await ctx.close(); }
        });
        for (const [mobile, bn] of [[false, false], [true, true]]) {
            await run('authenticated SuperAdmin platform dashboard ' + (bn ? 'Bangla/mobile' : 'English/desktop'), async () => {
                const ctx = await browser.newContext({
                    viewport: mobile ? {width:390,height:844} : {width:1366,height:850},
                    serviceWorkers:'block'
                });
                try {
                    const person = await login(ctx, adminEmail, adminPassword);
                    assert.match(person.redirectUrl || '', /\/Dashboard\/Admin/);
                    await ctx.addCookies([{
                        name:'.AspNetCore.Culture',
                        value:bn?'c=bn-BD|uic=bn-BD':'c=en-BD|uic=en-BD',
                        url:base
                    }]);
                    const page = await ctx.newPage();
                    page.on('pageerror', error => browserErrors.push(error.message));
                    const res = await visit(page, '/Dashboard/Admin');
                    assert.equal(res.status(), 200);
                    const heading = await page.locator('h1').first().innerText();
                    assert.match(heading, bn ? /প্ল্যাটফর্ম/ : /Platform Operations/);
                    assert.equal(await page.locator('#platformDashboard .dashboard-stat-card').count(), 8);
                    assert.equal(await page.locator('#platformDashboard .platform-tool-card a').count(), 4);
                    assert.equal(await page.locator('#platformRefresh').count(), 0);
                    assert.equal(await page.locator('#platformDashboard .dashboard-hero').count(), 0);
                    assert.equal(await page.locator('#platformRecentTenants').count(), 0,
                        'Removed legacy JS-only table identifier must not be required');
                    assert.equal(await page.locator('#platformDashboard table').count(), 2);
                    assert.equal(await page.locator('#platformDashboard time[datetime]').count() >= 1, true);
                    const anchors = await page.locator('#platformDashboard .platform-tool-card a').evaluateAll(els =>
                        els.map(x => x.pathname));
                    assert.deepEqual(anchors, [
                        '/PlatformTenants/Index', '/Account/PaymentVerification',
                        '/SubscriptionPaymentHistory/Index', '/SubscriptionPlanAdministration/Index'
                    ]);
                    assert.ok((await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)) <= 5,
                        'Horizontal overflow in ' + (mobile ? 'mobile' : 'desktop'));
                    const api = await ctx.request.get(base + '/api/dashboard/platform', {failOnStatusCode:false});
                    assert.equal(api.status(), 200);
                    assert.match(api.headers()['cache-control'] || '', /no-store/);
                    const payload = await api.json();
                    assert.equal(payload.success, true);
                    assert.ok(payload.data.totalTenants >= 2, 'Disposable system + demo tenant expected');
                    assert.equal(Number(await page.locator('#platformDashboard .dashboard-stat-card strong').first().innerText()),
                        payload.data.totalTenants, 'Displayed total must equal authoritative API count');
                    const help = page.locator('#pageContextHelp');
                    assert.equal(await help.count(), 1, 'One canonical Page Help control');
                    await help.click();
                    await page.locator('#pageHelpDrawer[open]').waitFor({timeout:7000});
                    assert.match(await page.locator('#pageHelpTitle').innerText(), bn ? /ড্যাশবোর্ড/ : /Dashboard/);
                    assert.equal(await page.locator('#pageHelpContent').innerText().then(x=>/Refresh metrics/.test(x)), false);
                    await page.keyboard.press('Escape');
                    assert.equal(await page.locator('#pageAssistantOpen').count(), 1, 'One shared Page Assistant');
                    assert.equal(await page.locator('#pageAssistantVoice').count(), 1, 'Shared Voice entry only');
                    await page.evaluate(() => { document.documentElement.dataset.workspaceTheme = 'dark'; });
                    assert.ok((await page.locator('#platformDashboard').evaluate(el =>
                        getComputedStyle(el).display)).length > 0);
                } finally { await ctx.close(); }
            });
        }
        for (const role of ['TenantAdmin', 'Teacher', 'Student']) {
            await run('real tenant dashboard & SuperAdmin isolation for ' + role, async () => {
                const ctx = await browser.newContext({
                    viewport: role === 'Student' ? {width:390,height:844} : {width:1366,height:850},
                    serviceWorkers:'block'
                });
                try {
                    const email='demo.'+role.toLowerCase()+'@eduos.example.invalid';
                    await login(ctx,email,tenantPassword);
                    const page = await ctx.newPage();
                    page.on('pageerror',error=>browserErrors.push(error.message));
                    const index=await visit(page,'/Dashboard/Index');
                    assert.equal(index.status(),200,'Institution dashboard should render');
                    assert.equal(await page.locator('#dashboardWelcome').count(),1);
                    assert.equal(await page.locator('h1').count()>=1,true);
                    assert.equal(await page.locator('#platformDashboard').count(),0);
                    assert.ok((await page.evaluate(()=>document.documentElement.scrollWidth-innerWidth))<=5,
                        'Tenant dashboard mobile overflow');
                    const denied=await ctx.request.get(base+'/api/dashboard/platform',{failOnStatusCode:false});
                    if (denied.status() !== 403) {
                        const denialBody = await denied.text();
                        console.error('Platform API denial status mismatch', {
                            status:denied.status(), contentType:denied.headers()['content-type'],
                            url:denied.url(), bodyPreview:denialBody.slice(0,160)
                        });
                    }
                    assert.equal(denied.status(),403,'Tenant cannot read platform-wide metrics');
                    const attempted=await ctx.request.get(base+'/Dashboard/Admin',{failOnStatusCode:false});
                    if (attempted.status() === 200)
                        assert.match(new URL(attempted.url()).pathname, /^\/(Account\/AccessDenied|Error)/,
                            'Authorized tenant must not see the platform page even after redirect');
                    assert.doesNotMatch(await attempted.text(), /id="platformDashboard"/);
                    const api=await ctx.request.get(base+'/api/dashboard',{failOnStatusCode:false});
                    assert.equal(api.status(),200);
                    assert.match(api.headers()['cache-control']||'',/no-store/);
                    const result=await api.json();
                    assert.equal(result.success,true);
                    assert.equal(result.data.institutionName,'EduOS Demonstration Institute');
                    if(role!=='TenantAdmin'){
                        assert.equal(result.data.financialSummaryAvailable,false);
                        assert.equal(result.data.monthlyCollection,null);
                        assert.equal(result.data.totalDues,null);
                    }
                } finally { await ctx.close(); }
            });
        }
        if (browserErrors.length) failures.push('Browser JavaScript exceptions: '+browserErrors.slice(0,6).join('; '));
        if(failures.length) {
            console.error('Dashboard real-browser QA failed: '+failures.join(' | '));
            process.exitCode=1;
        } else console.log('Dashboard real-browser QA PASS: real SQL data, cookie login, two routes, roles, API parity, desktop/mobile, both locales, page help and shared Assistant.');
    } finally { await browser.close(); }
})().catch(e => {console.error(e.stack||e);process.exitCode=1;});
