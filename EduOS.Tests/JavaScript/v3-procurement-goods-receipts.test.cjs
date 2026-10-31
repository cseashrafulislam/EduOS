const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const service = read('EduOS.Service/Services/Inventory/InventoryMovementProcurementReceipts.cs');
const migration = read('EduOS.Persistence/Migrations/20261031190000_V3ProcurementGoodsReceipts.cs');
const view = read('EduOS.App/Views/ProcurementGoodsReceipts/Index.cshtml');
const ui = read('EduOS.App/wwwroot/js/procurement/goods-receipts.js');

test('approved PO source, campus scope and cumulative serializable partial quantity', () => {
    assert.match(service, /BeginTransactionAsync\(IsolationLevel\.Serializable/);
    assert.match(service, /po\.StateCode != "Approved"/);
    assert.match(service, /x\.CampusId == po\.CampusId/);
    assert.match(service, /ReceiptCommittedQuantitiesAsync/);
    assert.match(service, /StateCode == "Submitted" \|\| header\.StateCode == "Posted"/);
    assert.match(service, /approved\[x\.Key\]\.Quantity/);
    assert.match(service, /receipt\.CreatedBy != user\.UserId/);
});

test('receipt cannot become stock without independent checker, revalidation and canonical lineage', () => {
    assert.match(service, /receipt\.CreatedBy == user\.UserId \|\| receipt\.SubmittedByUserId == user\.UserId/);
    assert.match(service, /ReceiptSourceValidAsync\(receipt, lines, ct\)/);
    assert.match(service, /movement\.SourceId = receipt\.Id/);
    assert.match(service, /await CheckPostingAsync\(movement, ct\)/);
    assert.match(service, /movement\.StateCode = "Posted"/);
    assert.match(service, /receipt\.ReviewFingerprint == hash/);
    assert.match(migration, /UX_InventoryMovements_ProcurementGoodsReceipt/);
});

test('three costing modes, immutable SQL snapshots, and rollback guard', () => {
    for (const mode of ['Automatic', 'ManualFinal', 'AutoPlusManual']) {
        assert.ok(service.includes(mode)); assert.ok(view.includes(mode));
    }
    assert.match(service, /Valuation override needs line reason/);
    assert.match(migration, /TR_ProcurementGoodsReceipt_SnapshotImmutable/);
    assert.match(migration, /TR_ProcurementGoodsReceiptLine_SnapshotImmutable/);
    assert.match(migration, /Goods receipt history requires controlled retention/);
});

test('responsive workflow, bilingual labels, safe DOM rendering and CSV route', () => {
    assert.match(view, /পণ্য গ্রহণ/);
    assert.match(view, /grReviewDialog/);
    assert.match(ui, /\/api\/procurement\/purchase-orders/);
    assert.match(ui, /\/api\/inventory\/catalog\/locations/);
    assert.match(ui, /purchaseOrderLineId: chosenPo\.lines\[i\]\.id/);
    assert.match(ui, /\.append\(/);
    assert.doesNotMatch(ui, /\.innerHTML\s*=/);
    assert.match(read('EduOS.App/Controllers/Api/ProcurementGoodsReceiptsController.cs'), /\[HttpGet\("export"\)/);
    assert.match(read('EduOS.App/Views/ProcurementPurchaseOrders/Index.cshtml'), /ProcurementGoodsReceipts/);
});
