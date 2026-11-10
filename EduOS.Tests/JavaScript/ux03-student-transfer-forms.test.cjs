'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = fs.readFileSync(path.resolve(__dirname,
    '../../EduOS.App/wwwroot/js/students/transfer-requests.js'), 'utf8');
const reference = '00000000-0000-4000-8000-000000000001';
const transferRef = '00000000-0000-4000-8000-000000000002';

function harness({ history = [], historyFailure = false, holdEligibility = false, destinationItems = [] } = {}) {
    const fields = new Map();
    class Element {
        constructor(tag = 'div') {
            this.tagName = tag.toUpperCase(); this.value = ''; this.textContent = '';
            this.children = []; this.handlers = {}; this.disabled = false; this.inert = false;
            this.className = ''; this.hidden = false; this.dataset = {};
            this.classList = { add: () => { this.hidden = true; }, remove: () => { this.hidden = false; } };
        }
        addEventListener(type, handler) { this.handlers[type] = handler; }
        append(...children) { this.children.push(...children); }
        replaceChildren(...children) { this.children = children; }
        focus() { this.focused = true; }
        reset() {
            for (const id of ['transferReason', 'transferDestination', 'destinationStudentReference',
                'destinationEnrollmentReference']) get(id).value = '';
        }
        querySelector() { return { value: 'csrf' }; }
    }
    const get = id => {
        if (!fields.has(id)) fields.set(id, new Element());
        return fields.get(id);
    };
    const body = { dataset: { authTenantId: '1', authSessionReference: 'session-one' } };
    const document = {
        body, getElementById: get, createElement: tag => new Element(tag),
        createTextNode: value => Object.assign(new Element('span'), { textContent: value })
    };
    const eligibility = {
        studentReference: reference, studentName: 'Student One', studentCode: 'S001',
        rollNo: 'R01', campusId: 10, enrollmentReference: transferRef,
        studentRowVersion: 'student-version', enrollmentRowVersion: 'enrollment-version'
    };
    const writes = [], pendingEligibility = [];
    const ok = data => ({ ok: true, async json() { return { success: true, data }; } });
    const fail = (message, status = 503) => ({ ok: false, status,
        async json() { return { success: false, message }; } });
    let failHistory = historyFailure, seq = 0;
    const fetch = async (url, options = {}) => {
        url = String(url);
        if (options.method === 'POST') return new Promise(resolve => writes.push({
            url, body: JSON.parse(options.body), resolve
        }));
        if (url === '/api/student-transfer-requests/destination/inbox') return ok(destinationItems);
        if (url.endsWith('/requests')) return failHistory ? fail('History unavailable') : ok(history);
        if (url.endsWith('/eligibility')) {
            if (holdEligibility) return new Promise(resolve => pendingEligibility.push(resolve));
            return ok(eligibility);
        }
        if (url.startsWith('/api/students?')) return ok({ items: [{
            reference, studentCode: 'S001', fullName: 'Student One', status: 'Active'
        }] });
        throw Error('Unexpected request: ' + url);
    };
    const window = { EduOSFormGuard: { create: () => ({
        isDirty: () => false, confirmDiscard: () => true, markClean() {}
    }) }, EduOSSessionTransitionPending: () => false };
    vm.runInNewContext(source, { window, document, fetch, crypto: { randomUUID: () =>
        '00000000-0000-4000-8000-' + String(++seq).padStart(12, '0') },
    URLSearchParams, location: { search: '?studentReference=' + reference },
    confirm: () => true, Date, Number, String });
    const flush = async () => { for (let i = 0; i < 35; i++) await Promise.resolve(); };
    const submit = id => get(id).handlers.submit({ preventDefault() {} });
    return { get, body, writes, pendingEligibility, eligibility, ok, fail, flush, submit,
        set failHistory(value) { failHistory = value; } };
}

test('confirmed source write stays successful when its follow-up history is unavailable', async () => {
    const h = harness(); await h.flush();
    h.get('transferReason').value = 'Family relocation';
    h.get('transferReason').handlers.input();
    assert.equal(h.get('transferSubmit').disabled, false);
    h.submit('transferSubmitForm'); h.submit('transferSubmitForm'); await h.flush();
    assert.equal(h.writes.length, 1, 'pending form must not double-submit');
    assert.equal(h.get('transferSubmitForm').inert, true);
    h.failHistory = true;
    h.writes[0].resolve(h.ok({ requestReference: transferRef }));
    await h.flush();
    assert.match(h.get('transferNotice').textContent, /request saved, but status refresh failed/i);
    assert.equal(h.get('transferReason').value, '');
    assert.equal(h.get('transferSubmit').disabled, true,
        'cannot resubmit using stale eligibility after confirmed save and failed readback');
    assert.equal(h.get('transferSubmitForm').inert, false);
});

test('uncertain transfer retry reuses request key while edited details rotate it', async () => {
    const h = harness(); await h.flush();
    h.get('transferReason').value = 'Family relocation';
    h.submit('transferSubmitForm'); await h.flush();
    h.writes[0].resolve(h.fail('Network uncertainty')); await h.flush();
    assert.match(h.get('transferNotice').textContent, /retry unchanged details/i);
    h.submit('transferSubmitForm'); await h.flush();
    assert.equal(h.writes[0].body.clientRequestId, h.writes[1].body.clientRequestId);
    h.writes[1].resolve(h.fail('Network uncertainty')); await h.flush();
    h.get('transferReason').value = 'Updated justification';
    h.submit('transferSubmitForm'); await h.flush();
    assert.notEqual(h.writes[1].body.clientRequestId, h.writes[2].body.clientRequestId);
    h.writes[2].resolve(h.fail('Network uncertainty')); await h.flush();
});

test('confirmed source review is not mislabeled a failed review on readback failure', async () => {
    const h = harness({ history: [{ reference: transferRef, state: 2, rowVersion: 'version',
        requestedAt: '2026-11-10', destinationTenantId: 3, reason: 'Relocation' }] });
    await h.flush();
    const review = h.get('transferHistory').children[0].children[4].children[0];
    review.handlers.click(); await h.flush();
    assert.equal(h.writes.length, 1);
    h.failHistory = true;
    h.writes[0].resolve(h.ok({})); await h.flush();
    assert.match(h.get('transferNotice').textContent, /Review recorded, but status refresh failed/i);
    assert.equal(h.get('transferRefresh').disabled, false);
});

test('cross-institution session switch rejects stale selection and subsequent commands', async () => {
    const h = harness({ holdEligibility: true }); await h.flush();
    assert.equal(h.pendingEligibility.length, 1);
    h.body.dataset.authTenantId = '2';
    h.pendingEligibility[0](h.ok(h.eligibility)); await h.flush();
    assert.equal(h.get('transferSelected').hidden, true);
    h.get('transferReason').value = 'Old institution note';
    h.submit('transferSubmitForm'); await h.flush();
    assert.equal(h.writes.length, 0);
    assert.equal(h.get('transferSubmit').disabled, true);
    assert.equal(h.get('transferRefresh').disabled, true);
});

test('destination enrollment is a single review-before-link action with a locked form', async () => {
    const h = harness({ destinationItems: [{ reference: transferRef, state: 4,
        rowVersion: 'incoming-version', destinationEnrollmentLinked: false, sourceTenantId: 12 }] });
    await h.flush();
    const link = h.get('destinationRows').children[0].children[4].children[0];
    link.handlers.click();
    assert.equal(h.get('destinationEnrollmentForm').hidden, false);
    h.get('destinationStudentReference').value = '00000000-0000-4000-8000-000000000003';
    h.get('destinationEnrollmentReference').value = '00000000-0000-4000-8000-000000000004';
    h.submit('destinationEnrollmentForm'); h.submit('destinationEnrollmentForm'); await h.flush();
    assert.equal(h.writes.length, 1);
    assert.equal(h.get('destinationEnrollmentForm').inert, true);
    assert.equal(h.writes[0].body.rowVersion, 'incoming-version');
    h.writes[0].resolve(h.ok({})); await h.flush();
    assert.match(h.get('transferNotice').textContent, /Destination enrollment linked/i);
    assert.equal(h.get('destinationEnrollmentForm').hidden, true);
    assert.equal(h.get('destinationEnrollmentForm').inert, false);
});

test('destination link failure retains user entries and requires inbox reconciliation', async () => {
    const h = harness({ destinationItems: [{ reference: transferRef, state: 4,
        rowVersion: 'incoming-version', destinationEnrollmentLinked: false }] });
    await h.flush();
    h.get('destinationRows').children[0].children[4].children[0].handlers.click();
    h.get('destinationStudentReference').value = '00000000-0000-4000-8000-000000000003';
    h.get('destinationEnrollmentReference').value = '00000000-0000-4000-8000-000000000004';
    h.submit('destinationEnrollmentForm'); await h.flush();
    h.writes[0].resolve(h.fail('Connection lost')); await h.flush();
    assert.match(h.get('transferNotice').textContent, /refresh the inbox before retrying/i);
    assert.equal(h.get('destinationStudentReference').value, '00000000-0000-4000-8000-000000000003');
    assert.equal(h.get('destinationEnrollmentForm').inert, false);
});

test('transfer UI retains original server-side rights and CSRF contract', () => {
    const view = fs.readFileSync(path.resolve(__dirname,
        '../../EduOS.App/Views/StudentTransferRequests/Index.cshtml'), 'utf8');
    const controller = fs.readFileSync(path.resolve(__dirname,
        '../../EduOS.App/Controllers/Api/StudentTransferRequestsController.cs'), 'utf8');
    assert.match(view, /form-guard\.js"[\s\S]*students\/transfer-requests\.js"/);
    assert.match(source, /RequestVerificationToken: token/);
    assert.match(controller, /Authorize\(Roles = "TenantAdmin"\)/);
    assert.match(controller, /AutoValidateAntiforgeryToken/);
    assert.match(controller, /RequireModule\("STUDENT"\)/);
});
