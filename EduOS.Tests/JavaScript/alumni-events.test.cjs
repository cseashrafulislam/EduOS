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


test('event page changes clear stale actions/CSV and ignore late responses', async () => {
    const start = script.indexOf('async function loadEvents()');
    const end = script.indexOf('const attendanceSerial = new WeakMap()', start);
    assert.ok(start > 0 && end > start);
    const element = () => ({ children: [], textContent: '',
        replaceChildren() { this.children = []; },
        append(...nodes) { this.children.push(...nodes); } });
    const elements = {
        alumniEventCards: element(), alumniEventPage: { textContent: '' },
        alumniEventPrev: { disabled: false }, alumniEventNext: { disabled: false },
        alumniEventsExport: { href: '/old', removeAttribute(name) {
            if (name === 'href') this.href = null;
        } }
    };
    const state = { eventRequestSerial: 0, eventPage: 1, eventTotal: 40, selected: null };
    const pending = [];
    const loadEvents = new Function('state', 'el', 'api', 'eventAdmin', 'document',
        'eventLabel', 'URLSearchParams', 'eventStatus', 'localDate', 'eventButton',
        'saveRsvp', 'changeEventStatus', 'loadAttendance', 'notice',
        script.slice(start, end) + '; return loadEvents;')(
        state, id => elements[id],
        url => new Promise((resolve, reject) => pending.push({ url, resolve, reject })),
        true, { createElement: element }, english => english, URLSearchParams,
        () => '', () => '', () => {}, () => {}, () => {}, () => {}, () => {});
    const first = loadEvents();
    assert.equal(elements.alumniEventsExport.href, null);
    assert.equal(elements.alumniEventPrev.disabled, true);
    assert.match(elements.alumniEventCards.children[0].textContent, /Loading/);
    state.eventPage = 2;
    const second = loadEvents();
    pending[1].resolve({ totalCount: 0, items: [] });
    await second;
    pending[0].resolve({ totalCount: 1, items: [{ title: 'old private event' }] });
    await first;
    assert.match(elements.alumniEventCards.children[0].textContent, /No events/);
    assert.match(elements.alumniEventsExport.href, /page=2/);
    state.eventPage = 3;
    const failed = loadEvents();
    pending[2].reject(new Error('network'));
    await assert.rejects(failed, /network/);
    assert.equal(elements.alumniEventsExport.href, null);
    assert.match(elements.alumniEventCards.children[0].textContent, /retry/);
});

test('attendance paging and detached event cards discard late private rosters', async () => {
    const start = script.indexOf('const attendanceSerial = new WeakMap()');
    const end = script.indexOf('async function markAttendance(', start);
    assert.ok(start > 0 && end > start);
    const element = () => ({ children: [], textContent: '', isConnected: true,
        replaceChildren() { this.children = []; },
        append(...nodes) { this.children.push(...nodes); } });
    const pending = [];
    const loadAttendance = new Function('api', 'document', 'eventLabel',
        'URLSearchParams', 'encodeURIComponent',
        script.slice(start, end) + '; return loadAttendance;')(
        url => new Promise((resolve, reject) => pending.push({ url, resolve, reject })),
        { createElement: element }, english => english, URLSearchParams, encodeURIComponent);
    const host = element(), event = { reference: 'event-1', status: 2 };
    const first = loadAttendance(event, host, 1);
    const second = loadAttendance(event, host, 2);
    pending[1].reject(new Error('network'));
    await assert.rejects(second, /network/);
    pending[0].resolve({ totalCount: 1, items: [{ fullName: 'old private attendee' }] });
    await first;
    assert.match(host.children[0].textContent, /retry/);
    host.isConnected = false;
    const detached = loadAttendance(event, host, 3);
    pending[2].resolve({ totalCount: 1, items: [{ fullName: 'private attendee' }] });
    await detached;
    assert.match(host.children[0].textContent, /Loading/);
});

test('ambiguous attendance timeout reuses command ID until confirmed success', async () => {
    const start = script.indexOf('async function markAttendance(');
    const end = script.indexOf('async function changeEventStatus(', start);
    assert.ok(start > 0 && end > start);
    const requests = [];
    let nextId = 0, networkFails = true, refreshed = 0;
    const state = { eventBusy: false, attendanceRequests: new Map() };
    const markAttendance = new Function('state', 'api', 'crypto', 'notice',
        'eventLabel', 'loadEvents', 'encodeURIComponent',
        script.slice(start, end) + '; return markAttendance;')(
        state,
        async (_url, options) => {
            requests.push(JSON.parse(options.body));
            if (networkFails) throw new Error('connection dropped after write');
            return {};
        },
        { randomUUID: () => 'request-' + (++nextId) },
        () => {}, english => english, async () => { refreshed++; }, encodeURIComponent);
    const event = { reference: 'event-1' };
    const attendee = { studentReference: 'graduate-1', rowVersion: 'version-1' };
    await markAttendance(event, attendee, true);
    assert.equal(state.attendanceRequests.size, 1);
    assert.equal(state.eventBusy, false);
    networkFails = false;
    await markAttendance(event, attendee, true);
    assert.equal(requests.length, 2);
    assert.equal(requests[0].clientRequestId, requests[1].clientRequestId);
    assert.equal(requests[0].clientRequestId, 'request-1');
    assert.equal(state.attendanceRequests.size, 0);
    assert.equal(refreshed, 1);
    await markAttendance(event, { ...attendee, rowVersion: 'version-2' }, false);
    assert.equal(requests[2].clientRequestId, 'request-2');
    assert.equal(state.attendanceRequests.size, 0);
    assert.equal(refreshed, 2);
});
