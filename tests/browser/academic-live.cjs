'use strict';
const assert = require('node:assert/strict');
const { chromium } = require('playwright');

const base = (process.env.EDUOS_BASE_URL || '').trim().replace(/\/$/, '');
const email = process.env.EDUOS_E2E_USER || '';
const password = process.env.EDUOS_E2E_PASSWORD || '';
const expectedTenant = process.env.EDUOS_E2E_TENANT_ID || '';
if (!base || !email || !password) {
    console.error('Missing EDUOS_BASE_URL, EDUOS_E2E_USER or EDUOS_E2E_PASSWORD. No production login attempted.');
    process.exit(2);
}
const target = new URL(base);
if (target.protocol !== 'https:' && !(target.hostname === 'localhost' || target.hostname === '127.0.0.1')) {
    console.error('Authenticated browser checks require HTTPS except for loopback development.');
    process.exit(2);
}

async function main() {
    const browser = await chromium.launch({ headless: true, args: ['--no-sandbox'] });
    try {
        const context = await browser.newContext({ ignoreHTTPSErrors: false });
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
        assert.equal(errors.length, 0, 'Unexpected browser JavaScript errors: ' + errors.join('; '));
        console.log('Academic authenticated browser E2E: PASS (read-only: login, tenant, catalog, year, term, sections, curriculum).');
    } finally { await browser.close(); }
}
main().catch(e => { console.error(e.message); process.exitCode = 1; });
