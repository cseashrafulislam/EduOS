'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const read = path => fs.readFileSync(path, 'utf8');
const service = read('EduOS.Service/Services/Transport/TransportService.cs');
const iface = read('EduOS.Core/Interfaces/IServices/ITransportService.cs');
const api = read('EduOS.App/Controllers/Api/TransportController.cs');
const js = read('EduOS.App/wwwroot/js/transport-operations/index.js');
const view = read('EduOS.App/Views/TransportOperations/Index.cshtml');

test('eligible student selection uses inclusive reservation overlap and bounded campus-scoped paging', () => {
    assert.match(service, /requestedStart = startDate \?\? today/);
    assert.match(service, /requestedEnd = endDate \?\? DateOnly.MaxValue/);
    assert.match(service, /x.StartDate <= requestedEnd/);
    assert.match(service, /x.EndDate.Value >= requestedStart/);
    assert.match(service, /allowed.Contains\(x.CampusId\)/);
    assert.match(service, /pageSize is < 1 or > 100/);
    assert.match(iface, /GetEligibleStudentsAsync\(int page, int pageSize, string\? search, DateOnly\? startDate/);
    assert.match(api, /\[FromQuery\] DateOnly\? startDate = null/);
    assert.match(api, /GetEligibleStudentsAsync\(page, pageSize, search, startDate, endDate, cancellationToken\)/);
});
test('assignment UI refreshes eligible students when reservation dates change', () => {
    assert.match(js, /startDate: el\('transportStart'\)\.value \|\| localDate\(\)/);
    assert.match(js, /q.set\('endDate', endDate\)/);
    assert.match(js, /\['transportStart', 'transportEnd'\]/);
    assert.match(js, /studentPage = 1/);
    assert.match(view, /Student eligibility uses the selected start\/end dates/);
    assert.doesNotMatch(js, /innerHTML/);
    new Function(js);
});
test('vehicle catalogue reports today occupancy, not all future reservations', () => {
    assert.match(service, /x.StartDate <= today && \(!x.EndDate.HasValue \|\| x.EndDate.Value >= today\)/);
    assert.match(js, /Riding today/);
    assert.match(view, /Vehicle counts show riders today/);
});
