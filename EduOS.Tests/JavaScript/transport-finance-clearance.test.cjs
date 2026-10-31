'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');

test('Transport Finance page exposes auto/manual policy, independent review and safe request binding', () => {
    const view = read('EduOS.App/Views/FinanceAdjustments/TransportClearances.cshtml');
    const js = read('EduOS.App/wwwroot/js/finance/transport-clearances.js');
    const controller = read('EduOS.App/Controllers/FinanceAdjustmentsController.cs');
    assert.match(view, /Auto · unused days of month/);
    assert.match(view, /Manual · enter credit/);
    assert.match(view, /Request an operational clearance/);
    assert.match(js, /RequestVerificationToken = token/);
    assert.match(js, /allowManualOverride/);
    assert.match(js, /clientRequestId: lastKey/);
    assert.match(js, /sourceRowVersion: selected\.rowVersion/);
    assert.match(js, /settlementJournalId/);
    assert.match(js, /noAdjustmentRequired/);
    assert.match(js, /reconciliationNote/);
    assert.match(controller, /TransportClearances\(\)/);
    assert.doesNotMatch(js, /innerHTML/);
    new Function(js);
});

test('Transport settlement cannot be reused and old assignment is linked to successor', () => {
    const service = read('EduOS.Service/Services/Transport/TransportService.OperatingVehicleTransfer.cs');
    const guard = read('EduOS.Service/Services/Transport/TransportService.FinanceClearance.cs');
    const close = read('EduOS.Service/Services/Transport/TransportService.cs');
    assert.match(service, /PreviousAssignmentId = source\.Id/);
    assert.match(service, /ConsumeTransportFinanceClearanceAsync/);
    assert.match(service, /IsolationLevel\.Serializable/);
    assert.match(guard, /row\.State != TransportClearanceState\.Approved/);
    assert.match(guard, /row\.State = TransportClearanceState\.Consumed/);
    assert.match(guard, /GetExposureHashAsync/);
    assert.match(close, /TransportClearanceOperation\.Closure/);
});
