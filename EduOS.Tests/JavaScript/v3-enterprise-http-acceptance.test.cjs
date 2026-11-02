const { test } = require('node:test');
const assert = require('node:assert/strict');
const { pathToFileURL } = require('node:url');
const { resolve } = require('node:path');
const source = pathToFileURL(resolve(__dirname, '../../scripts/v3-enterprise-http-acceptance.mjs')).href;
const stock = '11111111-1111-4111-8111-111111111111';
const asset = '22222222-2222-4222-8222-222222222222';
const otherAsset = '99999999-9999-4999-8999-999999999999';
const config = { base: 'http://localhost:3000', a: 'a-session', b: 'b-session', stock, asset,
    from: '2026-10-01', to: '2026-10-31', iterations: 12, concurrency: 3 };

function fakeFetch({ leak = false, denyCsv = false, detailCsvMismatch = false,
    registerCsvMismatch = false, campusLeak = false,
    additionalAsset = false, leakAdditionalAsset = false } = {}) {
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
            const primary = { reference: ref,
                number: kind === 'asset' ? 'DEPR-1' : 'STOCK-1',
                date: '2026-10-31', campusId: kind === 'asset' ? 10 : null,
                recordedValue: '25.0000000000',
                evidenceStatus: 'ExplicitBalancedReference',
                state: 'Posted', lineCount: 1 };
            const secondary = { ...primary, reference: otherAsset, number: 'DEPR-2' };
            const items = otherCampus && !campusLeak ? [] :
                foreign ? (leak ? [primary] :
                    leakAdditionalAsset && kind === 'asset' ? [secondary] : []) :
                    [primary, ...(additionalAsset && kind === 'asset' ? [secondary] : [])];
            if (isCsv) return csv(200,
                'Kind,Reference,Number,Date,Campus,State,RecordedValue,Lines,EvidenceStatus\n' +
                items.map(x => [kind,
                    registerCsvMismatch && !foreign
                        ? '33333333-3333-4333-8333-333333333333' : x.reference,
                    x.number, x.date, x.campusId ?? '', x.state, x.recordedValue,
                    x.lineCount, x.evidenceStatus].join(',')).join('\n'));
            return json(200, { kind, from: parsed.searchParams.get('from'),
                to: parsed.searchParams.get('to'),
                page: 1, pageSize: Number(parsed.searchParams.get('pageSize')),
                totalCount: items.length, items });
        }
        if (foreign && !leak) return isCsv ? csv(404, '') : json(404, null);
        if (isCsv) {
            const ref = detailCsvMismatch ? '33333333-3333-4333-8333-333333333333' : reference;
            return path.includes('asset-depreciation')
                ? csv(denyCsv ? 403 : 200,
                    'Evidence,Reference,State,Debit,Credit,RunAmount,LinesAmount,Campus,PeriodEnd\n' +
                    'AssetDepreciation,' + ref + ',Posted,,,25,25,10,2026-10-31')
                : csv(denyCsv ? 403 : 200,
                    'Evidence,Reference,Number,State,Debit,Credit,RecordedValue,SourceType,SourceId,GoodsReceiptReference,PurchaseOrderReference,SourceEvidenceStatus\n' +
                    'Stock,' + ref + ',STOCK-1,Posted,,,25,GoodsReceipt,42,,,Verified');
        }
        return json(200, path.includes('asset-depreciation')
            ? { runReference: asset, state: 'Posted', runTotalAmount: 25,
                depreciationLinesAmount: 25, campusId: 10, periodEnd: '2026-10-31',
                journals: [] }
            : { movementReference: stock, movementNumber: 'STOCK-1',
                state: 'Posted', recordedMovementValue: 25,
                sourceType: 'GoodsReceipt', sourceId: 42,
                procurementGoodsReceiptReference: null,
                procurementPurchaseOrderReference: null,
                sourceEvidenceStatus: 'Verified', journals: [] });
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
    assert.ok(report.maxClientInFlight >= 2);
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
    let foreignReads = 0;
    const fetcher = (url, options) =>
        url.includes('/register?') && url.includes('pageSize=25') &&
        options.headers.Cookie === 'b-session' && ++foreignReads > 2
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

test('HTTP acceptance times complete streamed responses and bounds unannounced payload size', async () => {
    const { request } = await import(source);
    const path = '/api/v3/enterprise/ledger-trace/' + stock;
    const encoder = new TextEncoder();
    const delayed = async () => new Response(new ReadableStream({
        start(controller) {
            controller.enqueue(encoder.encode('{"success":'));
            setTimeout(() => {
                controller.enqueue(encoder.encode('true}'));
                controller.close();
            }, 35);
        }
    }), { headers: { 'content-type': 'application/json', 'cache-control': 'no-store' } });
    const result = await request(config.base, config.a, path, delayed);
    assert.deepEqual(result.body, { success: true });
    assert.ok(result.elapsed >= 20, 'Header-only timing would miss delayed body.');
    const oversized = async () => new Response(new ReadableStream({
        start(controller) {
            controller.enqueue(new Uint8Array(1_100_000));
            controller.enqueue(new Uint8Array(1_100_000));
            controller.close();
        }
    }), { headers: { 'content-type': 'text/csv' } });
    await assert.rejects(request(config.base, config.a, path, oversized),
        /Oversized streamed response rejected/);
    const falseLength = async () => new Response('small', {
        headers: { 'content-length': '2000001', 'content-type': 'text/csv' }
    });
    await assert.rejects(request(config.base, config.a, path, falseLength),
        /Invalid or oversized response length/);
    const malformed = async () => new Response('{invalid', {
        headers: { 'content-type': 'application/json' }
    });
    await assert.rejects(request(config.base, config.a, path, malformed),
        /Invalid JSON response/);
});

test('concurrent tenant replay emits bounded per-scenario percentiles without session data', async () => {
    const { runAcceptance } = await import(source);
    const report = await runAcceptance({ ...config, iterations: 25 }, fakeFetch());
    assert.deepEqual(Object.keys(report.latencyByScenario).sort(),
        ['asset-own', 'asset-register-foreign', 'stock-detail-foreign',
            'stock-own', 'stock-register-own', 'stock-register-foreign'].sort());
    assert.equal(Object.values(report.latencyByScenario)
        .reduce((sum, row) => sum + row.samples, 0), 25);
    assert.ok(Object.values(report.latencyByScenario).every(row =>
        Number.isFinite(row.p95) && Number.isFinite(row.p99) &&
        row.p99 >= row.p95 && row.samples > 0));
    assert.ok(report.replayWallMs >= 0);
    assert.ok(Number.isFinite(report.replayRequestsPerSecond));
    assert.ok(!JSON.stringify(report).includes('a-session') &&
        !JSON.stringify(report).includes('b-session') &&
        !JSON.stringify(report).includes(stock));
});

test('HTTP acceptance rejects encoded path escapes before sending a tenant cookie', async () => {
    const { request } = await import(source);
    const noNetwork = async () => { throw new Error('Unsafe URL reached network.'); };
    for (const path of [
        '/api/v3/enterprise/ledger-trace/%2e%2e/admin',
        '/api/v3/enterprise/ledger-trace/%2fadmin',
        '/api/procurement/purchase-orders/%5cadmin',
        '/api/v3/enterprise/ledger-trace/' + stock + '#fragment'
    ]) await assert.rejects(request(config.base, config.a, path, noNetwork),
        /Invalid API path/);
});

test('mixed-tenant replay fails before network when scenarios or concurrency are missing', async () => {
    const { runAcceptance, validateConfig } = await import(source);
    const noNetwork = async () => { throw new Error('Incomplete replay reached network.'); };
    await assert.rejects(runAcceptance({ ...config, iterations: 4 }, noNetwork),
        /Mixed-tenant replay requires every scenario/);
    await assert.rejects(runAcceptance({ ...config, concurrency: 1 }, noNetwork),
        /Mixed-tenant replay requires every scenario/);
    await assert.rejects(runAcceptance({ ...config, procurement: stock,
        iterations: 6 }, noNetwork), /Mixed-tenant replay requires every scenario/);
    const env = { BASE_URL: 'http://localhost:3000/',
        TENANT_A_COOKIE: config.a, TENANT_B_COOKIE: config.b,
        STOCK_REFERENCE: stock, ASSET_REFERENCE: asset,
        FROM: config.from, TO: config.to };
    assert.throws(() => validateConfig({ ...env, ITERATIONS: '4' }),
        /Iterations must be 5..500/);
    assert.throws(() => validateConfig({ ...env, CONCURRENCY: '1' }),
        /Concurrency must be 2..16/);
});

test('baseline rejects non-seeded tenant A asset reference in foreign JSON and CSV', async () => {
    const { runAcceptance } = await import(source);
    await assert.rejects(runAcceptance(config, fakeFetch({
        additionalAsset: true, leakAdditionalAsset: true
    })), /Foreign register leaked tenant A evidence/);
});

test('concurrent foreign asset register rejects a non-seeded owner reference', async () => {
    const { runAcceptance } = await import(source);
    const baseline = fakeFetch({ additionalAsset: true });
    const leaking = fakeFetch({ additionalAsset: true, leakAdditionalAsset: true });
    let foreignAssetReads = 0;
    const fetcher = (url, options) => {
        const parsed = new URL(url);
        return parsed.pathname.endsWith('/register') &&
            parsed.searchParams.get('kind') === 'asset' &&
            parsed.searchParams.get('pageSize') === '25' &&
            options.headers.Cookie === 'b-session' && ++foreignAssetReads > 1
            ? leaking(url, options) : baseline(url, options);
    };
    await assert.rejects(runAcceptance({ ...config, iterations: 25 }, fetcher),
        /Concurrent cross-tenant register disclosed/);
});

test('baseline foreign CSV cannot contradict foreign register JSON', async () => {
    const { runAcceptance } = await import(source);
    const baseline = fakeFetch();
    const inconsistent = fakeFetch({ leakAdditionalAsset: true });
    const fetcher = (url, options) => {
        const parsed = new URL(url);
        return parsed.pathname.endsWith('/register/csv') &&
            parsed.searchParams.get('kind') === 'asset' &&
            options.headers.Cookie === 'b-session'
            ? inconsistent(url, options) : baseline(url, options);
    };
    await assert.rejects(runAcceptance(config, fetcher),
        /Foreign register CSV leaked tenant A evidence or disagrees with JSON/);
});

function populatedTenantRegister({ leakOnReplay = false, cacheOnReplay = false } = {}) {
    const baseline = fakeFetch();
    const foreignAsset = '33333333-3333-4333-8333-333333333333';
    const foreignStock = '44444444-4444-4444-8444-444444444444';
    const row = (reference, kind, number) => ({
        reference, number, date: '2026-10-31',
        campusId: kind === 'asset' ? 10 : null,
        state: 'Posted', recordedValue: '25.0000000000',
        lineCount: 1, evidenceStatus: 'ExplicitBalancedReference'
    });
    let foreignAssetReplayReads = 0;
    return async (url, options) => {
        const parsed = new URL(url);
        if (!parsed.pathname.includes('/register')) return baseline(url, options);
        const pageSize = Number(parsed.searchParams.get('pageSize') || 50);
        if (pageSize > 50) return baseline(url, options);
        const kind = parsed.searchParams.get('kind');
        const foreign = options.headers.Cookie === 'b-session';
        // A's non-seeded asset evidence is on page 2. The concurrent 25-row
        // tenant-B probe must still reject it after the 50-row full-page audit.
        const own = kind === 'asset'
            ? Array.from({ length: 55 }, (_, i) => row(
                i === 0 ? asset : i === 54 ? otherAsset :
                    (5000 + i).toString(16).padStart(8, '0') +
                        '-2222-4222-8222-222222222222',
                kind, 'ASSET-' + (i + 1)))
            : [row(stock, kind, 'STOCK-1')];
        const other = kind === 'asset'
            ? [row(foreignAsset, kind, 'FOREIGN-ASSET')]
            : [row(foreignStock, kind, 'FOREIGN-STOCK')];
        const items = (foreign ? other : own).map(x => ({ ...x }));
        if (foreign && kind === 'asset' && pageSize === 25 &&
            leakOnReplay && ++foreignAssetReplayReads > 1)
            items[0].reference = otherAsset;
        const page = Number(parsed.searchParams.get('page') || 1);
        const pageItems = items.slice((page - 1) * pageSize, page * pageSize);
        const isCsv = parsed.pathname.endsWith('/csv');
        const headers = {
            'content-type': isCsv ? 'text/csv' : 'application/json',
            'cache-control': foreign && pageSize === 25 && cacheOnReplay
                ? 'public, max-age=60' : 'no-store'
        };
        if (isCsv) {
            const csv = 'Kind,Reference,Number,Date,Campus,State,RecordedValue,Lines,EvidenceStatus\n' +
                pageItems.map(x => [kind, x.reference, x.number, x.date,
                    x.campusId ?? '', x.state, x.recordedValue,
                    x.lineCount, x.evidenceStatus].join(',')).join('\n');
            return new Response(csv, { status: 200, headers });
        }
        return new Response(JSON.stringify({ success: true, data: {
            kind, from: parsed.searchParams.get('from'),
            to: parsed.searchParams.get('to'), page, pageSize,
            totalCount: items.length, items: pageItems
        } }), { status: 200, headers });
    };
}

test('all-page audit with populated foreign tenant retains owner references for concurrent replay', async () => {
    const { runAcceptance } = await import(source);
    const report = await runAcceptance({ ...config, pagedAudit: true,
        iterations: 25 }, populatedTenantRegister());
    assert.deepEqual(report.registerCounts, [
        { kind: 'stock', count: 1, pagesAudited: 1 },
        { kind: 'asset', count: 55, pagesAudited: 2 }
    ]);
    assert.ok(report.maxClientInFlight >= 2);
    assert.ok(!JSON.stringify(report).includes(otherAsset),
        'Owner reference sets must not be printed in acceptance reports.');
});

test('all-page audit rejects a non-seeded owner asset leaked only during concurrent replay', async () => {
    const { runAcceptance } = await import(source);
    await assert.rejects(runAcceptance({ ...config, pagedAudit: true,
        iterations: 25 }, populatedTenantRegister({ leakOnReplay: true })),
    /Concurrent cross-tenant register disclosed/);
});

test('mixed-tenant replay rejects cacheable private evidence after safe baseline reads', async () => {
    const { runAcceptance } = await import(source);
    await assert.rejects(runAcceptance({ ...config, pagedAudit: true,
        iterations: 25 }, populatedTenantRegister({ cacheOnReplay: true })),
    /Private response must not be cached/);
});

test('concurrent foreign stock register rejects owner evidence after a safe baseline', async () => {
    const { runAcceptance } = await import(source);
    const baseline = fakeFetch();
    const leaking = fakeFetch({ leak: true });
    let foreignStockReads = 0;
    const fetcher = (url, options) => {
        const parsed = new URL(url);
        return parsed.pathname.endsWith('/register') &&
            parsed.searchParams.get('kind') === 'stock' &&
            parsed.searchParams.get('pageSize') === '25' &&
            options.headers.Cookie === 'b-session' && ++foreignStockReads > 1
            ? leaking(url, options) : baseline(url, options);
    };
    await assert.rejects(runAcceptance({ ...config, iterations: 24 }, fetcher),
        /Concurrent cross-tenant register disclosed/);
});

test('private register acceptance rejects no-cache-only and contradictory public cache policy', async () => {
    const { runAcceptance } = await import(source);
    const baseline = fakeFetch();
    for (const policy of ['no-cache', 'public, no-store', 'no-store, s-maxage=60',
        'x-no-store']) {
        const fetcher = async (url, options) => {
            const response = await baseline(url, options);
            if (response.status !== 200 || !url.includes('/register')) return response;
            return new Response(await response.text(), {
                status: response.status, headers: {
                    ...Object.fromEntries(response.headers), 'cache-control': policy
                }
            });
        };
        await assert.rejects(runAcceptance(config, fetcher),
            /Private response must not be cached/);
    }
    const accepted = async (url, options) => {
        const response = await baseline(url, options);
        if (response.status !== 200) return response;
        return new Response(await response.text(), {
            status: response.status, headers: {
                ...Object.fromEntries(response.headers),
                'cache-control': 'private, no-cache, no-store, max-age=0'
            }
        });
    };
    assert.equal((await runAcceptance(config, accepted)).errors, 0);
});

test('denied foreign detail cannot carry source data, even with HTTP 404', async () => {
    const { runAcceptance } = await import(source);
    const baseline = fakeFetch();
    const wrap = body => async (url, options) => {
        if (options.headers.Cookie === 'b-session' &&
            new URL(url).pathname.endsWith('/' + stock))
            return new Response(JSON.stringify(body), { status: 404, headers: {
                'content-type': 'application/json', 'cache-control': 'no-store'
            } });
        return baseline(url, options);
    };
    await assert.rejects(runAcceptance(config, wrap({
        success: false, data: { movementReference: stock }
    })), /Denied response contained private evidence/);
    await assert.rejects(runAcceptance(config, wrap({
        success: false, data: null, message: 'Not found: ' + stock
    })), /Denied response exposed private reference/);
});

test('denied CSV must not expose a private reference or permit caching', async () => {
    const { runAcceptance } = await import(source);
    const baseline = fakeFetch();
    const wrap = (body, cache) => async (url, options) => {
        if (options.headers.Cookie === 'b-session' &&
            new URL(url).pathname.endsWith('/' + stock + '/csv'))
            return new Response(body, { status: 404, headers: {
                'content-type': 'text/csv', 'cache-control': cache
            } });
        return baseline(url, options);
    };
    await assert.rejects(runAcceptance(config,
        wrap('Evidence,Reference\\nStock,' + stock, 'no-store')),
        /Denied response exposed private reference/);
    await assert.rejects(runAcceptance(config, wrap('', 'public, max-age=60')),
        /Private response must not be cached/);
});

test('restricted-role denial rejects evidence-bearing payloads and unsafe cache', async () => {
    const { runAcceptance } = await import(source);
    const baseline = fakeFetch();
    const extended = { ...config, restricted: 'restricted-session' };
    const wrap = (body, cache) => async (url, options) => {
        if (options.headers.Cookie === 'restricted-session')
            return new Response(JSON.stringify(body), { status: 403, headers: {
                'content-type': 'application/json', 'cache-control': cache
            } });
        return baseline(url, options);
    };
    await assert.rejects(runAcceptance(extended, wrap({
        success: false, data: { items: [{ reference: stock }] }
    }, 'no-store')), /Denied response contained private evidence/);
    await assert.rejects(runAcceptance(extended, wrap({
        success: false, data: null
    }, 'no-cache')), /Private response must not be cached/);
});


test('concurrent authorized stock detail rejects wrong-tenant success after safe baseline', async () => {
    const { runAcceptance } = await import(source);
    const baseline = fakeFetch();
    let stockReads = 0;
    const fetcher = (url, options) => {
        if (options.headers.Cookie === 'a-session' &&
            new URL(url).pathname.endsWith('/' + stock) && ++stockReads > 1)
            return new Response(JSON.stringify({ success: true,
                data: { movementReference: otherAsset } }), { status: 200,
                headers: { 'content-type': 'application/json', 'cache-control': 'no-store' } });
        return baseline(url, options);
    };
    await assert.rejects(runAcceptance(config, fetcher),
        /Concurrent authorized detail changed source or Finance evidence/);
});

test('concurrent authorized asset detail rejects changed Finance status under same reference', async () => {
    const { runAcceptance } = await import(source);
    const baseline = fakeFetch();
    let assetReads = 0;
    const fetcher = (url, options) => {
        if (options.headers.Cookie === 'a-session' &&
            new URL(url).pathname.endsWith('/asset-depreciation/' + asset) && ++assetReads > 1)
            return new Response(JSON.stringify({ success: true,
                data: { runReference: asset, financeEvidenceStatus: 'EvidenceConflict' } }),
                { status: 200, headers: { 'content-type': 'application/json',
                    'cache-control': 'no-store' } });
        return baseline(url, options);
    };
    await assert.rejects(runAcceptance(config, fetcher),
        /Concurrent authorized detail changed source or Finance evidence/);
});

test('concurrent authorized register rejects source amount drift despite unchanged UUID', async () => {
    const { runAcceptance } = await import(source);
    const baseline = fakeFetch();
    const fetcher = async (url, options) => {
        const parsed = new URL(url);
        if (options.headers.Cookie === 'a-session' &&
            parsed.pathname.endsWith('/register') &&
            parsed.searchParams.get('kind') === 'stock' &&
            parsed.searchParams.get('pageSize') === '25') {
            const response = await baseline(url, options);
            const payload = await response.json();
            payload.data.items[0].recordedValue = '999.0000000000';
            return new Response(JSON.stringify(payload), { status: 200,
                headers: { 'content-type': 'application/json', 'cache-control': 'no-store' } });
        }
        return baseline(url, options);
    };
    await assert.rejects(runAcceptance(config, fetcher),
        /Concurrent authorized register changed owner evidence/);
});

test('bounded audit concurrent own register rejects a foreign success row after safe 50-row audit', async () => {
    const { runAcceptance } = await import(source);
    const baseline = populatedTenantRegister();
    const fetcher = async (url, options) => {
        const parsed = new URL(url);
        if (options.headers.Cookie === 'a-session' &&
            parsed.pathname.endsWith('/register') &&
            parsed.searchParams.get('kind') === 'stock' &&
            parsed.searchParams.get('pageSize') === '25') {
            const response = await baseline(url, options);
            const payload = await response.json();
            payload.data.items[0].reference = '44444444-4444-4444-8444-444444444444';
            return new Response(JSON.stringify(payload), { status: 200,
                headers: { 'content-type': 'application/json', 'cache-control': 'no-store' } });
        }
        return baseline(url, options);
    };
    await assert.rejects(runAcceptance({ ...config, pagedAudit: true }, fetcher),
        /Concurrent authorized register changed owner evidence/);
});

test('concurrent foreign denial rejects a non-seeded owner asset reference in error text', async () => {
    const { runAcceptance } = await import(source);
    const baseline = fakeFetch({ additionalAsset: true });
    const fetcher = (url, options) => {
        if (options.headers.Cookie === 'b-session' &&
            new URL(url).pathname.endsWith('/' + stock))
            return new Response(JSON.stringify({ success: false,
                message: 'Missing source: ' + otherAsset }), { status: 404,
                headers: { 'content-type': 'application/json', 'cache-control': 'no-store' } });
        return baseline(url, options);
    };
    await assert.rejects(runAcceptance({ ...config, iterations: 24 }, fetcher),
        /Denied response exposed private reference/);
});

test('authorized replay rejects nested source, amount and checker drift despite 200/no-store', async () => {
    const { assertStableReplay } = await import(source);
    const baseline = { orderReference: stock, state: 'Approved',
        lines: [{ lineNo: 1, amount: '25.0000000000' }],
        checker: { id: 42, status: 'Approved' } };
    const response = data => ({ status: 200, body: { success: true, data } });
    assert.doesNotThrow(() => assertStableReplay(response({
        checker: { status: 'Approved', id: 42 },
        lines: [{ amount: '25.0000000000', lineNo: 1 }],
        state: 'Approved', orderReference: stock
    }), baseline));
    for (const data of [
        { ...baseline, lines: [{ lineNo: 1, amount: '26.0000000000' }] },
        { ...baseline, checker: { id: 43, status: 'Approved' } },
        { ...baseline, orderReference: otherAsset }
    ]) assert.throws(() => assertStableReplay(response(data), baseline),
        /Concurrent authorized evidence changed/);
    assert.throws(() => assertStableReplay({
        status: 403, body: { success: false, data: null }
    }, baseline), /Concurrent authorized evidence changed/);
});

test('concurrent foreign stock register rejects same-reference Finance amount drift', async () => {
    const { runAcceptance } = await import(source);
    const baseline = populatedTenantRegister();
    let reads = 0;
    const fetcher = async (url, options) => {
        const parsed = new URL(url);
        if (options.headers.Cookie === 'b-session' &&
            parsed.pathname.endsWith('/register') &&
            parsed.searchParams.get('kind') === 'stock' &&
            parsed.searchParams.get('pageSize') === '25' && ++reads > 1) {
            const response = await baseline(url, options);
            const payload = await response.json();
            payload.data.items[0].recordedValue = '999.0000000000';
            return new Response(JSON.stringify(payload), { status: 200,
                headers: { 'content-type': 'application/json', 'cache-control': 'no-store' } });
        }
        return baseline(url, options);
    };
    await assert.rejects(runAcceptance({ ...config, iterations: 24 }, fetcher),
        /Concurrent cross-tenant register disclosed or pagination invalid/);
});

test('concurrent foreign asset register rejects stale date-window success', async () => {
    const { runAcceptance } = await import(source);
    const baseline = populatedTenantRegister();
    let reads = 0;
    const fetcher = async (url, options) => {
        const parsed = new URL(url);
        if (options.headers.Cookie === 'b-session' &&
            parsed.pathname.endsWith('/register') &&
            parsed.searchParams.get('kind') === 'asset' &&
            parsed.searchParams.get('pageSize') === '25' && ++reads > 1) {
            const response = await baseline(url, options);
            const payload = await response.json();
            payload.data.from = '2026-09-01';
            return new Response(JSON.stringify(payload), { status: 200,
                headers: { 'content-type': 'application/json', 'cache-control': 'no-store' } });
        }
        return baseline(url, options);
    };
    await assert.rejects(runAcceptance({ ...config, iterations: 24 }, fetcher),
        /Concurrent cross-tenant register disclosed or pagination invalid/);
});

const nestedFinanceRef = '77777777-7777-4777-8777-777777777777';
function nestedFinanceDeniedFetch({ concurrentOnly = false } = {}) {
    const baseline = fakeFetch();
    let foreignStockReads = 0;
    return async (url, options) => {
        const path = new URL(url).pathname;
        if (path.endsWith('/' + stock) && options.headers.Cookie === 'a-session') {
            const response = await baseline(url, options);
            const payload = await response.json();
            payload.data.financeEvidence = { reversal: { sourceReference: nestedFinanceRef } };
            return new Response(JSON.stringify(payload), { status: 200, headers: {
                'content-type': 'application/json', 'cache-control': 'no-store'
            } });
        }
        if (path.endsWith('/' + stock) && options.headers.Cookie === 'b-session' &&
            (!concurrentOnly || ++foreignStockReads > 1))
            return new Response(JSON.stringify({ success: false,
                message: 'Missing source: ' + nestedFinanceRef }), { status: 404, headers: {
                'content-type': 'application/json', 'cache-control': 'no-store'
            } });
        return baseline(url, options);
    };
}

test('denied baseline cannot echo nested Finance UUID absent from stock register', async () => {
    const { runAcceptance } = await import(source);
    await assert.rejects(runAcceptance(config, nestedFinanceDeniedFetch()),
        /Denied response exposed private reference/);
});

test('concurrent foreign denial cannot echo nested Finance UUID after safe baseline', async () => {
    const { runAcceptance } = await import(source);
    await assert.rejects(runAcceptance({ ...config, iterations: 24 },
        nestedFinanceDeniedFetch({ concurrentOnly: true })),
        /Denied response exposed private reference/);
});

test('normal first-page CSV checks non-reference fields for both tenants', async () => {
    const { runAcceptance } = await import(source);
    for (const cookie of ['a-session', 'b-session']) {
        const baseline = cookie === 'a-session' ? fakeFetch() : populatedTenantRegister();
        const fetcher = async (url, options) => {
            const response = await baseline(url, options);
            const parsed = new URL(url);
            if (options.headers.Cookie !== cookie ||
                !parsed.pathname.endsWith('/register/csv') ||
                parsed.searchParams.get('kind') !== 'stock') return response;
            const body = (await response.text()).replace(
                cookie === 'a-session' ? 'STOCK-1' : 'FOREIGN-STOCK', 'FORGED-NUMBER');
            return new Response(body, { status: 200, headers: {
                'content-type': 'text/csv', 'cache-control': 'no-store'
            } });
        };
        await assert.rejects(runAcceptance(config, fetcher),
            /Register CSV does not match authorized JSON page/);
    }
});

test('normal first-page register rejects unneutralized CSV formula with matching JSON', async () => {
    const { runAcceptance } = await import(source);
    const baseline = fakeFetch();
    const fetcher = async (url, options) => {
        const response = await baseline(url, options);
        const parsed = new URL(url);
        if (options.headers.Cookie !== 'a-session' ||
            !parsed.pathname.includes('/register') ||
            parsed.searchParams.get('kind') !== 'stock') return response;
        if (parsed.pathname.endsWith('/register/csv'))
            return new Response((await response.text()).replace('STOCK-1', '=1+1'),
                { status: 200, headers: {
                    'content-type': 'text/csv', 'cache-control': 'no-store'
                } });
        if (parsed.pathname.endsWith('/register')) {
            const payload = await response.json();
            payload.data.items[0].number = '=1+1';
            return new Response(JSON.stringify(payload), { status: 200, headers: {
                'content-type': 'application/json', 'cache-control': 'no-store'
            } });
        }
        return response;
    };
    await assert.rejects(runAcceptance(config, fetcher),
        /Register CSV does not match authorized JSON page/);
});
