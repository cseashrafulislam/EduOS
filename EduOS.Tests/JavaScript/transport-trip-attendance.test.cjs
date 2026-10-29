const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const app = path.resolve(__dirname, '../../EduOS.App');
const read = p => fs.readFileSync(path.join(app,p),'utf8');

test('operator attendance is a real server write with anti-forgery and frozen rider key', () => {
 const js = read('wwwroot/js/transport-operations/trips.js');
 const view = read('Views/TransportOperations/Trips.cshtml');
 const api = read('Controllers/Api/TransportTripsController.cs');
 assert.match(js,/\/attendance/);
 assert.match(js,/clientRequestId:crypto\.randomUUID\(\),studentCode:row\.studentCode,eventType/);
 assert.match(js,/RequestVerificationToken/);
 assert.match(js,/row\.attendanceState === 'Awaiting'/);
 assert.match(view,/Attendance \/ উপস্থিতি/);
 assert.match(api,/AutoValidateAntiforgeryToken/);
 assert.match(api,/RecordAttendanceAsync/);
});

test('guardian readback uses authenticated tenant-scoped service, not simulated telemetry', () => {
 const js = read('wwwroot/js/transport-operations/family-schedule.js');
 const view = read('Views/TransportOperations/Family.cshtml');
 const api = read('Controllers/Api/TransportFamilyAttendanceController.cs');
 assert.match(js,/api\/transport\/family-attendance/);
 assert.match(js,/attendanceSerial/);
 assert.match(view,/familyAttendanceRows/);
 assert.match(api,/Authorize\(Roles = "Student,Guardian,Parent"\)/);
 assert.match(api,/RequireModule\("TRANSPORT"\)/);
 assert.doesNotMatch(js,/latitude|longitude|gps/i);
});
