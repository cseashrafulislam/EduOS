const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('seat forecast uses canonical reservations and bounded tenant-scoped queries', () => {
    const source = read('EduOS.Service/Services/Transport/TransportService.SeatForecast.cs');
    for (const token of ['_assignments.GetQueryable()', 'x.TenantId == tenant', '!x.IsDeleted',
        'TransportAssignmentState.Active', 'AllowedCampusIds(tenant)', 'Take(10001)',
        'PeakReservedSeats', 'GetSeatForecastAsync', 'ExportSeatForecastCsvAsync', 'CsvCell('])
        assert.ok(source.includes(token), token);
    assert.ok(!source.includes('MonthlyFare') && !source.includes('StudentName'));
});

test('seat forecast is wired through authorized API and existing operations workspace', () => {
    const api = read('EduOS.App/Controllers/Api/TransportController.cs');
    const view = read('EduOS.App/Views/TransportOperations/_SeatForecast.cshtml');
    const index = read('EduOS.App/Views/TransportOperations/Index.cshtml');
    const js = read('EduOS.App/wwwroot/js/transport-operations/seat-forecast.js');
    const contract = read('EduOS.Core/Interfaces/IServices/ITransportService.cs');
    assert.ok(api.includes('HttpGet("seat-forecast")'));
    assert.ok(api.includes('HttpGet("seat-forecast/export")'));
    assert.ok(api.includes('GetSeatForecastAsync'));
    assert.ok(api.includes('ExportSeatForecastCsvAsync'));
    assert.ok(contract.includes('GetSeatForecastAsync'));
    assert.ok(view.includes('transportSeatForecastFilter'));
    assert.ok(index.includes('partial name="_SeatForecast"'));
    assert.ok(index.includes('transport-operations/seat-forecast.js'));
    assert.ok(js.includes('/api/transport/seat-forecast?'));
    assert.ok(js.includes('textContent = value'));
});
