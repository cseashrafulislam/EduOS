'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.resolve(__dirname, '../..');
const read = p => fs.readFileSync(path.join(root, p), 'utf8');

test('promotion and readmission protect drafts, immutable retries and submission locks', () => {
    for (const [view, script, form] of [
        ['StudentProgressions', 'progressions', 'promotionForm'],
        ['StudentReadmissions', 'readmissions', 'readmissionForm']
    ]) {
        const markup = read(`EduOS.App/Views/${view}/Index.cshtml`);
        const js = read(`EduOS.App/wwwroot/js/students/${script}.js`);
        assert.match(markup, new RegExp('form-guard\\.js"[\\s\\S]*students/' + script + '\\.js"'));
        assert.ok(js.includes(`'${form}'`));
        assert.match(js, /confirmDiscard\(\)/);
        assert.match(js, /requestFingerprint\s*!==\s*fingerprint/);
        assert.match(js, /request\.clientRequestId = state\.requestId|request\.clientRequestId=state\.requestId/);
        assert.match(js, /\.inert = true|\.inert=true/);
    }
    const progression = read('EduOS.App/wwwroot/js/students/progressions.js');
    assert.match(progression, /request !== state\.selectionPending/);
    assert.match(progression, /reference !== state\.reference/);
    const readmission = read('EduOS.App/wwwroot/js/students/readmissions.js');
    assert.match(readmission, /request!==state\.optionsRequest/);
    assert.match(readmission, /request!==state\.searchRequest/);
});

test('readmission keeps retry key for uncertain identical POST and rotates only for changed request', async () => {
    const elements = new Map();
    const create = tag => ({ tagName: String(tag).toUpperCase(), value: '', disabled: false, inert: false,
        textContent: '', children: [], handlers: {}, classList: { add() {}, remove() {} },
        addEventListener(name, callback) { this.handlers[name] = callback; },
        append(...items) { this.children.push(...items); },
        replaceChildren(...items) { this.children = items; },
        reset() { for (const id of ['readmissionRoll', 'readmissionReason']) get(id).value = ''; },
        focus() {}, querySelector() { return { value: 'token' }; }
    });
    const get = id => {
        if (!elements.has(id)) elements.set(id, create('div'));
        return elements.get(id);
    };
    get('readmissionWorkspace').querySelector = () => ({ value: 'token' });
    const document = { getElementById: get, createElement: create };
    const studentReference = '00000000-0000-4000-8000-000000000001';
    const submitted = [];
    const fetch = async (url, options = {}) => {
        const address = String(url);
        let data;
        let ok = true;
        if (address.startsWith('/api/students?')) data = { items: [{
            reference: studentReference, studentCode: 'S001', fullName: 'Student', status: 'Exited'
        }] };
        else if (address.endsWith('/readmission/options')) data = {
            studentReference, studentCode: 'S001', previousStatus: 'Exited', previousEnrollmentId: 1,
            studentRowVersion: 'v1', previousEnrollmentRowVersion: 'v2',
            targets: [{ academicBatchId: 3, name: 'Batch 2027' }]
        };
        else if (address.endsWith('/readmission') && options.method === 'POST') {
            submitted.push(JSON.parse(options.body));
            ok = submitted.length === 3;
            data = { alreadyProcessed: false };
        }
        else throw Error('Unexpected request ' + address);
        return { ok, async json() { return ok ? { success: true, data } : { success: false, message: 'Network uncertain' }; } };
    };
    let counter = 0;
    const window = { EduOSFormGuard: { create: () => ({
        isDirty: () => false, confirmDiscard: () => true, markClean() {}
    }) } };
    vm.runInNewContext(read('EduOS.App/wwwroot/js/students/readmissions.js'), {
        document, window, fetch, URLSearchParams, location: { search: '' },
        confirm: () => true, crypto: { randomUUID: () => 'id-' + ++counter }
    });
    const tick = () => new Promise(resolve => setImmediate(resolve));
    get('readmissionQuery').value = 'S001';
    get('readmissionSearch').handlers.submit({ preventDefault() {} });
    await tick(); await tick();
    assert.equal(get('readmissionMatches').children.length, 1);
    get('readmissionMatches').children[0].handlers.click();
    await tick(); await tick();
    get('readmissionBatch').value = '3';
    get('readmissionRoll').value = 'R01';
    get('readmissionReason').value = 'Returned';
    get('readmissionForm').handlers.submit({ preventDefault() {} });
    await tick(); await tick();
    assert.equal(get('readmissionForm').inert, false);
    assert.equal(get('readmissionRoll').value, 'R01', 'failed write retains entered roll');
    get('readmissionForm').handlers.submit({ preventDefault() {} });
    await tick(); await tick();
    assert.equal(submitted[0].clientRequestId, submitted[1].clientRequestId,
        'identical retry must use the same server idempotency key');
    get('readmissionReason').value = 'Returned with new evidence';
    get('readmissionForm').handlers.submit({ preventDefault() {} });
    await tick(); await tick();
    assert.notEqual(submitted[1].clientRequestId, submitted[2].clientRequestId,
        'changed command must get a different retry key');
    assert.equal(get('readmissionForm').inert, false);
});
