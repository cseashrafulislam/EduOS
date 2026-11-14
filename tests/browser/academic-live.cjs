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
        await page.goto(base + '/Account/Login', { waitUntil: 'domcontentloaded' });
        await page.locator('#email').fill(email);
        await page.locator('#password').fill(password);
        await page.locator('#loginBtn').click();
        await page.waitForTimeout(1000);
        if (await page.locator('#institutionChoicePanel').isVisible()) {
            if (!expectedTenant) throw new Error('Multi-institution test user needs EDUOS_E2E_TENANT_ID.');
            await page.locator('#institutionChoice').selectOption(expectedTenant);
            await page.locator('#loginBtn').click();
        }
        await page.waitForTimeout(1200);
        if (page.url().includes('MfaChallenge'))
            throw new Error('MFA-enabled test user requires an interactive MFA test; this script does not bypass MFA.');
        const response = await page.goto(base + '/AcademicManagement/Index', { waitUntil: 'domcontentloaded' });
        assert.equal(response.status(), 200, 'TenantAdmin must access AcademicManagement/Index');
        await page.locator('#academicManagement').waitFor();
        await page.locator('#yearRows tr').first().waitFor({ timeout: 15000 });
        assert.equal(await page.locator('button[data-panel]').count(), 6);
        await page.locator('button[data-panel="curricula"]').click();
        assert(await page.locator('section[data-section="curricula"]').isVisible());
        await page.locator('button[data-panel="sections"]').click();
        assert(await page.locator('section[data-section="sections"]').isVisible());
        assert(apiResults.some(x => x.path.endsWith('/academic-years') && x.status === 200));
        assert(apiResults.some(x => x.path.endsWith('/academic-terms') && x.status === 200));
        assert(apiResults.some(x => x.path.endsWith('/catalog') && x.status === 200));
        assert.equal(blockedWrites.length, 0, 'Browser attempted a non-login write during read-only QA');
        assert.equal(errors.length, 0, 'Unexpected browser JavaScript errors: ' + errors.join('; '));
        console.log('Academic authenticated browser E2E: PASS (read-only: login, tenant, catalog, year, term, sections, curriculum).');
    } finally { await browser.close(); }
}
main().catch(e => { console.error(e.message); process.exitCode = 1; });
