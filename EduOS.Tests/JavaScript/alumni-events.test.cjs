const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const script = fs.readFileSync(path.join(__dirname, '../../EduOS.App/wwwroot/js/alumni/index.js'), 'utf8');
const view = fs.readFileSync(path.join(__dirname, '../../EduOS.App/Views/Alumni/Index.cshtml'), 'utf8');

test('alumni events require CSRF, idempotent RSVP and maker-checker publish', () => {
    assert.match(script, /RequestVerificationToken: token/);
    assert.match(script, /eventCreateRequest = crypto.randomUUID\(\)/);
    assert.match(script, /rsvpRequests\.set\(key, crypto.randomUUID\(\)\)/);
    assert.match(script, /const key = item\.reference \+ ':' \+ studentReference \+ ':' \+ response/);
    assert.match(script, /rowVersion: item\.rowVersion/);
    assert.match(script, /rowVersion: item\.myRowVersion/);
    assert.match(view, /A different administrator must publish/);
});
test('event cards are responsive, bilingual and avoid unsafe HTML injection', () => {
    assert.match(view, /id="alumniEventCards"/);
    assert.match(view, /col-md-6/);
    assert.match(view, /aria-live="polite"/);
    assert.match(script, /প্রাক্তন শিক্ষার্থীদের অনুষ্ঠান/);
    assert.match(script, /title\.textContent = item\.title/);
    assert.doesNotMatch(script, /innerHTML/);
    assert.match(view, /id="alumniEventsExport"/);
});

test('attendance roster is paged, bilingual, text-safe and uses versioned CSRF writes', () => {
    assert.match(script, /pageSize: 20/);
    assert.match(script, /studentReference: attendee\.studentReference, present/);
    assert.match(script, /rowVersion: attendee\.rowVersion/);
    assert.match(script, /Attendance updated/);
    assert.match(script, /attendee\.fullName \+ ' · ' \+ attendee\.studentCode/);
    assert.match(script, /table-responsive/);
    assert.match(view, /attendanceNote/);
    assert.doesNotMatch(script, /innerHTML/);
});

test('event list follows selected graduate and ignores stale responses on identity switch', () => {
    assert.match(script, /query\.set\('studentReference', selectedReference\)/);
    assert.match(script, /serial !== state\.eventRequestSerial/);
    assert.match(script, /state\.rsvpRequests\.clear\(\)/);
    assert.match(script, /studentReference, response,/);
});
