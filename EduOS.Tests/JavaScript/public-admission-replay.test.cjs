'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../');
const { createTracker } = require(path.join(root, 'EduOS.App/wwwroot/js/public-admission/retry-keys.js'));
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/public-admission/index.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/PublicAdmission/Index.cshtml'), 'utf8');
const service = fs.readFileSync(path.join(root, 'EduOS.Service/Services/Admission/PublicAdmissionService.cs'), 'utf8');

test('public application retries reuse one ID until payload changes or a new application begins', () => {
    let n = 0;
    const tracker = createTracker(() => 'id-' + ++n);
    const payload = { applicantName: 'Student', campusId: 2, customResponses: { consent: 'yes' } };
    assert.equal(tracker.forPayload('application', payload), 'id-1');
    assert.equal(tracker.forPayload('application', { ...payload }), 'id-1');
    assert.equal(tracker.forPayload('application', { ...payload, campusId: 3 }), 'id-2');
    tracker.clearAll();
    assert.equal(tracker.forPayload('application', payload), 'id-3');
    assert.match(js, /retry\.forPayload\('application', request\)/);
    assert.match(js, /applicationSubmitted = true/);
    assert.match(js, /if \(applicationSubmitted \|\| !openForms\.length\) return/);
    assert.match(js, /newApplicationButton\.addEventListener\('click'/);
    assert.ok(view.includes('id="newApplicationButton"'));
    assert.ok(view.indexOf('/retry-keys.js') < view.indexOf('/index.js'));
});

test('all public document types preserve file identity across retries and clear successful files', () => {
    let n = 0;
    const tracker = createTracker(() => 'id-' + ++n);
    const file = { name: 'acceptance.pdf', size: 100 };
    const sameMetadataDifferentFile = { name: 'acceptance.pdf', size: 100 };
    assert.equal(tracker.forFile('offer:app:phone', file), 'id-1');
    assert.equal(tracker.forFile('offer:app:phone', file), 'id-1');
    assert.equal(tracker.forFile('offer:app:phone', sameMetadataDifferentFile), 'id-2');
    assert.equal(tracker.forFile('document:app:ID', file), 'id-3');
    assert.equal(tracker.forFile('replacement:app:ID', file), 'id-4');
    tracker.clear('document:app:ID');
    assert.equal(tracker.forFile('document:app:ID', file), 'id-5');
    for (const key of ['document:', 'replacement:', 'offer:']) assert.ok(js.includes(key));
    assert.equal((js.match(/retry\.forFile\(/g) || []).length, 3);
    assert.match(js, /input\.value = ''; \/\/ Completed files/);
    assert.match(js, /retry\.clear\(key\)/);
    assert.match(service, /IsolationLevel\.Serializable/);
    assert.match(service, /x\.ClientRequestId == request\.ClientRequestId/);
});

test('closed public intakes cannot invite an impossible new application', () => {
    assert.match(js, /submitButton\.disabled = !openForms\.length \|\| applicationSubmitted/);
    assert.match(js, /No admission intake is open/);
});
