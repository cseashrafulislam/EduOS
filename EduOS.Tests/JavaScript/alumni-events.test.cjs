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

test('detached RSVP actions cannot submit for a different selected graduate or revoked consent', async () => {
    const start = script.indexOf('async function saveRsvp(');
    const end = script.indexOf('async function createEvent(', start);
    assert.ok(start > 0 && end > start);
    const source = script.slice(start, end);
    const state = {
        eventBusy: false, selected: { studentReference: 'graduate-B', directoryOptIn: true },
        rsvpRequests: new Map()
    };
    const requests = [];
    const saveRsvp = new Function('state', 'api', 'notice', 'eventLabel', 'loadEvents', 'crypto',
        source + '; return saveRsvp;')(
        state, async (url, options) => { requests.push({ url, options }); return {}; },
        () => {}, (en) => en, async () => {}, { randomUUID: () => 'request-1' });
    const card = { reference: 'event-1', myRowVersion: null };
    await saveRsvp(card, 1, 'graduate-A');
    assert.equal(requests.length, 0);
    assert.equal(state.rsvpRequests.size, 0);
    await saveRsvp(card, 1, 'graduate-B');
    assert.equal(requests.length, 1);
    assert.equal(JSON.parse(requests[0].options.body).studentReference, 'graduate-B');
    state.selected.directoryOptIn = false;
    await saveRsvp(card, 1, 'graduate-B');
    assert.equal(requests.length, 1);
});

test('graduate selection invalidates old RSVP cards, pagination and in-flight results', () => {
    assert.match(script, /state\.eventRequestSerial\+\+;[\s\S]*?state\.eventPage = 1;[\s\S]*?el\('alumniEventCards'\)\.replaceChildren\(\)/);
    assert.match(script, /\(\) => saveRsvp\(item, 1, selectedReference\)/);
    assert.match(script, /\(\) => saveRsvp\(item, 2, selectedReference\)/);
    assert.match(script, /state\.selected\.studentReference !== cardGraduateReference/);
});
