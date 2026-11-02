'use strict';
// Opt-in, synthetic-only read load. Reuses authenticated Playwright cookie jars;
// never transmits literal session Cookie headers or writes business data.
const assert = require('node:assert/strict');
const { performance } = require('node:perf_hooks');

function percentile(values, rank) {
    assert(Array.isArray(values) && values.length > 0 &&
        values.every(value => Number.isFinite(value) && value >= 0),
    'Invalid latency samples');
    assert(rank >= 0 && rank <= 100, 'Invalid percentile');
    const sorted = [...values].sort((a, b) => a - b);
    return sorted[Math.max(0, Math.ceil(rank / 100 * sorted.length) - 1)];
}
function privateNoStore(headers) {
    const directives = String(headers.get('cache-control') || '').toLowerCase()
        .split(',').map(part => part.trim());
    return directives.includes('no-store') && !directives.includes('public') &&
        !directives.some(part => /^(?:s-maxage|stale-if-error|stale-while-revalidate|immutable)\b/.test(part) ||
            (/^max-age\s*=/.test(part) && part.replace(/^max-age\s*=/, '').trim() !== '0'));
}
function lanes(config) {
    const common = 'from=' + encodeURIComponent(config.from) +
        '&to=' + encodeURIComponent(config.to) + '&page=1&pageSize=25';
    const base = config.base + '/api/v3/enterprise/ledger-trace/';
    return [
        { name: 'owner-stock-detail', session: 'owner-label',
            url: base + config.refs[0], statuses: [200], ownerReference: config.refs[0] },
        { name: 'owner-campus-asset-register', session: 'owner-label',
            url: base + 'register?kind=asset&' + common + '&campusId=' + config.campus,
            statuses: [200] },
        { name: 'foreign-own-stock-register', session: 'foreign-label',
            url: base + 'register?kind=stock&' + common, statuses: [200], deniedOwner: true },
        { name: 'foreign-owner-detail-denial', session: 'foreign-label',
            url: base + config.refs[0], statuses: [403, 404], deniedOwner: true },
        { name: 'restricted-owner-detail-denial', session: 'restricted-label',
            url: base + config.refs[0], statuses: [403, 404], deniedOwner: true }
    ];
}
async function runSyntheticReadLoad(config, fetcher, options = {}) {
    assert(config && typeof config.base === 'string' &&
        Array.isArray(config.refs) && config.refs.length >= 3 &&
        config.privateRefs instanceof Set, 'Validated staging configuration required');
    assert(typeof fetcher === 'function', 'Authenticated fetcher required');
    const iterations = options.iterations ?? 30;
    const concurrency = options.concurrency ?? 4;
    assert(Number.isInteger(iterations) && iterations >= 2 && iterations <= 60 &&
        Number.isInteger(concurrency) && concurrency >= 1 && concurrency <= 4,
    'Refusing unbounded synthetic load');
    const definitions = lanes(config);
    const tasks = [];
    for (let i = 0; i < iterations; i++)
        for (const lane of definitions) tasks.push(lane);
    const samples = new Map(definitions.map(lane => [lane.name, []]));
    let cursor = 0, inFlight = 0, peakInFlight = 0, failure = null;
    async function worker() {
        while (!failure && cursor < tasks.length) {
            const lane = tasks[cursor++];
            const start = performance.now();
            inFlight++;
            peakInFlight = Math.max(peakInFlight, inFlight);
            try {
                const response = await fetcher(lane.url, {
                    method: 'GET', headers: { Cookie: lane.session }
                });
                assert(lane.statuses.includes(response.status),
                    'Synthetic read returned an unexpected authorization status');
                assert(privateNoStore(response.headers),
                    'Synthetic read returned cacheable private evidence');
                const body = await response.text();
                assert(Buffer.byteLength(body, 'utf8') <= 2_000_000,
                    'Synthetic read returned oversized evidence');
                const normalized = body.toLowerCase();
                if (lane.ownerReference)
                    assert(normalized.includes(lane.ownerReference.toLowerCase()),
                        'Authorized synthetic source evidence missing');
                if (lane.deniedOwner)
                    for (const reference of config.privateRefs)
                        assert(!normalized.includes(reference.toLowerCase()),
                            'Cross-tenant or restricted synthetic response disclosed owner evidence');
                samples.get(lane.name).push(performance.now() - start);
            } catch (error) {
                failure = error;
            } finally {
                inFlight--;
            }
        }
    }
    await Promise.all(Array.from({ length: concurrency }, () => worker()));
    if (failure) throw failure;
    const metrics = definitions.map(lane => {
        const values = samples.get(lane.name);
        assert.equal(values.length, iterations, 'Incomplete synthetic read sample');
        return { lane: lane.name, requests: values.length,
            p95Ms: Math.round(percentile(values, 95)),
            p99Ms: Math.round(percentile(values, 99)) };
    });
    return { requests: tasks.length, peakInFlight, metrics };
}
module.exports = { percentile, privateNoStore, runSyntheticReadLoad };
