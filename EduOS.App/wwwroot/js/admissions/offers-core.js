export const el = id => document.getElementById(id);
export const val = id => el(id).value.trim();
export const num = x => Number.isSafeInteger(+x) && +x > 0 ? +x : null;
export const ctx = { reference: '', applicant: null, decision: null, requestId: '', fingerprint: '' };
const bn = () => el('offerLocale')?.value === 'bn-BD';
const localized = (en, bd) => bn() ? bd : en;
let loadSequence = 0;
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
    el('offerIssueButton').disabled = busy || !qualified || state !== 0;
    el('offerAcceptButton').disabled = busy || !qualified || state !== 2 || expired ||
        !num(val('offerEvidence')) || !el('offerIdentityChecked').checked || !ctx.decision?.rowVersion;
    el('offerEnrollButton').disabled = busy || !qualified || state !== 3 || !ctx.applicant?.rowVersion;
    const stage = !qualified ? 'review' : state === 3 ? 'enroll' : expired ? 'expired' : state === 2 ? 'accept' : state === 0 ? 'issue' : 'review';
    const captions = {
        review: localized('Application review required', 'আবেদন পর্যালোচনা প্রয়োজন'),
        issue: localized('Ready to issue offer', 'অফার প্রদানের জন্য প্রস্তুত'),
        accept: localized('Awaiting verified acceptance', 'যাচাইকৃত গ্রহণের অপেক্ষায়'),
        expired: localized('Offer expired; review required', 'অফারের মেয়াদ শেষ; পর্যালোচনা প্রয়োজন'),
        enroll: localized('Accepted; ready for enrollment', 'গৃহীত; ভর্তির জন্য প্রস্তুত')
    };
    el('offerCurrentState').textContent = captions[stage];
    for (const [id, visible] of [['offerIssueSection', stage === 'issue'], ['offerAcceptanceSection', stage === 'accept'], ['offerEnrollmentSection', stage === 'enroll']]) {
        const section = el(id);
        if (section) section.hidden = !visible;
    }
    const guidance = el('offerStageGuidance');
    if (guidance) guidance.textContent = ({
        review: localized('Complete the admission review before issuing an offer.', 'অফার দেওয়ার আগে আবেদন পর্যালোচনা সম্পন্ন করুন।'),
        issue: localized('Choose a batch and future expiry to issue the offer.', 'ব্যাচ ও ভবিষ্যতের মেয়াদ নির্বাচন করে অফার দিন।'),
        accept: localized('Verify the signed acceptance document and signer identity.', 'স্বাক্ষরিত গ্রহণপত্র ও স্বাক্ষরকারীর পরিচয় যাচাই করুন।'),
        expired: localized('The expired offer cannot be accepted. Review the application and contact an authorized officer.', 'মেয়াদোত্তীর্ণ অফার গ্রহণ করা যাবে না। আবেদন পর্যালোচনা করে দায়িত্বপ্রাপ্ত কর্মকর্তার সঙ্গে যোগাযোগ করুন।'),
        enroll: localized('Enter the roll and optional track to create the student record.', 'শিক্ষার্থী রেকর্ড তৈরির জন্য রোল ও প্রয়োজনে বিভাগ নির্বাচন করুন।')
    })[stage];
}
export async function refresh() {
    if (!ctx.reference) return;
    el('offerAcceptButton').disabled = el('offerEnrollButton').disabled = true;
    const reference = ctx.reference;
    const [applicant, decision] = await Promise.all([api('/api/admission-applications/' + encodeURIComponent(reference)), api('/api/admission-applications/' + encodeURIComponent(reference) + '/offer')]);
    if (!currentReference(reference)) return;
    ctx.applicant = applicant; ctx.decision = decision;
    if (!ctx.applicant?.rowVersion) throw Error('Current applicant version unavailable.');
    el('offerApplicantName').textContent = ctx.applicant.applicantName || ctx.applicant.applicationNumber || ctx.reference;
    if (ctx.decision?.offeredAcademicBatchId) el('offerBatch').value = String(ctx.decision.offeredAcademicBatchId);
    const docs = await api('/api/admission-intake/applications/' + encodeURIComponent(reference) + '/documents');
    if (!currentReference(reference)) return;
    const verified = window.EduOSAdmissionOffers?.eligibleEvidence(
        docs, ctx.decision?.offeredAt) || [];
    const selected = val('offerEvidence');
    fill('offerEvidence', verified.map(x => ({ id: x.id, name: 'Verified document #' + x.id + ' (v' + x.versionNo + ')' })),
        'Select verified signed evidence');
    if (verified.some(x => String(x.id) === selected)) el('offerEvidence').value = selected;
    else if (verified.length === 1) el('offerEvidence').value = String(verified[0].id);
    render();
}
export async function load(event) {
    event.preventDefault();
    const sequence = ++loadSequence;
    const id = val('offerApplicantReference');
    if (!/^[\da-f]{8}(-[\da-f]{4}){3}-[\da-f]{12}$/i.test(id)) return notice(localized('Invalid applicant reference.', 'আবেদনের রেফারেন্স সঠিক নয়।'), true);
    ctx.reference = id; ctx.applicant = ctx.decision = null; ctx.requestId = ctx.fingerprint = '';
    el('offerWorkspace').hidden = true;
    try {
        const data = await api(base() + '/enrollment-options');
        if (sequence !== loadSequence) return;
        fill('offerBatch', data.academicBatches || [], 'Select batch');
        fill('offerTrack', data.academicTracks || [], 'No additional track');
        const future = new Date(Date.now() + 7 * 86400000);
        el('offerExpiry').value = new Date(future - future.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
        el('offerIdentityChecked').checked = false;
        await refresh();
        if (sequence !== loadSequence) return;
        el('offerWorkspace').hidden = false;
        notice(localized('Applicant, offer and verified documents loaded.', 'আবেদনকারী, অফার ও যাচাইকৃত নথি লোড হয়েছে।'));
    } catch (error) { if (sequence === loadSequence) notice(error.message, true); }
}
