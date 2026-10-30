#!/usr/bin/env node
// Read-only V3 enterprise reconciliation acceptance against a disposable/staging
// EduOS instance. Never use production credentials or a live customer database.
import { performance } from 'node:perf_hooks';
import { pathToFileURL } from 'node:url';

const API = '/api/v3/enterprise/ledger-trace';
const fail = message => { const error = new Error(message); error.name = 'AcceptanceError'; throw error; };
const check = (condition, message) => { if (!condition) fail(message); };
export const percentile = (samples, p) => {
    check(samples.length > 0, 'No latency samples.');
    const sorted = [...samples].sort((a, b) => a - b);
    return Math.round(sorted[Math.ceil((p / 100) * sorted.length) - 1] * 100) / 100;
};

export function validateConfig(env) {
    const required = ['BASE_URL', 'TENANT_A_COOKIE', 'TENANT_B_COOKIE',
        'STOCK_REFERENCE', 'ASSET_REFERENCE', 'FROM', 'TO'];
    for (const key of required) check(env[key]?.trim(), 'Missing EDUOS_V3_' + key + '.');
    const base = new URL(env.BASE_URL);
    check(!base.username && !base.password && !base.search && !base.hash &&
        base.pathname === '/', 'Base URL must contain only an origin.');
    const local = ['localhost', '127.0.0.1', '[::1]'].includes(base.hostname);
    check(local || (base.protocol === 'https:' && base.hostname === env.STAGING_HOST),
        'Refusing non-local host without exact EDUOS_V3_STAGING_HOST HTTPS approval.');
    check(base.protocol === 'http:' || base.protocol === 'https:', 'Unsupported protocol.');
    check(env.TENANT_A_COOKIE !== env.TENANT_B_COOKIE, 'Two distinct tenant sessions required.');
    const uuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    check(uuid.test(env.STOCK_REFERENCE) && uuid.test(env.ASSET_REFERENCE), 'Invalid reference UUID.');
    const date = /^\d{4}-\d{2}-\d{2}$/;
    check(date.test(env.FROM) && date.test(env.TO) && env.FROM <= env.TO,
        'Invalid ISO date range.');
    const iterations = Number(env.ITERATIONS || 24);
    const concurrency = Number(env.CONCURRENCY || 4);
    check(Number.isInteger(iterations) && iterations >= 1 && iterations <= 500,
        'Iterations must be 1..500.');
    check(Number.isInteger(concurrency) && concurrency >= 1 && concurrency <= 16,
        'Concurrency must be 1..16.');
    return { base: base.origin, a: env.TENANT_A_COOKIE, b: env.TENANT_B_COOKIE,
        stock: env.STOCK_REFERENCE, asset: env.ASSET_REFERENCE,
        from: env.FROM, to: env.TO, iterations, concurrency };
}

export async function request(base, cookie, path, fetcher = fetch) {
    check(path.startsWith(API + '/') && !path.startsWith('//'), 'Invalid API path.');
    const started = performance.now();
    const response = await fetcher(base + path, {
        method: 'GET', redirect: 'manual', cache: 'no-store',
        headers: { Cookie: cookie, Accept: 'application/json, text/csv' },
        signal: AbortSignal.timeout(15000)
    });
    check(response.status < 300 || response.status >= 400, 'Unexpected redirect.');
    const elapsed = performance.now() - started;
    const type = response.headers.get('content-type') || '';
    const body = type.includes('json') ? await response.json() : await response.text();
    return { status: response.status, body, type, elapsed,
        cache: response.headers.get('cache-control') || '' };
}

export async function runAcceptance(config, fetcher = fetch) {
    const { base, a, b, stock, asset, from, to, iterations, concurrency } = config;
    const read = (cookie, path) => request(base, cookie, path, fetcher);
    const assertPrivate = async (path, reference) => {
        const own = await read(a, path);
        check(own.status === 200 && own.body?.success && own.body?.data,
            'Authorized detail not available.');
        check((own.cache.includes('no-store') || own.cache.includes('no-cache')),
            'Private detail must not be cached.');
        const foreign = await read(b, path);
        check([403, 404].includes(foreign.status), 'Cross-tenant detail disclosed.');
        const ownCsv = await read(a, path + '/csv');
        check(ownCsv.status === 200 && ownCsv.type.includes('text/csv') &&
            typeof ownCsv.body === 'string' && ownCsv.body.includes(reference),
            'Authorized CSV missing expected evidence.');
        const foreignCsv = await read(b, path + '/csv');
        check([403, 404].includes(foreignCsv.status), 'Cross-tenant CSV disclosed.');
        return own.body.data;
    };
    const stockData = await assertPrivate(API + '/' + stock, stock);
    const assetData = await assertPrivate(API + '/asset-depreciation/' + asset, asset);
    check(stockData.movementReference?.toLowerCase() === stock.toLowerCase(),
        'Stock reference mismatch.');
    check(assetData.runReference?.toLowerCase() === asset.toLowerCase(),
        'Asset reference mismatch.');
    const registers = [];
    for (const [kind, reference] of [['stock', stock], ['asset', asset]]) {
        const query = new URLSearchParams({ kind, from, to, page: '1', pageSize: '50' });
        const path = API + '/register?' + query;
        const own = await read(a, path);
        check(own.status === 200 && own.body?.success && own.body?.data,
            'Authorized register not available.');
        const d = own.body.data;
        check(d.page === 1 && d.pageSize === 50 && d.totalCount >= d.items.length &&
            d.items.length <= 50, 'Invalid bounded register pagination.');
        check(d.items.some(row => row.reference?.toLowerCase() === reference.toLowerCase()),
            'Seeded reference missing from first page; choose an isolated test date range.');
        const ownCsv = await read(a, API + '/register/csv?' + query);
        check(ownCsv.status === 200 && ownCsv.type.includes('text/csv') &&
            ownCsv.body.includes(reference), 'Register CSV does not match authorized page.');
        const foreign = await read(b, path);
        check(foreign.status === 200 && foreign.body?.success &&
            !foreign.body.data.items.some(row => row.reference?.toLowerCase() === reference.toLowerCase()),
            'Foreign register leaked tenant A evidence (or tenant B lacks required entitlement).');
        const foreignCsv = await read(b, API + '/register/csv?' + query);
        check(foreignCsv.status === 200 && !foreignCsv.body.includes(reference),
            'Foreign register CSV leaked tenant A evidence.');
        registers.push({ kind, count: d.totalCount });
    }
    const invalid = await read(a, API + '/register?' + new URLSearchParams({
        kind: 'stock', from, to, page: '1', pageSize: '1000'
    }));
    check(invalid.status === 400, 'Unbounded page size was accepted.');
    const samples = [];
    const paths = [API + '/' + stock, API + '/asset-depreciation/' + asset,
        API + '/register?' + new URLSearchParams({ kind: 'stock', from, to, page: '1', pageSize: '25' })];
    let cursor = 0;
    const worker = async () => {
        while (cursor < iterations) {
            const index = cursor++;
            const result = await read(a, paths[index % paths.length]);
            check(result.status === 200 && result.body?.success,
                'Concurrent read failed; no throughput claim is valid.');
            samples.push(result.elapsed);
        }
    };
    await Promise.all(Array.from({ length: Math.min(iterations, concurrency) }, worker));
    return { checks: ['stock and asset detail/CSV IDOR', 'register JSON/CSV isolation',
            'bounded paging', 'concurrent read replay'], registerCounts: registers,
        measuredHttpOnly: true, samples: samples.length,
        latencyMs: { p50: percentile(samples, 50), p95: percentile(samples, 95),
            p99: percentile(samples, 99) }, errors: 0 };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
    try {
        const env = Object.fromEntries(Object.entries(process.env)
            .filter(([key]) => key.startsWith('EDUOS_V3_'))
            .map(([key, value]) => [key.slice('EDUOS_V3_'.length), value]));
        const report = await runAcceptance(validateConfig(env));
        process.stdout.write(JSON.stringify(report, null, 2) + '\n');
    } catch (error) {
        // Never print request URL, session cookies, response bodies or private data.
        process.stderr.write('V3 acceptance failed: ' + (error.name === 'AcceptanceError'
            ? error.message : 'Network or response failure (details withheld)') + '\n');
        process.exitCode = 1;
    }
}
