const test = require('node:test'), assert = require('node:assert/strict'), fs = require('node:fs');
const read = p => fs.readFileSync(p, 'utf8');
const service = read('EduOS.Service/Services/Transport/TransportService.cs');
const dto = read('EduOS.Core/DTOs/Transport/TransportDtos.cs');
const api = read('EduOS.App/Controllers/Api/TransportController.cs');
const view = read('EduOS.App/Views/TransportOperations/Index.cshtml');
const js = read('EduOS.App/wwwroot/js/transport-operations/index.js');

test('canonical assignment request and response carry optional effective end date', () => {
    assert.match(dto, /class AssignStudentTransportRequestDto[\s\S]*?public DateOnly\? EndDate/);
    assert.match(dto, /class TransportAssignmentRowDto[\s\S]*?public DateOnly\? EndDate/);
    assert.match(service, /StartDate = startDate, EndDate = request.EndDate/);
    assert.match(service, /StartDate = x.StartDate, EndDate = x.EndDate/);
});
test('existing authorized API and workspace handle bounded and open-ended reservations', () => {
    assert.match(api, /\[HttpPost\("assignments"\)\]\s*\[Authorize\(Roles = "TenantAdmin,Principal,TransportManager"\)\]/);
    assert.match(view, /id="transportEnd"/);
    assert.match(view, /<th>End<\/th>/);
    assert.match(js, /endDate: el\('transportEnd'\)\.value \|\| null/);
    assert.match(js, /body.endDate < body.startDate/);
    assert.match(js, /item.endDate \|\| 'Open-ended'/);
    assert.doesNotMatch(js, /innerHTML/);
    new Function(js);
});
test('write checks full route coverage and peak concurrent occupancy inside serializable transaction', () => {
    assert.match(service, /_unitOfWork\.ExecuteInTransactionAsync\(async ct =>/);
    assert.match(service, /System\.Data\.IsolationLevel\.Serializable, cancellationToken/);
    assert.doesNotMatch(service, /new TransactionScope\(/);
    assert.match(service, /request.EndDate.HasValue && request.EndDate.Value < startDate/);
    assert.match(service, /request.EndDate.HasValue && x.EffectiveTo.Value >= request.EndDate.Value/);
    assert.match(service, /x.StartDate <= requestedEnd/);
    assert.match(service, /x.EndDate.Value >= startDate/);
    assert.match(service, /\.Take\(10001\)\.ToListAsync\(ct\)/);
    assert.match(service, /PeakReservedSeats\(overlapping.Select/);
    assert.match(service, /replay.EndDate != request.EndDate/);
    assert.match(service, /x.StartDate <= \(request.EndDate \?\? DateOnly.MaxValue\)/);
    assert.match(service, /x.EndDate.Value >= today/);
    assert.match(service, /assignment.EndDate.Value >= today/);
    assert.doesNotMatch(service, /var occupied = await _assignments\.GetQueryable\(\)\.AsNoTracking\(\)\.CountAsync/);
});
