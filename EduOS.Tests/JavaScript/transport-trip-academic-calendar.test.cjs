const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const app = path.resolve(__dirname, '../../EduOS.App');
const js = fs.readFileSync(path.join(app, 'wwwroot/js/transport-operations/trips.js'), 'utf8');
const view = fs.readFileSync(path.join(app, 'Views/TransportOperations/Trips.cshtml'), 'utf8');
const api = fs.readFileSync(path.join(app, 'Controllers/Api/TransportTripsController.cs'), 'utf8');

test('calendar-aware scheduling is explicitly opted in and remains server-authoritative', () => {
    assert.match(view, /id="tripScheduleAcademicCalendar"/);
    assert.match(view, /id="tripCalendarStatus"/);
    assert.match(js, /useAcademicCalendar: el\('tripScheduleAcademicCalendar'\)\.checked/);
    assert.match(js, /api\/transport\/trips\/schedule\/calendar/);
    assert.match(js, /result\.skippedDates/);
    assert.match(api, /HttpGet\("schedule\/calendar"\)/);
    assert.match(api, /GetScheduleCalendarAsync/);
});
