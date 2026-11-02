const { test } = require('node:test');
const assert = require('node:assert/strict');
const { pathToFileURL } = require('node:url');
const { resolve } = require('node:path');
const src = pathToFileURL(resolve(__dirname, '../../scripts/v3-enterprise-detail-csv-audit.mjs')).href;
const stock = '11111111-1111-4111-8111-111111111111';
const asset = '22222222-2222-4222-8222-222222222222';
const journal = '33333333-3333-4333-8333-333333333333';
const s = { movementReference: stock, movementNumber: 'STOCK-1', state: 'Posted',
    recordedMovementValue: 25, sourceType: 'GoodsReceipt', sourceId: 42,
    procurementGoodsReceiptReference: asset, procurementPurchaseOrderReference: journal,
    sourceEvidenceStatus: 'Verified',
    journals: [{ reference: journal, number: 'J-1', state: 'Posted', debit: 25, credit: 25 }] };
const a = { runReference: asset, state: 'Posted', runTotalAmount: -25,
    depreciationLinesAmount: -25, campusId: 10, periodEnd: '2026-10-31',
    journals: [{ reference: journal, state: 'Posted', debit: 25, credit: 25, campusId: 10 }] };
const sh = 'Evidence,Reference,Number,State,Debit,Credit,RecordedValue,SourceType,SourceId,GoodsReceiptReference,PurchaseOrderReference,SourceEvidenceStatus'.split(',');
const ah = 'Evidence,Reference,State,Debit,Credit,RunAmount,LinesAmount,Campus,PeriodEnd'.split(',');
const sr = [['Stock', stock, 'STOCK-1', 'Posted', '', '', '25.0000000000', 'GoodsReceipt', '42', asset, journal, 'Verified'],
    ['Journal', journal, 'J-1', 'Posted', '25', '25', '', '', '', '', '', '']];
const ar = [['AssetDepreciation', asset, 'Posted', '', '', "'-25", "'-25", '10', '2026-10-31'],
    ['Journal', journal, 'Posted', '25', '25', '', '', '10', '']];
test('exact stock and asset detail CSV including decimal scale accepted', async () => {
    const { assertDetailCsvParity } = await import(src);
    assert.doesNotThrow(() => assertDetailCsvParity({ kind: 'stock', data: s,
        csv: { header: sh, rows: sr }, reference: stock }));
    assert.doesNotThrow(() => assertDetailCsvParity({ kind: 'asset', data: a,
        csv: { header: ah, rows: ar }, reference: asset }));
});
test('stock CSV source linkage, journal money, count and schema corruption fail', async () => {
    const { assertDetailCsvParity } = await import(src);
    const check = (rows, header = sh) => assertDetailCsvParity({ kind: 'stock', data: s,
        csv: { header, rows }, reference: stock });
    assert.throws(() => check(sr.map((r, i) => i ? [...r] :
        r.map((x, j) => j === 9 ? stock : x))), /source\/Finance/);
    assert.throws(() => check(sr.map((r, i) => i ?
        r.map((x, j) => j === 4 ? '24' : x) : [...r])),
        /journal source\/Finance|monetary formula/);
    assert.throws(() => check(sr.slice(0, 1)), /row count/);
    assert.throws(() => check(sr, [...sh, 'Unexpected']), /schema/);
});
test('asset CSV period, campus, totals, formula and journal state corruption fail', async () => {
    const { assertDetailCsvParity } = await import(src);
    const check = rows => assertDetailCsvParity({ kind: 'asset', data: a,
        csv: { header: ah, rows }, reference: asset });
    for (const [col, value] of [[5, '-25'], [6, "'-26"], [7, '20'], [8, '2026-10-30']])
        assert.throws(() => check(ar.map((r, i) => i ? [...r] :
            r.map((x, j) => j === col ? value : x))),
            /campus\/period\/Finance|monetary formula/);
    assert.throws(() => check(ar.map((r, i) => i ?
        r.map((x, j) => j === 2 ? 'Draft' : x) : [...r])),
        /journal source\/Finance/);
});
