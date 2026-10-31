// Read-only, fail-closed acceptance of the canonical PO -> requisition -> supplier
// commitment evidence. Does NOT infer inventory receipts or Finance postings.
const ensure = (condition, message) => { if (!condition) throw new Error(message); };
const id = value => typeof value === 'string' &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
    ? value.toLowerCase() : null;
const amount = value => {
    const s = String(value);
    ensure(/^-?\d+(?:\.\d{1,10})?$/.test(s), 'Invalid monetary evidence.');
    const negative = s.startsWith('-');
    const [whole, fraction = ''] = (negative ? s.slice(1) : s).split('.');
    const n = BigInt(whole) * 10000000000n + BigInt(fraction.padEnd(10, '0'));
    return negative ? -n : n;
};
const cacheSafe = response => ensure(response.cache?.includes('no-store') ||
    response.cache?.includes('no-cache'), 'Procurement private response cache unsafe.');
const states = new Map([
    ['Approved', 'ApprovedCommitment'], ['CancelRequested', 'CancellationPending'],
    ['Cancelled', 'CancelledCommitment'], ['Rejected', 'RejectedCommitment'],
    ['Draft', 'DraftCommitment'], ['Submitted', 'PendingApproval']
]);

export async function auditProcurement({ read, parseCsv, api, reference, from, to,
    tenantA, tenantB, restricted = null, campusId = null,
    allowReferenceBeyondFirstPage = false }) {
    ensure(id(reference), 'Procurement reference must be a UUID.');
    const path = api + '/procurement/' + reference;
    const own = await read(tenantA, path);
    ensure(own.status === 200 && own.body?.success && own.body?.data,
        'Procurement detail unavailable.');
    cacheSafe(own);
    const d = own.body.data;
    ensure(id(d.orderReference) === id(reference) &&
        id(d.requisitionReference) && id(d.supplierReference),
        'Procurement canonical references disagree.');
    ensure(states.get(d.state) === d.evidenceStatus,
        'Procurement status does not match canonical commitment lifecycle.');
    ensure(d.currencyCode === 'BDT' && Number.isSafeInteger(d.lineCount) &&
        d.lineCount >= 1 && d.lineCount <= 100 &&
        Array.isArray(d.lines) && d.lines.length === d.lineCount,
        'Procurement detail line bounds or currency invalid.');
    ensure(Number.isSafeInteger(d.campusId) && d.campusId > 0,
        'Procurement campus invalid.');
    const foreign = await read(tenantB, path);
    ensure([403, 404].includes(foreign.status), 'Foreign procurement detail disclosed.');
    const ownCsv = await read(tenantA, path + '/csv');
    ensure(ownCsv.status === 200 && ownCsv.type.includes('text/csv'),
        'Procurement CSV unavailable.');
    cacheSafe(ownCsv);
    const parsed = parseCsv(ownCsv.body);
    ensure(parsed.header.join('|') === 'Evidence|Reference|State|ItemId|Quantity|UnitPrice|Amount|ApprovedBudget|BudgetVariance|Currency',
        'Procurement CSV schema drift.');
    ensure(parsed.rows.length === d.lines.length + 3,
        'Procurement CSV evidence row count mismatch.');
    const [po, req, supplier, ...lines] = parsed.rows;
    ensure(po[0] === 'PurchaseOrder' && id(po[1]) === id(reference) &&
        po[2] === d.state && po[9] === d.currencyCode &&
        amount(po[6]) === amount(d.totalAmount) &&
        amount(po[7]) === amount(d.approvedBudget) &&
        amount(po[8]) === amount(d.budgetVariance),
        'Procurement CSV header totals or status disagree.');
    ensure(req[0] === 'Requisition' && id(req[1]) === id(d.requisitionReference) &&
        req[2] === 'Approved' && supplier[0] === 'Supplier' &&
        id(supplier[1]) === id(d.supplierReference),
        'Procurement CSV source references disagree.');
    let sum = 0n;
    const seen = new Set();
    for (let i = 0; i < lines.length; i++) {
        const row = lines[i], line = d.lines[i];
        ensure(row[0] === 'PurchaseOrderLine' &&
            Number(row[1]) === line.lineNo &&
            Number(row[3]) === line.inventoryItemId &&
            amount(row[4]) === amount(line.quantity) &&
            amount(row[5]) === amount(line.unitPrice) &&
            amount(row[6]) === amount(line.amount),
            'Procurement CSV line evidence disagree.');
        ensure(Number.isSafeInteger(line.lineNo) && line.lineNo > 0 &&
            Number.isSafeInteger(line.inventoryItemId) && line.inventoryItemId > 0 &&
            !seen.has(line.lineNo), 'Procurement line numbering or item invalid.');
        seen.add(line.lineNo);
        const lineTotal = amount(line.quantity) * amount(line.unitPrice) / 10000000000n;
        ensure(lineTotal === amount(line.amount), 'Procurement line arithmetic disagrees.');
        sum += lineTotal;
    }
    ensure(sum === amount(d.totalAmount) &&
        amount(d.approvedBudget) - sum === amount(d.budgetVariance),
        'Procurement budget and line arithmetic disagree.');
    const foreignCsv = await read(tenantB, path + '/csv');
    ensure([403, 404].includes(foreignCsv.status), 'Foreign procurement CSV disclosed.');

    const query = new URLSearchParams({ kind: 'procurement', from, to,
        page: '1', pageSize: '50', ...(campusId ? { campusId } : {}) });
    const registerPath = api + '/register?' + query;
    const ownRegister = await read(tenantA, registerPath);
    ensure(ownRegister.status === 200 && ownRegister.body?.success &&
        ownRegister.body?.data?.kind === 'procurement',
        'Procurement register unavailable.');
    cacheSafe(ownRegister);
    const r = ownRegister.body.data;
    ensure(r.page === 1 && r.pageSize === 50 &&
        Number.isSafeInteger(r.totalCount) && r.totalCount >= r.items?.length &&
        r.items.length <= 50, 'Procurement register bounds invalid.');
    const match = r.items.filter(x => id(x.reference) === id(reference));
    ensure(match.length <= 1 &&
        (match.length === 1 || (allowReferenceBeyondFirstPage &&
            r.totalCount > r.items.length)) &&
        (match.length === 0 || (match[0].state === d.state &&
            match[0].evidenceStatus === d.evidenceStatus &&
            match[0].lineCount === d.lineCount &&
            match[0].campusId === d.campusId &&
            amount(match[0].recordedValue) === sum)),
        'Procurement detail and register evidence disagree.');
    const registerCsv = await read(tenantA, api + '/register/csv?' + query);
    ensure(registerCsv.status === 200 && registerCsv.type.includes('text/csv'),
        'Procurement register CSV unavailable.');
    cacheSafe(registerCsv);
    const reportCsv = parseCsv(registerCsv.body);
    ensure(reportCsv.rows.length === r.items.length &&
        reportCsv.rows.every((row, index) =>
            id(row[reportCsv.referenceIndex]) === id(r.items[index].reference) &&
            row[5] === r.items[index].state &&
            amount(row[6]) === amount(r.items[index].recordedValue) &&
            Number(row[7]) === r.items[index].lineCount &&
            row[8] === r.items[index].evidenceStatus),
        'Procurement register JSON/CSV parity failed.');
    const foreignRegister = await read(tenantB, registerPath);
    ensure(foreignRegister.status === 200 && foreignRegister.body?.success &&
        Array.isArray(foreignRegister.body.data?.items) &&
        !foreignRegister.body.data.items.some(x => id(x.reference) === id(reference)),
        'Foreign procurement register disclosed.');
    const foreignRegisterCsv = await read(tenantB, api + '/register/csv?' + query);
    ensure(foreignRegisterCsv.status === 200 &&
        !parseCsv(foreignRegisterCsv.body).rows.some(row =>
            id(row[reportCsv.referenceIndex]) === id(reference)),
        'Foreign procurement register CSV disclosed.');
    if (restricted) {
        for (const url of [path, path + '/csv', registerPath,
            api + '/register/csv?' + query]) {
            const result = await read(restricted, url);
            ensure(result.status === 403, 'Restricted role accessed procurement evidence.');
        }
    }
    return { reference: id(reference), state: d.state, lineCount: d.lineCount,
        reportCount: r.totalCount, firstPageReferenceFound: match.length === 1,
        campusId: d.campusId, totalAmount: d.totalAmount, checked: ['detail and CSV parity',
            'line and budget arithmetic', 'tenant and role IDOR',
            'register JSON/CSV parity', 'source references'] };
}
