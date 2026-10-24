const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('upcoming rider reschedule is an authorized complete operations slice', () => {
    const service = read('EduOS.Service/Services/Transport/TransportService.UpcomingStartReschedule.cs');
    const api = read('EduOS.App/Controllers/Api/TransportController.cs');
    const view = read('EduOS.App/Views/TransportOperations/Index.cshtml');
    const index = read('EduOS.App/wwwroot/js/transport-operations/index.js');
    const js = read('EduOS.App/wwwroot/js/transport-operations/upcoming-start-reschedule.js');
    assert.match(api, /HttpPost\("assignments\/\{reference:guid\}\/reschedule-start"\)/);
    assert.match(api, /RescheduleUpcomingRiderStartAsync/);
    assert.match(service, /IsolationLevel\.Serializable/);
    assert.match(service, /CanManageCampusAsync/);
    assert.match(service, /RowVersion\.AsSpan\(\)\.SequenceEqual/);
    assert.match(service, /PeakReservedSeats/);
    assert.match(service, /x\.StudentId == assignment\.StudentId/);
    assert.match(service, /x\.EffectiveFrom <= request\.StartDate/);
    assert.match(view, /transportUpcomingStartPanel/);
    assert.match(index, /transport:reschedule-upcoming/);
    assert.match(js, /rowVersion: pending\.rowVersion/);
    assert.match(js, /RequestVerificationToken: token/);
    assert.doesNotMatch(js, /innerHTML/);
});
