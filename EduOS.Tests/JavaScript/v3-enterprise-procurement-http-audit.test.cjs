const { test } = require('node:test');
const assert = require('node:assert/strict');
const { pathToFileURL } = require('node:url');
const { resolve } = require('node:path');
const moduleUrl = pathToFileURL(resolve(__dirname, '../../scripts/v3-enterprise-procurement-http-audit.mjs')).href;
const po = '11111111-1111-4111-8111-111111111111';
const req = '22222222-2222-4222-8222-222222222222';
const supplier = '33333333-3333-4333-8333-333333333333';
const api = '/api/v3/enterprise/ledger-trace';
const config = { api, reference: po, from: '2026-10-01', to: '2026-10-31', tenantA: 'a', tenantB: 'b' };
const headers = 'Evidence,Reference,State,ItemId,Quantity,UnitPrice,Amount,ApprovedBudget,BudgetVariance,Currency';
const detail = { orderReference: po, requisitionReference: req, supplierReference: supplier,
    state: 'Approved', evidenceStatus: 'ApprovedCommitment', currencyCode: 'BDT',
    campusId: 10, totalAmount: 24, approvedBudget: 50, budgetVariance: 26,
    lineCount: 1, lines: [{ lineNo: 1, inventoryItemId: 20, quantity: 4, unitPrice: 6, amount: 24 }] };
const csvDetail = () => headers + '\nPurchaseOrder,' + po + ',Approved,,,,24,50,26,BDT\n' +
    'Requisition,' + req + ',Approved,,,,,,,\nSupplier,' + supplier +
    ',,,,,,,,\nPurchaseOrderLine,1,,20,4,6,24,,,\n';
const registerRow = { reference: po, number: 'PO-1', date: '2026-10-31',
    campusId: 10, state: 'Approved', recordedValue: 24, lineCount: 1,
    evidenceStatus: 'ApprovedCommitment' };
const reportHeader = 'Kind,Reference,Number,Date,Campus,State,RecordedValue,Lines,EvidenceStatus';
const reportCsv = row => reportHeader + '\n' + (row ? ['procurement', row.reference,
    'PO-1', '2026-10-31', '10', row.state, row.recordedValue, row.lineCount,
    row.evidenceStatus].join(',') : '') + '\n';
const parseCsv = value => {
    const rows = value.trimEnd().split('\n').map(line => line.split(','));
    const header = rows.shift();
    return { header, rows, referenceIndex: header.indexOf('Reference') };
};
function mock({ foreignLeak = false, csvAmount = 24, lineAmount = 24,
    registerAmount = 24, status = 'ApprovedCommitment', restrictedLeak = false,
    reportCsvMismatch = false } = {}) {
    const requests = [];
    const read = async (cookie, path) => {
        requests.push({ cookie, path });
        const foreign = cookie === 'b', restricted = cookie === 'restricted';
        const isCsv = path.includes('/csv'), isRegister = path.includes('/register');
        const statusCode = restricted && !restrictedLeak ? 403 :
            foreign && !foreignLeak && !isRegister ? 404 : 200;
        const data = { ...detail, evidenceStatus: status,
            lines: [{ ...detail.lines[0], amount: lineAmount }] };
        const row = { ...registerRow, recordedValue: registerAmount, evidenceStatus: status };
        const visible = !foreign || foreignLeak;
        const body = isRegister
            ? isCsv ? reportCsv(visible ? { ...row,
                recordedValue: reportCsvMismatch ? 999 : row.recordedValue } : null)
                : { success: true, data: { kind: 'procurement', page: 1,
                    pageSize: 50, totalCount: visible ? 1 : 0,
                    items: visible ? [row] : [] } }
            : isCsv ? csvDetail().replace(',24,50,26,BDT', ',' + csvAmount + ',50,26,BDT')
                : { success: true, data };
        return { status: statusCode, body, type: isCsv ? 'text/csv' : 'application/json',
            cache: 'private, no-store' };
    };
    return { read, requests };
}
test('procurement HTTP audit validates source, budget, CSV, report and tenant isolation', async () => {
    const { auditProcurement } = await import(moduleUrl);
    const m = mock();
    const result = await auditProcurement({ ...config, read: m.read, parseCsv });
    assert.equal(result.lineCount, 1);
    assert.equal(result.reportCount, 1);
    assert.equal(m.requests.length, 8);
});
test('foreign detail and report disclosures fail closed', async () => {
    const { auditProcurement } = await import(moduleUrl);
    await assert.rejects(auditProcurement({ ...config,
        read: mock({ foreignLeak: true }).read, parseCsv }), /Foreign procurement detail/);
});
test('CSV, line arithmetic and register mismatches fail closed', async () => {
    const { auditProcurement } = await import(moduleUrl);
    for (const [options, expected] of [
        [{ csvAmount: 25 }, /CSV header totals/],
        [{ lineAmount: 25 }, /CSV line evidence/],
        [{ registerAmount: 25 }, /detail and register/],
        [{ status: 'EvidenceConflict' }, /status does not match/],
        [{ reportCsvMismatch: true }, /register JSON\/CSV parity/]
    ]) await assert.rejects(auditProcurement({ ...config,
        read: mock(options).read, parseCsv }), expected);
});
test('restricted role rejected on every procurement endpoint', async () => {
    const { auditProcurement } = await import(moduleUrl);
    const m = mock();
    await auditProcurement({ ...config, read: m.read, parseCsv, restricted: 'restricted' });
    assert.equal(m.requests.filter(x => x.cookie === 'restricted').length, 4);
    await assert.rejects(auditProcurement({ ...config, read: mock({ restrictedLeak: true }).read,
        parseCsv, restricted: 'restricted' }), /Restricted role/);
});
test('forged source IDs and invalid money rejected', async () => {
    const { auditProcurement } = await import(moduleUrl);
    await assert.rejects(auditProcurement({ ...config, reference: 'invalid',
        read: mock().read, parseCsv }), /UUID/);
    const m = mock();
    const read = async (cookie, path) => {
        const response = await m.read(cookie, path);
        if (cookie === 'a' && path === api + '/procurement/' + po)
            response.body.data.totalAmount = '1e99';
        return response;
    };
    await assert.rejects(auditProcurement({ ...config, read, parseCsv }), /Invalid monetary/);
});

test('procurement first-page audit can defer reference proof only to bounded all-page audit', async () => {
    const { auditProcurement } = await import(moduleUrl);
    const original = mock();
    const read = async (cookie, path) => {
        const result = await original.read(cookie, path);
        if (path.includes('/register?') && cookie === 'a' && result.status === 200) {
            result.body.data.totalCount = 65;
            result.body.data.items = [{ ...registerRow,
                reference: '99999999-9999-4999-8999-999999999999' }];
        }
        if (path.includes('/register/csv?') && cookie === 'a' && result.status === 200)
            result.body = reportCsv({ ...registerRow,
                reference: '99999999-9999-4999-8999-999999999999' });
        return result;
    };
    await assert.rejects(auditProcurement({ ...config, read, parseCsv }),
        /detail and register evidence/);
    const result = await auditProcurement({ ...config, read, parseCsv,
        allowReferenceBeyondFirstPage: true });
    assert.equal(result.firstPageReferenceFound, false);
    assert.equal(result.reportCount, 65);
});
