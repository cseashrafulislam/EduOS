// Read-only, bounded reconciliation register acceptance. No credentials are logged.
// A report page is evidence only when its JSON, CSV, tenant scope and pagination agree.
const requireEvidence = (ok, message) => { if (!ok) throw new Error(message); };
const safeValue = value => String(value ?? '');
const normalizeReference = value => safeValue(value).toLowerCase();
const MAX_PAGE_SIZE = 50;

export async function auditRegisterPages({ read, parseCsv, api, kind, reference,
    from, to, tenantA, tenantB, maxPages = 10 }) {
    requireEvidence(['stock', 'asset'].includes(kind), 'Invalid register kind.');
    requireEvidence(Number.isInteger(maxPages) && maxPages >= 1 && maxPages <= 20,
        'Invalid audit page limit.');
    const wanted = normalizeReference(reference);
    const pathFor = (page, csv = false) => api + '/register' + (csv ? '/csv' : '?') +
        (csv ? '?' : '') + new URLSearchParams({ kind, from, to,
            page: String(page), pageSize: String(MAX_PAGE_SIZE) });
    const privateResponse = result => requireEvidence(
        /no-store|no-cache/i.test(result.cache || ''),
        'Private register response is cacheable.');
    const snapshot = async (cookie, page, expectedTotal = null) => {
        const json = await read(cookie, pathFor(page));
        requireEvidence(json.status === 200 && json.body?.success && json.body?.data,
            'Register JSON page unavailable.');
        privateResponse(json);
        const data = json.body.data;
        requireEvidence(data.page === page && data.pageSize === MAX_PAGE_SIZE &&
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
        const parsed = parseCsv(csv.body);
        requireEvidence(parsed.rows.length === data.items.length,
            'Register CSV does not match authorized JSON page.');
        const col = name => parsed.header.indexOf(name);
        for (const name of ['Kind', 'Reference', 'State', 'Lines', 'EvidenceStatus'])
            requireEvidence(col(name) >= 0, 'Register CSV column missing: ' + name);
        for (let i = 0; i < data.items.length; i++) {
            const row = data.items[i], csvRow = parsed.rows[i];
            requireEvidence(normalizeReference(row.reference) ===
                normalizeReference(csvRow[col('Reference')]) &&
                csvRow[col('Kind')] === kind &&
                safeValue(row.state) === csvRow[col('State')] &&
                safeValue(row.lineCount) === csvRow[col('Lines')] &&
                safeValue(row.evidenceStatus) === csvRow[col('EvidenceStatus')],
                'Register CSV does not match authorized JSON page.');
            requireEvidence(typeof row.date === 'string' &&
                row.date >= from && row.date <= to,
                'Register row outside requested date window.');
        }
        return data;
    };
    const scan = async (cookie, foreign) => {
        const first = await snapshot(cookie, 1);
        const total = first.totalCount;
        const pages = Math.max(1, Math.ceil(total / MAX_PAGE_SIZE));
        requireEvidence(pages <= maxPages,
            'Register exceeds bounded audit pages; choose an isolated staging date range.');
        const seen = new Set();
        let found = false;
        let lastDate = null;
        let firstPageRefs = [];
        for (let page = 1; page <= pages; page++) {
            const data = page === 1 ? first : await snapshot(cookie, page, total);
            if (page === 1) firstPageRefs = data.items.map(x => normalizeReference(x.reference));
            for (const row of data.items) {
                const ref = normalizeReference(row.reference);
                requireEvidence(/^[0-9a-f]{8}-[0-9a-f-]{27,}$/.test(ref),
                    'Register reference is invalid.');
                requireEvidence(!seen.has(ref), 'Duplicate register reference across pages.');
                seen.add(ref);
                requireEvidence(lastDate === null || row.date <= lastDate,
                    'Register date order changed across pages.');
                lastDate = row.date;
                if (ref === wanted) found = true;
                if (foreign) requireEvidence(ref !== wanted,
                    'Foreign register leaked tenant A evidence.');
            }
        }
        requireEvidence(seen.size === total, 'Register pages do not reconcile to total count.');
        const replay = await snapshot(cookie, 1, total);
        requireEvidence(JSON.stringify(firstPageRefs) === JSON.stringify(
            replay.items.map(x => normalizeReference(x.reference))),
            'Register first-page replay changed during audit.');
        return { count: total, pages, found };
    };
    const own = await scan(tenantA, false);
    requireEvidence(own.found,
        'Seeded reference missing from bounded register pages.');
    await scan(tenantB, true);
    return { kind, count: own.count, pagesAudited: own.pages };
}
