'use strict';
// Read-only real Browser + SQL Server tenant-role navigation acceptance.
// Never use production URLs, real user data, or mutate business records.
const fs = require('node:fs');
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const origin = 'https://qa.eduos.test:7243';
const password = process.env.QA_DEMO_PASSWORD || '';
assert.equal(process.env.EDUOS_QA_ORIGIN, origin);
assert(/^[0-9a-f]{40}$/i.test(process.env.GITHUB_SHA || ''));
assert(password.length >= 16, 'Random synthetic credentials required');

function routePath(href) {
    if (!href || !href.startsWith('/') || href.startsWith('//')) return null;
    const u = new URL(href, origin);
    if (u.origin !== origin || /%(?:00|2e|2f|5c)/i.test(u.pathname) ||
        /^\/(?:api|account\/logout)/i.test(u.pathname) ||
        /\/(?:delete|remove|purge|post|approve|reject|logout|submit)(?:\/|$)/i.test(u.pathname))
        return null;
    return u.pathname + u.search;
}
async function signIn(page, account) {
    const response = await page.goto(origin+'/Account/Login', {waitUntil:'domcontentloaded'});
    assert(response && response.status() === 200);
    await page.locator('#userNameOrEmail').fill(account);
    await page.locator('#password').fill(password);
    await page.locator('#loginBtn').click();
    await page.waitForURL(url => url.pathname.toLowerCase() !== '/account/login',
        {timeout:25000});
}
async function main() {
    const browser = await chromium.launch({headless:true,args:['--no-sandbox']});
    const results = [];
    try {
        // Match all thirteen roles seeded by DemoInstitutionSeeder. Each
        // identity is in the disposable EDUOS-DEMO tenant and never production.
        for (const role of [
            'TenantAdmin', 'Admin', 'AdmissionOfficer', 'Principal',
            'VicePrincipal', 'Teacher', 'Student', 'Parent', 'Accountant',
            'HR', 'Librarian', 'ExamController', 'Staff'
        ]) {
            const account = 'demo.' + role.toLowerCase() + '@eduos.example.invalid';
            const ctx = await browser.newContext({
                ignoreHTTPSErrors:true,serviceWorkers:'block',
                acceptDownloads:false,viewport:{width:1440,height:900}
            });
            await ctx.route('**/*', route =>
                new URL(route.request().url()).origin === origin
                    ? route.continue() : route.abort('blockedbyclient'));
            const page = await ctx.newPage();
            await signIn(page,account);
            await page.locator('#sidebarMenu').waitFor({timeout:15000});
            const links = await page.locator('#sidebarMenu nav a[href]').evaluateAll(items =>
                items.filter(a => a.getAttribute('href')?.startsWith('/') &&
                    !a.closest('[hidden],[aria-hidden="true"],.d-none'))
                    .map(a => a.getAttribute('href')));
            const paths = [...new Set(links.map(routePath).filter(Boolean))];
            assert(paths.length >= (role === 'TenantAdmin' ? 8 : 1),
                'No authorized routes for ' + role);
            const checks = [];
            for (const path of paths) {
                try {
                    const response = await ctx.request.get(origin + path, {
                        timeout: 15000,maxRedirects:6,failOnStatusCode:false
                    });
                    const dest = new URL(response.url());
                    const ok = response.status() === 200 && dest.origin === origin &&
                        !/\/account\/(?:login|accessdenied|unauthorized)/i.test(dest.pathname) &&
                        /text\/html/i.test(response.headers()['content-type'] || '');
                    checks.push({path,status:response.status(),passed:ok,
                        cacheWarning:!/no-store/i.test(response.headers()['cache-control'] || '')});
                } catch (e) {
                    checks.push({path,status:0,passed:false,error:String(e.message).slice(0,100)});
                }
            }
            results.push({role,discovered:paths.length,checked:checks.length,
                passed:checks.filter(x=>x.passed).length,
                failed:checks.filter(x=>!x.passed).length,pages:checks});
            await ctx.close();
        }
        fs.mkdirSync('qa-artifacts',{recursive:true});
        fs.writeFileSync('qa-artifacts/real-menu-route-sweep.json',JSON.stringify({
            sha:process.env.GITHUB_SHA,environment:'disposable-ci',
            scope:'server-rendered role sidebar GET routes only, not all 204 canonical routes',
            results
        },null,2));
        const failures = results.flatMap(x=>x.pages.filter(p=>!p.passed)
            .map(p=>x.role+' '+p.path+' HTTP '+p.status));
        for (const role of results) console.log('[QA route] '+role.role+' '+role.passed+'/'+role.checked);
        assert.equal(results.length, 13, 'Every synthetic institution role must be checked');
        if (failures.length)
            throw Error('Failing authorized route GETs: '+failures.slice(0,30).join('; '));
        console.log('PASS real role-specific sidebar page GET sweep.');
    } finally {await browser.close();}
}
main().catch(e=>{console.error('Real menu QA failed: '+e.message);process.exitCode=1;});
