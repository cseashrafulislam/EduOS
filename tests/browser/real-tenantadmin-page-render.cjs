'use strict';
// Browser-render every actual TenantAdmin sidebar route against disposable CI only.
// Pages must render at desktop and mobile viewports without JS crashes.
// Never mutate business data; this is not a Create/Edit/Approve or UAT test.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require('playwright');
const origin = 'https://qa.eduos.test:7243';
const sha = process.env.GITHUB_SHA || '';
const password = process.env.QA_DEMO_PASSWORD || '';
assert.equal(process.env.EDUOS_QA_ORIGIN, origin, 'Disposable localhost-only QA origin');
assert(/^[0-9a-f]{40}$/i.test(sha), 'Require immutable source commit SHA');
assert(password.length >= 16, 'Require ephemeral demo credentials');

function safePath(href) {
    if (!href || !href.startsWith('/') || href.startsWith('//')) return null;
    const url = new URL(href, origin);
    if (url.origin !== origin || /%(?:00|2e|2f|5c)/i.test(url.pathname) ||
        /^\/(?:api|account\/logout)/i.test(url.pathname) ||
        /\/(?:delete|remove|purge|post|approve|reject|logout|submit)(?:\/|$)/i.test(url.pathname))
        return null;
    return url.pathname + url.search;
}
function failedMessage(error) {
    return String(error?.message || error).replace(/https?:\/\/[^\s)]+/g,
        '[redacted-url]').slice(0, 200);
}
async function run() {
    const browser = await chromium.launch({headless:true,args:['--no-sandbox']});
    const result = [];
    try {
        const context = await browser.newContext({
            viewport:{width:1366,height:768},ignoreHTTPSErrors:true,
            serviceWorkers:'block',acceptDownloads:false
        });
        const page = await context.newPage();
        const login = await page.goto(origin+'/Account/Login',{waitUntil:'domcontentloaded'});
        assert(login && login.status() === 200,'Demo login page must be HTTP 200');
        await page.locator('#userNameOrEmail').fill('demo.tenantadmin@eduos.example.invalid');
        await page.locator('#password').fill(password);
        await page.locator('#loginBtn').click();
        await page.waitForURL(url=>url.pathname.toLowerCase() !== '/account/login',
            {waitUntil:'domcontentloaded', timeout:25000});
        await page.locator('#sidebarMenu nav a[href]').first().waitFor({timeout:15000});
        const raw = await page.locator('#sidebarMenu nav a[href]').evaluateAll(elements=>
            elements.filter(x=>x.getAttribute('href')?.startsWith('/') &&
                !x.closest('[hidden],[aria-hidden="true"],.d-none'))
                .map(x=>x.getAttribute('href')));
        const paths = [...new Set(raw.map(safePath).filter(Boolean))];
        assert(paths.length >= 100,'TenantAdmin menu unexpectedly shrank below baseline');
        const mutations = [];
        let activePath = '';
        await context.route('**/*',route=>{
            const req=route.request(),url=new URL(req.url()),method=req.method();
            if(url.origin !== origin) return route.abort('blockedbyclient');
            if (!['GET','HEAD','OPTIONS'].includes(method)) {
                mutations.push({route:activePath,endpoint:url.pathname,method});
                return route.abort('blockedbyclient');
            }
            return route.continue();
        });
        const pageErrors=[];
        page.on('pageerror',error=>pageErrors.push({
            route:activePath,description:failedMessage(error)
        }));
        page.setDefaultTimeout(15000);
        for (const path of paths) {
            activePath=path;
            const startedErrors=pageErrors.length, startedMutations=mutations.length;
            let status=0, redirect='',desktopMain=false,mobileMain=false,emptyMain=false,issue='';
            try {
                await page.setViewportSize({width:1366,height:768});
                const response=await page.goto(origin+path,{
                    waitUntil:'domcontentloaded',timeout:20000
                });
                status=response?.status()||0;
                redirect=new URL(page.url()).pathname;
                // The deliberately standalone encrypted offline reader does
                // not use the authenticated shared Razor shell.
                const contentSelector = path === '/offline-study.html'
                    ? 'main#offlineStudyReader' : 'main#main-content';
                desktopMain=await page.locator(contentSelector).count() === 1;
                if(desktopMain){
                    const details=await page.locator(contentSelector).evaluate(el=>({
                        hasText:(el.innerText||'').trim().length>4,
                        hasInteractive:!!el.querySelector('input,select,table,button,a[href],canvas,svg')
                    }));
                    emptyMain=!details.hasText&&!details.hasInteractive;
                }
                await page.setViewportSize({width:390,height:844});
                mobileMain=await page.locator(contentSelector).isVisible().catch(()=>false);
                await page.waitForTimeout(120);
            } catch(error) {
                issue=failedMessage(error);
            }
            const errors=pageErrors.slice(startedErrors);
            const unauthorized=mutations.slice(startedMutations);
            const destDenied=/^\/account\/(?:login|accessdenied|unauthorized)/i.test(redirect);
            const passed=status===200&&!destDenied&&desktopMain&&mobileMain&&
                !emptyMain&&!issue&&!errors.length&&!unauthorized.length;
            result.push({path,status,redirect:redirect!==new URL(origin+path).pathname?redirect:undefined,
                desktopMain,mobileMain,emptyMain,errors,blockedMutations:unauthorized,
                passed,error:issue||undefined});
            if(!passed) console.error('[PAGE QA FAIL]',path,'HTTP',status,
                'JS errors',errors.length,'blockedWrites',unauthorized.length,
                'blank',emptyMain,issue.slice(0,90));
        }
        fs.mkdirSync('qa-artifacts',{recursive:true});
        fs.writeFileSync('qa-artifacts/real-tenantadmin-page-render.json',JSON.stringify({
            sha,environment:'GitHub runner disposable Staging SQL',
            role:'TenantAdmin',viewports:['1366x768','390x844'],
            scope:'actual accessible menu routes rendered in Chromium, GET-only; not CRUD/UAT',
            checked:result.length,passed:result.filter(x=>x.passed).length,
            failed:result.filter(x=>!x.passed).length,pages:result
        },null,2));
        console.log('[PAGE QA SUMMARY]',result.filter(x=>x.passed).length+'/' +result.length,
            'TenantAdmin routes rendered at desktop/mobile');
        assert.equal(result.length,paths.length,'Every discovered menu route must be visited');
        const failures=result.filter(x=>!x.passed);
        if(failures.length) throw Error('Browser-render issues: '+failures.slice(0,12)
            .map(x=>x.path+' (HTTP '+x.status+', JS '+x.errors.length+')').join('; '));
        await context.close();
    } finally {await browser.close();}
}
run().catch(error=>{console.error('Full real menu page render QA failed: '+
    failedMessage(error));process.exitCode=1;});
