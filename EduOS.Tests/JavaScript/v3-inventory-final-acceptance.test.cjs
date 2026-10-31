const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = rel => fs.readFileSync(path.join(root,rel),'utf8');
const controller = read('EduOS.App/Controllers/InventoryMovementsController.cs');
const print = read('EduOS.App/Views/InventoryMovements/PrintPhysicalCount.cshtml');
const view = read('EduOS.App/Views/InventoryMovements/_PhysicalCounts.cshtml');
const ui = read('EduOS.App/wwwroot/js/inventory/movements-physical-counts.js');
const sql = read('EduOS.Tests/App/SqlServerV3InventoryMultiStoreAcceptanceTests.cs');

test('count printing refreshes tenant-authorized server projection with private no-store response', () => {
    assert.match(controller,/PrintPhysicalCount\(Guid reference/);
    assert.match(controller,/await movements\.GetPhysicalCountAsync\(reference, ct\)/);
    assert.match(controller,/private, no-store/);
    assert.match(controller,/X-Content-Type-Options/);
    assert.match(print,/@model EduOS\.Core\.DTOs\.Inventory\.InventoryPhysicalCountDto/);
    assert.match(print,/blindDraft = draft && Model\.Blind/);
    assert.match(print,/line\.BookQuantity\?\.ToString/);
    assert.match(print,/@@media print/);
    assert.doesNotMatch(print,/\.IgnoreQueryFilters|@Html\.Raw\(/);
});

test('mobile help and count printing use authenticated route without script-generated private HTML', () => {
    assert.match(view,/countPrint/);
    assert.match(view,/countHelpText/);
    assert.match(view,/aria-expanded="false"/);
    assert.match(ui,/window\.open\('\/InventoryMovements\/PrintPhysicalCount\?reference='/);
    assert.match(ui,/encodeURIComponent\(selected\.reference\)/);
    assert.match(view,/ফিজিক্যাল স্টক গণনা/);
    assert.doesNotMatch(ui,/\.innerHTML\s*=/);
    assert.match(read('.github/workflows/inventory-physical-browser.yml'),/inventory-physical-count\.cjs/);
});

test('real SQL acceptance covers third-store reconciliation, reserved stock and competing source transfers', () => {
    assert.match(sql,/Three_stores_reconcile_posted_stock_reservation/);
    assert.match(sql,/Competing_seven_unit_transfers_to_different_stores/);
    assert.match(sql,/Task\.WhenAll\(first, second\)/);
    assert.match(sql,/Assert\.Single\(outcomes, x => x\.StatusCode == 409\)/);
    assert.match(sql,/QuantityOnHand \+ destination\.QuantityOnHand \+ third\.QuantityOnHand/);
    assert.match(sql,/foreign\.PrintPhysicalCount/);
});
