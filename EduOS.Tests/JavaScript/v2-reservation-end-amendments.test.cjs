const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const load = filename => fs.readFileSync(path.join(root, filename), 'utf8');

test('Hostel extension and shortening require exact end-date Finance consumption for charged stays', () => {
    for (const file of ['NoChargeStayExtension.cs', 'UpcomingNoChargeShortening.cs']) {
        const service = load('EduOS.Service/Services/Hostel/HostelService.' + file);
        assert.match(service, /financeExposure \|\| allocation\.MonthlyRent != 0m/);
        assert.match(service, /ConsumeHostelFinanceClearanceAsync/);
        assert.match(service, /HostelClearanceOperation\.UpcomingEndChange/);
        assert.match(service, /FinanceClearanceId\?\.ToString\(\) \?\? "none"/);
        assert.match(service, /IsolationLevel\.Serializable/);
        assert.match(service, /_commandReceipts\.RecordAsync/);
    }
    const finance = load('EduOS.Service/Services/Finance/FinanceAdjustmentHostelClearances.cs');
    assert.match(finance, /HostelEndChangeAllowed/);
    assert.match(finance, /ReservationTermQuoteCalculator\.Calculate/);
    assert.match(finance, /ReservationAutomaticCreditNote\.PrepareAsync/);
    assert.match(finance, /ReservationAutomaticInvoicePosting\.PrepareAsync/);
    assert.match(load('EduOS.App/Views/FinanceAdjustments/HostelClearances.cshtml'), /option value="4"/);
    assert.match(load('EduOS.App/wwwroot/js/finance/hostel-clearances.js'), /const isPeriod/);
});

test('Transport upcoming shortening uses one approved Finance source and is available from the responsive UI', () => {
    const service = load('EduOS.Service/Services/Transport/TransportService.UpcomingNoChargeShortening.cs');
    const finance = load('EduOS.Service/Services/Finance/FinanceAdjustmentTransportClearances.cs');
    assert.match(service, /financeExposure \|\| !IsNoChargeRiderFare/);
    assert.match(service, /ConsumeTransportFinanceClearanceAsync/);
    assert.match(service, /TransportClearanceOperation\.UpcomingRiderShortening/);
    assert.match(service, /FinanceClearanceId\?\.ToString\(\) \?\? "none"/);
    assert.match(finance, /UpcomingRiderShortening/);
    assert.match(finance, /ReservationAutomaticCreditNote\.PrepareAsync/);
    assert.match(load('EduOS.App/Views/FinanceAdjustments/TransportClearances.cshtml'), /option value="4"/);
    const ui = load('EduOS.App/wwwroot/js/transport-operations/upcoming-no-charge-shortening.js');
    assert.match(ui, /financeClearanceId/);
    assert.match(ui, /shorten-upcoming/);
    assert.match(ui, /RequestVerificationToken: token/);
    assert.doesNotMatch(ui, /innerHTML/);
});

test('End-change database constraints are additive with a data-preserving rollback stop', () => {
    const migration = load('EduOS.Persistence/Migrations/20261031170000_V2ReservationEndPeriodChanges.cs');
    assert.match(migration, /CK_HostelFinanceClearance_Operation/);
    assert.match(migration, /CK_TransportFinanceClearance_Operation/);
    assert.match(migration, /\[Operation\] IN \(1,2,3,4\)/);
    assert.match(migration, /IF EXISTS .* \[Operation\] = 4/);
    const snapshot = load('EduOS.Persistence/Migrations/EduOSDbContextModelSnapshot.cs');
    assert.match(snapshot, /CK_HostelFinanceClearance_Operation", "\[Operation\] IN \(1,2,3,4\)"/);
    assert.match(snapshot, /CK_TransportFinanceClearance_Operation", "\[Operation\] IN \(1,2,3,4\)"/);
});
