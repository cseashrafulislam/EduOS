'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { webcrypto } = require('node:crypto');
const root = path.resolve(__dirname, '../../');
const read = file => fs.readFileSync(path.join(root, file), 'utf8');
const workflow = require('../../EduOS.App/wwwroot/js/admissions/offer-workflow.js');
const retry = require('../../EduOS.App/wwwroot/js/public-admission/retry-keys.js');

const offeredAt = '2026-10-27T10:00:00Z';
const evidence = (id, version = 1) => ({
    id, versionNo: version, documentTypeCode: 'OFFER_ACCEPTANCE',
    isVerified: true, verifiedByUserId: 9,
    createdAt: '2026-10-27T10:01:00Z', verifiedAt: '2026-10-27T10:02:00Z'
});

test('offer acceptance excludes pre-offer, unverified and missing-audit evidence', () => {
    const docs = [
        { ...evidence(1), createdAt: '2026-10-27T09:00:00Z' },
        { ...evidence(2), verifiedAt: '2026-10-27T09:00:00Z' },
        { ...evidence(3), verifiedByUserId: null },
        { ...evidence(4), createdAt: null },
        { ...evidence(5), documentTypeCode: 'BIRTH_CERTIFICATE' },
        { ...evidence(6), isVerified: false },
        evidence(7, 1), evidence(8, 3)
    ];
    assert.deepEqual(workflow.eligibleEvidence(docs, offeredAt).map(x => x.id), [8, 7]);
    assert.deepEqual(workflow.eligibleEvidence([evidence(7)], null), []);
});

test('offer request IDs survive a reload, rotate with changed terms and clear on success', async () => {
    const map = new Map();
    const storage = {
        getItem: key => map.get(key) || null,
        setItem: (key, value) => map.set(key, value),
        removeItem: key => map.delete(key)
    };
    let sequence = 0;
    const cryptoApi = { subtle: webcrypto.subtle, randomUUID: () => 'id-' + ++sequence };
    const create = () => workflow.createRetryTracker(retry, cryptoApi, () => storage);
    const payload = workflow.offerFingerprint('applicant-a', 4, '2026-10-28T10:00:00Z');
    const first = await create().forPayload('offer:applicant-a', payload);
    assert.equal(await create().forPayload('offer:applicant-a', payload), first);
    assert.notEqual(await create().forPayload('offer:applicant-a', { ...payload, offeredAcademicBatchId: 5 }), first);
    create().clear('offer:applicant-a');
    assert.notEqual(await create().forPayload('offer:applicant-a', payload), first);
});

test('staff admission mutations send antiforgery tokens to authorized APIs', () => {
    const view = read('EduOS.App/Views/Admissions/Index.cshtml');
    const script = read('EduOS.App/wwwroot/js/admissions/index.js');
    const controller = read('EduOS.App/Controllers/Api/AdmissionApplicationsController.cs');
    const intake = read('EduOS.App/Controllers/Api/AdmissionIntakeController.cs');
    assert.match(view, /@Html\.AntiForgeryToken\(\)/);
    assert.match(script, /options\.headers\.RequestVerificationToken = csrf/);
    assert.match(script, /if \(method !== 'GET'\)/);
    assert.match(controller, /AutoValidateAntiforgeryToken/);
    assert.match(intake, /AutoValidateAntiforgeryToken/);
});

test('both offer workspaces use post-offer evidence and payload-aware retry', () => {
    const staff = read('EduOS.App/wwwroot/js/admissions/index.js');
    const standalone = read('EduOS.App/wwwroot/js/admissions/offers.js');
    const core = read('EduOS.App/wwwroot/js/admissions/offers-core.js');
    const dto = read('EduOS.Core/DTOs/Admission/AdmissionDtos.cs');
    const service = read('EduOS.Service/Services/Admission/AdmissionIntakeService.cs');
    for (const view of ['EduOS.App/Views/Admissions/Index.cshtml', 'EduOS.App/Views/Admissions/Offers.cshtml']) {
        const source = read(view);
        assert.match(source, /public-admission\/retry-keys\.js/);
        assert.match(source, /admissions\/offer-workflow\.js/);
    }
    assert.match(staff, /EduOSAdmissionOffers\?\.eligibleEvidence/);
    assert.match(core, /EduOSAdmissionOffers\?\.eligibleEvidence/);
    assert.match(staff, /offerRetry\.forPayload/);
    assert.match(standalone, /offerRetry\.forPayload/);
    assert.match(dto, /public DateTime CreatedAt \{ get; set; \}/);
    assert.match(service, /CreatedAt = doc\.CreatedAt/);
});
