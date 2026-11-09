export const el = id => document.getElementById(id);
export const val = id => el(id).value.trim();
export const num = x => Number.isSafeInteger(+x) && +x > 0 ? +x : null;
export const ctx = { reference: '', applicant: null, decision: null, requestId: '', fingerprint: '' };
const bn = () => el('offerLocale')?.value === 'bn-BD';
const localized = (en, bd) => bn() ? bd : en;
let loadSequence = 0;
let refreshSequence = 0;
let pendingRefresh = false;
let busy = false;
export const currentReference = reference => !!reference && ctx.reference === reference;
export function setBusy(value) {
    busy = !!value;
    const indicator = el('offerBusy');
    if (indicator) indicator.hidden = !busy;
    ['offerLookupButton','offerIssueButton','offerAcceptButton','offerEnrollButton','offerRefreshButton']
        .forEach(id => { const control = el(id); if (control) control.setAttribute('aria-busy', String(busy)); });
    render();
}
export const base = () => '/api/admission-applications/' + encodeURIComponent(ctx.reference);
export function notice(text, error = false) {
    el('offerNotice').className = 'alert ' + (error ? 'alert-danger' : 'alert-info');
    el('offerNotice').textContent = text;
}
export function fill(id, items, placeholder) {
    el(id).replaceChildren(new Option(placeholder, ''), ...items.map(x => new Option(x.name, String(x.id))));
}
export async function api(url, method = 'GET', data = null) {
    const headers = { Accept: 'application/json' };
    if (method !== 'GET') {
        const csrf = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (!csrf) throw Error('Security token missing. Reload the page.');
        headers.RequestVerificationToken = csrf;
        headers['Content-Type'] = 'application/json';
    }
    const response = await fetch(url, {
        method, headers, credentials: 'same-origin', cache: 'no-store',
        body: data === null ? undefined : JSON.stringify(data)
    });
    const result = await response.json().catch(() => null);
    if (!response.ok || !result?.success) throw Error(result?.message || 'Request failed.');
    return result.data;
}
export function render() {
    const state = Number(ctx.decision?.state || 0), qualified = Number(ctx.applicant?.state) === 6;
    const expired = state === 2 && (!ctx.decision?.expiresAt || Date.parse(ctx.decision.expiresAt) <= Date.now());
    el('offerBatch').disabled = !qualified || state === 2 || state === 3;
    el('offerIssueButton').disabled = busy || pendingRefresh || !qualified || state !== 0;
    el('offerAcceptButton').disabled = busy || pendingRefresh || !qualified || state !== 2 || expired ||
        !num(val('offerEvidence')) || !el('offerIdentityChecked').checked || !ctx.decision?.rowVersion;
    el('offerEnrollButton').disabled = busy || pendingRefresh || !qualified || state !== 3 || !ctx.applicant?.rowVersion;
    const stage = Number(ctx.applicant?.state) === 8 ? 'complete' : !qualified ? 'review' : state === 3 ? 'enroll' : expired ? 'expired' : state === 2 ? 'accept' : state === 0 ? 'issue' : 'review';
    const captions = {
        review: localized('Application review required', 'আবেদন পর্যালোচনা প্রয়োজন'),
        issue: localized('Ready to issue offer', 'অফার প্রদানের জন্য প্রস্তুত'),
        accept: localized('Awaiting verified acceptance', 'যাচাইকৃত গ্রহণের অপেক্ষায়'),
        expired: localized('Offer expired; review required', 'অফারের মেয়াদ শেষ; পর্যালোচনা প্রয়োজন'),
        complete: localized('Enrollment completed', 'ভর্তি সম্পন্ন'),
        enroll: localized('Accepted; ready for enrollment', 'গৃহীত; ভর্তির জন্য প্রস্তুত')
    };
    el('offerCurrentState').textContent = captions[stage];
    for (const [id, visible] of [['offerIssueSection', stage === 'issue'], ['offerAcceptanceSection', stage === 'accept'], ['offerEnrollmentSection', stage === 'enroll']]) {
        const section = el(id);
        if (section) section.hidden = !visible;
    }
    const completed = el('offerCompleted');
    if (completed) completed.hidden = stage !== 'complete';
    const steps = ['issue', 'accept', 'enroll'];
    const position = steps.indexOf(stage);
    steps.forEach((step, index) => {
        const element = el('offerProgress' + step[0].toUpperCase() + step.slice(1));
        if (!element) return;
        const done = stage === 'complete' || (position >= 0 && index < position);
        element.className = 'badge ' + (done ? 'text-bg-success' : index === position ? 'text-bg-primary' : 'text-bg-secondary');
        if (index === position) element.setAttribute('aria-current', 'step');
        else element.removeAttribute('aria-current');
    });
    const guidance = el('offerStageGuidance');
    if (guidance) guidance.textContent = ({
        review: localized('Complete the admission review before issuing an offer.', 'অফার দেওয়ার আগে আবেদন পর্যালোচনা সম্পন্ন করুন।'),
        issue: localized('Choose a batch and future expiry to issue the offer.', 'ব্যাচ ও ভবিষ্যতের মেয়াদ নির্বাচন করে অফার দিন।'),
        accept: localized('Verify the signed acceptance document and signer identity.', 'স্বাক্ষরিত গ্রহণপত্র ও স্বাক্ষরকারীর পরিচয় যাচাই করুন।'),
        expired: localized('The expired offer cannot be accepted. Review the application and contact an authorized officer.', 'মেয়াদোত্তীর্ণ অফার গ্রহণ করা যাবে না। আবেদন পর্যালোচনা করে দায়িত্বপ্রাপ্ত কর্মকর্তার সঙ্গে যোগাযোগ করুন।'),
        complete: localized('The student record already exists; do not enroll again.', 'শিক্ষার্থী রেকর্ড ইতিমধ্যে তৈরি হয়েছে; পুনরায় ভর্তি জমা দেবেন না।'),
        enroll: localized('Enter the roll and optional track to create the student record.', 'শিক্ষার্থী রেকর্ড তৈরির জন্য রোল ও প্রয়োজনে বিভাগ নির্বাচন করুন।')
    })[stage];
}
// Commit a complete server snapshot only after all three reads succeed. A slower
// response for the same applicant must never overwrite a newer refresh.
export async function refresh() {
    if (!ctx.reference) return false;
    const reference = ctx.reference;
    const sequence = ++refreshSequence;
    pendingRefresh = true;
    el('offerAcceptButton').disabled = el('offerEnrollButton').disabled = true;
    try {
        const [applicant, decision, docs] = await Promise.all([
            api('/api/admission-applications/' + encodeURIComponent(reference)),
            api('/api/admission-applications/' + encodeURIComponent(reference) + '/offer'),
            api('/api/admission-intake/applications/' + encodeURIComponent(reference) + '/documents')
        ]);
        if (sequence !== refreshSequence || !currentReference(reference)) return false;
        if (!applicant?.rowVersion) throw Error(localized('Current applicant version unavailable. Refresh and retry.', 'আবেদনকারীর বর্তমান সংস্করণ পাওয়া যায়নি। আবার লোড করুন।'));
        const verified = window.EduOSAdmissionOffers?.eligibleEvidence(docs, decision?.offeredAt) || [];
        const selected = val('offerEvidence');
        ctx.applicant = applicant;
        ctx.decision = decision;
        el('offerApplicantName').textContent = applicant.applicantName || applicant.applicationNumber || reference;
        if (decision?.offeredAcademicBatchId) el('offerBatch').value = String(decision.offeredAcademicBatchId);
        fill('offerEvidence', verified.map(x => ({ id: x.id, name: localized('Verified document #', 'যাচাইকৃত নথি #') + x.id + ' (v' + x.versionNo + ')' })),
            localized('Select verified signed evidence', 'যাচাইকৃত স্বাক্ষরিত নথি নির্বাচন করুন'));
        if (verified.some(x => String(x.id) === selected)) el('offerEvidence').value = selected;
        else if (verified.length === 1) el('offerEvidence').value = String(verified[0].id);
        return true;
    } finally {
        if (sequence === refreshSequence) {
            pendingRefresh = false;
            render();
        }
    }
}
export async function load(event) {
    event.preventDefault();
    const sequence = ++loadSequence;
    ++refreshSequence; // invalidate any older refresh, including the same reference
    const id = val('offerApplicantReference');
    if (!/^[\da-f]{8}(-[\da-f]{4}){3}-[\da-f]{12}$/i.test(id)) return notice(localized('Invalid applicant reference.', 'আবেদনের রেফারেন্স সঠিক নয়।'), true);
    ctx.reference = id; ctx.applicant = ctx.decision = null; ctx.requestId = ctx.fingerprint = '';
    el('offerWorkspace').hidden = true;
    el('offerLookupButton').disabled = true;
    try {
        const data = await api(base() + '/enrollment-options');
        if (sequence !== loadSequence) return;
        fill('offerBatch', data.academicBatches || [], 'Select batch');
        fill('offerTrack', data.academicTracks || [], 'No additional track');
        const future = new Date(Date.now() + 7 * 86400000);
        el('offerExpiry').value = new Date(future - future.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
        el('offerIdentityChecked').checked = false;
        const loaded = await refresh();
        if (sequence !== loadSequence || !loaded) return;
        el('offerWorkspace').hidden = false;
        notice(localized('Applicant, offer and verified documents loaded.', 'আবেদনকারী, অফার ও যাচাইকৃত নথি লোড হয়েছে।'));
    } catch (error) { if (sequence === loadSequence) notice(error.message || localized('Unable to load applicant. Retry lookup.', 'আবেদনকারী লোড হয়নি। আবার চেষ্টা করুন।'), true); }
    finally { if (sequence === loadSequence) el('offerLookupButton').disabled = false; }
}
