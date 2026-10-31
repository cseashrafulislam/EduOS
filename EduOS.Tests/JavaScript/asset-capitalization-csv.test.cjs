const { test } = require('node:test');
const assert = require('node:assert/strict');
const { pathToFileURL } = require('node:url');
const path = require('node:path');

test('bounded bilingual capitalization evidence CSV is safe for spreadsheet imports', async () => {
    const module = await import(pathToFileURL(path.resolve(__dirname,
        '../../EduOS.App/wwwroot/js/inventory/asset-capitalization-csv.mjs')).href);
    const csv = module.capitalizationCsv({
        assetTag: '=HYPERLINK("bad")', campusId: 7, statusCode: 'ReviewRequired',
        candidates: Array.from({ length: 30 }, (_, i) => ({
            receiptReference: 'receipt-' + i, postedMovementReference: 'movement-' + i,
            purchaseOrderId: 4, lineNo: i + 1,
            quantity: 1, postedUnitCost: 120, lotOrBatchNo: i === 0 ? '+SUM(1,1)' : 'lot',
            reviewedAtUtc: '2026-10-31T00:00:00Z'
        }))
    });
    assert.ok(csv.startsWith('\uFEFF'));
    assert.equal(csv.trimEnd().split('\r\n').length, 26);
    assert.ok(csv.includes("'=HYPERLINK"));
    assert.ok(csv.includes("'+SUM"));
    assert.ok(csv.includes('Posted stock movement'));
    assert.ok(csv.includes('movement-0'));
    assert.ok(!csv.includes('receipt-29'));
    assert.ok(!csv.includes('movement-29'));
});
