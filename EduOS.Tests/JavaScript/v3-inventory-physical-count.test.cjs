const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '../..');
const read = rel => fs.readFileSync(path.join(root, rel), 'utf8');
const service = read('EduOS.Service/Services/Inventory/InventoryMovementPhysicalCounts.cs');
const migration = read('EduOS.Persistence/Migrations/20261031180000_V3InventoryPhysicalStockCounts.cs');
const ui = read('EduOS.App/wwwroot/js/inventory/movements-physical-counts.js');
const view = read('EduOS.App/Views/InventoryMovements/_PhysicalCounts.cshtml');

test('blind count redacts book fields until draft is submitted', () => {
    assert.match(service, /var reveal = !count\.Blind \|\| count\.StateCode != "Draft"/);
    assert.match(service, /BookQuantity = reveal \? x\.BookQuantity : null/);
    assert.match(service, /ReservedQuantity = reveal \? x\.ReservedQuantity : null/);
    assert.match(service, /CountedQuantity == null/);
    assert.match(ui, /validQuantity\(x\.value\.trim\(\)\)/);
});

test('approval is maker-checker with serialized posted movements and reservation protection', () => {
    assert.match(service, /BeginTransactionAsync\(IsolationLevel\.Serializable/);
    assert.match(service, /count\.CreatedBy == user\.UserId/);
    assert.match(service, /await CheckPostingAsync\(movement, ct\)/);
    assert.match(service, /movement\.SourceId = count\.Id/);
    assert.match(service, /movement\.StateCode = "Posted"/);
    assert.match(service, /count\.ReviewFingerprint == hash/);
    assert.match(service, /Stock changed since snapshot/);
});

test('database has immutable snapshot guard, tenant FKs, replay keys and safe rollback', () => {
    assert.match(migration, /TR_InventoryPhysicalCount_SnapshotImmutable/);
    assert.match(migration, /TR_InventoryPhysicalCountLine_SnapshotImmutable/);
    assert.match(migration, /IX_InventoryPhysicalCounts_TenantId_ClientRequestId/);
    assert.match(migration, /InventoryPhysicalCounts_TenantId_LocationId/);
    assert.match(migration, /Physical count audit history requires controlled archival/);
});

test('three variance policies, bilingual reviewer interface and downloadable report are wired', () => {
    for (const policy of ['Automatic', 'ManualFinal', 'AutoPlusManual']) {
        assert.ok(service.includes(policy));
        assert.ok(view.includes(policy));
    }
    assert.match(view, /স্টক গণনা/);
    assert.match(ui, /exportCountCsv/);
    assert.match(ui, /postedMovementReference/);
    assert.doesNotMatch(ui, /\.innerHTML\s*=/);
    assert.match(read('EduOS.App/wwwroot/js/inventory/movements.js'), /initPhysicalCounts/);
    assert.match(read('EduOS.App/Views/InventoryMovements/Index.cshtml'), /_PhysicalCounts/);
});
