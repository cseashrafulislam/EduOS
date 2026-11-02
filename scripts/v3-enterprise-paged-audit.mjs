// Read-only, bounded reconciliation register acceptance. No credentials are logged.
// A report page is evidence only when its JSON, CSV, tenant scope and pagination agree.
const requireEvidence = (ok, message) => { if (!ok) throw new Error(message); };
const safeValue = value => String(value ?? '');
const normalizeReference = value => safeValue(value).toLowerCase();
const MAX_PAGE_SIZE = 50;
// A private register must prohibit storage, not merely demand revalidation.
// Split directives exactly: "x-no-store" is not a no-store directive, and
// "public, no-store" is contradictory shared-cache evidence.
export const assertPrivateCache = (value, message = 'Private response must not be cached.') => {
    const directives = String(value || '').toLowerCase().split(',')
        .map(part => part.trim());
    requireEvidence(directives.includes('no-store') &&
        !directives.some(part => part === 'public' || /^s-maxage\s*=/.test(part)),
        message);
};
const CSV_COLUMNS = ['Kind', 'Reference', 'Number', 'Date', 'Campus',
    'State', 'RecordedValue', 'Lines', 'EvidenceStatus'];
// The controller prefixes formula-like cells, including negative amounts.
// Fail closed if a CSV response omits that spreadsheet safety prefix.
const protectedCell = value => {
    const s = safeValue(value);
    return s.length && '=+-@\t\r'.includes(s[0]) ? "'" + s : s;
};
const matchingMoneyCell = (csv, json) => {
    const plain = csv.startsWith("'") ? csv.slice(1) : csv;
    requireEvidence(csv === protectedCell(plain),
        'Unsafe register CSV monetary cell.');
    return scaledDecimal(plain) === scaledDecimal(json);
};
const scaledDecimal = value => {
    const s = String(value);
    requireEvidence(/^-?\d+(?:\.\d{1,10})?$/.test(s),
        'Invalid register monetary evidence.');
    const negative = s.startsWith('-');
    const [whole, fraction = ''] = (negative ? s.slice(1) : s).split('.');
    const scaled = BigInt(whole) * 10000000000n +
        BigInt(fraction.padEnd(10, '0'));
    return negative ? -scaled : scaled;
};

// Shared strict JSON/CSV field parity for both first-page and all-page acceptance.
// A matching Reference alone cannot certify amount, campus or formula safety.
export const assertRegisterCsvParity = ({ kind, data, csv, from, to }) => {
    const parsed = csv;
    requireEvidence(parsed.rows.length === data.items.length,
        'Register CSV does not match authorized JSON page.');
    requireEvidence(parsed.header.join('|') === CSV_COLUMNS.join('|') &&
        parsed.rows.every(row => row.length === CSV_COLUMNS.length),
        'Register CSV schema mismatch.');
    const col = name => CSV_COLUMNS.indexOf(name);
    for (let i = 0; i < data.items.length; i++) {
        const row = data.items[i], csvRow = parsed.rows[i];
        requireEvidence(Number.isSafeInteger(row.lineCount) && row.lineCount >= 0 &&
            (row.campusId == null ||
                (Number.isSafeInteger(row.campusId) && row.campusId > 0)) &&
            (kind !== 'procurement' || row.campusId != null),
            'Register source line count or campus invalid.');
        requireEvidence(
            protectedCell(row.reference) === csvRow[col('Reference')] &&
            csvRow[col('Kind')] === kind &&
            protectedCell(row.number) === csvRow[col('Number')] &&
            protectedCell(row.date) === csvRow[col('Date')] &&
            protectedCell(row.state) === csvRow[col('State')] &&
            protectedCell(row.lineCount) === csvRow[col('Lines')] &&
            protectedCell(row.evidenceStatus) === csvRow[col('EvidenceStatus')],
            'Register CSV does not match authorized JSON page.');
        requireEvidence(typeof row.date === 'string' &&
            /^\d{4}-\d{2}-\d{2}$/.test(row.date) &&
            row.date >= from && row.date <= to,
            'Register row outside requested date window.');
        requireEvidence(
            protectedCell(row.campusId) === csvRow[col('Campus')] &&
            matchingMoneyCell(csvRow[col('RecordedValue')], row.recordedValue),
            kind === 'procurement'
                ? 'Procurement register CSV/source amount or campus mismatch.'
                : 'Register CSV/source amount or campus mismatch.');
    }
};

export async function auditRegisterPages({ read, parseCsv, api, kind, reference,
    from, to, tenantA, tenantB, maxPages = 10, expectedEvidence = null,
    collectOwnerReferences = false }) {
    requireEvidence(['stock', 'asset', 'procurement'].includes(kind), 'Invalid register kind.');
    requireEvidence(Number.isInteger(maxPages) && maxPages >= 1 && maxPages <= 20,
        'Invalid audit page limit.');
    const wanted = normalizeReference(reference);
    const pathFor = (page, csv = false) => api + '/register' + (csv ? '/csv' : '?') +
        (csv ? '?' : '') + new URLSearchParams({ kind, from, to,
            page: String(page), pageSize: String(MAX_PAGE_SIZE) });
    const privateResponse = result => assertPrivateCache(result.cache,
        'Private register response is cacheable.');
    const snapshot = async (cookie, page, expectedTotal = null) => {
        const json = await read(cookie, pathFor(page));
        requireEvidence(json.status === 200 && json.body?.success && json.body?.data,
            'Register JSON page unavailable.');
        privateResponse(json);
        const data = json.body.data;
        requireEvidence(data.kind === kind && data.from === from && data.to === to &&
            data.page === page && data.pageSize === MAX_PAGE_SIZE &&
            Number.isSafeInteger(data.totalCount) && data.totalCount >= 0 &&
            Array.isArray(data.items) && data.items.length <= MAX_PAGE_SIZE,
            'Register page metadata invalid.');
        if (expectedTotal !== null)
            requireEvidence(data.totalCount === expectedTotal,
                'Register count changed during paged audit.');
        const expectedLength = Math.max(0,
            Math.min(MAX_PAGE_SIZE, data.totalCount - (page - 1) * MAX_PAGE_SIZE));
        requireEvidence(data.items.length === expectedLength,
            'Register page contains missing or excess rows.');
        const csv = await read(cookie, pathFor(page, true));
        requireEvidence(csv.status === 200 && csv.type?.includes('text/csv'),
            'Register CSV page unavailable.');
        privateResponse(csv);
        assertRegisterCsvParity({ kind, data, csv: parseCsv(csv.body), from, to });
        return data;
    };
    // Recheck every bounded page: a stable first page alone cannot prove that
    // later pages or in-place Finance/stock evidence stayed unchanged.
    const evidenceSignature = row => JSON.stringify(
        Object.keys(row).sort().map(key => [key, row[key]]));
    // A seeded UUID alone is insufficient: any shared reference across tenant
    // registers is a privacy/integrity conflict, including on later pages.
    const scan = async (cookie, ownerReferences = null) => {
        const first = await snapshot(cookie, 1);
        const total = first.totalCount;
        const pages = Math.max(1, Math.ceil(total / MAX_PAGE_SIZE));
        requireEvidence(pages <= maxPages,
            'Register exceeds bounded audit pages; choose an isolated staging date range.');
        const seen = new Set();
        let found = false;
        let lastDate = null;
        const pageSnapshots = [];
        for (let page = 1; page <= pages; page++) {
            const data = page === 1 ? first : await snapshot(cookie, page, total);
            pageSnapshots.push(data.items.map(evidenceSignature));
            for (const row of data.items) {
                const ref = normalizeReference(row.reference);
                requireEvidence(/^[0-9a-f]{8}-[0-9a-f-]{27,}$/.test(ref),
                    'Register reference is invalid.');
                requireEvidence(!seen.has(ref), 'Duplicate register reference across pages.');
                seen.add(ref);
                requireEvidence(lastDate === null || row.date <= lastDate,
                    'Register date order changed across pages.');
                lastDate = row.date;
                if (ref === wanted) {
                    found = true;
                    if (!ownerReferences && kind === 'procurement' && expectedEvidence)
                        requireEvidence(row.state === expectedEvidence.state &&
                            row.lineCount === expectedEvidence.lineCount &&
                            row.campusId === expectedEvidence.campusId &&
                            scaledDecimal(row.recordedValue) ===
                                scaledDecimal(expectedEvidence.totalAmount),
                            'Procurement detail and later-page register evidence disagree.');
                }
                if (ownerReferences) requireEvidence(!ownerReferences.has(ref),
                    'Foreign register leaked tenant A evidence.');
            }
        }
        requireEvidence(seen.size === total, 'Register pages do not reconcile to total count.');
        for (let page = 1; page <= pages; page++) {
            const replay = await snapshot(cookie, page, total);
            requireEvidence(JSON.stringify(pageSnapshots[page - 1]) ===
                JSON.stringify(replay.items.map(evidenceSignature)),
                'Register page replay changed during audit.');
        }
        return { count: total, pages, found, references: seen,
            firstPageRows: first.items.slice(0, 25) };
    };
    const own = await scan(tenantA);
    requireEvidence(own.found,
        'Seeded reference missing from bounded register pages.');
    await scan(tenantB, own.references);
    // Expose the owner set only to in-process acceptance callers that explicitly
    // request it. Never include it in the CLI/report output.
    return { kind, count: own.count, pagesAudited: own.pages,
        ...(collectOwnerReferences ? { ownerReferences: own.references,
            ownerFirstPageRows: own.firstPageRows } : {}) };
}
