(function (root) {
    'use strict';

    // Match the server's post-offer evidence policy; missing timestamps must fail closed.
    function eligibleEvidence(documents, offeredAt) {
        const issued = Date.parse(offeredAt || '');
        if (!Number.isFinite(issued)) return [];
        return (Array.isArray(documents) ? documents : []).filter(doc => {
            if (doc?.documentTypeCode !== 'OFFER_ACCEPTANCE' || !doc.isVerified ||
                !Number.isSafeInteger(Number(doc.verifiedByUserId)) || Number(doc.verifiedByUserId) <= 0) return false;
            const uploaded = Date.parse(doc.createdAt || '');
            const verified = Date.parse(doc.verifiedAt || '');
            return Number.isFinite(uploaded) && Number.isFinite(verified) &&
                uploaded >= issued && verified >= issued;
        }).sort((a, b) => (Number(b.versionNo) || 0) - (Number(a.versionNo) || 0) ||
            (Number(b.id) || 0) - (Number(a.id) || 0));
    }

    function createRetryTracker(retryApi, cryptoApi, storageProvider) {
        if (!retryApi?.createPersistentTracker || !cryptoApi?.subtle || !cryptoApi?.randomUUID)
            throw new Error('Secure offer retry support is unavailable.');
        return retryApi.createPersistentTracker(
            () => cryptoApi.randomUUID(), storageProvider,
            async raw => {
                const digest = await cryptoApi.subtle.digest('SHA-256', new TextEncoder().encode(raw));
                return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, '0')).join('');
            }, 'offers');
    }

    function offerFingerprint(reference, batch, expiresAt) {
        return { admissionApplicantReference: reference, offeredAcademicBatchId: batch, expiresAt };
    }

    const api = { eligibleEvidence, createRetryTracker, offerFingerprint };
    if (typeof module !== 'undefined' && module.exports) module.exports = api;
    if (root) root.EduOSAdmissionOffers = api;
})(typeof globalThis !== 'undefined' ? globalThis : null);
