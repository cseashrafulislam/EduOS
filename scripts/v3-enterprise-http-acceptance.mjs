#!/usr/bin/env node
// Read-only V3 enterprise reconciliation acceptance against a disposable/staging
// EduOS instance. Never use production credentials or a live customer database.
import { performance } from 'node:perf_hooks';
import { auditRegisterPages } from './v3-enterprise-paged-audit.mjs';
import { auditProcurement } from './v3-enterprise-procurement-http-audit.mjs';
import { auditSupplierCommitment } from './v3-enterprise-supplier-commitment-audit.mjs';
import { pathToFileURL } from 'node:url';

const API = '/api/v3/enterprise/ledger-trace';
const fail = message => { const error = new Error(message); error.name = 'AcceptanceError'; throw error; };
const check = (condition, message) => { if (!condition) fail(message); };
export const percentile = (samples, p) => {
    check(samples.length > 0, 'No latency samples.');
    const sorted = [...samples].sort((a, b) => a - b);
    return Math.round(sorted[Math.ceil((p / 100) * sorted.length) - 1] * 100) / 100;
};

export function parseCsv(input) {
    check(typeof input === 'string' && input.length <= 2_000_000,
        'Invalid or oversized CSV response.');
    const value = input.charCodeAt(0) === 0xFEFF ? input.slice(1) : input;
    const rows = [];
    let row = [], cell = '', quoted = false, closed = false;
    const endCell = () => { row.push(cell); cell = ''; closed = false; };
    const endRow = () => { endCell(); rows.push(row); row = []; };
    for (let i = 0; i < value.length; i++) {
        const c = value[i];
        if (quoted) {
            if (c === '"') {
                if (value[i + 1] === '"') { cell += '"'; i++; }
                else { quoted = false; closed = true; }
            } else cell += c;
            continue;
        }
        if (c === '"') {
            check(!closed && cell.length === 0, 'Malformed CSV quote.');
            quoted = true;
        } else if (c === ',') endCell();
        else if (c === '\n' || c === '\r') {
            endRow();
            if (c === '\r' && value[i + 1] === '\n') i++;
        } else {
            check(!closed, 'Malformed CSV cell.');
            cell += c;
        }
    }
    check(!quoted, 'Unterminated CSV quote.');
    if (cell.length || row.length || closed) endRow();
    check(rows.length > 0, 'Empty CSV response.');
    const header = rows.shift();
    const referenceIndex = header.indexOf('Reference');
    check(referenceIndex >= 0 && rows.every(r => r.length === header.length),
        'CSV header or row width mismatch.');
    return { header, rows, referenceIndex };
}

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
    check(!env.PROCUREMENT_REFERENCE || uuid.test(env.PROCUREMENT_REFERENCE),
        'Invalid procurement reference UUID.');
    check(!env.SUPPLIER_CONFIRMATION_ID || (env.PROCUREMENT_REFERENCE &&
        /^[1-9]\d*$/.test(env.SUPPLIER_CONFIRMATION_ID) &&
        Number.isSafeInteger(Number(env.SUPPLIER_CONFIRMATION_ID))),
        'Supplier confirmation ID requires procurement UUID and positive safe integer.');
    const date = /^\d{4}-\d{2}-\d{2}$/;
    const validDate = value => {
        if (!date.test(value)) return false;
        const parsed = new Date(value + 'T00:00:00Z');
        return !Number.isNaN(parsed.getTime()) &&
            parsed.toISOString().slice(0, 10) === value;
    };
    check(validDate(env.FROM) && validDate(env.TO) && env.FROM <= env.TO &&
        (Date.parse(env.TO) - Date.parse(env.FROM)) / 86400000 <= 366,
        'Invalid or unbounded ISO date range.');
    check(!env.RESTRICTED_COOKIE || (env.RESTRICTED_COOKIE !== env.TENANT_A_COOKIE &&
        env.RESTRICTED_COOKIE !== env.TENANT_B_COOKIE),
        'Restricted-role session must be distinct.');
    const campusA = env.ASSET_CAMPUS_ID, campusOther = env.OTHER_CAMPUS_ID;
    check(Boolean(campusA) === Boolean(campusOther),
        'Both asset campus IDs are required for campus acceptance.');
    if (campusA) check(/^[1-9]\d*$/.test(campusA) &&
        /^[1-9]\d*$/.test(campusOther) && campusA !== campusOther,
        'Asset campus IDs must be distinct positive integers.');
    check(!env.AUDIT_ALL_PAGES || ['0', '1'].includes(env.AUDIT_ALL_PAGES),
        'AUDIT_ALL_PAGES must be 0 or 1.');
    const auditPages = Number(env.AUDIT_MAX_PAGES || 10);
    check(Number.isInteger(auditPages) && auditPages >= 1 && auditPages <= 20,
        'AUDIT_MAX_PAGES must be 1..20.');
    const iterations = Number(env.ITERATIONS || 24);
    const concurrency = Number(env.CONCURRENCY || 4);
    check(Number.isInteger(iterations) && iterations >= 1 && iterations <= 500,
        'Iterations must be 1..500.');
    check(Number.isInteger(concurrency) && concurrency >= 1 && concurrency <= 16,
        'Concurrency must be 1..16.');
    return { base: base.origin, a: env.TENANT_A_COOKIE, b: env.TENANT_B_COOKIE,
        stock: env.STOCK_REFERENCE, asset: env.ASSET_REFERENCE,
        procurement: env.PROCUREMENT_REFERENCE || null,
        supplierConfirmation: env.SUPPLIER_CONFIRMATION_ID ?
            Number(env.SUPPLIER_CONFIRMATION_ID) : null,
        from: env.FROM, to: env.TO, iterations, concurrency,
        restricted: env.RESTRICTED_COOKIE || null,
        assetCampus: campusA || null, otherCampus: campusOther || null,
        pagedAudit: env.AUDIT_ALL_PAGES === '1', auditPages };
}

export async function request(base, cookie, path, fetcher = fetch) {
    const procurementApi = ['/api/procurement/supplier-confirmations',
        '/api/procurement/purchase-orders'];
    check(!path.startsWith('//') && !path.includes('..') && !path.includes('\\') &&
        (path.startsWith(API + '/') || procurementApi.some(prefix =>
            path === prefix || path.startsWith(prefix + '/') || path.startsWith(prefix + '?'))),
        'Invalid API path.');
    const started = performance.now();
    const response = await fetcher(base + path, {
        method: 'GET', redirect: 'manual', cache: 'no-store',
        headers: { Cookie: cookie, Accept: 'application/json, text/csv' },
        signal: AbortSignal.timeout(15000)
    });
    check(response.status < 300 || response.status >= 400, 'Unexpected redirect.');
    const elapsed = performance.now() - started;
    const type = response.headers.get('content-type') || '';
    const contentLength = Number(response.headers.get('content-length') || 0);
    check(contentLength <= 2_000_000, 'Oversized response rejected.');
    const body = type.includes('json') ? await response.json() : await response.text();
    if (typeof body === 'string') check(body.length <= 2_000_000,
        'Oversized response rejected.');
    return { status: response.status, body, type, elapsed,
        cache: response.headers.get('cache-control') || '' };
}

export async function runAcceptance(config, fetcher = fetch) {
    const { base, a, b, stock, asset, procurement = null, supplierConfirmation = null,
        from, to, iterations, concurrency,
        restricted, assetCampus, otherCampus, pagedAudit = false, auditPages = 10 } = config;
    check(!supplierConfirmation || procurement,
        'Supplier confirmation requires canonical procurement reference.');
    const noStore = result => check(result.cache.includes('no-store') ||
        result.cache.includes('no-cache'), 'Private response must not be cached.');
    const references = csv => {
        const parsed = parseCsv(csv);
        return parsed.rows.map(row => row[parsed.referenceIndex].toLowerCase());
    };
    const read = (cookie, path) => request(base, cookie, path, fetcher);
    const assertPrivate = async (path, reference) => {
        const own = await read(a, path);
        check(own.status === 200 && own.body?.success && own.body?.data,
            'Authorized detail not available.');
        noStore(own);
        const foreign = await read(b, path);
        check([403, 404].includes(foreign.status), 'Cross-tenant detail disclosed.');
        const ownCsv = await read(a, path + '/csv');
        check(ownCsv.status === 200 && ownCsv.type.includes('text/csv'),
            'Authorized CSV missing expected evidence.');
        noStore(ownCsv);
        const expected = [reference, ...(own.body.data.journals || [])
            .map(j => j.reference)].map(value => value.toLowerCase());
        const actual = references(ownCsv.body);
        check(actual.length === expected.length &&
            actual.every((value, index) => value === expected[index]),
            'Detail CSV and JSON evidence disagree.');
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
    const procurementResult = procurement ? await auditProcurement({
        read, parseCsv, api: API, reference: procurement, from, to,
        tenantA: a, tenantB: b, restricted
    }) : null;
    const supplierResult = supplierConfirmation ? await auditSupplierCommitment({
        read, parseCsv, api: API, reference: procurement,
        confirmationId: supplierConfirmation, tenantA: a, tenantB: b, restricted
    }) : null;
    const registers = [];
    if (procurementResult) {
        if (pagedAudit) {
            const result = await auditRegisterPages({ read, parseCsv, api: API,
                kind: 'procurement', reference: procurement, from, to,
                tenantA: a, tenantB: b, maxPages: auditPages });
            registers.push({ kind: 'procurement', count: result.count,
                pagesAudited: result.pagesAudited });
        } else registers.push({ kind: 'procurement', count: procurementResult.reportCount });
    }
    for (const [kind, reference] of [['stock', stock], ['asset', asset]]) {
        if (pagedAudit) {
            const result = await auditRegisterPages({ read, parseCsv, api: API, kind, reference,
                from, to, tenantA: a, tenantB: b, maxPages: auditPages });
            registers.push({ kind, count: result.count, pagesAudited: result.pagesAudited });
            continue;
        }
        const query = new URLSearchParams({ kind, from, to, page: '1', pageSize: '50' });
        const path = API + '/register?' + query;
        const own = await read(a, path);
        check(own.status === 200 && own.body?.success && own.body?.data,
            'Authorized register not available.');
        noStore(own);
        const d = own.body.data;
        check(d.page === 1 && d.pageSize === 50 && d.totalCount >= d.items.length &&
            d.items.length <= 50, 'Invalid bounded register pagination.');
        check(d.items.some(row => row.reference?.toLowerCase() === reference.toLowerCase()),
            'Seeded reference missing from first page; choose an isolated test date range.');
        const ownCsv = await read(a, API + '/register/csv?' + query);
        check(ownCsv.status === 200 && ownCsv.type.includes('text/csv'),
            'Register CSV unavailable.');
        noStore(ownCsv);
        const ownRefs = references(ownCsv.body);
        check(ownRefs.length === d.items.length &&
            ownRefs.every((value, index) =>
                value === d.items[index].reference.toLowerCase()),
            'Register CSV does not match authorized JSON page.');
        const foreign = await read(b, path);
        check(foreign.status === 200 && foreign.body?.success &&
            !foreign.body.data.items.some(row => row.reference?.toLowerCase() === reference.toLowerCase()),
            'Foreign register leaked tenant A evidence (or tenant B lacks required entitlement).');
        const foreignCsv = await read(b, API + '/register/csv?' + query);
        check(foreignCsv.status === 200 &&
            !references(foreignCsv.body).includes(reference.toLowerCase()),
            'Foreign register CSV leaked tenant A evidence.');
        registers.push({ kind, count: d.totalCount });
    }
    const invalid = await read(a, API + '/register?' + new URLSearchParams({
        kind: 'stock', from, to, page: '1', pageSize: '1000'
    }));
    check(invalid.status === 400, 'Unbounded page size was accepted.');
    if (restricted) {
        for (const path of [API + '/' + stock,
            API + '/asset-depreciation/' + asset,
            API + '/register?' + new URLSearchParams({ kind: 'stock', from, to }),
            API + '/' + stock + '/csv',
            API + '/register/csv?' + new URLSearchParams({ kind: 'asset', from, to })]) {
            const result = await read(restricted, path);
            check(result.status === 403, 'Restricted role accessed enterprise evidence.');
        }
    }
    if (assetCampus && otherCampus) {
        const campusQuery = campusId => API + '/register?' + new URLSearchParams({
            kind: 'asset', from, to, campusId, page: '1', pageSize: '50'
        });
        const ownCampus = await read(a, campusQuery(assetCampus));
        const other = await read(a, campusQuery(otherCampus));
        check(ownCampus.status === 200 && other.status === 200 &&
            ownCampus.body?.data?.items?.some(x =>
                x.reference.toLowerCase() === asset.toLowerCase()) &&
            !other.body?.data?.items?.some(x =>
                x.reference.toLowerCase() === asset.toLowerCase()),
            'Asset campus filter exposed or hid the seeded asset run.');
        const foreignCampus = await read(b, campusQuery(assetCampus));
        check([403, 404].includes(foreignCampus.status),
            'Foreign tenant accepted another tenant campus ID.');
    }
    const samples = [];
    const paths = [
        { cookie: a, path: API + '/' + stock, own: true },
        { cookie: a, path: API + '/asset-depreciation/' + asset, own: true },
        { cookie: a, path: API + '/register?' + new URLSearchParams({
            kind: 'stock', from, to, page: '1', pageSize: '25' }), own: true },
        { cookie: b, path: API + '/' + stock, foreignDetail: true },
        { cookie: b, path: API + '/register?' + new URLSearchParams({
            kind: 'asset', from, to, page: '1', pageSize: '25' }), foreignRegister: true }
    ];
    if (procurement) {
        paths.push({ cookie: a, path: API + '/procurement/' + procurement, own: true });
        paths.push({ cookie: b, path: API + '/procurement/' + procurement,
            foreignDetail: true });
    }
    if (supplierResult) {
        const path = '/api/procurement/supplier-confirmations/' + supplierConfirmation;
        paths.push({ cookie: a, path, own: true });
        paths.push({ cookie: b, path, foreignDetail: true });
    }
    let cursor = 0;
    const worker = async () => {
        while (cursor < iterations) {
            const index = cursor++;
            const probe = paths[index % paths.length];
            const result = await read(probe.cookie, probe.path);
            if (probe.foreignDetail)
                check([403, 404].includes(result.status),
                    'Concurrent cross-tenant detail disclosed.');
            else if (probe.foreignRegister)
                check(result.status === 200 && result.body?.success &&
                    !result.body.data.items.some(x =>
                        x.reference.toLowerCase() === asset.toLowerCase()),
                    'Concurrent cross-tenant register disclosed.');
            else check(result.status === 200 && result.body?.success,
                'Concurrent authorized read failed.');
            samples.push(result.elapsed);
        }
    };
    await Promise.all(Array.from({ length: Math.min(iterations, concurrency) }, worker));
    return { checks: ['stock and asset detail/CSV IDOR and parity',
            'register JSON/CSV exact-page parity and cache isolation',
            'bounded paging', 'mixed-tenant concurrent replay',
            ...(restricted ? ['restricted-role authorization'] : []),
            ...(assetCampus ? ['asset campus isolation'] : []),
            ...(procurement ? ['procurement source/CSV/register integrity and tenant IDOR'] : []),
            ...(supplierResult ? ['supplier confirmation to PO/enterprise/commitment/export integrity'] : []),
            ...(pagedAudit ? ['bounded all-page JSON/CSV parity and cross-tenant isolation'] : [])],
        registerCounts: registers,
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
