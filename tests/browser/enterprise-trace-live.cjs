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
        new Set(Object.values(users).map(u => u.email.toLowerCase())).size === 3,
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
    return { base, users, refs, confirmation, from, to, campus, otherCampus, languages,
        privateRefs: new Set(refs.map(value => value.toLowerCase())) };
}
// Fail closed before any browser navigation or authenticated API request.
// No cross-origin resources, encoded path separators/dots, fragments or URL
// normalization that could conceal a different target. No private URL logging.
function safeStagingUrl(raw, base) {
    if (typeof raw !== 'string' || !raw.startsWith(base + '/')) return null;
    try {
        const url = new URL(raw);
        if (url.href !== raw || url.origin !== base || url.protocol !== 'https:' ||
            url.username || url.password || url.hash ||
            /%(?:00|25|2e|2f|5c)/i.test(url.pathname) ||
            url.pathname.includes('\\') || url.pathname.includes('//') ||
            url.pathname.split('/').some(segment => segment === '.' || segment === '..'))
            return null;
        return url;
    } catch { return null; }
}
function isAllowedBrowserRequest(raw, method, base) {
    const url = safeStagingUrl(raw, base);
    if (!url) return false;
    if (['GET', 'HEAD', 'OPTIONS'].includes(method)) return true;
    return method === 'POST' && !url.search &&
        ['/api/auth/login', '/account/login'].includes(url.pathname.toLowerCase());
}
function isAllowedEvidenceRequest(raw, base) {
    const url = safeStagingUrl(raw, base);
    return Boolean(url && /^\/api\/(?:[A-Za-z0-9_-]+\/)*[A-Za-z0-9_-]+$/.test(url.pathname));
}
// The Playwright BrowserContext.request cookie jar carries real login state.
// Placeholder labels are never sent to the server as Cookie headers.
function makeAuthenticatedFetcher(base, contexts) {
    return async (url, options) => {
        assert(isAllowedEvidenceRequest(url, base),
            'Refusing unapproved authenticated request');
        assert.equal(options.method, 'GET', 'Staging acceptance is read-only');
        assert(!options.body, 'Staging acceptance cannot send a body');
        const context = contexts[options.headers?.Cookie];
        assert(context?.request, 'Unknown synthetic session label');
        const response = await context.request.get(url, {
            maxRedirects: 0, timeout: 15000, failOnStatusCode: false,
            headers: { Accept: 'application/json, text/csv' }
        });
        assert(typeof response.url === 'function' && response.url() === url,
            'Unexpected authenticated response target');
        const status = response.status();
        assert(status < 300 || status >= 400,
            'Authenticated evidence redirect is forbidden');
        const bytes = await response.body();
        assert(bytes.length <= 2_000_000, 'Oversized staging evidence response');
        const headers = response.headers();
        return new Response([204, 205, 304].includes(status) ? null : bytes, {
            status, headers: { 'content-type': headers['content-type'] || '',
                'cache-control': headers['cache-control'] || '',
                'content-length': String(bytes.length) }
        });
    };
}

/**
 * Observe actual Chromium network responses, not only rendered DOM or synthetic
 * HTTP fixtures. Foreign sessions must never receive owner evidence in an
 * error body, hidden JSON/CSV payload or a cacheable API response.
 * No response body, URL, cookie or tenant identifier is printed.
 */
function captureEvidenceResponses(page, base) {
    const responses = [];
    page.on('response', response => {
        const url = safeStagingUrl(response.url(), base);
        if (url && /^\/api\/v3\/enterprise\/ledger-trace(?:\/|$)/i.test(url.pathname))
            responses.push(response);
    });
    return responses;
}
async function assertForeignResponsePrivacy(config, session, requireNetworkEvidence = false) {
    const responses = session.evidenceResponses.splice(0);
    if (requireNetworkEvidence)
        assert(responses.length > 0,
            'Foreign browser did not exercise an evidence HTTP request');
    for (const response of responses) {
        assert([200, 400, 401, 403, 404, 409].includes(response.status()),
            'Unexpected foreign evidence response status');
        const directives = String(response.headers()['cache-control'] || '')
            .toLowerCase().split(',').map(value => value.trim());
        assert(directives.includes('no-store') &&
            !directives.includes('public') &&
            !directives.some(value => /^s-maxage\s*=/.test(value)),
            'Foreign evidence response is cacheable');
        const body = await response.body();
        assert(body.length <= 2_000_000, 'Oversized foreign evidence response');
        const text = body.toString('utf8').toLowerCase();
        for (const reference of config.privateRefs || config.refs)
            assert(!text.includes(reference.toLowerCase()),
                'Foreign browser received owner evidence in response body');
    }
}

/**
 * Extract owner-only source and nested Finance UUIDs from ACTUAL authorized
 * Chromium HTTP responses. The set is kept in memory, never logged or stored.
 * A seeded source ID alone is not sufficient to detect nested journal leakage.
 */
async function collectOwnerEvidenceReferences(config, session, expectedReference = null) {
    const responses = session.evidenceResponses.splice(0);
    assert(responses.length > 0, 'Owner browser did not exercise an evidence HTTP request');
    const observed = new Set();
    for (const response of responses) {
        assert.equal(response.status(), 200, 'Authorized owner evidence request failed');
        const directives = String(response.headers()['cache-control'] || '')
            .toLowerCase().split(',').map(value => value.trim());
        assert(directives.includes('no-store') && !directives.includes('public') &&
            !directives.some(value => /^s-maxage\s*=/.test(value)),
            'Owner evidence response is cacheable');
        const body = await response.body();
        assert(body.length <= 2_000_000, 'Oversized owner evidence response');
        const uuid = /\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b/gi;
        for (const match of body.toString('utf8').matchAll(uuid)) {
            const reference = match[0].toLowerCase();
            observed.add(reference);
            config.privateRefs.add(reference);
            assert(config.privateRefs.size <= 10000,
                'Unbounded owner reference inventory');
        }
    }
    if (expectedReference)
        assert(observed.has(expectedReference.toLowerCase()),
            'Authorized source reference absent from observed HTTP evidence');
}

/**
 * Real Chromium direct-navigation denial, not a mock or a DOM-only assertion.
 * Denied JSON/CSV endpoints must not redirect, render a successful response,
 * return cacheable bytes or expose any captured owner source/Finance UUID.
 */
async function assertDeniedDirectEvidence(config, session, relativePath) {
    assert(/^\/api\/v3\/enterprise\/ledger-trace\/(?:asset-depreciation\/|procurement\/)?[0-9a-f-]{36}(?:\/csv)?$/i.test(relativePath),
        'Refusing an unapproved direct evidence path');
    const target = config.base + relativePath;
    assert(isAllowedEvidenceRequest(target, config.base),
        'Refusing an unsafe direct evidence URL');
    const response = await session.page.goto(target, { waitUntil: 'domcontentloaded' });
    assert(response && response.url() === target &&
        [403, 404].includes(response.status()),
        'Foreign/restricted direct evidence request was not denied');
    await assertForeignResponsePrivacy(config, session, true);
}

async function login(browser, config, key) {
    const user = config.users[key];
    const context = await browser.newContext({
        viewport: { width: key === 'B' ? 390 : 1280, height: 844 },
        serviceWorkers: 'block', ignoreHTTPSErrors: false, acceptDownloads: false
    });
    const page = await context.newPage(), errors = [], blockedWrites = [];
    const evidenceResponses = captureEvidenceResponses(page, config.base);
    page.on('pageerror', () => errors.push('script-error'));
    await context.route('**/*', route => {
        const req = route.request(), method = req.method().toUpperCase();
        if (isAllowedBrowserRequest(req.url(), method, config.base))
            return route.continue();
        blockedWrites.push('blocked-origin-or-write');
        return route.abort('blockedbyclient');
    });
    const response = await page.goto(config.base + '/Account/Login',
        { waitUntil: 'domcontentloaded' });
    assert.equal(response?.status(), 200, 'Synthetic login page unavailable');
    assert(isAllowedBrowserRequest(page.url(), 'GET', config.base),
        'Synthetic login left approved staging origin');
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
    assert(isAllowedBrowserRequest(page.url(), 'GET', config.base),
        'Authentication left approved staging origin');
    assert(!page.url().includes('MfaChallenge'), 'MFA requires an approved test flow; no bypass');
    assert(!await page.locator('#alertContainer.alert-danger').isVisible(),
        'Synthetic login rejected');
    const workspace = await page.goto(config.base + '/workspace',
        { waitUntil: 'domcontentloaded' });
    assert.equal(workspace?.status(), 200, 'Authenticated workspace unavailable');
    assert(isAllowedBrowserRequest(page.url(), 'GET', config.base),
        'Workspace left approved staging origin');
    assert.equal(await page.locator('body').getAttribute('data-auth-tenant-id'),
        user.tenant, 'Unexpected authenticated tenant');
    return { context, page, errors, blockedWrites, evidenceResponses };
}
function isDeniedRestrictedRoute(status, raw, base) {
    const url = safeStagingUrl(raw, base);
    if (!url) return false;
    if ([401, 403, 404].includes(status)) return true;
    if (status !== 200 || !/^\/(?:Account\/)?AccessDenied\/?$/i.test(url.pathname))
        return false;
    const query = [...url.searchParams.entries()];
    return query.length === 0 || (query.length === 1 &&
        query[0][0].toLowerCase() === 'returnurl' &&
        query[0][1].toLowerCase() === '/v3enterprisetrace/index');
}
function isScopedRegisterExport(href, config, kind, campus) {
    if (typeof href !== 'string' ||
        !href.startsWith('/api/v3/enterprise/ledger-trace/register/csv?'))
        return false;
    const url = safeStagingUrl(config.base + href, config.base);
    if (!url || url.pathname !== '/api/v3/enterprise/ledger-trace/register/csv')
        return false;
    const expected = { kind, from: config.from, to: config.to,
        page: '1', pageSize: '25' };
    if (campus) expected.campusId = campus;
    const entries = [...url.searchParams.entries()];
    return entries.length === Object.keys(expected).length &&
        entries.every(([key, value]) => Object.hasOwn(expected, key) &&
            expected[key] === value);
}
async function verifyRestrictedView(config, session) {
    const response = await session.page.goto(config.base + '/V3EnterpriseTrace/Index',
        { waitUntil: 'domcontentloaded' });
    assert(isDeniedRestrictedRoute(response?.status(), session.page.url(), config.base),
        'Restricted account reached privileged Enterprise Trace page');
    assert.equal(await session.page.locator('#v3LedgerTrace').count(), 0,
        'Restricted account rendered private trace controls');
    const visible = (await session.page.locator('body').innerText()).toLowerCase();
    for (const ref of config.refs)
        assert(!visible.includes(ref.toLowerCase()),
            'Restricted route exposed synthetic owner reference');
    assert.deepEqual(session.errors, [], 'Restricted browser JavaScript error');
    assert.deepEqual(session.blockedWrites, [], 'Unexpected restricted browser request');
    await assertForeignResponsePrivacy(config, session);
}
async function verifyRegister(config, session, key) {
    const page = session.page;
    const response = await page.goto(config.base + '/V3EnterpriseTrace/Index',
        { waitUntil: 'domcontentloaded' });
    assert.equal(response?.status(), 200, 'Authorized register route unavailable');
    assert.equal(await page.locator('body').getAttribute('data-auth-tenant-id'),
        config.users[key].tenant, 'Register tenant changed across browser sessions');
    const kind = key === 'A' ? 'asset' : 'stock';
    const campus = key === 'A' ? config.campus : '';
    await page.locator('#v3RegisterKind').selectOption(kind);
    await page.locator('#v3RegisterFrom').fill(config.from);
    await page.locator('#v3RegisterTo').fill(config.to);
    await page.locator('#v3RegisterSize').selectOption('25');
    if (campus) {
        await page.locator('#v3RegisterCampus option[value="' + campus + '"]')
            .waitFor({ timeout: 15000 });
        await page.locator('#v3RegisterCampus').selectOption(campus);
    }
    await page.locator('#v3RegisterForm button[type="submit"]').click();
    await page.locator('#v3RegisterResult:not([hidden])').waitFor({ timeout: 15000 });
    assert(await page.locator('#v3RegisterRows tr').count() <= 25,
        'Unbounded register page in browser');
    assert(isScopedRegisterExport(await page.locator('#v3RegisterCsv')
        .getAttribute('href'), config, kind, campus),
    'Register CSV export escaped selected tenant/campus/date/page scope');
    if (key === 'A') await collectOwnerEvidenceReferences(config, session);
    assert.deepEqual(session.errors, [], 'Register browser JavaScript error');
    assert.deepEqual(session.blockedWrites, [], 'Unexpected register browser request');
    if (key === 'B') await assertForeignResponsePrivacy(config, session, true);
}
async function verifyView(config, session, key, kind, reference) {
    const { page } = session;
    const response = await page.goto(config.base + '/V3EnterpriseTrace/Index',
        { waitUntil: 'domcontentloaded' });
    assert.equal(response?.status(), 200, 'Enterprise trace route unavailable');
    assert(isAllowedBrowserRequest(page.url(), 'GET', config.base),
        'Enterprise trace left approved staging origin');
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
    await page.locator('#v3TraceKind').selectOption(kind);
    await page.locator('#v3TraceReference').fill(reference);
    await page.locator('#v3TraceForm button[type="submit"]').click();
    if (key === 'A') {
        await page.locator('#v3TraceResult:not([hidden])').waitFor({ timeout: 15000 });
        assert(await page.locator('#v3TraceRows tr').count() > 0,
            'Authorized source/Finance evidence absent');
        const route = kind === 'asset' ? 'asset-depreciation/' :
            kind === 'procurement' ? 'procurement/' : '';
        assert.equal(await page.locator('#v3TraceCsv').getAttribute('href'),
            '/api/v3/enterprise/ledger-trace/' + route + reference + '/csv',
            'Authorized source-specific CSV link mismatch');
        await collectOwnerEvidenceReferences(config, session, reference);
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
    if (key === 'B') await assertForeignResponsePrivacy(config, session, true);
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
        for (const [kind, reference] of [
            ['stock', config.refs[0]], ['asset', config.refs[1]],
            ['procurement', config.refs[2]]
        ]) {
            await verifyView(config, sessions.A, 'A', kind, reference);
            await verifyView(config, sessions.B, 'B', kind, reference);
        }
        await verifyRegister(config, sessions.A, 'A');
        await verifyRegister(config, sessions.B, 'B');
        await verifyRestrictedView(config, sessions.RESTRICTED);
        // Six direct source/CSV routes for each foreign and restricted session.
        // This supplements (never replaces) the cookie-jar HTTP replay.
        for (const key of ['B', 'RESTRICTED']) {
            for (const [prefix, reference] of [
                ['', config.refs[0]],
                ['asset-depreciation/', config.refs[1]],
                ['procurement/', config.refs[2]]
            ]) {
                const route = '/api/v3/enterprise/ledger-trace/' + prefix + reference;
                await assertDeniedDirectEvidence(config, sessions[key], route);
                await assertDeniedDirectEvidence(config, sessions[key], route + '/csv');
            }
        }
        await verifyView(config, sessions.A, 'A', 'stock', config.refs[0]);
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
            'three trace kinds, twelve direct foreign/restricted JSON/CSV denials, ' +
            'nested owner Finance reference privacy, campus-scoped register CSV, ' +
            'bilingual/mobile and bounded read-only source/Finance replay. ' +
            'No production/UAT certification.');
    } finally {
        await Promise.all(Object.values(sessions).map(s => s.context.close()));
        await browser.close();
    }
}
module.exports = { validateEnvironment, makeAuthenticatedFetcher,
    captureEvidenceResponses, assertForeignResponsePrivacy,
    collectOwnerEvidenceReferences, assertDeniedDirectEvidence,
    isAllowedBrowserRequest, isAllowedEvidenceRequest, isDeniedRestrictedRoute,
    isScopedRegisterExport, main };
if (require.main === module)
    main().catch(() => {
        console.error('Authenticated staging acceptance FAILED; no private response printed.');
        process.exitCode = 1;
    });
