const { test } = require('node:test');
const assert = require('node:assert/strict');
const { pathToFileURL } = require('node:url');
const { resolve } = require('node:path');
const source = pathToFileURL(resolve(__dirname, '../../scripts/v3-enterprise-paged-audit.mjs')).href;
const audit = async args => (await import(source)).auditRegisterPages(args);
const stock = '11111111-1111-4111-8111-111111111111';
const api = '/api/v3/enterprise/ledger-trace';
const from = '2026-10-01', to = '2026-10-31';
const reference = index => `${index.toString(16).padStart(8, '0')}-1111-4111-8111-111111111111`;
const rows = (n, seed, prefix = 1) => Array.from({ length: n }, (_, i) => ({
    reference: i === seed ? stock : reference(prefix * 1000 + i),
    number: 'STOCK-' + i, campusId: 7, recordedValue: '24.0000000000',
    state: 'Posted', lineCount: 1, evidenceStatus: 'NoExplicitReference',
    date: '2026-10-31'
}));
const parseCsv = body => {
    const lines = body.trim().split('\n').map(x => x.split(','));
    return { header: lines[0], rows: lines.slice(1) };
};
const response = (status, body, type = 'application/json') => ({ status,
    body, type, cache: 'no-store' });
const csvCell = value => {
    const s = String(value ?? '');
    return s && '=+-@\t\r'.includes(s[0]) ? "'" + s : s;
};
const csvOf = (kind, items) =>
    'Kind,Reference,Number,Date,Campus,State,RecordedValue,Lines,EvidenceStatus\n' +
    items.map(x => [kind, x.reference, x.number, x.date, x.campusId,
        x.state, x.recordedValue, x.lineCount, x.evidenceStatus]
        .map(csvCell).join(',')).join('\n');
function fakeRead(aRows, bRows, options = {}) {
    const jsonReads = new Map();
    return async (cookie, path) => {
        const url = new URL('http://localhost' + path);
        const page = Number(url.searchParams.get('page'));
        const kind = url.searchParams.get('kind');
        const source = cookie === 'a' ? aRows : bRows;
        const items = source.slice((page - 1) * 50, page * 50);
        const key = cookie + ':' + page;
        if (!path.includes('/csv?'))
            jsonReads.set(key, (jsonReads.get(key) || 0) + 1);
        // A legitimate-looking JSON+CSV pair may drift only on replay.
        // Keep the page count and references unchanged to expose the blind spot.
        if (options.lateMutation?.cookie === cookie &&
            options.lateMutation.page === page &&
            (jsonReads.get(key) || 0) > 1 && items.length)
            items[0] = { ...items[0], state: 'Draft' };
        if (options.duplicate && page === 2 && items.length) items[0] = source[0];
        if (options.foreignLeak && cookie === 'b' && page === 2 && items.length)
            items[0] = { ...items[0], reference: stock };
        if (options.foreignOverlapPage === page && cookie === 'b' && items.length)
            items[0] = { ...items[0], reference: aRows[10].reference.toUpperCase() };
        if (path.includes('/csv?')) {
            const csvRows = items.map((x, i) => {
                if (i !== 0 || page !== 2) return x;
                if (options.csvMismatch && cookie === 'a') return { ...x, state: 'Draft' };
                if (options.csvField && cookie === 'a')
                    return { ...x, [options.csvField]: options.csvValue };
                if (options.foreignCsvLeak && cookie === 'b')
                    return { ...x, reference: stock };
                return x;
            });
            let csv = csvOf(kind, csvRows);
            if (options.unsafeFormula && cookie === 'a' && page === 2)
                csv = csv.replace("'=EXTERNAL", '=EXTERNAL');
            if (options.badHeader && cookie === 'a' && page === 2)
                csv = csv.replace('RecordedValue,', 'RecordedValue,RecordedValue,');
            return response(200, csv, 'text/csv');
        }
        return response(200, { success: true, data: { page, pageSize: 50,
            kind: options.wrongKind && cookie === 'a' && page === 2 ? 'asset' : kind,
            from: options.wrongFrom && cookie === 'a' && page === 2 ? '2026-09-01' : from,
            to, totalCount: options.countDrift && page === 2 ? source.length + 1 : source.length,
            items } });
    };
}
const args = read => ({ read, parseCsv, api, kind: 'stock', reference: stock,
    from, to, tenantA: 'a', tenantB: 'b' });
test('finds seeded evidence on second page and reconciles all tenant pages', async () => {
    const result = await audit(args(fakeRead(rows(65, 55), rows(61, -1, 2))));
    assert.deepEqual(result, { kind: 'stock', count: 65, pagesAudited: 2 });
});
test('detects foreign evidence on second page', async () => {
    await assert.rejects(audit(args(fakeRead(rows(65, 55),
        rows(61, -1, 2), { foreignLeak: true }))), /Foreign register leaked/);
});
test('rejects non-seeded tenant A references leaked on foreign first or later page', async () => {
    for (const foreignOverlapPage of [1, 2])
        await assert.rejects(audit(args(fakeRead(rows(65, 55),
            rows(61, -1, 2), { foreignOverlapPage }))),
        /Foreign register leaked tenant A evidence/);
});
test('detects duplicate page boundary, count drift and CSV mismatch', async () => {
    for (const [key, error] of [['duplicate', /Duplicate register/],
        ['countDrift', /count changed/], ['csvMismatch', /CSV does not match/]])
        await assert.rejects(audit(args(fakeRead(rows(65, 55),
            rows(61, -1, 2), { [key]: true }))), error);
});
test('rejects in-place and later-page drift even when JSON/CSV and counts agree', async () => {
    for (const [cookie, page] of [['a', 1], ['a', 2], ['b', 2]]) {
        const read = fakeRead(rows(65, 55), rows(61, -1, 2), {
            lateMutation: { cookie, page }
        });
        await assert.rejects(audit(args(read)),
            /Register page replay changed during audit/);
    }
});

test('fails closed on unbounded scans rather than accepting partial tenant evidence', async () => {
    await assert.rejects(audit({ ...args(fakeRead(rows(101, 55), [])),
        maxPages: 2 }), /exceeds bounded audit pages/);
});

const procurementRow = (index, wanted = false) => ({
    reference: wanted ? stock : reference(5000 + index),
    number: 'PO-' + index, date: '2026-10-31', campusId: 7,
    state: 'Approved', lineCount: 1, recordedValue: '24.0000000000',
    evidenceStatus: 'ApprovedCommitment'
});
const procurementCsv = items => 'Kind,Reference,Number,Date,Campus,State,RecordedValue,Lines,EvidenceStatus\n' +
    items.map(x => ['procurement', x.reference, x.number, x.date, x.campusId,
        x.state, x.recordedValue, x.lineCount, x.evidenceStatus].join(',')).join('\n');
function procurementRead(options = {}) {
    const a = Array.from({ length: 63 }, (_, i) => procurementRow(i, i === 54));
    const b = Array.from({ length: 55 }, (_, i) => ({
        ...procurementRow(i + 1000), reference: reference(9000 + i)
    }));
    return async (cookie, path) => {
        const url = new URL('http://localhost' + path);
        const page = Number(url.searchParams.get('page'));
        const source = cookie === 'a' ? a : b;
        const items = source.slice((page - 1) * 50, page * 50).map(x => ({ ...x }));
        if (options.foreignLeak && cookie === 'b' && page === 2 && items.length)
            items[0].reference = stock;
        if (options.foreignOverlap && cookie === 'b' && page === 2 && items.length)
            items[0].reference = a[10].reference.toUpperCase();
        if (options.amountMismatch && cookie === 'a' && page === 2 && items.length)
            items[0].recordedValue = '999.00';
        if (options.campusMismatch && cookie === 'a' && page === 2 && items.length)
            items[0].campusId = 999;
        if (path.includes('/csv?')) {
            const original = source.slice((page - 1) * 50, page * 50);
            return response(200, procurementCsv(options.amountMismatch ||
                options.campusMismatch ? original : items), 'text/csv');
        }
        return response(200, { success: true, data: { kind: 'procurement',
            page, pageSize: 50, from, to, totalCount: source.length, items } });
    };
}
const procurementArgs = read => ({ ...args(read), kind: 'procurement' });
test('procurement multi-page trace finds PO on page two with full CSV/tenant parity', async () => {
    const result = await audit(procurementArgs(procurementRead()));
    assert.deepEqual(result, { kind: 'procurement', count: 63, pagesAudited: 2 });
});
test('procurement later-page foreign disclosure, campus and decimal mismatches fail closed', async () => {
    for (const [key, expected] of [
        ['foreignLeak', /Foreign register leaked/],
        ['amountMismatch', /amount or campus mismatch/],
        ['campusMismatch', /amount or campus mismatch/]
    ]) await assert.rejects(audit(procurementArgs(
        procurementRead({ [key]: true }))), expected);
});

test('procurement foreign page cannot expose another tenant non-seeded PO reference', async () => {
    await assert.rejects(audit(procurementArgs(
        procurementRead({ foreignOverlap: true }))),
    /Foreign register leaked tenant A evidence/);
});
test('procurement later-page JSON and CSV may agree but must also match canonical PO detail', async () => {
    const read = procurementRead();
    const corrupted = async (cookie, path) => {
        const result = await read(cookie, path);
        const url = new URL('http://localhost' + path);
        if (cookie === 'a' && Number(url.searchParams.get('page')) === 2) {
            if (path.includes('/csv?')) {
                const lines = result.body.split('\n');
                lines[5] = lines[5].replace('24.0000000000', '25.0000000000');
                result.body = lines.join('\n');
            }
            else if (result.body?.data?.items)
                result.body.data.items[4].recordedValue = '25.0000000000';
        }
        return result;
    };
    await assert.rejects(audit({
        ...procurementArgs(corrupted),
        expectedEvidence: { state: 'Approved', lineCount: 1,
            campusId: 7, totalAmount: '24.0000000000' }
    }), /detail and later-page register evidence/);
});

test('stock and asset multi-page CSV must match every canonical source column', async () => {
    for (const kind of ['stock', 'asset']) {
        const a = rows(65, 55), b = rows(61, -1, 2);
        assert.equal((await audit({ ...args(fakeRead(a, b)), kind })).count, 65);
        for (const [csvField, csvValue] of [
            ['number', 'FORGED-ROW'], ['date', '2026-10-30'],
            ['campusId', 99], ['recordedValue', '999.0000000000']
        ]) await assert.rejects(audit({ ...args(fakeRead(a, b,
            { csvField, csvValue })), kind }), /CSV\/source|CSV does not match/);
    }
});

test('stock CSV accepts escaped negative money and formula-like number, rejects unsafe CSV', async () => {
    const a = rows(65, 55), b = rows(61, -1, 2);
    a[0].recordedValue = '-12.5000000000';
    a[50].number = '=EXTERNAL';
    a[54].campusId = null; // inter-campus movement has no single campus
    assert.equal((await audit(args(fakeRead(a, b)))).pagesAudited, 2);
    await assert.rejects(audit(args(fakeRead(a, b, { unsafeFormula: true }))),
        /CSV does not match/);
});

test('register audit rejects changed filters and malformed CSV schema on later pages', async () => {
    for (const [option, error] of [
        ['wrongKind', /metadata invalid/], ['wrongFrom', /metadata invalid/],
        ['badHeader', /CSV schema mismatch/],
        ['foreignCsvLeak', /CSV does not match/]
    ]) await assert.rejects(audit(args(fakeRead(rows(65, 55),
        rows(61, -1, 2), { [option]: true }))), error);
});

test('register rejects invalid source line counts and campus IDs even when CSV agrees', async () => {
    for (const [field, value] of [['lineCount', -1], ['campusId', 0]]) {
        const a = rows(65, 55);
        a[54][field] = value;
        await assert.rejects(audit(args(fakeRead(a, rows(61, -1, 2)))),
            /source line count or campus invalid/);
    }
});
