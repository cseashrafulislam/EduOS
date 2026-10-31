const { test } = require('node:test');
const assert = require('node:assert/strict');
const { pathToFileURL } = require('node:url');
const { resolve } = require('node:path');
const moduleUrl = pathToFileURL(resolve(__dirname,
    '../../scripts/v3-enterprise-supplier-commitment-audit.mjs')).href;
const reference = '11111111-1111-4111-8111-111111111111';
const clientRequestId = '22222222-2222-4222-8222-222222222222';
const api = '/api/v3/enterprise/ledger-trace';
const confirm = '/api/procurement/supplier-confirmations';
const orders = '/api/procurement/purchase-orders';
const timestamp = '2026-10-31T02:00:00Z';
const submitted = '2026-10-31T03:00:00Z';
const decided = '2026-10-31T04:00:00Z';
const c = { id: 71, clientRequestId, purchaseOrderId: 21, campusId: 7,
    supplierId: 31, orderNumber: 'PO-TEST-21', supplierReference: 'SUP-ACK-001',
    stateCode: 'Confirmed', responseDate: '2026-10-30T00:00:00',
    promisedDeliveryDate: '2026-11-03T00:00:00', createdBy: 101,
    decidedByUserId: 102, createdAt: timestamp, submittedAt: submitted,
    decidedAt: decided };
const po = { id: 21, orderNumber: c.orderNumber, campusId: 7, supplierId: 31,
    stateCode: 'Approved', currencyCode: 'BDT', totalAmount: 24 };
const trace = { orderReference: reference, orderNumber: c.orderNumber,
    campusId: 7, state: 'Approved', evidenceStatus: 'ApprovedCommitment',
    currencyCode: 'BDT', totalAmount: 24 };
const commitment = { ...po, approvedAmount: 24,
    supplierConfirmationReference: c.supplierReference,
    supplierPromisedDeliveryDate: c.promisedDeliveryDate,
    supplierConfirmedAt: decided };
const commitmentHeader = 'Order,Campus ID,Supplier ID,Supplier,Approval Status,Approved UTC,Expected Delivery,Aging Date,Schedule,Days Overdue,Lines,Currency,Approved Commitment,Supplier Confirmed UTC,Supplier Promised Delivery,Supplier Reference';
const confirmationHeader = 'Order,Campus,Supplier,Reference,Response Date,Promised Delivery,State,Checker UTC';
const quote = value => '"' + String(value ?? '').replaceAll('"', '""') + '"';
const csv = (header, rows) => '\ufeff' + header + '\r\n' + rows.map(row =>
    row.map(quote).join(',')).join('\r\n') + (rows.length ? '\r\n' : '');
const commitmentCsv = value => csv(commitmentHeader, [[value.orderNumber, value.campusId,
    value.supplierId, 'Supplier A', value.stateCode, decided, '', '2026-10-31',
    'Upcoming', 0, 1, value.currencyCode, value.approvedAmount,
    value.supplierConfirmedAt, '2026-11-03', value.supplierConfirmationReference]]);
const confirmationCsv = value => csv(confirmationHeader, [[value.orderNumber,
    value.campusId, value.supplierId, value.supplierReference, '2026-10-30',
    '2026-11-03', value.stateCode, value.decidedAt]]);
function parseCsv(input) {
    const text = input.charCodeAt(0) === 0xfeff ? input.slice(1) : input;
    const lines = text.trimEnd().split('\r\n');
    const parse = line => [...line.matchAll(/"((?:[^"]|"")*)"(?:,|$)/g)]
        .map(x => x[1].replaceAll('""', '"'));
    const header = lines.shift().split(',');
    return { header, rows: lines.filter(Boolean).map(parse),
        referenceIndex: header.indexOf('Reference') };
}
function mock(options = {}) {
    const requests = [];
    const ownConfirmation = { ...c, ...options.confirmation };
    const ownPo = { ...po, ...options.po };
    const ownTrace = { ...trace, ...options.trace };
    const ownCommitment = { ...commitment, ...options.commitment };
    const read = async (cookie, path) => {
        requests.push({ cookie, path });
        const foreign = cookie === 'b', restricted = cookie === 'restricted';
        const isCsv = path.includes('/export');
        if (restricted && !options.restrictedLeak)
            return { status: 403, body: { success: false }, cache: 'no-store' };
        if (foreign && !options.foreignLeak &&
            (path === confirm + '/71' || path === orders + '/21' ||
                path === api + '/procurement/' + reference))
            return { status: 404, body: { success: false }, cache: 'no-store' };
        let body;
        const visible = !foreign || options.foreignLeak ||
            (isCsv && options.foreignExportLeak) || (!isCsv && options.foreignListLeak);
        if (path === confirm + '/71') body = { success: true, data: ownConfirmation };
        else if (path === orders + '/21') body = { success: true, data: ownPo };
        else if (path === api + '/procurement/' + reference)
            body = { success: true, data: ownTrace };
        else if (path.startsWith(orders + '/commitments/export?'))
            body = visible ? commitmentCsv(ownCommitment) : csv(commitmentHeader, []);
        else if (path.startsWith(confirm + '/export?'))
            body = visible ? confirmationCsv({ ...ownConfirmation,
                supplierReference: options.csvSupplierReference ?? ownConfirmation.supplierReference })
                : csv(confirmationHeader, []);
        else if (path.startsWith(orders + '/commitments?'))
            body = { success: true, data: { page: Number(new URL('http://x' + path)
                .searchParams.get('page')), pageSize: 100, totalCount: visible ? 1 : 0,
                items: visible ? [ownCommitment] : [] } };
        else if (path.startsWith(confirm + '?'))
            body = { success: true, data: { page: Number(new URL('http://x' + path)
                .searchParams.get('page')), pageSize: 100, totalCount: visible ? 1 : 0,
                items: visible ? [ownConfirmation] : [] } };
        else throw new Error('Unexpected request ' + path);
        return { status: 200, body, cache: options.cacheLeak ? 'public' : 'private, no-store',
            type: isCsv ? 'text/csv' : 'application/json' };
    };
    return { read, requests };
}
const options = { api, reference, confirmationId: 71, tenantA: 'a', tenantB: 'b',
    parseCsv };
test('supplier confirmation is reconciled across PO, enterprise trace, commitment and exports', async () => {
    const { auditSupplierCommitment } = await import(moduleUrl);
    const m = mock();
    const result = await auditSupplierCommitment({ ...options, read: m.read,
        restricted: 'restricted' });
    assert.equal(result.purchaseOrderId, 21);
    assert.equal(result.checks.length, 5);
    assert.equal(m.requests.filter(x => x.cookie === 'restricted').length, 7);
    assert.ok(m.requests.every(x => !x.path.includes('/submit')));
});
test('cross-tenant detail disclosure and role bypass fail closed', async () => {
    const { auditSupplierCommitment } = await import(moduleUrl);
    await assert.rejects(auditSupplierCommitment({ ...options,
        read: mock({ foreignLeak: true }).read }), /Foreign supplier\/PO detail/);
    await assert.rejects(auditSupplierCommitment({ ...options,
        read: mock({ restrictedLeak: true }).read, restricted: 'restricted' }),
    /Restricted role/);
});
test('maker/checker, canonical PO and approval state mismatches fail closed', async () => {
    const { auditSupplierCommitment } = await import(moduleUrl);
    for (const [variation, error] of [
        [{ confirmation: { decidedByUserId: 101 } }, /checker/],
        [{ confirmation: { stateCode: 'Submitted' } }, /checker/],
        [{ confirmation: { promisedDeliveryDate: '2026-10-29T00:00:00' } }, /timeline/],
        [{ confirmation: { decidedAt: timestamp } }, /timeline/],
        [{ po: { supplierId: 999 } }, /canonical PO/],
        [{ trace: { totalAmount: 25 } }, /enterprise trace/],
        [{ commitment: { supplierConfirmationReference: 'WRONG' } }, /supplier evidence/],
        [{ commitment: { approvedAmount: 25 } }, /supplier evidence/],
        [{ csvSupplierReference: 'WRONG' }, /export row/],
        [{ cacheLeak: true }, /cache policy/]
    ]) await assert.rejects(auditSupplierCommitment({ ...options,
        read: mock(variation).read }), error);
});
test('invalid reference or confirmation ID never starts an HTTP audit', async () => {
    const { auditSupplierCommitment } = await import(moduleUrl);
    const m = mock();
    await assert.rejects(auditSupplierCommitment({ ...options,
        reference: 'not-uuid', read: m.read }), /requires a PO UUID/);
    await assert.rejects(auditSupplierCommitment({ ...options,
        confirmationId: -1, read: m.read }), /positive confirmation ID/);
    assert.equal(m.requests.length, 0);
});

test('foreign list and CSV export leaks are detected independently of detail IDOR', async () => {
    const { auditSupplierCommitment } = await import(moduleUrl);
    await assert.rejects(auditSupplierCommitment({ ...options,
        read: mock({ foreignListLeak: true }).read }), /Foreign commitments list/);
    await assert.rejects(auditSupplierCommitment({ ...options,
        read: mock({ foreignExportLeak: true }).read }), /Foreign commitments export/);
});
