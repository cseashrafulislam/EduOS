'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '../../');
const { createTracker, createPersistentTracker } = require(path.join(root, 'EduOS.App/wwwroot/js/public-admission/retry-keys.js'));
const { createHash } = require('node:crypto');
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

test('public form uses the actual API AcademicLevelId and AcademicLevels contracts', () => {
    const dto = fs.readFileSync(path.join(root, 'EduOS.Core/DTOs/Admission/AdmissionApplicationDtos.cs'), 'utf8');
    const intake = fs.readFileSync(path.join(root, 'EduOS.Core/DTOs/Admission/AdmissionIntakeDtos.cs'), 'utf8');
    assert.match(dto, /public long AcademicLevelId/);
    assert.match(dto, /List<AdmissionReferenceOptionDto> AcademicLevels/);
    assert.match(intake, /class PublicAdmissionIntakeFormDto[\\s\\S]*?public long AcademicLevelId/);
    assert.match(js, /data\\.academicLevels \\|\\| \\[\\]/);
    assert.match(js, /selected\\.academicLevelId/);
    assert.match(js, /academicLevelId: Number\\(value\\('academicUnitId'\\)\\)/);
    assert.doesNotMatch(js, /data\\.academicUnits|selected\\.academicUnitId|academicUnitId: Number/);
});

test('page reload reuses the same opaque request ID without persisting applicant details', async () => {
    const store = new Map();
    const storage = {
        getItem: key => store.get(key) || null,
        setItem: (key, value) => store.set(key, value),
        removeItem: key => store.delete(key)
    };
    const digest = raw => Promise.resolve(createHash('sha256').update(raw).digest('hex'));
    let n = 0;
    const factory = () => 'request-' + ++n;
    const first = createPersistentTracker(factory, storage, digest, 'tenant-a', () => 1000);
    const payload = { applicantName: 'Private Student', campusId: 2 };
    const id = await first.forPayload('application', payload);
    assert.equal(id, 'request-1');
    assert.ok([...store.values()].every(v => !v.includes('Private Student')));
    const reloaded = createPersistentTracker(factory, storage, digest, 'tenant-a', () => 2000);
    assert.equal(await reloaded.forPayload('application', { ...payload }), id);
    assert.notEqual(await reloaded.forPayload('application', { ...payload, campusId: 3 }), id);
    reloaded.clearAll();
    assert.notEqual(await reloaded.forPayload('application', payload), id);
    const otherTenant = createPersistentTracker(factory, storage, digest, 'tenant-b', () => 2000);
    assert.notEqual(await otherTenant.forPayload('application', payload), id);
    assert.match(js, /submissionInFlight/);
    assert.match(js, /await retry\\.forPayload\\('application', request\\)/);
});

test('closed intake replay is resolved inside the serializable transaction', () => {
    const transaction = service.indexOf('using var tx = new TransactionScope', service.indexOf('CreateAsync('));
    const replay = service.indexOf('var replay = await _applicants.GetQueryable()', transaction);
    const openForm = service.indexOf('var form = replay == null', replay);
    assert.ok(transaction > 0 && replay > transaction && openForm > replay);
    assert.match(service.slice(openForm, openForm + 450), /FindOpenFormAsync/);
    assert.match(service.slice(openForm, openForm + 450), /_forms.GetQueryable/);
});
