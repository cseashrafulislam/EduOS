// Read-only Track 5 cross-module contract: independently checked supplier
// confirmation -> canonical PO -> enterprise procurement evidence -> commitments.
// A confirmation is NOT a stock receipt, payable, or Finance posting.
const requireTrue = (condition, message) => { if (!condition) throw new Error(message); };
const uuid = value => typeof value === 'string' &&
    /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
const integer = value => Number.isSafeInteger(value) && value > 0;
const money = value => {
    const s = String(value);
    requireTrue(/^\d+(?:\.\d{1,10})?$/.test(s), 'Invalid supplier commitment amount.');
    const [whole, fraction = ''] = s.split('.');
    return BigInt(whole) * 10000000000n + BigInt(fraction.padEnd(10, '0'));
};
const day = value => {
    requireTrue(typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T/.test(value) &&
        !Number.isNaN(Date.parse(value)), 'Invalid supplier confirmation business date.');
    const d = value.slice(0, 10);
    requireTrue(new Date(d + 'T00:00:00Z').toISOString().slice(0, 10) === d,
        'Invalid supplier confirmation calendar date.');
    return d;
};
const instant = value => {
    const n = Date.parse(value);
    requireTrue(typeof value === 'string' && Number.isFinite(n),
        'Missing supplier confirmation approval timestamp.');
    return n;
};
const privateRead = async (read, cookie, path, label) => {
    const response = await read(cookie, path);
    requireTrue(response.status === 200 && response.body?.success && response.body?.data,
        label + ' unavailable.');
    requireTrue(response.cache?.includes('no-store') || response.cache?.includes('no-cache'),
        label + ' private cache policy unsafe.');
    return response.body.data;
};
const privateCsv = async (read, parseCsv, cookie, path, label) => {
    const response = await read(cookie, path);
    requireTrue(response.status === 200 && response.type?.includes('text/csv'),
        label + ' CSV unavailable.');
    requireTrue(response.cache?.includes('no-store') || response.cache?.includes('no-cache'),
        label + ' CSV cache policy unsafe.');
    return parseCsv(response.body);
};
const denied = async (read, cookie, path, label, statuses = [403, 404]) => {
    const result = await read(cookie, path);
    requireTrue(statuses.includes(result.status), label + ' disclosed to unauthorized session.');
};
const commitmentsHeader = 'Order|Campus ID|Supplier ID|Supplier|Approval Status|Approved UTC|Expected Delivery|Aging Date|Schedule|Days Overdue|Lines|Currency|Approved Commitment|Supplier Confirmed UTC|Supplier Promised Delivery|Supplier Reference';
const confirmationsHeader = 'Order|Campus|Supplier|Reference|Response Date|Promised Delivery|State|Checker UTC';

async function pages({ read, cookie, path, label, maxPages = 5 }) {
    const rows = [];
    let count = null;
    for (let page = 1; page <= maxPages; page++) {
        const separator = path.includes('?') ? '&' : '?';
        const data = await privateRead(read, cookie,
            path + separator + 'page=' + page + '&pageSize=100', label);
        requireTrue(data.page === page && data.pageSize === 100 &&
            Number.isSafeInteger(data.totalCount) && data.totalCount >= 0,
            label + ' pagination metadata invalid.');
        requireTrue(Array.isArray(data.items) && data.items.length <= 100 &&
            data.totalCount <= maxPages * 100, label + ' page unbounded.');
        if (count === null) count = data.totalCount;
        requireTrue(data.totalCount === count &&
            data.items.length === Math.min(100, count - rows.length),
            label + ' count drift or missing page.');
        rows.push(...data.items);
        if (rows.length === count) return rows;
    }
    throw new Error(label + ' pagination exceeded bounded audit.');
}

export async function auditSupplierCommitment({ read, parseCsv, api,
    reference, confirmationId, tenantA, tenantB, restricted = null }) {
    requireTrue(uuid(reference) && integer(confirmationId),
        'Supplier audit requires a PO UUID and positive confirmation ID.');
    const confirmations = '/api/procurement/supplier-confirmations';
    const orders = '/api/procurement/purchase-orders';
    const confirmationPath = confirmations + '/' + confirmationId;
    const c = await privateRead(read, tenantA, confirmationPath, 'Supplier confirmation');
    requireTrue(c.id === confirmationId && integer(c.purchaseOrderId) &&
        integer(c.campusId) && integer(c.supplierId) && integer(c.createdBy) &&
        integer(c.decidedByUserId) && c.createdBy !== c.decidedByUserId &&
        uuid(c.clientRequestId) && c.stateCode === 'Confirmed' &&
        typeof c.supplierReference === 'string' &&
        c.supplierReference.trim().length >= 3 &&
        c.supplierReference.trim().length <= 80,
        'Supplier confirmation identity, checker, or status invalid.');
    const responseDate = day(c.responseDate);
    const promised = day(c.promisedDeliveryDate);
    requireTrue(promised >= responseDate &&
        instant(c.submittedAt) >= instant(c.createdAt) &&
        instant(c.decidedAt) >= instant(c.submittedAt),
        'Supplier confirmation dates or maker/checker timeline invalid.');
    const poPath = orders + '/' + c.purchaseOrderId;
    const po = await privateRead(read, tenantA, poPath, 'Canonical purchase order');
    const tracePath = api + '/procurement/' + reference;
    const trace = await privateRead(read, tenantA, tracePath, 'Enterprise PO trace');
    requireTrue(po.id === c.purchaseOrderId && po.orderNumber === c.orderNumber &&
        po.campusId === c.campusId && po.supplierId === c.supplierId &&
        po.stateCode === 'Approved' && po.currencyCode === 'BDT' &&
        trace.orderReference.toLowerCase() === reference.toLowerCase() &&
        trace.orderNumber === po.orderNumber && trace.campusId === po.campusId &&
        trace.state === 'Approved' && trace.evidenceStatus === 'ApprovedCommitment' &&
        trace.currencyCode === po.currencyCode &&
        money(po.totalAmount) === money(trace.totalAmount),
        'Supplier confirmation, canonical PO, and enterprise trace disagree.');

    const filter = new URLSearchParams({ campusId: String(c.campusId),
        supplierId: String(c.supplierId), search: po.orderNumber });
    const commitmentsPath = orders + '/commitments?' + filter;
    const commitments = await pages({ read, cookie: tenantA,
        path: commitmentsPath, label: 'Approved commitments' });
    const matches = commitments.filter(x => x.id === po.id);
    requireTrue(matches.length === 1, 'Canonical PO missing or duplicated in commitments.');
    const commitment = matches[0];
    requireTrue(commitment.orderNumber === po.orderNumber &&
        commitment.campusId === c.campusId && commitment.supplierId === c.supplierId &&
        commitment.stateCode === 'Approved' && commitment.currencyCode === 'BDT' &&
        money(commitment.approvedAmount) === money(po.totalAmount) &&
        commitment.supplierConfirmationReference === c.supplierReference &&
        day(commitment.supplierPromisedDeliveryDate) === promised &&
        instant(commitment.supplierConfirmedAt) === instant(c.decidedAt),
        'Approved commitment supplier evidence or amount disagrees.');
    const commitmentCsv = await privateCsv(read, parseCsv, tenantA,
        orders + '/commitments/export?' + filter, 'Commitments export');
    requireTrue(commitmentCsv.header.join('|') === commitmentsHeader &&
        commitmentCsv.rows.length === commitments.length,
        'Commitments export schema or row count drift.');
    for (let i = 0; i < commitments.length; i++) {
        const x = commitments[i], row = commitmentCsv.rows[i];
        requireTrue(row[0] === x.orderNumber && row[1] === String(x.campusId) &&
            row[2] === String(x.supplierId) && row[4] === x.stateCode &&
            row[11] === x.currencyCode && money(row[12]) === money(x.approvedAmount) &&
            row[15] === (x.supplierConfirmationReference || ''),
            'Commitments export row disagrees with authorized JSON.');
    }

    const confirmationsFilter = new URLSearchParams({
        campusId: String(c.campusId), state: 'Confirmed' });
    const confirmationList = await pages({ read, cookie: tenantA,
        path: confirmations + '?' + confirmationsFilter, label: 'Supplier confirmations' });
    const matchingConfirmations = confirmationList.filter(x => x.id === c.id);
    requireTrue(matchingConfirmations.length === 1 &&
        matchingConfirmations[0].purchaseOrderId === c.purchaseOrderId &&
        matchingConfirmations[0].supplierId === c.supplierId &&
        matchingConfirmations[0].supplierReference === c.supplierReference,
        'Supplier confirmation list/detail parity failed.');
    const confirmationCsv = await privateCsv(read, parseCsv, tenantA,
        confirmations + '/export?' + confirmationsFilter, 'Supplier confirmation export');
    requireTrue(confirmationCsv.header.join('|') === confirmationsHeader &&
        confirmationCsv.rows.length === confirmationList.length,
        'Supplier confirmation export schema or row count drift.');
    for (let i = 0; i < confirmationList.length; i++) {
        const x = confirmationList[i], row = confirmationCsv.rows[i];
        requireTrue(row[0] === x.orderNumber && row[1] === String(x.campusId) &&
            row[2] === String(x.supplierId) && row[3] === x.supplierReference &&
            row[4] === day(x.responseDate) &&
            row[5] === day(x.promisedDeliveryDate) && row[6] === x.stateCode &&
            instant(row[7]) === instant(x.decidedAt),
            'Supplier confirmation export row disagrees with authorized JSON.');
    }

    for (const path of [confirmationPath, poPath, tracePath])
        await denied(read, tenantB, path, 'Foreign supplier/PO detail');
    const foreignCommitments = await pages({ read, cookie: tenantB,
        path: commitmentsPath, label: 'Foreign commitments' });
    requireTrue(!foreignCommitments.some(x => x.orderNumber === po.orderNumber &&
        x.supplierConfirmationReference === c.supplierReference),
        'Foreign commitments list disclosed seeded evidence.');
    const foreignCommitmentCsv = await privateCsv(read, parseCsv, tenantB,
        orders + '/commitments/export?' + filter, 'Foreign commitments export');
    requireTrue(!foreignCommitmentCsv.rows.some(row =>
        row[0] === po.orderNumber && row[15] === c.supplierReference),
        'Foreign commitments export disclosed seeded evidence.');
    const foreignConfirmations = await pages({ read, cookie: tenantB,
        path: confirmations + '?' + confirmationsFilter, label: 'Foreign confirmations' });
    requireTrue(!foreignConfirmations.some(x => x.id === c.id &&
        x.clientRequestId === c.clientRequestId &&
        x.supplierReference === c.supplierReference && x.orderNumber === c.orderNumber),
        'Foreign confirmation list disclosed seeded evidence.');
    const foreignExport = await privateCsv(read, parseCsv, tenantB,
        confirmations + '/export?' + confirmationsFilter, 'Foreign confirmation export');
    requireTrue(!foreignExport.rows.some(row =>
        row[0] === c.orderNumber && row[3] === c.supplierReference),
        'Foreign supplier confirmation export disclosed seeded evidence.');
    if (restricted) {
        for (const path of [confirmationPath, poPath, tracePath,
            commitmentsPath + '&page=1&pageSize=100',
            confirmations + '?' + confirmationsFilter + '&page=1&pageSize=100',
            confirmations + '/export?' + confirmationsFilter,
            orders + '/commitments/export?' + filter])
            await denied(read, restricted, path, 'Restricted role', [403]);
    }
    return { confirmationId, purchaseOrderId: po.id,
        checks: ['canonical supplier/PO/enterprise lineage', 'independent checker and dates',
            'commitment amount and supplier evidence', 'bounded JSON/CSV parity',
            'cross-tenant IDOR and restricted role'] };
}
