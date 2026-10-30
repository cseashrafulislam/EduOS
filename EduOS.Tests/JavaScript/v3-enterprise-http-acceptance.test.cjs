const { test } = require('node:test');
const assert = require('node:assert/strict');
const { pathToFileURL } = require('node:url');
const { resolve } = require('node:path');
const source = pathToFileURL(resolve(__dirname, '../../scripts/v3-enterprise-http-acceptance.mjs')).href;
const stock = '11111111-1111-4111-8111-111111111111';
const asset = '22222222-2222-4222-8222-222222222222';
const config = { base: 'http://localhost:3000', a: 'a-session', b: 'b-session', stock, asset,
    from: '2026-10-01', to: '2026-10-31', iterations: 12, concurrency: 3 };

function fakeFetch({ leak = false, denyCsv = false } = {}) {
    return async (url, options) => {
        assert.equal(options.method, 'GET');
        assert.equal(options.redirect, 'manual');
        const parsed = new URL(url);
        const path = parsed.pathname;
        const foreign = options.headers.Cookie === 'b-session';
        const reference = path.includes('asset-depreciation') ? asset : stock;
        const isCsv = path.endsWith('/csv');
        const isRegister = path.includes('/register');
        const json = (status, data) => new Response(JSON.stringify({ success: status === 200, data }), {
            status, headers: { 'content-type': 'application/json', 'cache-control': 'no-store' }
        });
        const csv = (status, body) => new Response(body, { status,
            headers: { 'content-type': 'text/csv', 'cache-control': 'no-store' } });
        if (isRegister) {
            if (Number(parsed.searchParams.get('pageSize')) > 50) return json(400, null);
            const kind = parsed.searchParams.get('kind');
            const ref = kind === 'asset' ? asset : stock;
            const items = foreign && !leak ? [] : [{ reference: ref, evidenceStatus: 'ExplicitReferencePresent' }];
            if (isCsv) return csv(200, 'Kind,Reference\n' + items.map(x => x.reference).join('\n'));
            return json(200, { page: 1, pageSize: Number(parsed.searchParams.get('pageSize')),
                totalCount: items.length, items });
        }
        if (foreign && !leak) return isCsv ? csv(404, '') : json(404, null);
        if (isCsv) return csv(denyCsv ? 403 : 200, 'Evidence,Reference\n' + reference);
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
