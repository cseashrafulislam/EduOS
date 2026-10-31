const { test } = require('node:test');
const assert = require('node:assert/strict');
const { pathToFileURL } = require('node:url');
const { resolve } = require('node:path');
const source = pathToFileURL(resolve(__dirname, '../../scripts/v3-enterprise-http-acceptance.mjs')).href;
const stock = '11111111-1111-4111-8111-111111111111';
const asset = '22222222-2222-4222-8222-222222222222';
const config = { base: 'http://localhost:3000', a: 'a-session', b: 'b-session', stock, asset,
    from: '2026-10-01', to: '2026-10-31', iterations: 12, concurrency: 3 };

function fakeFetch({ leak = false, denyCsv = false, detailCsvMismatch = false,
    registerCsvMismatch = false, campusLeak = false } = {}) {
    return async (url, options) => {
        assert.equal(options.method, 'GET');
        assert.equal(options.redirect, 'manual');
        const parsed = new URL(url);
        const path = parsed.pathname;
        const foreign = options.headers.Cookie === 'b-session';
        const restricted = options.headers.Cookie === 'restricted-session';
        const reference = path.includes('asset-depreciation') ? asset : stock;
        const isCsv = path.endsWith('/csv');
        const isRegister = path.includes('/register');
        const json = (status, data) => new Response(JSON.stringify({ success: status === 200, data }), {
            status, headers: { 'content-type': 'application/json', 'cache-control': 'no-store' }
        });
        const csv = (status, body) => new Response(body, { status,
            headers: { 'content-type': 'text/csv', 'cache-control': 'no-store' } });
        if (restricted) return json(403, null);
        if (isRegister) {
            if (Number(parsed.searchParams.get('pageSize')) > 50) return json(400, null);
            if (foreign && parsed.searchParams.has('campusId')) return json(404, null);
            const kind = parsed.searchParams.get('kind');
            const ref = kind === 'asset' ? asset : stock;
            const otherCampus = parsed.searchParams.get('campusId') === '20';
            const items = (foreign && !leak) || (otherCampus && !campusLeak)
                ? [] : [{ reference: ref, evidenceStatus: 'ExplicitBalancedReference',
                    state: 'Posted', lineCount: 1, date: '2026-10-31' }];
            if (isCsv) return csv(200, 'Kind,Reference,State,Lines,EvidenceStatus\n' +
                (registerCsvMismatch && !foreign ? kind + ',33333333-3333-4333-8333-333333333333,Posted,1,ExplicitBalancedReference'
                    : items.map(x => [kind, x.reference, x.state, x.lineCount,
                        x.evidenceStatus].join(',')).join('\n')));
            return json(200, { page: 1, pageSize: Number(parsed.searchParams.get('pageSize')),
                totalCount: items.length, items });
        }
        if (foreign && !leak) return isCsv ? csv(404, '') : json(404, null);
        if (isCsv) return csv(denyCsv ? 403 : 200, 'Evidence,Reference\n' +
            (path.includes('asset-depreciation') ? 'AssetDepreciation,' : 'Stock,') +
            (detailCsvMismatch ? '33333333-3333-4333-8333-333333333333' : reference));
        return json(200, path.includes('asset-depreciation')
            ? { runReference: asset } : { movementReference: stock });
    };
}

test('configuration refuses unapproved hosts and shared sessions', async () => {
    const { validateConfig } = await import(source);
    const env = { BASE_URL: 'http://localhost:3000/', TENANT_A_COOKIE: 'a-session',
        TENANT_B_COOKIE: 'b-session', STOCK_REFERENCE: stock, ASSET_REFERENCE: asset,
        FROM: config.from, TO: config.to };
    assert.equal(validateConfig(env).iterations, 24);
    assert.throws(() => validateConfig({ ...env, BASE_URL: 'http://production.example' }), /Refusing/);
    assert.throws(() => validateConfig({ ...env, TENANT_B_COOKIE: 'a-session' }), /distinct/);
    assert.throws(() => validateConfig({ ...env, ITERATIONS: '100000' }), /Iterations/);
    assert.throws(() => validateConfig({ ...env, BASE_URL: 'https://user:secret@localhost/' }), /origin/);
});

test('tenant-isolated detail, CSV, register and bounded concurrent reads pass', async () => {
    const { runAcceptance, percentile } = await import(source);
    assert.equal(percentile([1, 3, 2, 4], 95), 4);
    const report = await runAcceptance(config, fakeFetch());
    assert.equal(report.samples, 12);
    assert.equal(report.errors, 0);
    assert.equal(report.measuredHttpOnly, true);
    assert.deepEqual(report.registerCounts, [{ kind: 'stock', count: 1 },
        { kind: 'asset', count: 1 }]);
});

test('cross-tenant detail and CSV leaks fail closed', async () => {
    const { runAcceptance } = await import(source);
    await assert.rejects(runAcceptance(config, fakeFetch({ leak: true })), /Cross-tenant detail/);
    await assert.rejects(runAcceptance(config, fakeFetch({ denyCsv: true })), /Authorized CSV/);
});

test('cross-tenant register disclosure is rejected', async () => {
    const { runAcceptance } = await import(source);
    let registerOnlyLeak = false;
    const baseline = fakeFetch();
    const wrapped = async (url, options) => {
        if (url.includes('/register') && options.headers.Cookie === 'b-session') {
            registerOnlyLeak = true;
            return fakeFetch({ leak: true })(url, options);
        }
        return baseline(url, options);
    };
    await assert.rejects(runAcceptance(config, wrapped), /Foreign register leaked/);
    assert.equal(registerOnlyLeak, true);
});

test('CSV parser enforces BOM, quoted fields, row shape and exact evidence', async () => {
    const { parseCsv } = await import(source);
    const parsed = parseCsv('\uFEFFEvidence,Reference\r\n"Stock","a""b"\r\n');
    assert.equal(parsed.rows[0][1], 'a"b');
    assert.equal(parsed.referenceIndex, 1);
    assert.throws(() => parseCsv('Kind,Reference\n"Stock","unterminated'), /Unterminated/);
    assert.throws(() => parseCsv('Kind,Reference\n"Stock"'), /width/);
    assert.throws(() => parseCsv('Kind,Reference\n"Stock","a"garbage'), /Malformed/);
});

test('configuration rejects invalid calendar days, excessive range and partial campus setup', async () => {
    const { validateConfig } = await import(source);
    const env = { BASE_URL: 'http://localhost:3000/', TENANT_A_COOKIE: 'a-session',
        TENANT_B_COOKIE: 'b-session', STOCK_REFERENCE: stock, ASSET_REFERENCE: asset,
        FROM: config.from, TO: config.to };
    assert.throws(() => validateConfig({ ...env, FROM: '2026-02-30' }), /date range/);
    assert.throws(() => validateConfig({ ...env, FROM: '2025-01-01' }), /date range/);
    assert.throws(() => validateConfig({ ...env, ASSET_CAMPUS_ID: '10' }), /Both asset campus/);
    assert.throws(() => validateConfig({ ...env, ASSET_CAMPUS_ID: '10',
        OTHER_CAMPUS_ID: '10' }), /distinct/);
    assert.throws(() => validateConfig({ ...env,
        RESTRICTED_COOKIE: 'a-session' }), /distinct/);
});

test('JSON and CSV mismatches are rejected, not counted as successful reconciliation', async () => {
    const { runAcceptance } = await import(source);
    await assert.rejects(runAcceptance(config, fakeFetch({ detailCsvMismatch: true })),
        /Detail CSV and JSON evidence disagree/);
    await assert.rejects(runAcceptance(config, fakeFetch({ registerCsvMismatch: true })),
        /Register CSV does not match/);
});

test('restricted role, two-campus asset isolation and mixed-tenant replay pass', async () => {
    const { runAcceptance } = await import(source);
    const extended = { ...config, restricted: 'restricted-session',
        assetCampus: '10', otherCampus: '20', iterations: 25 };
    const report = await runAcceptance(extended, fakeFetch());
    assert.equal(report.samples, 25);
    assert.ok(report.checks.includes('restricted-role authorization'));
    assert.ok(report.checks.includes('asset campus isolation'));
    await assert.rejects(runAcceptance(extended, fakeFetch({ campusLeak: true })),
        /Asset campus filter exposed/);
});

test('concurrent foreign-register disclosure fails even when initial 50-row page is safe', async () => {
    const { runAcceptance } = await import(source);
    const baseline = fakeFetch();
    const leaking = fakeFetch({ leak: true });
    const fetcher = (url, options) =>
        url.includes('/register?') && url.includes('pageSize=25') &&
        options.headers.Cookie === 'b-session'
            ? leaking(url, options) : baseline(url, options);
    await assert.rejects(runAcceptance({ ...config, iterations: 25 }, fetcher),
        /Concurrent cross-tenant register disclosed/);
});

test('opt-in bounded multi-page audit integrates with enterprise acceptance', async () => {
    const { runAcceptance, validateConfig } = await import(source);
    const report = await runAcceptance({ ...config, pagedAudit: true }, fakeFetch());
    assert.deepEqual(report.registerCounts, [{ kind: 'stock', count: 1, pagesAudited: 1 },
        { kind: 'asset', count: 1, pagesAudited: 1 }]);
    assert.ok(report.checks.includes('bounded all-page JSON/CSV parity and cross-tenant isolation'));
    const env = { BASE_URL: 'http://localhost:3000/', TENANT_A_COOKIE: 'a-session',
        TENANT_B_COOKIE: 'b-session', STOCK_REFERENCE: stock, ASSET_REFERENCE: asset,
        FROM: config.from, TO: config.to, AUDIT_ALL_PAGES: '1' };
    assert.equal(validateConfig(env).pagedAudit, true);
    assert.throws(() => validateConfig({ ...env, AUDIT_MAX_PAGES: '21' }), /AUDIT_MAX_PAGES/);
});

test('procurement audit opt-in validates UUID and preserves stock/asset-only default', async () => {
    const { validateConfig } = await import(source);
    const env = { BASE_URL: 'http://localhost:3000/', TENANT_A_COOKIE: 'a-session',
        TENANT_B_COOKIE: 'b-session', STOCK_REFERENCE: stock, ASSET_REFERENCE: asset,
        FROM: config.from, TO: config.to };
    assert.equal(validateConfig(env).procurement, null);
    assert.equal(validateConfig({ ...env,
        PROCUREMENT_REFERENCE: '44444444-4444-4444-8444-444444444444' }).procurement,
        '44444444-4444-4444-8444-444444444444');
    assert.throws(() => validateConfig({ ...env,
        PROCUREMENT_REFERENCE: 'forged' }), /procurement reference UUID/);
});


test('supplier acceptance opt-in is bounded and cannot target arbitrary URLs', async () => {
    const { validateConfig, request } = await import(source);
    const env = { BASE_URL: 'http://localhost:3000/', TENANT_A_COOKIE: 'a-session',
        TENANT_B_COOKIE: 'b-session', STOCK_REFERENCE: stock, ASSET_REFERENCE: asset,
        FROM: config.from, TO: config.to };
    assert.equal(validateConfig(env).supplierConfirmation, null);
    assert.throws(() => validateConfig({ ...env,
        SUPPLIER_CONFIRMATION_ID: '71' }), /requires procurement UUID/);
    assert.throws(() => validateConfig({ ...env,
        PROCUREMENT_REFERENCE: stock, SUPPLIER_CONFIRMATION_ID: '0' }), /positive safe integer/);
    assert.throws(() => validateConfig({ ...env,
        PROCUREMENT_REFERENCE: stock, SUPPLIER_CONFIRMATION_ID: '9007199254740993' }),
    /positive safe integer/);
    assert.equal(validateConfig({ ...env,
        PROCUREMENT_REFERENCE: stock, SUPPLIER_CONFIRMATION_ID: '71' })
        .supplierConfirmation, 71);
    await assert.rejects(request('http://localhost:3000', 'a-session',
        '/api/procurement/../admin', async () => { throw new Error('unexpected fetch'); }),
    /Invalid API path/);
    await assert.rejects(request('http://localhost:3000', 'a-session',
        '//evil.example/api/procurement/purchase-orders', async () => {
            throw new Error('unexpected fetch');
        }), /Invalid API path/);
});
