const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('lot aging is a real server-authorized bilingual paged report with no synthetic valuation', () => {
    const service = read('EduOS.Service/Services/Inventory/InventoryMovementLotAging.cs');
    const api = read('EduOS.App/Controllers/Api/InventoryMovementsLotAging.cs');
    const ui = read('EduOS.App/Views/InventoryMovements/_LotAging.cshtml');
    const js = read('EduOS.App/wwwroot/js/inventory/movements-lot-aging.js');
    assert.match(service, /Ledger\(null\)/);
    assert.match(service, /IsolationLevel\.Serializable/);
    assert.match(service, /ReservedQuantitiesAsync/);
    assert.match(service, /Locations\(\)\.AnyAsync/);
    assert.match(api, /Authorize\(Roles/);
    assert.match(api, /TotalCount > 250/);
    assert.match(api, /Cache-Control/);
    assert.match(api, /"=\+-@/);
    assert.match(ui, /FIFO/);
    assert.match(ui, /bn \?/);
    assert.match(js, /toISOString/);
    assert.match(js, /pagehide/);
    assert.match(js, /textContent/);
    assert.match(js, /noopener,noreferrer/);
});
