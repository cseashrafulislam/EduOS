'use strict';
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const { validateTarget, isAllowedReadOnlyRequest } = require('./academic-live-guards.cjs');

const email = process.env.EDUOS_E2E_USER || '';
const password = process.env.EDUOS_E2E_PASSWORD || '';
const expectedTenant = process.env.EDUOS_E2E_TENANT_ID || '';
let base;
try {
    base = validateTarget(process.env.EDUOS_BASE_URL, process.env.EDUOS_E2E_EXPECTED_HOST);
    assert(email && password, 'Missing synthetic test login configuration');
} catch (error) {
    console.error('Academic authenticated smoke preflight rejected: ' + error.message);
    process.exit(2);
}

async function main() {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        const context = await browser.newContext({
            ignoreHTTPSErrors: false, serviceWorkers: 'block', acceptDownloads: false
        });
        const blockedWrites = [];
        await context.route('**/*', route => {
            const request = route.request();
            if (isAllowedReadOnlyRequest(request.url(), request.method(), base))
                return route.continue();
            if (!['GET', 'HEAD', 'OPTIONS'].includes(request.method()))
                blockedWrites.push(request.method());
            return route.abort('blockedbyclient');
        });
        const page = await context.newPage();
        const errors = [];
        const apiResults = [];
        page.on('pageerror', error => errors.push(error.message));
        page.on('response', response => {
            if (response.url().includes('/api/academic-setup/') ||
                response.url().includes('/api/institution-onboarding/'))
                apiResults.push({ path: new URL(response.url()).pathname, status: response.status() });
        });
        const loginPage = await page.goto(base + '/Account/Login', { waitUntil: 'domcontentloaded' });
        assert(loginPage && loginPage.status() === 200, 'Login page unavailable');
        await page.locator('#userNameOrEmail').fill(email);
        await page.locator('#password').fill(password);
        await page.locator('#loginBtn').click();
        const awaitLogin = async () => page.waitForFunction(() =>
            location.pathname.toLowerCase() !== '/account/login' ||
            !!document.querySelector('#institutionChoicePanel:not([hidden])') ||
            !!document.querySelector('#alertContainer.alert-danger'), null, { timeout: 20000 });
        await awaitLogin();
        if (await page.locator('#institutionChoicePanel').isVisible()) {
            if (!expectedTenant) throw new Error('Multi-institution test user needs EDUOS_E2E_TENANT_ID.');
            await page.locator('#institutionChoice').selectOption(expectedTenant);
            await page.locator('#loginBtn').click();
            await awaitLogin();
        }
        assert.equal(await page.locator('#alertContainer.alert-danger').isVisible(), false,
            'Synthetic login was rejected');
        if (page.url().includes('MfaChallenge'))
            throw new Error('MFA-enabled test user requires an interactive MFA test; this script does not bypass MFA.');
        // Index is a legacy bookmark redirect; active features are distinct routes.
        const response = await page.goto(base + '/AcademicManagement/Index', {
            waitUntil: 'domcontentloaded'
        });
        assert(response && response.status() === 200, 'TenantAdmin must access Academic Management');
        assert.equal(new URL(page.url()).pathname.toLowerCase(), '/academicmanagement/years',
            'Index must redirect to canonical Academic Years');
        await page.locator('#academicManagement[data-active-feature="years"]').waitFor();
        await page.locator('#yearRows').waitFor();
        await page.waitForFunction(() => !!window.eduosAcademicManagement?.state?.catalog,
            null, { timeout: 20000 });
        assert(await page.locator('section[data-section="years"]').isVisible());
        for (const [route, feature] of [['Curricula', 'curricula'], ['Batches', 'sections']]) {
            const pageResult = await page.goto(base + '/AcademicManagement/' + route, {
                waitUntil: 'domcontentloaded'
            });
            assert(pageResult && pageResult.status() === 200, 'Canonical academic route failed: ' + route);
            await page.locator('#academicManagement[data-active-feature="' + feature + '"]').waitFor();
            assert(await page.locator('section[data-section="' + feature + '"]').isVisible(),
                'Active academic feature not visible: ' + feature);
            assert.equal(await page.locator('section[data-section="years"]').isVisible(), false,
                'Unselected academic editor must stay hidden');
        }
        const sections = await page.goto(base + '/AcademicManagement/Sections', {
            waitUntil: 'domcontentloaded'
        });
        assert(sections && sections.status() === 200, 'Canonical Academic Sections register unavailable');
        await page.locator('#academicSectionsPage').waitFor();
        assert(apiResults.some(x => x.path.endsWith('/academic-years') && x.status === 200));
        assert(apiResults.some(x => x.path.endsWith('/academic-terms') && x.status === 200));
        assert(apiResults.some(x => x.path.endsWith('/catalog') && x.status === 200));
        assert.equal(blockedWrites.length, 0, 'Browser attempted a non-login write during read-only QA');
        assert.equal(errors.length, 0, 'Unexpected browser JavaScript errors: ' + errors.join('; '));
        console.log('Academic authenticated browser E2E: PASS (read-only: login, tenant, catalog, year, term, sections, curriculum).');
    } finally { await browser.close(); }
}
main().catch(e => { console.error(e.message); process.exitCode = 1; });
