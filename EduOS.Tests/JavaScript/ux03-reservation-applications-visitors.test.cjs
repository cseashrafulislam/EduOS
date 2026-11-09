'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = name => fs.readFileSync(path.join(root, name), 'utf8');

test('Reservation forms use current-scope options, one active post and stable retry fingerprint', () => {
    const ui = read('EduOS.App/wwwroot/js/library/reservations.js');
    const page = read('EduOS.App/Views/Library/Reservations.cshtml');
    assert.match(page, /form-guard\.js"[\s\S]*library\/reservations\.js"/);
    assert.match(ui, /if \(createSaving \|\| !catalogReady \|\| !sameSession\(\)/);
    assert.match(ui, /if \(fingerprint !== createFingerprint\)/);
    assert.match(ui, /createForm\.inert = true/);
    assert.match(ui, /createGuard\?\.markClean\(\)/);
    assert.match(ui, /if \(sequence !== state\.studentRequest/);
    assert.match(ui, /if \(request !== state\.exportRequest \|\| !sameSession\(\)/);
    assert.doesNotMatch(ui, /addEventListener\('change', \(\) => \{ state\.key = crypto\.randomUUID\(\)/);
});

test('Visitor forms require fresh campus hosts, isolate retries and stop repeated actions', () => {
    const ui = read('EduOS.App/wwwroot/js/facilities/visitors.js');
    const page = read('EduOS.App/Views/FacilityVisitors/Index.cshtml');
    assert.match(page, /form-guard\.js"[\s\S]*facilities\/visitors\.js"/);
    assert.match(ui, /if \(creating \|\| !hostReady \|\| !createForm\.reportValidity\(\)\) return/);
    assert.match(ui, /if \(epoch !== hostEpoch \|\| Number\(\$\('fvCampus'\)\.value\) !== campus\) return/);
    assert.match(ui, /creating = true; createForm\.inert = true/);
    assert.match(ui, /if \(fingerprint !== lastCreateFingerprint\)/);
    assert.match(ui, /if \(requestingPolicy \|\| !policyForm\.reportValidity\(\)\) return/);
    assert.match(ui, /requestingPolicy = true; policyForm\.inert = true/);
    assert.match(ui, /if \(inFlightActions\.has\(key\)\) return/);
    assert.match(ui, /inFlightActions\.delete\(key\)/);
    assert.match(ui, /policyReviewIds\.has\(reviewKey\)/);
    assert.match(ui, /policyReviewIds\.delete\(reviewKey\)/);
});

function fakeElement(tagName = 'DIV') {
    const listeners = {};
    const classes = new Set();
    return { tagName: tagName.toUpperCase(), listeners, value: '', disabled: false, children: [], textContent: '',
        classList: { add: name => classes.add(name), remove: name => classes.delete(name),
            toggle(name, force) { if (force) classes.add(name); else classes.delete(name); },
            contains: name => classes.has(name) },
        addEventListener(name, fn) { listeners[name] = fn; },
        append(...nodes) { this.children.push(...nodes); },
        replaceChildren(...nodes) { this.children = [...nodes]; },
        get options() { return this.children; },
        scrollIntoView() {}, focus() {}, reportValidity() { return true; },
        closest() { return this; }
    };
}

test('Hostel failed offer stays editable, duplicate submission is blocked and retry succeeds', async () => {
    const ui = read('EduOS.App/wwwroot/js/hostel-operations/applications.js');
    const page = read('EduOS.App/Views/HostelOperations/Index.cshtml');
    assert.match(page, /form-guard\.js"[\s\S]*hostel-operations\/applications\.js"/);
    const elements = new Map();
    const get = id => { if (!elements.has(id)) elements.set(id, fakeElement()); return elements.get(id); };
    get('hostelApplicationOfferHours').value = '48';
    let postCount = 0, failFirst;
    const row = { id: 72, hostelName: 'Blue Hall', startDate: '2026-11-20', endDate: null,
        studentName: 'Student', studentCode: 'S1', state: 'Submitted', submittedAtUtc: '', rowVersion: 'v1' };
    const fetch = async (url, options = {}) => {
        if (options.method === 'POST') {
            postCount++;
            if (postCount === 1) return new Promise(resolve => { failFirst = resolve; });
            return { ok: true, json: async () => ({ success: true, data: {} }) };
        }
        let data;
        if (url.includes('/options?')) data = { items: [{ enrollmentReference: 'e', hostelReference: 'h',
            campusName: 'Campus', hostelName: 'Blue Hall' }], totalPages: 1 };
        else if (url.includes('/available-beds?')) data = { items: [{ bedId: 81, hostelName: 'Blue Hall',
            roomNumber: '101', bedNumber: 'A' }], totalPages: 1 };
        else if (url.includes('/queue?')) data = { items: [row], totalPages: 1 };
        else data = { items: [], totalPages: 1 };
        return { ok: true, json: async () => ({ success: true, data }) };
    };
    const document = { documentElement: { lang: 'en-BD' },
        getElementById: get, querySelectorAll: () => [],
        querySelector: () => ({ content: 'antiforgery' }), createElement: fakeElement };
    const window = { EduOSFormGuard: { create: () => ({ markClean() {}, confirmDiscard: () => true }) } };
    const Option = function Option(text, value) { this.textContent = text; this.value = value; };
    vm.runInNewContext(ui, { document, window, fetch, Option, crypto: { randomUUID: () => 'request-id' },
        URLSearchParams, Date, Number, Promise, console });
    await new Promise(resolve => setImmediate(resolve));
    function findButton(element, label) {
        if (element.tagName === 'BUTTON' && element.textContent === label) return element;
        for (const child of element.children || []) {
            const found = findButton(child, label);
            if (found) return found;
        }
        return null;
    }
    const offer = findButton(get('hostelApplicationQueueRows'), 'Offer');
    assert.ok(offer, 'Expected offer action from the authoritative queue');
    await offer.listeners.click();
    const form = get('hostelApplicationDecision');
    assert.equal(form.classList.contains('d-none'), false);
    get('hostelApplicationOfferedBed').value = '81';
    const submit = () => form.listeners.submit({ preventDefault() {}, currentTarget: form });
    const first = submit();
    await submit();
    assert.equal(postCount, 1, 'Only one in-flight decision is sent');
    failFirst({ ok: false, json: async () => ({ success: false, message: 'Conflict: outdated offer' }) });
    await first;
    assert.equal(form.classList.contains('d-none'), false, 'An error must not silently close the decision form');
    assert.equal(get('hostelApplicationOfferedBed').value, '81', 'Preserve the selected bed for retry');
    await submit();
    assert.equal(postCount, 2);
    assert.equal(form.classList.contains('d-none'), true, 'Close only after the server accepts the decision');
});
