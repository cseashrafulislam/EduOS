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
let enrollmentSections = [], sectionBatch = null, sectionVersion = 0;
async function loadEnrollmentSections() {
 const batch = num(val('offerBatch')), request = ++sectionVersion;
 const select = el('offerAcademicSection'), previous = select?.value;
 if (!select) return;
 enrollmentSections = [];
 sectionBatch = batch;
 select.replaceChildren(new Option('Select academic section / শাখা', ''));
 if (!batch) return;
 try {
  const data = await api('/api/academic-sections?academicBatchId=' + encodeURIComponent(String(batch)) +
   '&page=1&pageSize=100');
  if (request !== sectionVersion || batch !== num(val('offerBatch'))) return;
  enrollmentSections = (data.items || []).filter(x => x.isActive && x.capacity > 0);
  enrollmentSections.forEach(x => select.add(new Option(x.name + ' (' + x.code + ')', String(x.id))));
  if (enrollmentSections.some(x => String(x.id) === previous)) select.value = previous;
  else if (enrollmentSections.length === 1) select.value = String(enrollmentSections[0].id);
 } catch(error) {
  if (request === sectionVersion) notice(error.message || 'Sections could not be loaded.', true);
 }
}

async function runMutation(work, success, fallback) {
 if (mutationInFlight || !ctx.reference) return;
 mutationInFlight = true;
 const reference = ctx.reference;
 setBusy(true);
 try {
  const result = await work(reference);
  if (!currentReference(reference)) return;
  const updated = await refresh();
  if (updated && currentReference(reference)) {
   await loadEnrollmentSections();
   notice(success(result));
  }
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
 if (sectionBatch !== batch) await loadEnrollmentSections();
 const sectionId = num(val('offerAcademicSection')) || null;
 if (enrollmentSections.length && !enrollmentSections.some(x => x.id === sectionId))
  return notice(say('Select an active academic section.','সক্রিয় শাখা নির্বাচন করুন।'), true);
 if(!validateRoll(batch,roll)) return notice(say('A valid batch and roll are required.','সঠিক ব্যাচ ও রোল প্রয়োজন।'),true);
 const rowVersion=ctx.applicant?.rowVersion;
 await runMutation(reference=>api('/api/admission-applications/'+encodeURIComponent(reference)+'/admit','POST',{
  academicBatchId:batch,academicSectionId:sectionId,academicTrackId:track,roll,rowVersion
 }),result=>say('Student enrolled: ','শিক্ষার্থী ভর্তি হয়েছে: ')+(result?.studentCode||say('Created','তৈরি হয়েছে')),'Enrollment failed.');
}
document.addEventListener('DOMContentLoaded', () => {
    const state=el('offerCurrentState');
    if(state)new MutationObserver(updateStage).observe(state,{childList:true,subtree:true,characterData:true});
    if (!el('offerLookupForm')) return;
    el('offerLookupForm').addEventListener('submit', load);
    el('offerBatch').addEventListener('change', loadEnrollmentSections);
    document.addEventListener('admission:offer-data-loaded', () => {
        loadEnrollmentSections().catch(error => notice(error.message, true));
    });
    initializeOfferSearch();
    [['offerIssueButton', issue], ['offerAcceptButton', accept], ['offerEnrollButton', enroll]].forEach(
        ([id, handler]) => el(id).addEventListener('click', handler));
    el('offerRefreshButton').addEventListener('click', async () => {
        const reference = ctx.reference;
        try {
            if (await refresh() && currentReference(reference))
                notice(say('Current status and evidence refreshed.','বর্তমান অবস্থা ও নথি হালনাগাদ হয়েছে।'));
        } catch (error) {
            if (currentReference(reference))
                notice(error.message || say('Refresh failed. Retry without resubmitting.','হালনাগাদ হয়নি। পুনরায় জমা না দিয়ে আবার চেষ্টা করুন।'), true);
        }
    });
    ['offerEvidence', 'offerIdentityChecked', 'offerBatch'].forEach(id =>
        el(id).addEventListener('change', render));
}, { once: true });
