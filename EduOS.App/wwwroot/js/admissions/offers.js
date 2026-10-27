import { el, val, num, ctx, base, notice, api, render, refresh, load } from './offers-core.js';
import { initializeOfferSearch } from './offers-search.js';
async function issue() {
    const batch = num(val('offerBatch')), expiry = new Date(val('offerExpiry'));
    if (!batch || !Number.isFinite(expiry.getTime()) || expiry.getTime() <= Date.now())
        return notice('Select a valid batch and future expiry.', true);
    if (issuingOffer) return;
    issuingOffer = true;
    el('offerIssueButton').disabled = true;
    try {
        const expiresAt = expiry.toISOString();
        const key = 'offer:' + ctx.reference;
        const requestId = await offerRetry.forPayload(key,
            window.EduOSAdmissionOffers.offerFingerprint(ctx.reference, batch, expiresAt));
        await api(base() + '/offer', 'POST', {
            clientRequestId: requestId, admissionApplicantReference: ctx.reference,
            state: 2, offeredAcademicBatchId: batch, expiresAt,
            applicantRowVersion: ctx.applicant.rowVersion
        });
        offerRetry.clear(key);
        await refresh();
        notice('Offer issued. Verify signed evidence before acceptance.');
    } catch (error) { notice(error.message || 'Offer failed.', true); }
    finally { issuingOffer = false; render(); }
}
async function accept() {
    if (!el('offerIdentityChecked').checked || !num(val('offerEvidence'))) return;
    el('offerAcceptButton').disabled = true;
    try {
        await api(base() + '/offer/verified-acceptance', 'POST', {
            acceptanceDocumentId: num(val('offerEvidence')),
            decisionRowVersion: ctx.decision.rowVersion,
            signerRole: val('offerSigner'), identityChecked: true
        });
        await refresh();
        notice('Verified acceptance recorded. Enrollment unlocked.');
    } catch (error) { notice(error.message || 'Acceptance failed.', true); }
    finally { render(); }
}
async function enroll() {
    const batch = num(val('offerBatch')), roll = val('offerRoll');
    if (!batch || !roll || roll.length > 50) return notice('Batch and roll are required.', true);
    el('offerEnrollButton').disabled = true;
    try {
        const result = await api(base() + '/admit', 'POST', {
            academicBatchId: batch, academicTrackId: num(val('offerTrack')),
            roll, rowVersion: ctx.applicant.rowVersion
        });
        await refresh();
        notice('Student enrolled: ' + (result?.studentCode || 'Created'));
    } catch (error) { notice(error.message || 'Enrollment failed.', true); }
    finally { render(); }
}
document.addEventListener('DOMContentLoaded', () => {
    if (!el('offerLookupForm')) return;
    el('offerLookupForm').addEventListener('submit', load);
    initializeOfferSearch();
    [['offerIssueButton', issue], ['offerAcceptButton', accept], ['offerEnrollButton', enroll]].forEach(
        ([id, handler]) => el(id).addEventListener('click', handler));
    el('offerRefreshButton').addEventListener('click', async () => {
        try { await refresh(); notice('Current status refreshed.'); }
        catch (error) { notice(error.message || 'Refresh failed.', true); }
    });
    ['offerEvidence', 'offerIdentityChecked', 'offerBatch'].forEach(id =>
        el(id).addEventListener('change', render));
}, { once: true });
