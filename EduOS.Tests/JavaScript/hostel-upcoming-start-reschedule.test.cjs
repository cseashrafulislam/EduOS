const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('hostel pre-arrival reschedule is a complete authorized operations slice', () => {
    const service = read('EduOS.Service/Services/Hostel/HostelService.UpcomingStartReschedule.cs');
    const api = read('EduOS.App/Controllers/Api/HostelController.cs');
    const view = read('EduOS.App/Views/HostelOperations/Index.cshtml');
    const index = read('EduOS.App/wwwroot/js/hostel-operations/index.js');
    const js = read('EduOS.App/wwwroot/js/hostel-operations/upcoming-start-reschedule.js');
    assert.match(api, /HttpPost\("allocations\/\{id:long\}\/reschedule-start"\)/);
    assert.match(api, /RescheduleUpcomingAllocationStartAsync/);
    assert.match(service, /IsolationLevel\.Serializable/);
    assert.match(service, /CanManageCampusAsync/);
    assert.match(service, /RowVersion\.AsSpan\(\)\.SequenceEqual/);
    assert.match(service, /PeakConcurrentStays/);
    assert.match(service, /x\.StudentId == allocation\.StudentId/);
    assert.match(service, /x\.HostelBedId == allocation\.HostelBedId/);
    assert.match(view, /hostelStartReschedulePanel/);
    assert.match(index, /hostelUpcomingStartReschedule/);
    assert.match(index, /hostel-start-rescheduled/);
    assert.match(js, /rowVersion: pending\.rowVersion/);
    assert.match(js, /RequestVerificationToken: token/);
    assert.doesNotMatch(js, /innerHTML/);
});
