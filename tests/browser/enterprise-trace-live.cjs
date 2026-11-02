'use strict';
// Opt-in read-only Chromium acceptance against an explicitly isolated staging SQL deployment.
// Three synthetic accounts; no screenshots, payloads, credentials or cookies in output.
const assert = require('node:assert/strict');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
function validateEnvironment(env) {
    const get = key => {
        const value = (env['EDUOS_E2E_' + key] || '').trim();
        assert(value, 'Missing synthetic staging configuration: ' + key);
        return value;
    };
    const base = get('BASE_URL').replace(/\/$/, '');
    let url;
    try { url = new URL(base); } catch { throw Error('Invalid staging origin'); }
    assert(url.protocol === 'https:' && url.origin === base &&
        url.hostname.toLowerCase() === get('STAGING_HOST').toLowerCase() &&
        /(?:^|[.-])(staging|stage|test|qa|sandbox)(?:[.-]|$)/i.test(url.hostname) &&
        !url.username && !url.password && !url.search && !url.hash,
        'Refusing non-isolated staging origin');
    assert.equal(get('ISOLATED_STAGING_ACK'), 'I_CONFIRM_DISPOSABLE_SYNTHETIC',
        'Explicit disposable synthetic staging confirmation required');
    const users = {};
    for (const key of ['A', 'B', 'RESTRICTED']) {
        const tenant = get(key + '_TENANT_ID');
        assert(/^[1-9]\d*$/.test(tenant) && Number.isSafeInteger(Number(tenant)),
            'Invalid synthetic tenant identity');
        users[key] = { email: get(key + '_USER'), password: get(key + '_PASSWORD'), tenant };
    }
    assert(users.A.tenant !== users.B.tenant &&
        users.RESTRICTED.tenant === users.A.tenant &&
        new Set(Object.values(users).map(u => u.email)).size === 3,
        'Independent owner, foreign and restricted synthetic accounts required');
    const refs = ['STOCK_REFERENCE', 'ASSET_REFERENCE', 'PROCUREMENT_REFERENCE'].map(get);
    assert(refs.every(x => UUID.test(x)) &&
        new Set(refs.map(x => x.toLowerCase())).size === 3,
        'Three distinct synthetic source UUIDs required');
    const confirmation = get('SUPPLIER_CONFIRMATION_ID');
    assert(/^[1-9]\d*$/.test(confirmation) &&
        Number.isSafeInteger(Number(confirmation)), 'Invalid supplier confirmation ID');
    const from = get('FROM'), to = get('TO');
    const validDate = value => {
        if (!/^\d{4}-\d{2}-\d{2}$/.test(value)) return false;
        const parsed = new Date(value + 'T00:00:00Z');
        return !Number.isNaN(parsed.getTime()) && parsed.toISOString().slice(0, 10) === value;
    };
    assert(validDate(from) && validDate(to) && from <= to &&
        (Date.parse(to) - Date.parse(from)) / 86400000 <= 366,
        'Invalid bounded synthetic date range');
    const campus = get('ASSET_CAMPUS_ID'), otherCampus = get('OTHER_CAMPUS_ID');
    assert(/^[1-9]\d*$/.test(campus) && /^[1-9]\d*$/.test(otherCampus) &&
        campus !== otherCampus, 'Distinct campus IDs required');
    const languages = { A: get('A_LANGUAGE'), B: get('B_LANGUAGE') };
    assert(['en', 'bn'].includes(languages.A) &&
        ['en', 'bn'].includes(languages.B) && languages.A !== languages.B,
        'English and Bangla synthetic accounts required');
    return { base, users, refs, confirmation, from, to, campus, otherCampus, languages };
}
// The Playwright BrowserContext.request cookie jar carries real login state.
// Placeholder labels are never sent to the server as Cookie headers.
function makeAuthenticatedFetcher(base, contexts) {
    return async (url, options) => {
        const target = new URL(url);
        assert(target.origin === base && target.pathname.startsWith('/api/'),
            'Refusing unapproved authenticated request');
        assert.equal(options.method, 'GET', 'Staging acceptance is read-only');
        const context = contexts[options.headers?.Cookie];
        assert(context?.request, 'Unknown synthetic session label');
        const response = await context.request.get(url, {
            maxRedirects: 0, timeout: 15000, failOnStatusCode: false,
            headers: { Accept: 'application/json, text/csv' }
        });
        const bytes = await response.body();
        assert(bytes.length <= 2_000_000, 'Oversized staging evidence response');
        const headers = response.headers(), status = response.status();
        return new Response([204, 205, 304].includes(status) ? null : bytes, {
            status, headers: { 'content-type': headers['content-type'] || '',
                'cache-control': headers['cache-control'] || '',
                'content-length': String(bytes.length) }
        });
    };
}
async function login(browser, config, key) {
    const user = config.users[key];
    const context = await browser.newContext({
        viewport: { width: key === 'B' ? 390 : 1280, height: 844 },
        serviceWorkers: 'block', ignoreHTTPSErrors: false, acceptDownloads: false
    });
    const page = await context.newPage(), errors = [], blockedWrites = [];
    page.on('pageerror', () => errors.push('script-error'));
    await context.route('**/*', route => {
        const req = route.request(), method = req.method().toUpperCase();
        const url = new URL(req.url());
        if (['GET', 'HEAD', 'OPTIONS'].includes(method)) return route.continue();
        if (method === 'POST' && url.origin === config.base &&
            ['/api/auth/login', '/account/login'].includes(url.pathname.toLowerCase()))
            return route.continue();
        blockedWrites.push('blocked-write');
        return route.abort('blockedbyclient');
    });
    const response = await page.goto(config.base + '/Account/Login',
        { waitUntil: 'domcontentloaded' });
    assert.equal(response?.status(), 200, 'Synthetic login page unavailable');
    await page.locator('#userNameOrEmail').fill(user.email);
    await page.locator('#password').fill(user.password);
    await page.locator('#loginBtn').click();
    await page.waitForFunction(() => location.pathname.toLowerCase() !== '/account/login' ||
        !!document.querySelector('#institutionChoicePanel:not([hidden])') ||
        !!document.querySelector('#alertContainer.alert-danger'), null, { timeout: 20000 });
    if (await page.locator('#institutionChoicePanel').isVisible()) {
        const choices = await page.locator('#institutionChoice option').evaluateAll(
            nodes => nodes.map(n => n.value));
        assert(choices.includes(user.tenant), 'Synthetic institution unavailable');
        await page.locator('#institutionChoice').selectOption(user.tenant);
        await page.locator('#loginBtn').click();
        await page.waitForFunction(() => location.pathname.toLowerCase() !== '/account/login' ||
            !!document.querySelector('#alertContainer.alert-danger'), null, { timeout: 20000 });
    }
    assert(!page.url().includes('MfaChallenge'), 'MFA requires an approved test flow; no bypass');
    assert(!await page.locator('#alertContainer.alert-danger').isVisible(),
        'Synthetic login rejected');
    const workspace = await page.goto(config.base + '/workspace',
        { waitUntil: 'domcontentloaded' });
    assert.equal(workspace?.status(), 200, 'Authenticated workspace unavailable');
    assert.equal(await page.locator('body').getAttribute('data-auth-tenant-id'),
        user.tenant, 'Unexpected authenticated tenant');
    return { context, page, errors, blockedWrites };
}
async function verifyView(config, session, key, reference) {
    const { page } = session;
    const response = await page.goto(config.base + '/V3EnterpriseTrace/Index',
        { waitUntil: 'domcontentloaded' });
    assert.equal(response?.status(), 200, 'Enterprise trace route unavailable');
    await page.locator('#v3LedgerTrace').waitFor();
    assert.equal(await page.locator('#v3LedgerTrace').getAttribute('data-language'),
        config.languages[key], 'Bilingual route mismatch');
    if (key === 'B') {
        // Check mobile keyboard reachability before the expected 403 disables controls.
        const width = await page.locator('#v3LedgerTrace').evaluate(
            e => e.getBoundingClientRect().width);
        assert(width > 0 && width <= 390, 'Bangla mobile viewport overflow');
        await page.locator('#v3TraceReference').focus();
        assert.equal(await page.evaluate(() => document.activeElement?.id),
            'v3TraceReference', 'Keyboard focus unavailable');
    }
    await page.locator('#v3TraceReference').fill(reference);
    await page.locator('#v3TraceForm button[type="submit"]').click();
    if (key === 'A') {
        await page.locator('#v3TraceResult:not([hidden])').waitFor({ timeout: 15000 });
        assert(await page.locator('#v3TraceRows tr').count() > 0,
            'Authorized source/Finance evidence absent');
        assert((await page.locator('#v3TraceCsv').getAttribute('href') || '')
            .startsWith('/api/v3/enterprise/ledger-trace/'),
            'Authorized CSV link missing');
    } else {
        await page.waitForFunction(() => {
            const msg = document.getElementById('v3TraceMessage')?.textContent || '';
            return !/Checking|যাচাই হচ্ছে/.test(msg) && msg.length > 0;
        }, null, { timeout: 15000 });
        assert.equal(await page.locator('#v3TraceResult').isVisible(), false,
            'Foreign session displayed owner evidence');
        assert.equal(await page.locator('#v3TraceRows tr').count(), 0,
            'Foreign session retained owner rows');
        assert.equal(await page.locator('#v3TraceCsv').getAttribute('href'), null,
            'Foreign session retained export');
    }
    assert.deepEqual(session.errors, [], 'Browser JavaScript error');
    assert.deepEqual(session.blockedWrites, [], 'Unexpected staging write');
}
async function main(env = process.env) {
    const config = validateEnvironment(env);
    const { chromium } = require('playwright');
    const { validateConfig, runAcceptance } = await import(pathToFileURL(path.resolve(
        __dirname, '../../scripts/v3-enterprise-http-acceptance.mjs')).href);
    const browser = await chromium.launch({ headless: true });
    const sessions = {};
    try {
        for (const key of ['A', 'B', 'RESTRICTED'])
            sessions[key] = await login(browser, config, key);
        await verifyView(config, sessions.A, 'A', config.refs[0]);
        await verifyView(config, sessions.B, 'B', config.refs[0]);
        const acceptance = validateConfig({
            BASE_URL: config.base, STAGING_HOST: new URL(config.base).hostname,
            TENANT_A_COOKIE: 'owner-label', TENANT_B_COOKIE: 'foreign-label',
            RESTRICTED_COOKIE: 'restricted-label',
            STOCK_REFERENCE: config.refs[0], ASSET_REFERENCE: config.refs[1],
            PROCUREMENT_REFERENCE: config.refs[2],
            SUPPLIER_CONFIRMATION_ID: config.confirmation,
            FROM: config.from, TO: config.to,
            ASSET_CAMPUS_ID: config.campus, OTHER_CAMPUS_ID: config.otherCampus,
            AUDIT_ALL_PAGES: '1', AUDIT_MAX_PAGES: '10',
            ITERATIONS: '30', CONCURRENCY: '4'
        });
        const fetcher = makeAuthenticatedFetcher(config.base, {
            'owner-label': sessions.A.context, 'foreign-label': sessions.B.context,
            'restricted-label': sessions.RESTRICTED.context
        });
        const report = await runAcceptance(acceptance, fetcher);
        assert.equal(report.errors, 0, 'Authenticated replay errors');
        assert(report.maxClientInFlight >= 2, 'Concurrent replay not observed');
        console.log('Authenticated staging browser acceptance PASS: three synthetic accounts, ' +
            'bilingual/mobile, tenant/campus/role and bounded read-only source/Finance replay. ' +
            'No production/UAT certification.');
    } finally {
        await Promise.all(Object.values(sessions).map(s => s.context.close()));
        await browser.close();
    }
}
module.exports = { validateEnvironment, makeAuthenticatedFetcher, main };
if (require.main === module)
    main().catch(() => {
        console.error('Authenticated staging acceptance FAILED; no private response printed.');
        process.exitCode = 1;
    });
