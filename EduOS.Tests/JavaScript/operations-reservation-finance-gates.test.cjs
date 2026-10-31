'use strict';
const fs = require('node:fs'), path = require('node:path'), assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const cases = [
    ['EduOS.Service/Services/Transport/TransportService.ActiveStopCorrection.cs', 'assignment.MonthlyFare', 'assignment.PickupStopId = request.PickupStopId'],
    ['EduOS.Service/Services/Transport/TransportService.UpcomingStopCorrection.cs', 'assignment.MonthlyFare', 'assignment.PickupStopId = request.PickupStopId'],
    ['EduOS.Service/Services/Transport/TransportService.UpcomingVehicleChange.cs', 'assignment.MonthlyFare', 'assignment.VehicleId = vehicle.Id'],
    ['EduOS.Service/Services/Transport/TransportService.OperatingVehicleTransfer.cs', 'source.MonthlyFare', 'source.EndDate = request.EffectiveDate.AddDays(-1)'],
    ['EduOS.Service/Services/Hostel/HostelService.UpcomingBedReassignment.cs', 'allocation.MonthlyRent, allocation.SecurityDeposit, allocation.FeeHeadId.HasValue', 'allocation.HostelBedId = target.Id'],
    ['EduOS.Service/Services/Hostel/HostelService.UpcomingRoomReassignment.cs', 'allocation.MonthlyRent, allocation.SecurityDeposit, allocation.FeeHeadId.HasValue', 'allocation.HostelBedId = target.Bed.Id']
];
for (const [file, chargeTerms, mutation] of cases) {
    const source = read(file);
    assert.match(source, /HasPostedActivityAsync\(tenant,/);
    assert.ok(source.includes('ReservationFinancePolicy.HasChargeableTerms('), file);
    assert.ok(source.includes(chargeTerms), file);
    const posted = source.indexOf('HasPostedActivityAsync(tenant,');
    const charged = source.indexOf('ReservationFinancePolicy.HasChargeableTerms(');
    const write = source.indexOf(mutation);
    assert.ok(posted >= 0 && charged > posted && write > charged, file + ': finance gate must precede mutation');
    assert.match(source, /IsolationLevel\.Serializable/);
    assert.match(source, /CanManageCampusAsync/);
}
const hostel = read('EduOS.App/Views/HostelOperations/Index.cshtml');
const transport = read('EduOS.App/Views/TransportOperations/Index.cshtml');
assert.match(hostel, /Charged stays require Finance-approved adjustment/);
assert.match(transport, /Charged\/billed transfers require an approved, single-use/);
assert.match(transport, /Charged changes require Finance-approved adjustment/);
console.log('Hostel and Transport reservation mutation Finance gates: PASS');
