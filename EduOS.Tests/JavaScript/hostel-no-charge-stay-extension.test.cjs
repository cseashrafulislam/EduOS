const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('no-charge extension has authorized transactional service and safe UI', () => {
    const service = read('EduOS.Service/Services/Hostel/HostelService.NoChargeStayExtension.cs');
    const api = read('EduOS.App/Controllers/Api/HostelController.cs');
    const view = read('EduOS.App/Views/HostelOperations/Index.cshtml');
    const index = read('EduOS.App/wwwroot/js/hostel-operations/index.js');
    const js = read('EduOS.App/wwwroot/js/hostel-operations/no-charge-stay-extension.js');
    assert.match(api, /HttpPost\("allocations\/\{id:long\}\/extend-no-charge"\)/);
    assert.match(api, /ExtendNoChargeStayAsync/);
    for (const term of ['IsolationLevel.Serializable', 'CanManageCampusAsync', 'PeakConcurrentStays',
        'allocation.MonthlyRent != 0m', 'allocation.SecurityDeposit != 0m',
        'allocation.FeeHeadId.HasValue', 'RowVersion.AsSpan().SequenceEqual'])
        assert.ok(service.includes(term), term);
    assert.match(view, /hostelNoChargeExtensionPanel/);
    assert.match(index, /hostelNoChargeStayExtension/);
    assert.match(index, /hostel-stay-extended/);
    assert.match(js, /rowVersion: pending\.rowVersion/);
    assert.match(js, /RequestVerificationToken: token/);
    assert.doesNotMatch(js, /innerHTML/);
});
