'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const { chromium } = require('playwright');
const origin = 'https://qa.eduos.test:7243';
const password = process.env.QA_DEMO_PASSWORD || '';
const yearName = process.env.QA_YEAR_NAME || '';
const termName = process.env.QA_TERM_NAME || '';
assert.equal(process.env.EDUOS_QA_ORIGIN, origin, 'Only isolated CI origin permitted');
assert(/^[0-9a-f]{40}$/i.test(process.env.GITHUB_SHA || ''), 'Exact commit SHA required');
assert(password.length >= 16 && yearName.startsWith('QA Academic Year ') &&
    termName.startsWith('QA First Term '), 'Synthetic-only test names/password required');

async function login(page, user) {
    const res = await page.goto(origin + '/Account/Login', {waitUntil:'domcontentloaded'});
    assert(res && res.status() === 200, 'Actual login page unavailable');
    await page.locator('#userNameOrEmail').fill(user);
    await page.locator('#password').fill(password);
    // Wait for the actual auth POST, not only for navigation. On a persistent
    // Codespaces demo DB, old Identity hashes or lockout may reject login;
    // reporting HTTP status is actionable and never prints passwords/tokens.
    const loginResponse = page.waitForResponse(response =>
        response.request().method() === 'POST' &&
        new URL(response.url()).pathname === '/api/auth/login',
        {timeout: 25000});
    await page.locator('#loginBtn').click();
    const auth = await loginResponse;
    const result = await auth.json().catch(() => null);
    assert(auth.status() === 200 && result?.success === true,
        'Synthetic demo sign-in rejected: HTTP ' + auth.status() +
        ' (verify isolated demo credentials, lockout, roles, or rate limiting)');
    try {
        await page.waitForURL(url => url.pathname.toLowerCase() !== '/account/login',
            {timeout: 25000});
    } catch {
        const alertType = await page.locator('#alertContainer')
            .getAttribute('class').catch(() => '');
        throw new Error('Demo auth API succeeded, but browser remained on login; ' +
            'alertClass=' + String(alertType).slice(0,60));
    }
    assert(!/MfaChallenge/i.test(page.url()), 'Unexpected MFA for synthetic user');
}
async function academic(page, route, feature) {
    const res = await page.goto(origin + '/AcademicManagement/' + route,
        {waitUntil:'domcontentloaded'});
    assert(res && res.status() === 200, 'Academic endpoint failed: ' + route);
    await page.locator('#academicManagement[data-active-feature="' + feature + '"]').waitFor();
    await page.waitForFunction(() => Boolean(window.eduosAcademicManagement?.state?.catalog),
        null, {timeout: 25000});
}
async function reveal(page, form) {
    if (!(await page.locator('#' + form).isVisible()))
        await page.locator('[aria-controls="' + form + '"]').click();
    await page.locator('#' + form).waitFor({state:'visible'});
}
async function main() {
    const browser = await chromium.launch({headless:true, args:['--no-sandbox']});
    try {
        const ctx = await browser.newContext({ignoreHTTPSErrors:true,
            serviceWorkers:'block', acceptDownloads:false});
        await ctx.route('**/*', route => {
            const url = new URL(route.request().url());
            return url.origin === origin ? route.continue() : route.abort('blockedbyclient');
        });
        const page = await ctx.newPage();
        page.setDefaultTimeout(20000);
        const failures = [];
        page.on('response', res => {
            if (res.request().method() === 'POST' &&
                /^\/api\/institution-onboarding\/academic-(year|term)$/.test(new URL(res.url()).pathname) &&
                res.status() >= 400)
                failures.push({status:res.status(),path:new URL(res.url()).pathname});
        });
        await login(page, 'demo.tenantadmin@eduos.example.invalid');
        await academic(page, 'Years', 'years');
        const initial = await page.evaluate(name =>
            window.eduosAcademicManagement.state.years.some(x => x.name === name), yearName);
        assert.equal(initial, false, 'QA database must start without synthetic entry');
        await reveal(page, 'yearForm');
        await page.locator('#yearName').fill(yearName);
        await page.locator('#yearStart').fill('2027-01-01');
        await page.locator('#yearEnd').fill('2027-12-31');
        await page.locator('#yearForm button[type="submit"]').click();
        await page.waitForFunction(name =>
            window.eduosAcademicManagement?.state?.years?.some(x => x.name === name),
            yearName, {timeout: 25000});
        assert.deepEqual(failures, []);
        await page.reload({waitUntil:'domcontentloaded'});
        await page.waitForFunction(name =>
            window.eduosAcademicManagement?.state?.years?.some(x => x.name === name),
            yearName, {timeout: 25000});
        const year = await page.evaluate(name =>
            window.eduosAcademicManagement.state.years.find(x => x.name === name),yearName);
        assert(year.id > 0 && year.code, 'Academic Year backend-generated code missing');
        await academic(page, 'Terms', 'terms');
        await reveal(page,'termForm');
        await page.locator('#termYear').selectOption(String(year.id));
        await page.locator('#termName').fill(termName);
        await page.locator('#termStart').fill('2027-02-01');
        await page.locator('#termEnd').fill('2027-06-30');
        await page.locator('#termForm button[type="submit"]').click();
        await page.waitForFunction(name =>
            window.eduosAcademicManagement?.state?.terms?.some(x => x.name === name),
            termName, {timeout: 25000});
        assert.deepEqual(failures, []);
        await page.reload({waitUntil:'domcontentloaded'});
        await page.waitForFunction(name =>
            window.eduosAcademicManagement?.state?.terms?.some(x => x.name === name),
            termName, {timeout: 25000});
        const term = await page.evaluate(name =>
            window.eduosAcademicManagement.state.terms.find(x => x.name === name), termName);
        assert.equal(term.academicYearId, year.id, 'Term FK mismatch');
        assert(term.code, 'Academic Term backend code missing');
        await ctx.close();
        const student = await browser.newContext({ignoreHTTPSErrors:true,
            serviceWorkers:'block', acceptDownloads:false});
        await student.route('**/*', route => new URL(route.request().url()).origin === origin
            ? route.continue() : route.abort('blockedbyclient'));
        const restrictedPage = await student.newPage();
        await login(restrictedPage,'demo.student@eduos.example.invalid');
        const restricted = await restrictedPage.goto(origin +
            '/api/institution-onboarding/academic-years', {waitUntil:'domcontentloaded'});
        assert(restricted && restricted.status() === 403, 'Student must not read tenant admin API');
        await student.close();
        fs.mkdirSync('qa-artifacts',{recursive:true});
        fs.writeFileSync('qa-artifacts/real-data-entry.json',JSON.stringify({
            sha:process.env.GITHUB_SHA,status:'PASS',target:'disposable-github-runner',
            checks:['login','year-form-save','year-reload','year-code','term-form-save',
                'term-reload','term-year-reference','student-403'],
            yearId:year.id, termId:term.id
        },null,2));
        console.log('PASS: actual UI writes, SQL-backed reload and unauthorized Student denial.');
    } finally { await browser.close(); }
}
main().catch(error => {
    console.error('Real data-entry QA failed: ' + error.message);
    process.exitCode = 1;
});
