const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../EduOS.App');
const script = fs.readFileSync(path.join(root, 'wwwroot/js/inventory/movements-adjustments.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'Views/InventoryMovements/_Adjustments.cshtml'), 'utf8');
const ledger = fs.readFileSync(path.join(root, 'wwwroot/js/inventory/movements-ledger.js'), 'utf8');
test('stock adjustments use dedicated CSRF-protected API and independent approval', () => {
    assert.match(script, /api\('movements\/adjustments', 'POST'/);
    assert.match(script, /clientRequestId: requestId/);
    assert.match(script, /requestId = crypto\.randomUUID\(\)/);
    assert.match(ledger, /row\.sourceType === 'ManualAdjustment'/);
    assert.match(ledger, /row\.createdByUserId/);
});
test('bilingual adjustment form has genuine reason, lot, quantity and cost inputs', () => {
    for (const id of ['adjustmentReason', 'adjustmentLot', 'adjustmentQuantity', 'adjustmentCost', 'adjustmentLocation']) {
        assert.match(view, new RegExp('id="' + id + '"'));
    }
    assert.match(view, /bn \?/);
    assert.match(view, /minlength="10"/);
});
