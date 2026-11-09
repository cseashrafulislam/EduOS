import {label,nextStage,validateIssue,validateRoll} from './offers-journey.js';
import { el, val, num, ctx, base, notice, api, render, refresh, load, currentReference, setBusy } from './offers-core.js';
import { initializeOfferSearch } from './offers-search.js';
const offerRetry = window.EduOSAdmissionOffers.createRetryTracker(
    window.EduOSAdmissionRetry, window.crypto, () => window.sessionStorage);
const bn=()=>document.getElementById('offerLocale')?.value==='bn-BD';
const say=(en,bd)=>bn()?bd:en;
function updateStage(){
 const target=el('offerJourneyStage');
 if(target)target.textContent=ctx.applicant?label(nextStage(ctx.applicant.state,ctx.decision?.state,ctx.decision?.expiresAt),bn()):'';
}
let mutationInFlight = false;
async function runMutation(work, success, fallback) {
 if (mutationInFlight || !ctx.reference) return;
 mutationInFlight = true;
 const reference = ctx.reference;
 setBusy(true);
 try {
  const result = await work(reference);
  if (!currentReference(reference)) return;
  await refresh();
  if (currentReference(reference)) notice(success(result));
 } catch(error) {
  if (currentReference(reference)) {
   notice(error.message || fallback, true);
   // Reload server authority after a conflict; do not reuse a stale row version.
   if (/conflict|version|modified|409|412/i.test(error.message || '')) {
    try { await refresh(); } catch { /* retain original actionable error */ }
   }
  }
 } finally {
  mutationInFlight = false;
  setBusy(false);
 }
}

async function issue() {
 const batch=num(val('offerBatch')), expiryText=val('offerExpiry');
 if(!validateIssue(batch,expiryText)) return notice(say('Select a valid batch and future expiry.','সঠিক ব্যাচ ও ভবিষ্যতের মেয়াদ নির্বাচন করুন।'),true);
 const expiresAt=new Date(expiryText).toISOString();
 await runMutation(async reference=>{
  const key='offer:'+reference;
  const requestId=await offerRetry.forPayload(key,window.EduOSAdmissionOffers.offerFingerprint(reference,batch,expiresAt));
  if(!currentReference(reference)) return null;
  const result=await api('/api/admission-applications/'+encodeURIComponent(reference)+'/offer','POST',{
   clientRequestId:requestId,admissionApplicantReference:reference,state:2,offeredAcademicBatchId:batch,
   expiresAt,applicantRowVersion:ctx.applicant.rowVersion
  });
  offerRetry.clear(key);
  return result;
 },()=>say('Offer issued. Verify signed evidence before acceptance.','অফার দেওয়া হয়েছে। গ্রহণের আগে স্বাক্ষরিত নথি যাচাই করুন।'),'Offer failed.');
}
async function accept() {
 const evidence=num(val('offerEvidence'));
 if(!el('offerIdentityChecked').checked||!evidence) return notice(say('Verify signer identity and select evidence.','স্বাক্ষরকারীর পরিচয় যাচাই করে নথি নির্বাচন করুন।'),true);
 const signer=val('offerSigner'),rowVersion=ctx.decision?.rowVersion;
 await runMutation(reference=>api('/api/admission-applications/'+encodeURIComponent(reference)+'/offer/verified-acceptance','POST',{
  acceptanceDocumentId:evidence,decisionRowVersion:rowVersion,signerRole:signer,identityChecked:true
 }),()=>say('Verified acceptance recorded. Enrollment unlocked.','যাচাইকৃত গ্রহণ সংরক্ষিত হয়েছে। ভর্তি করা যাবে।'),'Acceptance failed.');
}
async function enroll() {
 const batch=num(val('offerBatch')),roll=val('offerRoll'),track=num(val('offerTrack'));
 if(!validateRoll(batch,roll)) return notice(say('A valid batch and roll are required.','সঠিক ব্যাচ ও রোল প্রয়োজন।'),true);
 const rowVersion=ctx.applicant?.rowVersion;
 await runMutation(reference=>api('/api/admission-applications/'+encodeURIComponent(reference)+'/admit','POST',{
  academicBatchId:batch,academicTrackId:track,roll,rowVersion
 }),result=>say('Student enrolled: ','শিক্ষার্থী ভর্তি হয়েছে: ')+(result?.studentCode||say('Created','তৈরি হয়েছে')),'Enrollment failed.');
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
        try { await refresh(); notice(say('Current status refreshed.','বর্তমান অবস্থা হালনাগাদ হয়েছে।')); }
        catch (error) { notice(error.message || 'Refresh failed.', true); }
    });
    ['offerEvidence', 'offerIdentityChecked', 'offerBatch'].forEach(id =>
        el(id).addEventListener('change', render));
}, { once: true });
