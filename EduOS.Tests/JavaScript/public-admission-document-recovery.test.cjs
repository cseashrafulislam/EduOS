'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const path = require('node:path');
const fs = require('node:fs');
const { createHash } = require('node:crypto');
const root = path.resolve(__dirname, '../../');
const { recover } = require(path.join(root, 'EduOS.App/wwwroot/js/public-admission/document-recovery.js'));
const { createPersistentTracker } = require(path.join(root, 'EduOS.App/wwwroot/js/public-admission/retry-keys.js'));
const js = fs.readFileSync(path.join(root, 'EduOS.App/wwwroot/js/public-admission/index.js'), 'utf8');
const view = fs.readFileSync(path.join(root, 'EduOS.App/Views/PublicAdmission/Index.cshtml'), 'utf8');

test('verified status recovers only latest received document types, never rejected latest versions', () => {
    const requirements = [{ documentType: 'birth-certificate', isRequired: true },
        { documentType: 'photo', isRequired: true }];
    const documents = [
        { documentTypeCode: 'BIRTH-CERTIFICATE', versionNo: 1, verificationStatus: 2 },
        { documentTypeCode: 'BIRTH-CERTIFICATE', versionNo: 2, verificationStatus: 3 },
        { documentTypeCode: 'PHOTO', versionNo: 1, verificationStatus: 1 }
    ];
    assert.deepEqual(recover(requirements, documents).completedTypes, ['PHOTO']);
    assert.deepEqual(recover(requirements, [{ ...documents[0] }]).completedTypes, ['BIRTH-CERTIFICATE']);
    assert.deepEqual(recover(requirements, []).completedTypes, []);
    assert.match(js, /data\.canUploadDocuments/);
    assert.match(js, /data\.documentRequirements/);
    assert.match(js, /EduOSAdmissionDocuments\.recover/);
    assert.ok(view.indexOf('/document-recovery.js') < view.indexOf('/index.js'));
});

test('same file bytes retain idempotency key after reload; changed bytes and tenants do not', async () => {
    const store = new Map();
    const storage = {
        getItem: key => store.get(key) || null,
        setItem: (key, value) => store.set(key, value),
        removeItem: key => store.delete(key)
    };
    const digest = file => Promise.resolve(createHash('sha256').update(file.bytes).digest('hex'));
    const payloadDigest = raw => Promise.resolve(createHash('sha256').update(raw).digest('hex'));
    let count = 0;
    const make = tenant => createPersistentTracker(() => 'request-' + ++count,
        storage, payloadDigest, tenant, () => 1000);
    const file = { bytes: Buffer.from('private-document'), name: 'birth.pdf' };
    const key = 'document:application-1:BIRTH-CERTIFICATE';
    const id = await make('school-a').forFileDigest(key, file, digest);
    assert.equal(await make('school-a').forFileDigest(key,
        { bytes: Buffer.from('private-document'), name: 'birth.pdf' }, digest), id);
    assert.notEqual(await make('school-a').forFileDigest(key,
        { bytes: Buffer.from('changed-document'), name: 'birth.pdf' }, digest), id);
    assert.notEqual(await make('school-b').forFileDigest(key, file, digest), id);
    assert.ok([...store.values()].every(value => !value.includes('private-document')));
    const tracker = make('school-c');
    const initial = await tracker.forFileDigest(key, file, digest);
    tracker.clear(key);
    assert.notEqual(await tracker.forFileDigest(key, file, digest), initial);
});
