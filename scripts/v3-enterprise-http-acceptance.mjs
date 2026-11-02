#!/usr/bin/env node
// Read-only V3 enterprise reconciliation acceptance against a disposable/staging
// EduOS instance. Never use production credentials or a live customer database.
import { performance } from 'node:perf_hooks';
import { auditRegisterPages, assertPrivateCache, assertRegisterCsvParity } from './v3-enterprise-paged-audit.mjs';
import { auditProcurement } from './v3-enterprise-procurement-http-audit.mjs';
import { auditSupplierCommitment } from './v3-enterprise-supplier-commitment-audit.mjs';
import { assertDetailCsvParity } from './v3-enterprise-detail-csv-audit.mjs';
import { pathToFileURL } from 'node:url';

const API = '/api/v3/enterprise/ledger-trace';
const MAX_RESPONSE_BYTES = 2_000_000;
const fail = message => { const error = new Error(message); error.name = 'AcceptanceError'; throw error; };
const check = (condition, message) => { if (!condition) fail(message); };
export const percentile = (samples, p) => {
    check(samples.length > 0, 'No latency samples.');
    const sorted = [...samples].sort((a, b) => a - b);
    return Math.round(sorted[Math.ceil((p / 100) * sorted.length) - 1] * 100) / 100;
};

// Stable, private in-memory fingerprints: detect wrong-tenant success responses
// and source/Finance drift during a read-only concurrent replay. Never report them.
const evidenceSignature = value => JSON.stringify(value, (_, entry) => {
    if (!entry || Array.isArray(entry) || typeof entry !== 'object') return entry;
    return Object.fromEntries(Object.keys(entry).sort().map(key => [key, entry[key]]));
});

// Include nested Finance/source UUIDs absent from register pages in denial guards.
// Keep this set in process only; never print identifiers or private evidence.
export const collectPrivateReferences = (...values) => {
    const references = new Set(), visited = new Set();
    const uuid = /[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}/gi;
    const visit = (value, depth) => {
        check(depth <= 32, 'Authorized evidence nesting exceeds audit limit.');
        if (typeof value === 'string') {
            for (const match of value.match(uuid) || []) references.add(match.toLowerCase());
        } else if (value && typeof value === 'object' && !visited.has(value)) {
            visited.add(value);
            for (const item of Object.values(value)) visit(item, depth + 1);
        }
    };
    for (const value of values) visit(value, 0);
    return references;
};

export const assertStableReplay = (result, baseline, message =
    'Concurrent authorized evidence changed.') => {
    check(result.status === 200 && result.body?.success === true &&
        result.body.data &&
        evidenceSignature(result.body.data) === evidenceSignature(baseline), message);
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
    check(Number.isInteger(iterations) && iterations >= 5 && iterations <= 500,
        'Iterations must be 5..500 for all baseline tenant scenarios.');
    check(Number.isInteger(concurrency) && concurrency >= 2 && concurrency <= 16,
        'Concurrency must be 2..16 for mixed-tenant replay.');
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

// Bound actual streamed bytes, not just the optional Content-Length header.
// Latency ends after complete body consumption, not at response headers.
const readBoundedResponse = async (response, type) => {
    const declared = response.headers.get('content-length');
    if (declared !== null)
        check(/^\d+$/.test(declared) && Number(declared) <= MAX_RESPONSE_BYTES,
            'Invalid or oversized response length.');
    const reader = response.body?.getReader();
    const decoder = new TextDecoder('utf-8', { fatal: true });
    let bytes = 0, body = '';
    if (reader) {
        try {
            while (true) {
                const { done, value } = await reader.read();
                if (done) break;
                check(value instanceof Uint8Array, 'Invalid response body chunk.');
                bytes += value.byteLength;
                check(bytes <= MAX_RESPONSE_BYTES, 'Oversized streamed response rejected.');
                body += decoder.decode(value, { stream: true });
            }
            body += decoder.decode();
        } catch (error) {
            try { await reader.cancel(); } catch { /* Preserve original error. */ }
            throw error;
        } finally {
            reader.releaseLock();
        }
    }
    if (type.includes('json')) {
        try { return JSON.parse(body); }
        catch { fail('Invalid JSON response.'); }
    }
    return body;
};

export async function request(base, cookie, path, fetcher = fetch) {
    const procurementApi = ['/api/procurement/supplier-confirmations',
        '/api/procurement/purchase-orders'];
    check(!path.startsWith('//') && !path.includes('..') && !path.includes('\\') &&
        (path.startsWith(API + '/') || procurementApi.some(prefix =>
            path === prefix || path.startsWith(prefix + '/') || path.startsWith(prefix + '?'))),
        'Invalid API path.');
    // A literal prefix is not sufficient: WHATWG URL parsing normalizes
    // percent-encoded dot segments and can escape an approved API subtree.
    // Check the raw pathname and its canonical URL before attaching a cookie.
    const rawPath = path.split('?')[0];
    const canonical = new URL(base + path);
    check(!path.includes('#') && !/%(?:2e|2f|5c|00)/i.test(rawPath) &&
        canonical.origin === new URL(base).origin &&
        canonical.pathname === rawPath, 'Invalid API path.');
    const started = performance.now();
    const response = await fetcher(base + path, {
        method: 'GET', redirect: 'manual', cache: 'no-store',
        headers: { Cookie: cookie, Accept: 'application/json, text/csv' },
        signal: AbortSignal.timeout(15000)
    });
    check(response.status < 300 || response.status >= 400, 'Unexpected redirect.');
    const type = response.headers.get('content-type') || '';
    const body = await readBoundedResponse(response, type);
    const elapsed = performance.now() - started;
    return { status: response.status, body, type, elapsed,
        cache: response.headers.get('cache-control') || '' };
}

export async function runAcceptance(config, fetcher = fetch) {
    const { base, a, b, stock, asset, procurement = null, supplierConfirmation = null,
        from, to, iterations, concurrency,
        restricted, assetCampus, otherCampus, pagedAudit = false, auditPages = 10 } = config;
    check(!supplierConfirmation || procurement,
        'Supplier confirmation requires canonical procurement reference.');
    // A passing replay must actually exercise every authorized and foreign
    // scenario. One request (or one worker) cannot establish concurrent IDOR.
    const scenarioCount = 6 + (procurement ? 2 : 0) +
        (supplierConfirmation ? 2 : 0);
    check(Number.isInteger(iterations) && iterations >= scenarioCount &&
        iterations <= 500 && Number.isInteger(concurrency) &&
        concurrency >= 2 && concurrency <= 16,
        'Mixed-tenant replay requires every scenario and at least two workers.');
    const noStore = result => assertPrivateCache(result.cache);
    // A denied status alone is not privacy proof: an error payload or cache
    // can still disclose a private register/detail to a foreign session.
    // Only fixed failure labels reach CLI output; never echo response bodies.
    const assertDenied = (result, message, allowed = [403, 404], privateRefs = []) => {
        check(allowed.includes(result.status), message);
        noStore(result);
        const body = result.body;
        if (body && typeof body === 'object')
            check(body.success !== true && body.Success !== true &&
                body.data == null && body.Data == null &&
                body.items == null && body.journals == null,
                'Denied response contained private evidence.');
        const serialized = (typeof body === 'string' ? body :
            JSON.stringify(body ?? '')).toLowerCase();
        check(privateRefs.every(ref => !serialized.includes(String(ref).toLowerCase())),
            'Denied response exposed private reference.');
    };
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
        const privateRefs = [...collectPrivateReferences(own.body.data), reference];
        assertDenied(foreign, 'Cross-tenant detail disclosed.', [403, 404], privateRefs);
        const ownCsv = await read(a, path + '/csv');
        check(ownCsv.status === 200 && ownCsv.type.includes('text/csv'),
            'Authorized CSV missing expected evidence.');
        noStore(ownCsv);
        const expected = [reference, ...(own.body.data.journals || [])
            .map(j => j.reference)].map(value => value.toLowerCase());
        const actual = references(ownCsv.body);
        assertDetailCsvParity({ kind: path.includes('/asset-depreciation/') ? 'asset' : 'stock',
            data: own.body.data, csv: parseCsv(ownCsv.body), reference });
        check(actual.length === expected.length &&
            actual.every((value, index) => value === expected[index]),
            'Detail CSV and JSON evidence disagree.');
        const foreignCsv = await read(b, path + '/csv');
        assertDenied(foreignCsv, 'Cross-tenant CSV disclosed.', [403, 404], privateRefs);
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
        tenantA: a, tenantB: b, restricted,
        allowReferenceBeyondFirstPage: pagedAudit
    }) : null;
    const supplierResult = supplierConfirmation ? await auditSupplierCommitment({
        read, parseCsv, api: API, reference: procurement,
        confirmationId: supplierConfirmation, tenantA: a, tenantB: b, restricted
    }) : null;
    const registers = [];
    // Baseline checks must protect every visible owner reference, not only the
    // seeded UUID. Full-history/page coverage remains the opt-in paged audit.
    const ownerFirstPageReferences = new Map();
    const ownerReplayBaselines = new Map();
    if (procurementResult) {
        if (pagedAudit) {
            const result = await auditRegisterPages({ read, parseCsv, api: API,
                kind: 'procurement', reference: procurement, from, to,
                tenantA: a, tenantB: b, maxPages: auditPages,
                expectedEvidence: procurementResult });
            registers.push({ kind: 'procurement', count: result.count,
                pagesAudited: result.pagesAudited });
        } else registers.push({ kind: 'procurement', count: procurementResult.reportCount });
    }
    for (const [kind, reference] of [['stock', stock], ['asset', asset]]) {
        if (pagedAudit) {
            const result = await auditRegisterPages({ read, parseCsv, api: API, kind, reference,
                from, to, tenantA: a, tenantB: b, maxPages: auditPages,
                collectOwnerReferences: true });
            // Full-page audits must still seed the concurrent foreign-register
            // guard. Otherwise a populated tenant-B register triggers an
            // unhandled TypeError rather than meaningful isolation evidence.
            ownerFirstPageReferences.set(kind, result.ownerReferences);
            ownerReplayBaselines.set(kind, { count: result.count,
                rows: result.ownerFirstPageRows.map(evidenceSignature) });
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
        check(Array.isArray(d.items) && Number.isSafeInteger(d.totalCount) &&
            d.page === 1 && d.pageSize === 50 &&
            d.items.some(row => row.reference?.toLowerCase() === reference.toLowerCase()),
            'Seeded reference missing from first page; choose an isolated test date range.');
        const ownerRefs = new Set();
        for (const row of d.items) {
            check(typeof row.reference === 'string' &&
                /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(row.reference) &&
                !ownerRefs.has(row.reference.toLowerCase()),
                'Owner register reference missing, invalid or duplicated.');
            ownerRefs.add(row.reference.toLowerCase());
        }
        ownerFirstPageReferences.set(kind, ownerRefs);
        ownerReplayBaselines.set(kind, { count: d.totalCount,
            rows: d.items.slice(0, 25).map(evidenceSignature) });
        const ownCsv = await read(a, API + '/register/csv?' + query);
        check(ownCsv.status === 200 && ownCsv.type.includes('text/csv'),
            'Register CSV unavailable.');
        noStore(ownCsv);
        const ownRefs = references(ownCsv.body);
        assertRegisterCsvParity({ kind, data: d, csv: parseCsv(ownCsv.body), from, to });
        check(ownRefs.length === d.items.length &&
            ownRefs.every((value, index) =>
                value === d.items[index].reference.toLowerCase()),
            'Register CSV does not match authorized JSON page.');
        const foreign = await read(b, path);
        check(foreign.status === 200 && foreign.body?.success &&
            Array.isArray(foreign.body?.data?.items) &&
            foreign.body.data.items.every(row => typeof row.reference === 'string' &&
                !ownerRefs.has(row.reference.toLowerCase())),
            'Foreign register leaked tenant A evidence (or tenant B lacks required entitlement).');
        noStore(foreign);
        const foreignCsv = await read(b, API + '/register/csv?' + query);
        check(foreignCsv.status === 200 && foreignCsv.type.includes('text/csv'),
            'Foreign register CSV unavailable.');
        noStore(foreignCsv);
        const foreignRefs = references(foreignCsv.body);
        check(foreignRefs.length === foreign.body.data.items.length &&
            foreignRefs.every((value, index) =>
                value === foreign.body.data.items[index].reference.toLowerCase() &&
                !ownerRefs.has(value)),
            'Foreign register CSV leaked tenant A evidence or disagrees with JSON.');
        assertRegisterCsvParity({ kind, data: foreign.body.data,
            csv: parseCsv(foreignCsv.body), from, to });
        registers.push({ kind, count: d.totalCount });
    }
    for (const kind of ['stock', 'asset']) check(
        ownerFirstPageReferences.get(kind) instanceof Set &&
        ownerFirstPageReferences.get(kind).size > 0,
        'Missing owner register baseline for concurrent replay.');
    // Capture immutable, private in-memory snapshots after canonical source
    // audits. A 200/no-store response alone cannot prove unchanged approval,
    // amount, checker or PO/supplier linkage under concurrent tenant replay.
    const authorizedReplayBaselines = new Map();
    const additionalOwnerRefs = new Set();
    if (procurementResult) {
        const path = API + '/procurement/' + procurement;
        const response = await read(a, path);
        noStore(response);
        const data = response.body?.data;
        check(response.status === 200 && response.body?.success === true &&
            data?.orderReference?.toLowerCase() === procurementResult.reference &&
            data.state === procurementResult.state &&
            data.lineCount === procurementResult.lineCount &&
            data.campusId === procurementResult.campusId &&
            String(data.totalAmount) === String(procurementResult.totalAmount),
            'Procurement replay baseline disagrees with audited source.');
        authorizedReplayBaselines.set(path, data);
        for (const ref of [data.requisitionReference, data.supplierReference])
            if (typeof ref === 'string') additionalOwnerRefs.add(ref.toLowerCase());
    }
    if (supplierResult) {
        const path = '/api/procurement/supplier-confirmations/' + supplierConfirmation;
        const response = await read(a, path);
        noStore(response);
        const data = response.body?.data;
        check(response.status === 200 && response.body?.success === true &&
            data?.id === supplierConfirmation &&
            data.purchaseOrderId === supplierResult.purchaseOrderId &&
            data.stateCode === 'Confirmed',
            'Supplier replay baseline disagrees with audited source.');
        authorizedReplayBaselines.set(path, data);
        if (typeof data.clientRequestId === 'string')
            additionalOwnerRefs.add(data.clientRequestId.toLowerCase());
    }
    // An independently stable tenant-B register is essential: checking only
    // that tenant-A UUIDs are absent misses stale scopes and Finance drift.
    const foreignReplayBaselines = new Map();
    for (const kind of ['stock', 'asset']) {
        const path = API + '/register?' + new URLSearchParams({
            kind, from, to, page: '1', pageSize: '25'
        });
        const response = await read(b, path);
        noStore(response);
        const data = response.body?.data;
        const ownerRefs = ownerFirstPageReferences.get(kind);
        check(response.status === 200 && response.body?.success === true &&
            data?.kind === kind && data.from === from && data.to === to &&
            data.page === 1 && data.pageSize === 25 &&
            Number.isSafeInteger(data.totalCount) && data.totalCount >= 0 &&
            Array.isArray(data.items) &&
            data.items.length === Math.min(25, data.totalCount) &&
            data.items.every(row => typeof row.reference === 'string' &&
                /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(row.reference) &&
                !ownerRefs.has(row.reference.toLowerCase())) &&
            new Set(data.items.map(row => row.reference.toLowerCase())).size === data.items.length,
            'Foreign register replay baseline invalid or disclosed owner evidence.');
        foreignReplayBaselines.set(kind, {
            count: data.totalCount, rows: data.items.map(evidenceSignature)
        });
    }
    const allOwnerRefs = [...new Set([...ownerFirstPageReferences.values()]
        .flatMap(refs => [...refs]).concat([...additionalOwnerRefs],
            [...collectPrivateReferences(stockData, assetData, procurementResult,
                supplierResult, ...authorizedReplayBaselines.values())]))];
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
            assertDenied(result, 'Restricted role accessed enterprise evidence.',
                [403], [...allOwnerRefs, procurement].filter(Boolean));
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
        assertDenied(foreignCampus, 'Foreign tenant accepted another tenant campus ID.',
            [403, 404], [asset]);
    }
    const samples = [];
    const paths = [
        { cookie: a, path: API + '/' + stock, ownDetail: 'stock' },
        { cookie: a, path: API + '/asset-depreciation/' + asset, ownDetail: 'asset' },
        { cookie: a, path: API + '/register?' + new URLSearchParams({
            kind: 'stock', from, to, page: '1', pageSize: '25' }), ownRegister: 'stock' },
        { cookie: b, path: API + '/' + stock, foreignDetail: true },
        { cookie: b, path: API + '/register?' + new URLSearchParams({
            kind: 'asset', from, to, page: '1', pageSize: '25' }), foreignRegister: 'asset' },
        { cookie: b, path: API + '/register?' + new URLSearchParams({
            kind: 'stock', from, to, page: '1', pageSize: '25' }), foreignRegister: 'stock' }
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
    // Fixed scenario labels avoid exposing cookies or private reference IDs.
    const labels = ['stock-own', 'asset-own', 'stock-register-own',
        'stock-detail-foreign', 'asset-register-foreign', 'stock-register-foreign',
        ...(procurement ? ['procurement-own', 'procurement-foreign'] : []),
        ...(supplierResult ? ['supplier-own', 'supplier-foreign'] : [])];
    const byScenario = new Map();
    let inFlight = 0, maxClientInFlight = 0;
    let cursor = 0;
    const replayStart = performance.now();
    const worker = async () => {
        while (cursor < iterations) {
            const index = cursor++;
            const probeIndex = index % paths.length;
            const probe = paths[probeIndex];
            inFlight++;
            maxClientInFlight = Math.max(maxClientInFlight, inFlight);
            let result;
            try { result = await read(probe.cookie, probe.path); }
            finally { inFlight--; }
            // Private cache directives must remain safe under concurrent
            // tenant switching, not merely on the initial baseline reads.
            noStore(result);
            if (probe.foreignDetail)
                assertDenied(result, 'Concurrent cross-tenant detail disclosed.',
                    [403, 404], [...allOwnerRefs, procurement].filter(Boolean));
            else if (probe.ownDetail) {
                const baseline = probe.ownDetail === 'stock' ? stockData : assetData;
                check(result.status === 200 && result.body?.success === true &&
                    result.body.data &&
                    evidenceSignature(result.body.data) === evidenceSignature(baseline),
                    'Concurrent authorized detail changed source or Finance evidence.');
            }
            else if (probe.ownRegister) {
                const data = result.body?.data;
                const baseline = ownerReplayBaselines.get(probe.ownRegister);
                check(result.status === 200 && result.body?.success === true &&
                    data?.kind === probe.ownRegister && data.from === from &&
                    data.to === to && data.page === 1 && data.pageSize === 25 &&
                    Number.isSafeInteger(data.totalCount) &&
                    data.totalCount === baseline?.count && Array.isArray(data.items) &&
                    data.items.length === baseline.rows.length &&
                    data.items.every((row, i) =>
                        evidenceSignature(row) === baseline.rows[i]),
                    'Concurrent authorized register changed owner evidence.');
            }
            else if (probe.foreignRegister) {
                const data = result.body?.data;
                const ownerRefs = ownerFirstPageReferences.get(probe.foreignRegister);
                const baseline = foreignReplayBaselines.get(probe.foreignRegister);
                check(result.status === 200 && result.body?.success &&
                    data?.kind === probe.foreignRegister &&
                    data.from === from && data.to === to &&
                    data.page === 1 && data.pageSize === 25 &&
                    Number.isSafeInteger(data.totalCount) &&
                    data.totalCount === baseline?.count &&
                    Array.isArray(data.items) && data.items.length <= 25 &&
                    data.items.length === Math.min(25, data.totalCount) &&
                    data.items.length === baseline.rows.length &&
                    data.items.every((row, index) =>
                        evidenceSignature(row) === baseline.rows[index]) &&
                    data.items.every(x => typeof x.reference === 'string' &&
                        /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(x.reference) &&
                        !ownerRefs.has(x.reference.toLowerCase())) &&
                    new Set(data.items.map(x => x.reference.toLowerCase())).size === data.items.length,
                    'Concurrent cross-tenant register disclosed or pagination invalid.');
            }
            else if (probe.own)
                assertStableReplay(result, authorizedReplayBaselines.get(probe.path),
                    'Concurrent authorized procurement or supplier evidence changed.');
            else fail('Unexpected replay scenario.');
            samples.push(result.elapsed);
            const label = labels[probeIndex];
            if (!byScenario.has(label)) byScenario.set(label, []);
            byScenario.get(label).push(result.elapsed);
        }
    };
    await Promise.all(Array.from({ length: Math.min(iterations, concurrency) }, worker));
    check(byScenario.size === paths.length,
        'Mixed-tenant replay omitted an authorization scenario.');
    check(maxClientInFlight >= 2,
        'Mixed-tenant replay did not overlap client requests.');
    const replayWallMs = performance.now() - replayStart;
    return { checks: ['stock and asset detail/CSV IDOR and parity',
            'register JSON/CSV exact-page parity and cache isolation',
            'bounded paging', 'mixed-tenant concurrent stock/asset replay and authorized evidence stability',
            ...(restricted ? ['restricted-role authorization'] : []),
            ...(assetCampus ? ['asset campus isolation'] : []),
            ...(procurement ? ['procurement source/CSV/register integrity and tenant IDOR'] : []),
            ...(supplierResult ? ['supplier confirmation to PO/enterprise/commitment/export integrity'] : []),
            ...(pagedAudit ? ['bounded all-page JSON/CSV parity and cross-tenant isolation'] : [])],
        registerCounts: registers,
        measuredHttpOnly: true, samples: samples.length,
        maxClientInFlight,
        latencyMs: { p50: percentile(samples, 50), p95: percentile(samples, 95),
            p99: percentile(samples, 99) },
        latencyByScenario: Object.fromEntries([...byScenario].map(([name, values]) =>
            [name, { samples: values.length, p95: percentile(values, 95),
                p99: percentile(values, 99) }])),
        replayWallMs: Math.round(replayWallMs * 100) / 100,
        replayRequestsPerSecond: Math.round(iterations / replayWallMs * 100000) / 100,
        errors: 0 };
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
