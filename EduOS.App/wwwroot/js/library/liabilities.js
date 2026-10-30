(() => {
'use strict';
const root = document.getElementById('libraryLiabilitiesApp');
if (!root) return;
const el = id => document.getElementById(id);
const bn = document.documentElement.lang?.toLowerCase().startsWith('bn');
const t = (en, bd) => bn ? bd : en;
if (bn) root.querySelectorAll('[data-bn]').forEach(node => { node.textContent = node.dataset.bn; });
const session = document.body?.dataset.authSessionReference || '';
const tenant = document.body?.dataset.authTenantId || '';
const live = () => /^[0-9A-F]{64}$/.test(session) &&
    document.body?.dataset.authSessionReference === session &&
    document.body?.dataset.authTenantId === tenant &&
    window.EduOSSessionTransitionPending?.() !== true;
const active = new Set();
const incident = { page: 1, pages: 1, seq: 0 };
const liability = { page: 1, pages: 1, seq: 0 };
const policy = { page: 1, pages: 1, seq: 0 };
let pickedIncident = null, pickedLiability = null, requestId = null;
let reversalRecord = null, policyRequestId = null, reversalRequestId = null;
const optionVersions = { period: 0, head: 0, campus: 0 };
const optionTimers = {};
const price = amount => Number(amount || 0).toLocaleString(bn ? 'bn-BD' : 'en-BD',
    { minimumFractionDigits: 2, maximumFractionDigits: 2 });
function notify(message, okay = false) {
    const n = el('liabilityAlert'); n.className = 'alert alert-' + (okay ? 'success' : 'danger');
    n.textContent = message;
}
async function api(path, method, body) {
    if (!live()) throw Error(t('Session changed. Reload.', 'সেশন বদলেছে। পুনরায় লোড করুন।'));
    const controller = new AbortController(); active.add(controller);
    try {
        const headers = { Accept: 'application/json' };
        if (method === 'POST') {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw Error(t('Security token missing.', 'নিরাপত্তা টোকেন অনুপস্থিত।'));
            headers.RequestVerificationToken = token;
            headers['Content-Type'] = 'application/json';
        }
        const response = await fetch(path, { method: method || 'GET',
            body: body ? JSON.stringify(body) : undefined, headers, credentials: 'same-origin',
            cache: 'no-store', signal: controller.signal });
        if (!live()) throw Error('Session changed.');
        const result = await response.json();
        if (!response.ok || !result?.success) throw Error(result?.message || 'Request failed.');
        return result.data;
    } finally { active.delete(controller); }
}
async function exportPage() {
    if (!live()) return;
    const button = el('liabilityExport'); button.disabled = true;
    const controller = new AbortController(); active.add(controller);
    try {
        const q = new URLSearchParams({ status: el('liabilityState').value,
            page: String(liability.page), pageSize: '20', language: bn ? 'bn-BD' : 'en-BD' });
        const response = await fetch('/api/library/liabilities/export?' + q, {
            credentials: 'same-origin', cache: 'no-store', signal: controller.signal });
        if (!live() || !response.ok) throw Error(t('Export unavailable.', 'রপ্তানি করা যায়নি।'));
        const url = URL.createObjectURL(await response.blob());
        const link = document.createElement('a'); link.href = url;
        link.download = 'library-liabilities-page.csv';
        document.body.append(link); link.click(); link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (error) { if (error.name !== 'AbortError') notify(error.message); }
    finally { button.disabled = false; active.delete(controller); }
}
function td(tr, value) {
    const node = document.createElement('td'); node.textContent = value ?? '—';
    tr.append(node); return node;
}
function button(label, click, secondary = false) {
    const node = document.createElement('button');
    node.type = 'button'; node.className = 'btn btn-sm ' + (secondary ? 'btn-outline-secondary' : 'btn-outline-primary');
    node.textContent = label; node.addEventListener('click', click); return node;
}
function blank(table, text) {
    const rows = el(table); rows.replaceChildren();
    const tr = document.createElement('tr'); td(tr, text).colSpan = 5; rows.append(tr);
}
async function loadIncidents() {
    const seq = ++incident.seq; blank('incidentRows', t('Loading…', 'লোড হচ্ছে…'));
    try {
        const q = new URLSearchParams({ search: el('incidentSearch').value.trim(),
            page: String(incident.page), pageSize: '20' });
        const data = await api('/api/library/liabilities/' +
            (el('incidentKind').value === 'overdue' ? 'overdue-candidates' : 'incidents') + '?' + q);
        if (seq !== incident.seq) return;
        const rows = el('incidentRows'); rows.replaceChildren();
        for (const item of data?.items || []) {
            const tr = document.createElement('tr');
            td(tr, item.bookTitle + ' · ' + item.accessionNumber);
            td(tr, item.studentName + ' · ' + item.studentCode);
            td(tr, item.kind === 1 ? t('Lost', 'হারানো') : item.kind === 2 ? t('Damaged', 'ক্ষতিগ্রস্ত') :
                (item.overdueDays || 0) + ' ' + t('days overdue', 'দিন বিলম্ব'));
            const quoted = item.kind === 3 ? item.calculatedAmount : item.replacementPrice;
            td(tr, quoted === null || quoted === undefined ? '—' : price(quoted));
            const actions = td(tr, ''); actions.textContent = '';
            if (!item.assessed && quoted > 0)
                actions.append(button(t('Assess', 'দায় নির্ধারণ'), () => openAssess(item)));
            else actions.textContent = item.assessed ? t('Recorded', 'নথিভুক্ত') :
                (item.policyStatus || t('Price needed', 'মূল্য প্রয়োজন'));
            rows.append(tr);
        }
        if (!rows.children.length) blank('incidentRows', t('No incidents.', 'কোনো ঘটনা নেই।'));
        incident.pages = Math.max(1, Number(data?.totalPages || 1));
        el('incidentPage').textContent = incident.page + ' / ' + incident.pages;
        el('incidentPrevious').disabled = incident.page <= 1;
        el('incidentNext').disabled = incident.page >= incident.pages;
    } catch (error) {
        if (seq === incident.seq && error.name !== 'AbortError') {
            blank('incidentRows', t('Could not load.', 'লোড করা যায়নি।')); notify(error.message);
        }
    }
}
async function loadLiabilities() {
    const seq = ++liability.seq; blank('liabilityRows', t('Loading…', 'লোড হচ্ছে…'));
    try {
        const q = new URLSearchParams({ status: el('liabilityState').value,
            page: String(liability.page), pageSize: '20' });
        const data = await api('/api/library/liabilities?' + q);
        if (seq !== liability.seq) return;
        const rows = el('liabilityRows'); rows.replaceChildren();
        for (const item of data?.items || []) {
            const tr = document.createElement('tr');
            td(tr, item.studentName + ' · ' + item.accessionNumber);
            td(tr, price(item.amount));
            td(tr, item.state === 1 ? t('Assessed', 'নির্ধারিত') : item.state === 2 ?
                t('Waived', 'মওকুফ') : item.state === 4 ? t('Reversed', 'বাতিলকৃত') :
                item.isSettled ? t('Settled', 'আদায়কৃত') : t('Posted', 'পোস্ট করা'));
            td(tr, item.dueAmount === null ? '—' : price(item.dueAmount));
            const actions = td(tr, ''); actions.textContent = '';
            if (item.state === 1) actions.append(button(t('Review', 'অনুমোদন'), () => openReview(item)));
            else if (item.invoiceNumber) {
                const small = document.createElement('small'); small.className = 'text-muted';
                small.textContent = item.invoiceNumber; actions.append(small);
                if (item.state === 3 && Number(item.paidAmount) === 0 &&
                    Number(item.dueAmount) === Number(item.amount)) {
                    actions.append(document.createTextNode(' '));
                    actions.append(button(t('Reverse', 'বাতিল জার্নাল'), () => openReversal(item), true));
                }
            } else actions.textContent = '—';
            rows.append(tr);
        }
        if (!rows.children.length) blank('liabilityRows', t('No assessments.', 'কোনো দায় নির্ধারিত নেই।'));
        liability.pages = Math.max(1, Number(data?.totalPages || 1));
        el('liabilityPage').textContent = liability.page + ' / ' + liability.pages;
        el('liabilityPrevious').disabled = liability.page <= 1;
        el('liabilityNext').disabled = liability.page >= liability.pages;
    } catch (error) {
        if (seq === liability.seq && error.name !== 'AbortError') {
            blank('liabilityRows', t('Could not load.', 'লোড করা যায়নি।')); notify(error.message);
        }
    }
}
function openAssess(item) {
    if (!live()) return;
    pickedIncident = item; requestId = crypto.randomUUID();
    el('assessmentContext').textContent = item.studentName + ' · ' + item.bookTitle +
        ' · ' + item.accessionNumber;
    const amount = item.kind === 3 ? item.calculatedAmount : item.replacementPrice;
    el('assessmentAmount').value = amount;
    el('assessmentAmount').max = amount;
    el('assessmentAmount').readOnly = item.kind !== 2;
    el('assessmentReason').value = item.kind === 3 ? 'Late return: ' + item.overdueDays + ' overdue days' : '';
    el('assessmentDialog').showModal();
}
async function saveAssessment(event) {
    event.preventDefault();
    if (!pickedIncident || !live() || !el('assessmentForm').reportValidity()) return;
    const button = el('assessmentSave'); button.disabled = true;
    try {
        await api('/api/library/liabilities', 'POST', {
            clientRequestId: requestId, issueReference: pickedIncident.issueReference,
            amount: Number(el('assessmentAmount').value), reason: el('assessmentReason').value.trim()
        });
        el('assessmentDialog').close();
        notify(t('Assessment recorded; another administrator must review.',
            'দায় নথিভুক্ত হয়েছে; অন্য প্রশাসক অনুমোদন করবেন।'), true);
        await Promise.all([loadIncidents(), loadLiabilities()]);
    } catch (error) { notify(error.message); }
    finally { button.disabled = false; }
}
async function options(type, searchId, selectId) {
    const seq = ++optionVersions[type];
    const select = el(selectId); select.replaceChildren(new Option(t('Loading…', 'লোড হচ্ছে…'), ''));
    const q = new URLSearchParams({ type, search: el(searchId).value.trim(), page: '1', pageSize: '50' });
    try {
        const result = await api('/api/library/liabilities/posting-options?' + q);
        if (!pickedLiability || seq !== optionVersions[type]) return;
        select.replaceChildren(new Option(t('Select…', 'নির্বাচন করুন…'), ''));
        for (const item of result?.items || []) select.add(new Option(item.label, String(item.id)));
        if (result?.totalCount > (result?.items || []).length)
            select.add(new Option(t('More results: narrow the search', 'আরও ফল: অনুসন্ধান সীমিত করুন'), ''));
    } catch (error) {
        if (seq !== optionVersions[type]) return;
        select.replaceChildren(new Option(t('Unavailable', 'পাওয়া যায়নি'), ''));
        if (error.name !== 'AbortError') notify(error.message);
    }
}
function decisionChanged() {
    const waive = root.querySelector('input[name="reviewDecision"]:checked')?.value === 'waive';
    el('waiverFields').classList.toggle('d-none', !waive);
    el('postingFields').classList.toggle('d-none', waive);
    el('waiverReason').required = waive;
    el('postingPeriod').required = !waive;
    el('postingHead').required = !waive;
}
async function openReview(item) {
    if (!live()) return;
    pickedLiability = item;
    el('reviewContext').textContent = item.studentName + ' · ' + item.bookTitle + ' · ' + price(item.amount) + ' BDT';
    el('reviewForm').reset(); decisionChanged();
    el('reviewDialog').showModal();
    await Promise.all([options('period', 'periodSearch', 'postingPeriod'),
        options('head', 'headSearch', 'postingHead')]);
}
async function saveReview(event) {
    event.preventDefault();
    if (!pickedLiability || !live() || !el('reviewForm').reportValidity()) return;
    const button = el('reviewSave'); button.disabled = true;
    try {
        const waive = root.querySelector('input[name="reviewDecision"]:checked')?.value === 'waive';
        const request = { rowVersion: pickedLiability.rowVersion, waive,
            waiverReason: waive ? el('waiverReason').value.trim() : null,
            accountingPeriodId: waive ? null : Number(el('postingPeriod').value),
            feeHeadId: waive ? null : Number(el('postingHead').value) };
        await api('/api/library/liabilities/' + pickedLiability.reference + '/review', 'POST', request);
        el('reviewDialog').close();
        notify(waive ? t('Assessment waived.', 'দায় মওকুফ হয়েছে।') :
            t('Invoice and journal posted to Finance.', 'ইনভয়েস ও জার্নাল ফাইন্যান্সে পোস্ট হয়েছে।'), true);
        await Promise.all([loadLiabilities(), loadIncidents()]);
    } catch (error) { notify(error.message); }
    finally { button.disabled = false; }
}
async function loadPolicies() {
    const seq = ++policy.seq; blank('policyRows', t('Loading…', 'লোড হচ্ছে…'));
    try {
        const result = await api('/api/library/liabilities/fine-policies?page=' + policy.page + '&pageSize=20');
        if (seq !== policy.seq) return;
        const rows = el('policyRows'); rows.replaceChildren();
        for (const p of result?.items || []) {
            const tr = document.createElement('tr');
            td(tr, p.campusName); td(tr, p.effectiveFrom); td(tr, String(p.graceDays));
            td(tr, price(p.dailyRate)); td(tr, p.maximumCharge === null ? '—' : price(p.maximumCharge));
            rows.append(tr);
        }
        if (!rows.children.length) blank('policyRows', t('No policies yet.', 'কোনো নীতি নেই।'));
        policy.pages = Math.max(1, Number(result?.totalPages || 1));
        el('policyPage').textContent = policy.page + ' / ' + policy.pages;
        el('policyPrevious').disabled = policy.page <= 1;
        el('policyNext').disabled = policy.page >= policy.pages;
    } catch (error) {
        if (seq === policy.seq && error.name !== 'AbortError') {
            blank('policyRows', t('Unable to load.', 'লোড করা যায়নি।')); notify(error.message);
        }
    }
}
async function openPolicy() {
    if (!live()) return;
    policyRequestId = crypto.randomUUID(); el('policyForm').reset();
    el('policyDialog').showModal();
    el('policyCampusSearch').value = '';
    await loadPolicyCampuses();
}
async function loadPolicyCampuses() {
    const seq = ++optionVersions.campus;
    const picker = el('policyCampus');
    picker.replaceChildren(new Option(t('Loading…', 'লোড হচ্ছে…'), ''));
    try {
        const q = new URLSearchParams({ type: 'campus', search: el('policyCampusSearch').value.trim(),
            page: '1', pageSize: '50' });
        const result = await api('/api/library/liabilities/posting-options?' + q);
        if (seq !== optionVersions.campus || !el('policyDialog').open) return;
        picker.replaceChildren(new Option(t('Select…', 'নির্বাচন করুন…'), ''));
        for (const item of result?.items || []) picker.add(new Option(item.label, String(item.id)));
        if (result?.totalCount > (result?.items || []).length)
            picker.add(new Option(t('More results: search by campus code', 'আরও ক্যাম্পাস: কোড দিয়ে খুঁজুন'), ''));
    } catch (error) { if (error.name !== 'AbortError') notify(error.message); }
}
async function savePolicy(event) {
    event.preventDefault();
    if (!policyRequestId || !live() || !el('policyForm').reportValidity()) return;
    const button = el('policySave'); button.disabled = true;
    try {
        await api('/api/library/liabilities/fine-policies', 'POST', {
            clientRequestId: policyRequestId, campusId: Number(el('policyCampus').value),
            effectiveFrom: el('policyDate').value, graceDays: Number(el('policyGrace').value),
            dailyRate: Number(el('policyRate').value),
            maximumCharge: el('policyMax').value ? Number(el('policyMax').value) : null
        });
        el('policyDialog').close();
        notify(t('Policy revision recorded.', 'নতুন নীতি সংরক্ষিত হয়েছে।'), true);
        policy.page = 1; await Promise.all([loadPolicies(), loadIncidents()]);
    } catch (error) { notify(error.message); }
    finally { button.disabled = false; }
}
function reversalDecisionChanged() {
    const reject = root.querySelector('input[name="reversalDecision"]:checked')?.value === 'reject';
    el('reversalRejection').required = reject;
}
async function openReversal(item) {
    if (!live()) return;
    pickedLiability = item; reversalRecord = null; reversalRequestId = crypto.randomUUID();
    el('reversalForm').reset(); el('reversalContext').textContent =
        item.studentName + ' · ' + item.invoiceNumber + ' · ' + price(item.amount) + ' BDT';
    el('reversalDialog').showModal();
    try {
        reversalRecord = await api('/api/library/liabilities/' + item.reference + '/reversal');
        if (!el('reversalDialog').open || !pickedLiability) return;
        const pending = reversalRecord?.reference && reversalRecord.reference !== '00000000-0000-0000-0000-000000000000' &&
            reversalRecord.state === 2;
        el('reversalRequestFields').classList.toggle('d-none', pending);
        el('reversalReviewFields').classList.toggle('d-none', !pending);
        for (const id of ['reversalDate', 'reversalPeriod', 'reversalReason']) el(id).required = !pending;
        if (pending) {
            el('reversalJournal').textContent = reversalRecord.journalNumber;
            reversalDecisionChanged();
        } else {
            el('reversalRejection').required = false;
            const periods = await api('/api/library/liabilities/posting-options?type=period&page=1&pageSize=50');
            const picker = el('reversalPeriod'); picker.replaceChildren(new Option(t('Select…', 'নির্বাচন করুন…'), ''));
            for (const x of periods?.items || []) picker.add(new Option(x.label, String(x.id)));
            if (periods?.totalCount > (periods?.items || []).length)
                picker.add(new Option(t('Narrow via accounting setup', 'হিসাবকালে খুঁজুন'), ''));
        }
    } catch (error) { notify(error.message); el('reversalDialog').close(); }
}
async function saveReversal(event) {
    event.preventDefault();
    if (!pickedLiability || !live() || !el('reversalForm').reportValidity()) return;
    const button = el('reversalSave'); button.disabled = true;
    try {
        const existing = reversalRecord?.reference && reversalRecord.state === 2;
        if (existing) {
            const approve = root.querySelector('input[name="reversalDecision"]:checked')?.value === 'approve';
            await api('/api/library/liabilities/' + pickedLiability.reference + '/reversal/' +
                reversalRecord.reference + '/review', 'POST', {
                rowVersion: reversalRecord.rowVersion, approve,
                rejectionReason: approve ? null : el('reversalRejection').value.trim()
            });
        } else {
            await api('/api/library/liabilities/' + pickedLiability.reference + '/reversal', 'POST', {
                clientRequestId: reversalRequestId, rowVersion: pickedLiability.rowVersion,
                reversalDate: el('reversalDate').value,
                accountingPeriodId: Number(el('reversalPeriod').value), reason: el('reversalReason').value.trim()
            });
        }
        el('reversalDialog').close();
        notify(t('Reversal workflow updated.', 'বাতিল জার্নালের কার্যক্রম হালনাগাদ হয়েছে।'), true);
        await loadLiabilities();
    } catch (error) { notify(error.message); }
    finally { button.disabled = false; }
}
function reset() {
    incident.seq++; liability.seq++; policy.seq++;
    optionVersions.period++; optionVersions.head++; optionVersions.campus++;
    for (const timer of Object.values(optionTimers)) clearTimeout(timer);
    for (const controller of active) controller.abort(); active.clear();
    pickedIncident = null; pickedLiability = null; requestId = null;
    reversalRecord = null; reversalRequestId = null; policyRequestId = null;
    blank('incidentRows', ''); blank('liabilityRows', ''); el('liabilityAlert').className = 'd-none';
    for (const id of ['postingPeriod', 'postingHead']) el(id).replaceChildren();
    el('reviewContext').textContent = ''; el('assessmentContext').textContent = '';
    for (const id of ['assessmentDialog', 'reviewDialog', 'policyDialog', 'reversalDialog'])
        if (el(id).open) el(id).close();
}
el('incidentFilters').addEventListener('submit', e => { e.preventDefault(); incident.page = 1; loadIncidents(); });
el('incidentKind').addEventListener('change', () => { incident.page = 1; loadIncidents(); });
el('liabilityFilters').addEventListener('submit', e => { e.preventDefault(); liability.page = 1; loadLiabilities(); });
el('liabilityExport').addEventListener('click', exportPage);
for (const [prefix, model, loader] of [['incident', incident, loadIncidents], ['liability', liability, loadLiabilities]]) {
    el(prefix + 'Previous').addEventListener('click', () => { if (model.page > 1) { model.page--; loader(); } });
    el(prefix + 'Next').addEventListener('click', () => { if (model.page < model.pages) { model.page++; loader(); } });
}
el('assessmentForm').addEventListener('submit', saveAssessment);
el('policyOpen').addEventListener('click', openPolicy);
el('policyForm').addEventListener('submit', savePolicy);
el('policyCampusSearch').addEventListener('input', () => {
    clearTimeout(optionTimers.campus);
    optionTimers.campus = setTimeout(() => { if (el('policyDialog').open) loadPolicyCampuses(); }, 300);
});
el('policyCancel').addEventListener('click', () => el('policyDialog').close());
el('policyDialog').addEventListener('close', () => { policyRequestId = null; });
el('policyPrevious').addEventListener('click', () => { if (policy.page > 1) { policy.page--; loadPolicies(); } });
el('policyNext').addEventListener('click', () => { if (policy.page < policy.pages) { policy.page++; loadPolicies(); } });
el('reversalForm').addEventListener('submit', saveReversal);
el('reversalCancel').addEventListener('click', () => el('reversalDialog').close());
el('reversalDialog').addEventListener('close', () => {
    reversalRecord = null; reversalRequestId = null; pickedLiability = null;
});
root.querySelectorAll('input[name="reversalDecision"]').forEach(node =>
    node.addEventListener('change', reversalDecisionChanged));
el('assessmentCancel').addEventListener('click', () => el('assessmentDialog').close());
el('reviewForm').addEventListener('submit', saveReview);
el('reviewCancel').addEventListener('click', () => el('reviewDialog').close());
el('assessmentDialog').addEventListener('close', () => { pickedIncident = null; requestId = null; });
el('reviewDialog').addEventListener('close', () => { pickedLiability = null; });
root.querySelectorAll('input[name="reviewDecision"]').forEach(node => node.addEventListener('change', decisionChanged));
for (const [type, searchId, selectId] of [['period', 'periodSearch', 'postingPeriod'], ['head', 'headSearch', 'postingHead']]) {
    el(searchId).addEventListener('input', () => {
        clearTimeout(optionTimers[type]);
        optionTimers[type] = setTimeout(() => { if (pickedLiability) options(type, searchId, selectId); }, 300);
    });
}
const observer = new MutationObserver(() => { if (!live()) { reset(); observer.disconnect(); } });
observer.observe(document.body, { attributes: true,
    attributeFilter: ['data-auth-session-reference', 'data-auth-tenant-id'] });
window.addEventListener('pagehide', reset);
window.addEventListener('focus', () => { if (!live()) reset(); });
if (live()) Promise.all([loadIncidents(), loadLiabilities(), loadPolicies()]); else reset();
})();
