const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const app = path.resolve(__dirname, '../../EduOS.App');
const service = path.resolve(__dirname, '../../EduOS.Service');
const read = (root, p) => fs.readFileSync(path.join(root, p), 'utf8');

test('operator and driver close only server-verified awaiting riders', () => {
  const operator = read(app, 'wwwroot/js/transport-operations/trips.js');
  const driver = read(app, 'wwwroot/js/transport-operations/driver-trips.js');
  const api = read(app, 'Controllers/Api/TransportTripsController.cs');
  const driverApi = read(app, 'Controllers/Api/TransportDriverTripsController.cs');
  const core = read(service, 'Services/Transport/TransportTripService.NoShowBatch.cs');
  for (const js of [operator, driver]) {
    assert.match(js, /no-show\/preview/);
    assert.match(js, /expectedAwaitingCount/);
    assert.match(js, /clientRequestId/);
    assert.match(js, /guard\.valid\(\)/);
    assert.doesNotMatch(js, /innerHTML|latitude|longitude|gps/i);
  }
  assert.match(api, /MarkAwaitingNoShowAsync/);
  assert.match(driverApi, /MarkDriverAwaitingNoShowAsync/);
  assert.match(api, /AutoValidateAntiforgeryToken/);
  assert.match(driverApi, /Authorize\(Roles = "Staff"\)/);
  assert.match(core, /IsolationLevel\.Serializable/);
  assert.match(core, /ExpectedAwaitingCount/);
  assert.match(core, /CanDriveTripAsync/);
  assert.match(core, /HasCampusAsync/);
  assert.match(core, /NoShowEventKey/);
});
