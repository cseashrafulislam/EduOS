'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const { percentile, privateNoStore, runSyntheticReadLoad } =
    require('../../tests/browser/enterprise-trace-readonly-load.cjs');
const config = {
    base: 'https://qa.eduos.example',
    refs: ['11111111-1111-4111-8111-111111111111',
        '22222222-2222-4222-8222-222222222222',
        '33333333-3333-4333-8333-333333333333'],
    from: '2026-10-01', to: '2026-10-31', campus: '10',
    privateRefs: new Set(['11111111-1111-4111-8111-111111111111',
        '22222222-2222-4222-8222-222222222222',
        '33333333-3333-4333-8333-333333333333',
        '99999999-9999-4999-8999-999999999999'])
};
const headers = value => new Headers({ 'cache-control': value });
function fakeFetch(overrides = {}) {
    return async (url, options) => {
        assert.equal(options.method, 'GET');
        assert(['owner-label', 'foreign-label', 'restricted-label'].includes(options.headers.Cookie));
        assert(url.startsWith(config.base + '/api/v3/enterprise/ledger-trace/'));
        const foreign = options.headers.Cookie === 'foreign-label';
        const restricted = options.headers.Cookie === 'restricted-label';
        const isRegister = url.includes('/register?');
        const denial = (foreign || restricted) && !isRegister;
        const sourceRef = config.refs.find(reference => url.endsWith('/' + reference));
        const status = overrides.status ?? (denial ? 404 : 200);
        const kind = url.includes('kind=asset') ? 'asset' : 'stock';
        const detailKey = url.includes('/asset-depreciation/') ? 'runReference' :
            url.includes('/procurement/') ? 'orderReference' : 'movementReference';
        const data = isRegister ?
            { kind, from: config.from, to: config.to, page: 1, pageSize: 25,
                totalCount: 0, items: [] } :
            { [detailKey]: sourceRef || config.refs[0] };
        const body = overrides.body ?? (denial ? '{"success":false}' :
            JSON.stringify({ success: true, data }));
        const contentType = overrides.contentType ?? 'application/json; charset=utf-8';
        return new Response(body, { status, headers: { 'cache-control': overrides.cache ?? 'private, no-store',
            'content-type': contentType } });
    };
}
test('nearest-rank p95/p99 are deterministic and never mutate samples', () => {
    const samples = Array.from({ length: 100 }, (_, i) => 100 - i);
    assert.equal(percentile(samples, 95), 95);
    assert.equal(percentile(samples, 99), 99);
    assert.equal(samples[0], 100);
    assert.throws(() => percentile([], 95));
    assert.throws(() => percentile([1, -2], 95));
});
test('read-only synthetic load exercises five scoped lanes without secrets', async () => {
    const result = await runSyntheticReadLoad(config, fakeFetch(), { iterations: 3, concurrency: 2 });
    assert.equal(result.requests, 33);
    assert(result.peakInFlight <= 2);
    assert.equal(result.metrics.length, 11);
    assert(result.metrics.some(row => row.lane === 'owner-procurement-detail'));
    assert(result.metrics.some(row => row.lane === 'restricted-owner-asset-denial'));
    assert(Number.isFinite(result.requestsPerSecond) && result.elapsedMs >= 0);
    assert(result.metrics.every(row => row.requests === 3 && row.p95Ms >= 0 && row.p99Ms >= 0));
    assert(!JSON.stringify(result).includes(config.refs[0]));
});
test('private cache guard rejects shared and stale cache despite no-store', () => {
    for (const value of ['public, no-store', 'private, max-age=60, no-store',
        'no-store, s-maxage=5', 'no-store, stale-while-revalidate=30',
        'no-store, stale-if-error=30', 'no-cache'])
        assert.equal(privateNoStore(headers(value)), false, value);
    assert(privateNoStore(headers('private, no-store, max-age=0')));
});
test('synthetic load fails closed on owner drift, foreign leakage and 200 IDOR', async () => {
    for (const overrides of [
        { body: '{"items":[]}' },
        { body: JSON.stringify({ reference: config.refs[0], nested: [...config.privateRefs][1] }) },
        { status: 200 },
        { cache: 'public, no-store' },
        { cache: 'private, no-store, max-age=60' },
        { contentType: 'text/html' },
        { body: 'x'.repeat(2_000_001) }
    ]) await assert.rejects(runSyntheticReadLoad(config, fakeFetch(overrides),
        { iterations: 2, concurrency: 2 }));
});
test('load is bounded before issuing requests', async () => {
    let called = 0;
    const fetch = async () => { called++; return new Response('{}'); };
    for (const opts of [{ iterations: 0 }, { iterations: 14 }, { iterations: 61 },
        { concurrency: 0 }, { concurrency: 5 }, { iterations: 1.5 }])
        await assert.rejects(runSyntheticReadLoad(config, fetch, opts), /unbounded/);
    assert.equal(called, 0);
});

test('all source kinds exercise independent owner, foreign and restricted sessions', async () => {
    const calls = [];
    const fetch = async (url, options) => {
        calls.push({ url, session: options.headers.Cookie });
        return fakeFetch()(url, options);
    };
    const result = await runSyntheticReadLoad(config, fetch, { iterations: 2, concurrency: 3 });
    assert.equal(result.requests, 22);
    for (const [kind, prefix, reference] of [
        ['stock', '', config.refs[0]],
        ['asset', 'asset-depreciation/', config.refs[1]],
        ['procurement', 'procurement/', config.refs[2]]
    ]) {
        const target = '/api/v3/enterprise/ledger-trace/' + prefix + reference;
        for (const session of ['owner-label', 'foreign-label', 'restricted-label'])
            assert.equal(calls.filter(call => call.session === session &&
                call.url.endsWith(target)).length, 2, kind + ' ' + session);
    }
    assert.equal(calls.length, 22);
});
test('foreign and restricted asset/procurement redacted 200 responses fail closed', async () => {
    for (const [route, session] of [
        ['asset-depreciation/', 'foreign-label'],
        ['procurement/', 'foreign-label'],
        ['asset-depreciation/', 'restricted-label'],
        ['procurement/', 'restricted-label']
    ]) {
        const fetch = async (url, options) => {
            if (url.includes(route) && options.headers.Cookie === session)
                return new Response('{"success":true}', { status: 200,
                    headers: { 'cache-control': 'private, no-store',
                        'content-type': 'application/json' } });
            return fakeFetch()(url, options);
        };
        await assert.rejects(runSyntheticReadLoad(config, fetch,
            { iterations: 2, concurrency: 2 }), /unexpected authorization status/);
    }
});
test('authorized source evidence rejects HTML login and plaintext responses', async () => {
    for (const contentType of ['text/html', 'text/plain', 'application/octet-stream'])
        await assert.rejects(runSyntheticReadLoad(config,
            fakeFetch({ contentType }), { iterations: 2, concurrency: 2 }),
            /not JSON/);
});
test('default read-only request budget is 132 and cannot exceed 143', async () => {
    const result = await runSyntheticReadLoad(config, fakeFetch());
    assert.equal(result.requests, 132);
    assert(result.peakInFlight <= 4);
    assert(result.metrics.every(row => row.requests === 12));
    const maximum = await runSyntheticReadLoad(config, fakeFetch(),
        { iterations: 13, concurrency: 4 });
    assert.equal(maximum.requests, 143);
});

test('owner HTTP 200 requires successful canonical source data, not UUID in an error', async () => {
    for (const body of [
        JSON.stringify({ reference: config.refs[0] }),
        JSON.stringify({ success: true, data: { error: config.refs[0] } }),
        JSON.stringify({ success: true, data: { movementReference: config.refs[1] } }),
        '{"success":true,"data":',
        JSON.stringify({ success: false, data: { movementReference: config.refs[0] } })
    ])
        await assert.rejects(runSyntheticReadLoad(config, fakeFetch({ body }),
            { iterations: 2, concurrency: 2 }));
});
test('register scope, pagination, duplicate references and campus bleed fail closed', async () => {
    const base = { kind: 'asset', from: config.from, to: config.to,
        page: 1, pageSize: 25, totalCount: 1,
        items: [{ reference: config.refs[1], campusId: config.campus }] };
    for (const changed of [
        { kind: 'stock' }, { from: '2026-09-01' }, { page: 2 },
        { pageSize: 500 }, { totalCount: 2 }, { totalCount: -1 },
        { items: [{ reference: config.refs[1], campusId: '999' }] },
        { totalCount: 2, items: [base.items[0], base.items[0]] }
    ]) {
        const fetch = async (url, options) => {
            if (url.includes('kind=asset'))
                return new Response(JSON.stringify({ success: true, data: { ...base, ...changed } }),
                    { status: 200, headers: { 'cache-control': 'private, no-store',
                        'content-type': 'application/json' } });
            return fakeFetch()(url, options);
        };
        await assert.rejects(runSyntheticReadLoad(config, fetch,
            { iterations: 2, concurrency: 2 }));
    }
});
test('foreign register and denied detail reject Unicode-escaped Finance UUID', async () => {
    const secret = [...config.privateRefs][1];
    const escaped = secret.split('').map(char =>
        '\\u' + char.charCodeAt(0).toString(16).padStart(4, '0')).join('');
    for (const denied of [false, true]) {
        const fetch = async (url, options) => {
            const foreign = options.headers.Cookie === 'foreign-label';
            if (foreign && (denied ? url.endsWith(config.refs[0]) : url.includes('kind=stock'))) {
                const body = denied ? '{"error":"' + escaped + '"}' :
                    '{"success":true,"data":{"kind":"stock","from":"' + config.from +
                    '","to":"' + config.to +
                    '","page":1,"pageSize":25,"totalCount":0,"items":[],"note":"' +
                    escaped + '"}}';
                return new Response(body, { status: denied ? 404 : 200,
                    headers: { 'cache-control': 'private, no-store',
                        'content-type': 'application/json' } });
            }
            return fakeFetch()(url, options);
        };
        await assert.rejects(runSyntheticReadLoad(config, fetch,
            { iterations: 2, concurrency: 2 }), /disclosed private/);
    }
});
test('oversized stream and false content-length fail before unbounded buffering', async () => {
    for (const length of ['2000001', 'bogus', '-1']) {
        const fetch = async (url, options) => {
            const response = await fakeFetch()(url, options);
            const h = new Headers(response.headers);
            h.set('content-length', length);
            return new Response(response.body, { status: response.status, headers: h });
        };
        await assert.rejects(runSyntheticReadLoad(config, fetch,
            { iterations: 2, concurrency: 2 }), /content length/);
    }
    const fetch = async (url, options) => {
        const response = await fakeFetch()(url, options);
        if (!url.endsWith(config.refs[0]) || options.headers.Cookie !== 'owner-label')
            return response;
        return new Response(new ReadableStream({
            start(controller) {
                controller.enqueue(new Uint8Array(1_000_001));
                controller.enqueue(new Uint8Array(1_000_001));
                controller.close();
            }
        }), { status: 200, headers: { 'cache-control': 'private, no-store',
            'content-type': 'application/json' } });
    };
    await assert.rejects(runSyntheticReadLoad(config, fetch,
        { iterations: 2, concurrency: 2 }), /oversized evidence/);
});
test('concurrent owner source evidence drift cannot count as a stable load pass', async () => {
    let revision = 0;
    const fetch = async (url, options) => {
        const response = await fakeFetch()(url, options);
        if (url.endsWith(config.refs[0]) && options.headers.Cookie === 'owner-label')
            return new Response(JSON.stringify({ success: true, data: {
                movementReference: config.refs[0], revision: ++revision } }),
                { status: 200, headers: { 'cache-control': 'private, no-store',
                    'content-type': 'application/json' } });
        return response;
    };
    await assert.rejects(runSyntheticReadLoad(config, fetch,
        { iterations: 2, concurrency: 2 }), /drifted/);
});

test('unsafe origin, path injection and untracked owner UUID fail before network', async () => {
    let calls = 0;
    const fetch = async () => { calls++; throw new Error('network must not run'); };
    for (const change of [
        { base: 'http://qa.eduos.example' },
        { base: 'https://production.eduos.example' },
        { base: 'https://qa.eduos.example/extra' },
        { base: 'https://qa.eduos.example@production.eduos.example' },
        { refs: ['../finance', config.refs[1], config.refs[2]] },
        { refs: [config.refs[0], config.refs[0], config.refs[2]] },
        { privateRefs: new Set([config.refs[0]]) },
        { from: '2026-02-30' }, { to: '2028-12-31' }, { campus: '0' }
    ]) await assert.rejects(runSyntheticReadLoad({ ...config, ...change }, fetch,
        { iterations: 2, concurrency: 2 }));
    assert.equal(calls, 0);
});

test('hung requests expire, abort in-flight work and stop new lane dispatch', async () => {
    let calls = 0, aborted = 0;
    const hung = (_url, options) => {
        calls++;
        return new Promise((_, reject) => {
            options.signal.addEventListener('abort', () => {
                aborted++;
                reject(new Error('aborted synthetic request'));
            }, { once: true });
        });
    };
    await assert.rejects(runSyntheticReadLoad(config, hung,
        { iterations: 2, concurrency: 2, timeoutMs: 40 }), /timed out/);
    assert.equal(calls, 2);
    assert.equal(aborted, 2);
});

test('stalled streamed body expires and cancels without accepting partial evidence', async () => {
    let cancelled = 0;
    const stalled = async () => new Response(new ReadableStream({
        start(controller) { controller.enqueue(new Uint8Array([123])); },
        cancel() { cancelled++; }
    }), { status: 200, headers: {
        'cache-control': 'private, no-store', 'content-type': 'application/json'
    } });
    await assert.rejects(runSyntheticReadLoad(config, stalled,
        { iterations: 2, concurrency: 1, timeoutMs: 40 }), /timed out/);
    assert.equal(cancelled, 1);
});

test('timeout cannot be disabled or expanded beyond the synthetic budget', async () => {
    let calls = 0;
    const fetch = async () => { calls++; throw new Error('network must not run'); };
    for (const timeoutMs of [0, -1, 24, 30001, 1.5, Infinity])
        await assert.rejects(runSyntheticReadLoad(config, fetch,
            { iterations: 2, concurrency: 2, timeoutMs }), /unbounded/);
    assert.equal(calls, 0);
});
