const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('no-charge future checkout shortening is a complete authorized vertical slice', () => {
    const service = read('EduOS.Service/Services/Hostel/HostelService.UpcomingNoChargeShortening.cs');
    const api = read('EduOS.App/Controllers/Api/HostelController.cs');
    const view = read('EduOS.App/Views/HostelOperations/Index.cshtml');
    const index = read('EduOS.App/wwwroot/js/hostel-operations/index.js');
    const js = read('EduOS.App/wwwroot/js/hostel-operations/upcoming-no-charge-shortening.js');
    assert.match(api, /HttpPost\("allocations\/\{id:long\}\/shorten-upcoming-no-charge"\)/);
    assert.match(api, /ShortenUpcomingNoChargeStayAsync/);
    for (const term of ['IsolationLevel.Serializable', 'CanManageCampusAsync',
        'allocation.MonthlyRent != 0m', 'allocation.SecurityDeposit != 0m',
        'allocation.FeeHeadId.HasValue', 'RowVersion.AsSpan().SequenceEqual',
        'allocation.UpdatedBy', 'allocation.StartDate <= today'])
        assert.ok(service.includes(term), term);
    assert.match(view, /hostelNoChargeShorteningPanel/);
    assert.match(index, /hostelUpcomingNoChargeShortening/);
    assert.match(index, /hostel-stay-shortened/);
    assert.match(js, /rowVersion: pending\.rowVersion/);
    assert.match(js, /RequestVerificationToken: token/);
    assert.doesNotMatch(js, /innerHTML/);
    new Function(js);
});
