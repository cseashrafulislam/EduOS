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
        const denial = (foreign || restricted) && !url.includes('/register?');
        const status = overrides.status ?? (denial ? 404 : 200);
        const body = overrides.body ?? (denial ? '{"success":false}' :
            (foreign ? '{"items":[]}' : JSON.stringify({ reference: config.refs[0] })));
        return new Response(body, { status, headers: headers(overrides.cache ?? 'private, no-store') });
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
    assert.equal(result.requests, 15);
    assert(result.peakInFlight <= 2);
    assert.equal(result.metrics.length, 5);
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
        { body: 'x'.repeat(2_000_001) }
    ]) await assert.rejects(runSyntheticReadLoad(config, fakeFetch(overrides),
        { iterations: 2, concurrency: 2 }));
});
test('load is bounded before issuing requests', async () => {
    let called = 0;
    const fetch = async () => { called++; return new Response('{}'); };
    for (const opts of [{ iterations: 0 }, { iterations: 61 },
        { concurrency: 0 }, { concurrency: 5 }, { iterations: 1.5 }])
        await assert.rejects(runSyntheticReadLoad(config, fetch, opts), /unbounded/);
    assert.equal(called, 0);
});
