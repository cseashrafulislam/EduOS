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
    // Reuse canonical source URLs; no inferred stock or Finance linkage.
    const sources = [
        { kind: 'stock', route: '', reference: config.refs[0] },
        { kind: 'asset', route: 'asset-depreciation/', reference: config.refs[1] },
        { kind: 'procurement', route: 'procurement/', reference: config.refs[2] }
    ];
    const scoped = sources.flatMap(source => [
        { name: 'owner-' + source.kind + '-detail', session: 'owner-label',
            url: base + source.route + source.reference, statuses: [200],
            kind: 'detail', source: source.kind, ownerReference: source.reference },
        { name: 'foreign-owner-' + source.kind + '-denial', session: 'foreign-label',
            url: base + source.route + source.reference, statuses: [403, 404],
            deniedOwner: true },
        { name: 'restricted-owner-' + source.kind + '-denial', session: 'restricted-label',
            url: base + source.route + source.reference, statuses: [403, 404],
            deniedOwner: true }
    ]);
    return [
        ...scoped,
        { name: 'owner-campus-asset-register', session: 'owner-label',
            url: base + 'register?kind=asset&' + common + '&campusId=' +
                encodeURIComponent(config.campus),
            statuses: [200], kind: 'register', registerKind: 'asset', campus: true },
        { name: 'foreign-own-stock-register', session: 'foreign-label',
            url: base + 'register?kind=stock&' + common,
            statuses: [200], deniedOwner: true, kind: 'register', registerKind: 'stock' }
    ];
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
// Decode JSON string escapes before searching denied/private payloads.
function assertNoPrivateEvidence(body, refs) {
    const forms = [body.toLowerCase()];
    try { forms.push(JSON.stringify(JSON.parse(body)).toLowerCase()); }
    catch { /* Denied endpoints can return plain text. */ }
    for (const ref of refs)
        assert(forms.every(form => !form.includes(ref.toLowerCase())),
            'Synthetic denial disclosed private source or Finance evidence');
}
async function readBoundedBody(response) {
    const length = response.headers.get('content-length');
    assert(length === null || (/^\d+$/.test(length) && Number(length) <= 2_000_000),
        'Invalid or oversized synthetic content length');
    const reader = response.body?.getReader?.();
    if (!reader) {
        const text = await response.text();
        assert(Buffer.byteLength(text, 'utf8') <= 2_000_000,
            'Synthetic read returned oversized evidence');
        return text;
    }
    const decoder = new TextDecoder('utf-8', { fatal: true });
    let text = '', bytes = 0;
    try {
        while (true) {
            const { done, value } = await reader.read();
            if (done) break;
            assert(value instanceof Uint8Array, 'Invalid synthetic response chunk');
            bytes += value.byteLength;
            assert(bytes <= 2_000_000, 'Synthetic read returned oversized evidence');
            text += decoder.decode(value, { stream: true });
        }
        return text + decoder.decode();
    } catch (error) {
        try { await reader.cancel(); } catch { /* Preserve original failure. */ }
        throw error;
    } finally {
        reader.releaseLock();
    }
}
function authorizedFingerprint(lane, body, config) {
    let payload;
    try { payload = JSON.parse(body); }
    catch { assert.fail('Authorized synthetic response is not valid JSON'); }
    assert(payload && payload.success === true && payload.data &&
        typeof payload.data === 'object' && !Array.isArray(payload.data),
    'Authorized synthetic response lacks successful canonical data');
    const data = payload.data;
    if (lane.kind === 'detail') {
        const key = { stock: 'movementReference', asset: 'runReference',
            procurement: 'orderReference' }[lane.source];
        assert(typeof data[key] === 'string' &&
            data[key].toLowerCase() === lane.ownerReference.toLowerCase(),
        'Authorized synthetic source identity mismatch');
    } else {
        assert(data.kind === lane.registerKind &&
            data.from === config.from && data.to === config.to &&
            data.page === 1 && data.pageSize === 25 &&
            Number.isSafeInteger(data.totalCount) && data.totalCount >= 0 &&
            Array.isArray(data.items) &&
            data.items.length === Math.min(25, data.totalCount),
        'Synthetic register scope or pagination mismatch');
        const seen = new Set();
        for (const row of data.items) {
            assert(typeof row.reference === 'string' && UUID.test(row.reference) &&
                !seen.has(row.reference.toLowerCase()),
            'Synthetic register source reference invalid or duplicated');
            seen.add(row.reference.toLowerCase());
            if (lane.campus)
                assert(String(row.campusId) === String(config.campus),
                    'Synthetic register disclosed another campus');
        }
    }
    return JSON.stringify(data);
}

async function runSyntheticReadLoad(config, fetcher, options = {}) {
    assert(config && typeof config.base === 'string' &&
        Array.isArray(config.refs) && config.refs.length >= 3 &&
        config.privateRefs instanceof Set, 'Validated staging configuration required');
    assert(typeof fetcher === 'function', 'Authenticated fetcher required');
    const iterations = options.iterations ?? 12;
    const concurrency = options.concurrency ?? 4;
    assert(Number.isInteger(iterations) && iterations >= 2 && iterations <= 13 &&
        Number.isInteger(concurrency) && concurrency >= 1 && concurrency <= 4,
    'Refusing unbounded synthetic load');
    const definitions = lanes(config);
    const tasks = [];
    for (let i = 0; i < iterations; i++)
        for (const lane of definitions) tasks.push(lane);
    assert(tasks.length <= 150, 'Refusing excessive synthetic request budget');
    const samples = new Map(definitions.map(lane => [lane.name, []]));
    const baselines = new Map();
    let cursor = 0, inFlight = 0, peakInFlight = 0, failure = null;
    const runStarted = performance.now();
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
                if (response.status === 200)
                    assert(/^application\/(?:json|[a-z0-9.+-]+\+json)(?:\s*;|\s*$)/i
                        .test(String(response.headers.get('content-type') || '')),
                    'Authorized synthetic evidence is not JSON');
                const body = await readBoundedBody(response);
                if (lane.deniedOwner) assertNoPrivateEvidence(body, config.privateRefs);
                if (response.status === 200) {
                    const signature = authorizedFingerprint(lane, body, config);
                    if (baselines.has(lane.name))
                        assert.equal(signature, baselines.get(lane.name),
                            'Concurrent synthetic authorized evidence drifted');
                    else baselines.set(lane.name, signature);
                }
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
    const elapsedMs = performance.now() - runStarted;
    const metrics = definitions.map(lane => {
        const values = samples.get(lane.name);
        assert.equal(values.length, iterations, 'Incomplete synthetic read sample');
        return { lane: lane.name, requests: values.length,
            p95Ms: Math.round(percentile(values, 95)),
            p99Ms: Math.round(percentile(values, 99)) };
    });
    return { requests: tasks.length, peakInFlight,
        elapsedMs: Math.round(elapsedMs),
        requestsPerSecond: Math.round(tasks.length / Math.max(elapsedMs, 1) * 1000),
        metrics };
}
module.exports = { percentile, privateNoStore, runSyntheticReadLoad };
