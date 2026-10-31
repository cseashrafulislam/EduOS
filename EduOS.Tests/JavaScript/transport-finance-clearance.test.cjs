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
    assert.match(service, /previous\.PreviousAssignmentId == source\.Id/);
    assert.match(service, /HasSqlSerializationConflict\(ex\)/);
});

test('V2 SQL release gate exercises maker-checker, transfer lineage and concurrent clearance consumption', () => {
    const tests = read('EduOS.Tests/App/SqlServerTransportFinanceClearanceAcceptanceTests.cs');
    const workflow = read('.github/workflows/sqlserver-migration.yml');
    assert.match(tests, /Finance_approved_transport_transfer_is_single_use_tenant_scoped_and_preserves_lineage/);
    assert.match(tests, /Concurrent_transport_clearance_consumption_creates_exactly_one_successor/);
    assert.match(tests, /Charged_transport_closure_rejects_unposted_settlement_and_requires_independent_zero_attestation/);
    assert.match(tests, /ScopedContext\(fixture\.Options, fixture\.TenantId \+ 10_000/);
    assert.match(tests, /Assert\.Equal\(TransportClearanceState\.Consumed/);
    assert.match(workflow, /sql-v2-transport-finance-acceptance\.trx/);
    assert.match(workflow, /Concurrent_transport_clearance_consumption_creates_exactly_one_successor/);
});
