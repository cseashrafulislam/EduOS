'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');
const script = read('EduOS.App/wwwroot/js/students/transfer-requests.js');

test('source and destination student-transfer forms have draft and single-flight protections', () => {
    const razor = read('EduOS.App/Views/StudentTransferRequests/Index.cshtml');
    assert.match(razor, /form-guard\.js"[\s\S]*students\/transfer-requests\.js"/);
    assert.match(script, /const transferGuard = window\.EduOSFormGuard\?\.create\(transferForm\)/);
    assert.match(script, /const destinationGuard = window\.EduOSFormGuard\?\.create\(destinationForm\)/);
    assert.match(script, /state\.studentGeneration/);
    assert.match(script, /generation !== state\.studentGeneration/);
    assert.match(script, /generation !== state\.destinationGeneration/);
    assert.match(script, /state\.requestFingerprint !== fingerprint/);
    assert.match(script, /transferForm\.inert = true/);
    assert.match(script, /destinationForm\.inert = true/);
    assert.match(script, /transferGuard\?\.isDirty\(\) && !transferGuard\.confirmDiscard\(\)/);
    assert.match(script, /destinationGuard\?\.isDirty\(\) &&/);
});

test('late student-transfer responses cannot replace the newer selected student', async () => {
    const elements = new Map();
    const create = (tag = 'div') => ({
        tagName: tag.toUpperCase(), value: '', textContent: '', disabled: false, inert: false,
        children: [], handlers: {}, dataset: {}, classList: { add() {}, remove() {} },
        addEventListener(name, callback) { this.handlers[name] = callback; },
        append(...items) { this.children.push(...items); },
        replaceChildren(...items) { this.children = items; },
        reset() { for (const field of ['transferReason', 'transferDestination']) get(field).value = ''; },
        focus() {}, querySelector() { return { value: 'csrf' }; }
    });
    const get = id => {
        if (!elements.has(id)) elements.set(id, create());
        return elements.get(id);
    };
    const document = { getElementById: get, createElement: create,
        createTextNode: text => ({ textContent: text }) };
    const rootElement = get('transferRequestsApp');
    rootElement.querySelector = () => ({ value: 'csrf' });
    const student = reference => ({ reference, studentCode: reference, fullName: reference, status: 'Active' });
    let finishOldHistory;
    const fetch = async input => {
        const url = String(input);
        let data;
        if (url.startsWith('/api/students?')) data = { items: [student('old'), student('new')] };
        else if (url.endsWith('/destination/inbox')) data = [];
        else if (url.endsWith('/students/old/requests'))
            data = await new Promise(resolve => { finishOldHistory = resolve; });
        else if (url.endsWith('/students/new/requests')) data = [];
        else if (url.endsWith('/students/new/eligibility')) data = {
            studentReference: 'new', studentName: 'New student', studentCode: 'NEW',
            rollNo: '2', campusId: 1, enrollmentReference: 'e', studentRowVersion: 'v', enrollmentRowVersion: 'v'
        };
        else if (url.endsWith('/students/old/eligibility')) throw Error('Stale eligibility should not start');
        else throw Error('Unexpected request ' + url);
        return { ok: true, async json() { return { success: true, data }; } };
    };
    const window = { EduOSFormGuard: { create: () => ({
        isDirty: () => false, confirmDiscard: () => true, markClean() {}
    }) } };
    vm.runInNewContext(script, {
        document, window, fetch, URLSearchParams, location: { search: '' },
        crypto: { randomUUID: () => 'request-id' }, confirm: () => true, setTimeout
    });
    const tick = () => new Promise(resolve => setImmediate(resolve));
    get('transferSearch').value = 'student';
    get('transferSearchForm').handlers.submit({ preventDefault() {} });
    await tick(); await tick();
    const [oldButton, newButton] = get('transferMatches').children;
    assert.ok(oldButton && newButton, 'search produces both selectable students');
    oldButton.handlers.click();
    await tick();
    newButton.handlers.click();
    await tick(); await tick();
    assert.match(get('transferStudentName').textContent, /New student/);
    finishOldHistory([{ state: 2, reason: 'Old request', requestedAt: '2026-01-01' }]);
    await tick();
    assert.match(get('transferStudentName').textContent, /New student/);
    assert.equal(get('transferHistory').children.length, 1);
    assert.equal(get('transferSubmit').disabled, true, 'empty reason remains unsubmitted');
});
