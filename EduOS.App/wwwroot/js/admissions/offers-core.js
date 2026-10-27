export const el = id => document.getElementById(id);
export const val = id => el(id).value.trim();
export const num = x => Number.isSafeInteger(+x) && +x > 0 ? +x : null;
export const ctx = { reference: '', applicant: null, decision: null, requestId: '', fingerprint: '' };
let loadSequence = 0;
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
    el('offerIssueButton').disabled = !qualified || state !== 0;
    el('offerAcceptButton').disabled = !qualified || state !== 2 || expired ||
        !num(val('offerEvidence')) || !el('offerIdentityChecked').checked || !ctx.decision?.rowVersion;
    el('offerEnrollButton').disabled = !qualified || state !== 3 || !ctx.applicant?.rowVersion;
    el('offerCurrentState').textContent = 'Application: ' + ctx.applicant?.state + ' · Offer: ' +
        (state === 3 ? 'Accepted' : expired ? 'Expired' : state === 2 ? 'Offered' : state ? 'Review required' : 'Not issued');
}
export async function refresh() {
    if (!ctx.reference) return;
    el('offerAcceptButton').disabled = el('offerEnrollButton').disabled = true;
    const reference = ctx.reference;
    const [applicant, decision] = await Promise.all([api(base()), api(base() + '/offer')]);
    if (ctx.reference !== reference) return;
    ctx.applicant = applicant; ctx.decision = decision;
    if (!ctx.applicant?.rowVersion) throw Error('Current applicant version unavailable.');
    el('offerApplicantName').textContent = ctx.applicant.applicantName || ctx.applicant.applicationNumber || ctx.reference;
    if (ctx.decision?.offeredAcademicBatchId) el('offerBatch').value = String(ctx.decision.offeredAcademicBatchId);
    const docs = await api('/api/admission-intake/applications/' + encodeURIComponent(reference) + '/documents');
    if (ctx.reference !== reference) return;
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
    if (!/^[\da-f]{8}(-[\da-f]{4}){3}-[\da-f]{12}$/i.test(id)) return notice('Invalid applicant reference.', true);
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
        notice('Applicant, offer and verified documents loaded.');
    } catch (error) { if (sequence === loadSequence) notice(error.message, true); }
}
