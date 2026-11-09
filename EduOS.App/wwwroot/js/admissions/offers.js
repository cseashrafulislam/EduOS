import {label,nextStage,validateIssue,validateRoll} from './offers-journey.js';
import { el, val, num, ctx, base, notice, api, render, refresh, load } from './offers-core.js';
import { initializeOfferSearch } from './offers-search.js';
const offerRetry = window.EduOSAdmissionOffers.createRetryTracker(
    window.EduOSAdmissionRetry, window.crypto, () => window.sessionStorage);
const bn=()=>document.getElementById('offerLocale')?.value==='bn-BD';
const say=(en,bd)=>bn()?bd:en;
function updateStage(){
 const target=el('offerJourneyStage');
 if(target)target.textContent=ctx.applicant?label(nextStage(ctx.applicant.state,ctx.decision?.state,ctx.decision?.expiresAt),bn()):'';
}
let issuingOffer = false;
async function issue() {
    const batch = num(val('offerBatch')), expiry = new Date(val('offerExpiry'));
    if (!validateIssue(batch,val('offerExpiry')))
        return notice(say('Select a valid batch and future expiry.','সঠিক ব্যাচ ও ভবিষ্যতের মেয়াদ নির্বাচন করুন।'), true);
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
        notice(say('Offer issued. Verify signed evidence before acceptance.','অফার দেওয়া হয়েছে। গ্রহণের আগে স্বাক্ষরিত নথি যাচাই করুন।'));
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
        notice(say('Verified acceptance recorded. Enrollment unlocked.','যাচাইকৃত গ্রহণ সংরক্ষিত হয়েছে। ভর্তি করা যাবে।'));
    } catch (error) { notice(error.message || 'Acceptance failed.', true); }
    finally { render(); }
}
async function enroll() {
    const batch = num(val('offerBatch')), roll = val('offerRoll');
    if (!validateRoll(batch,roll)) return notice(say('A valid batch and roll are required.','সঠিক ব্যাচ ও রোল প্রয়োজন।'), true);
    el('offerEnrollButton').disabled = true;
    try {
        const result = await api(base() + '/admit', 'POST', {
            academicBatchId: batch, academicTrackId: num(val('offerTrack')),
            roll, rowVersion: ctx.applicant.rowVersion
        });
        await refresh();
        notice(say('Student enrolled: ','শিক্ষার্থী ভর্তি হয়েছে: ') + (result?.studentCode || say('Created','তৈরি হয়েছে')));
    } catch (error) { notice(error.message || 'Enrollment failed.', true); }
    finally { render(); }
}
document.addEventListener('DOMContentLoaded', () => {
    const state=el('offerCurrentState');
    if(state)new MutationObserver(updateStage).observe(state,{childList:true,subtree:true,characterData:true});
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
