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
        const body = overrides.body ?? (denial ? '{"success":false}' :
            (foreign ? '{"items":[]}' : JSON.stringify({ reference: sourceRef || config.refs[0] })));
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
