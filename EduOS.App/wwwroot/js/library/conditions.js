(() => {
'use strict';
const root = document.getElementById('libraryConditionsApp');
if (!root) return;
const el = id => document.getElementById(id);
const bn = document.documentElement.lang?.toLowerCase().startsWith('bn');
const words = (en, bd) => bn ? bd : en;
const admin = root.dataset.canRetire === 'true';
const initialSession = document.body?.dataset.authSessionReference || '';
const initialTenant = document.body?.dataset.authTenantId || '';
const currentSession = () => /^[0-9A-F]{64}$/.test(initialSession) &&
    document.body?.dataset.authSessionReference === initialSession &&
    document.body?.dataset.authTenantId === initialTenant &&
    window.EduOSSessionTransitionPending?.() !== true;
let page = 1, pages = 1, sequence = 0, selected = null, requestId = null;
const active = new Set();
const stateNames = { 1: words('Available', 'উপলব্ধ'), 2: words('Issued', 'ইস্যুকৃত'),
    3: words('Reserved', 'সংরক্ষিত'), 4: words('Lost', 'হারানো'),
    5: words('Damaged', 'ক্ষতিগ্রস্ত'), 6: words('Retired', 'অবসৃত') };
function alertMessage(message, success = false) {
    const node = el('conditionAlert');
    node.className = 'alert alert-' + (success ? 'success' : 'danger');
    node.textContent = message;
}
function clearAlert() { el('conditionAlert').className = 'd-none'; el('conditionAlert').textContent = ''; }
function cell(tr, value) {
    const td = document.createElement('td');
    td.textContent = value === null || value === undefined || value === '' ? '—' : String(value);
    tr.append(td); return td;
}
function empty(message) {
    const tbody = el('conditionRows'); tbody.replaceChildren();
    const tr = document.createElement('tr'); cell(tr, message).colSpan = 7; tbody.append(tr);
}
function query() {
    return new URLSearchParams({ accession: el('conditionAccession').value.trim(),
        state: el('conditionState').value, page: String(page), pageSize: '20' });
}
async function api(url, options = {}) {
    if (!currentSession()) throw Error(words('Session changed. Reload.', 'সেশন পরিবর্তিত হয়েছে। রিলোড করুন।'));
    const controller = new AbortController(); active.add(controller);
    try {
        const headers = { Accept: 'application/json', ...options.headers };
        if (options.method === 'POST') {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw Error(words('Security token missing.', 'নিরাপত্তা টোকেন পাওয়া যায়নি।'));
            headers.RequestVerificationToken = token;
            headers['Content-Type'] = 'application/json';
        }
        const response = await fetch(url, { ...options, headers, signal: controller.signal,
            credentials: 'same-origin', cache: 'no-store' });
        if (!currentSession()) throw Error('Session changed.');
        if (options.raw) {
            if (!response.ok) throw Error('Export unavailable.');
            return response.blob();
        }
        const payload = await response.json();
        if (!response.ok || !payload?.success) throw Error(payload?.message || 'Request failed.');
        return payload.data;
    } finally { active.delete(controller); }
}
function choices(item) {
    if (item.openIssueCount > 1) return [];
    if (item.state === 2) return [4, 5];
    if (item.state === 1) return admin ? [4, 5, 6] : [4, 5];
    if (item.state === 4 || item.state === 5) return admin ? [1, 6] : [];
    return [];
}
function openEditor(item) {
    if (!currentSession()) return;
    selected = item; requestId = crypto.randomUUID();
    el('conditionSelected').textContent = item.accessionNumber + ' — ' + item.bookTitle;
    const select = el('conditionTarget'); select.replaceChildren();
    for (const state of choices(item)) select.add(new Option(stateNames[state], String(state)));
    el('conditionReason').value = '';
    el('conditionDialog').showModal();
    select.focus();
}
async function history(item) {
    try {
        const data = await api('/api/library/copies/' + item.copyId + '/condition-history?page=1&pageSize=50');
        const box = el('conditionHistoryRows'); box.replaceChildren();
        for (const entry of data?.items || []) {
            const p = document.createElement('p');
            p.textContent = new Date(entry.occurredAtUtc).toLocaleString() + ' · ' +
                entry.previousState + ' → ' + entry.newState + ' · ' + entry.reason;
            box.append(p);
        }
        if (!box.children.length) box.textContent = words('No recorded transitions.', 'কোনো পরিবর্তনের ইতিহাস নেই।');
        el('conditionHistoryDialog').showModal();
    } catch (e) { alertMessage(e.message); }
}
async function load() {
    const seq = ++sequence;
    empty(words('Loading…', 'লোড হচ্ছে…'));
    try {
        const data = await api('/api/library/copies/conditions?' + query());
        if (seq !== sequence) return;
        const tbody = el('conditionRows'); tbody.replaceChildren();
        for (const item of data?.items || []) {
            const tr = document.createElement('tr');
            [item.accessionNumber, item.bookTitle, item.campusId, item.branchCode,
                stateNames[item.state] || item.state, item.openIssueCount].forEach(value => cell(tr, value));
            const actions = document.createElement('td');
            const allowed = choices(item);
            if (allowed.length) {
                const edit = document.createElement('button');
                edit.type = 'button'; edit.className = 'btn btn-sm btn-outline-primary me-2';
                edit.textContent = words('Change', 'পরিবর্তন');
                edit.addEventListener('click', () => openEditor(item)); actions.append(edit);
            }
            const audit = document.createElement('button');
            audit.type = 'button'; audit.className = 'btn btn-sm btn-outline-secondary';
            audit.textContent = words('History', 'ইতিহাস');
            audit.addEventListener('click', () => history(item)); actions.append(audit);
            tr.append(actions); tbody.append(tr);
        }
        if (!tbody.children.length) empty(words('No copies found.', 'কোনো কপি পাওয়া যায়নি।'));
        pages = Math.max(1, Number(data?.totalPages || 1));
        el('conditionCount').textContent = String(data?.totalCount || 0);
        el('conditionPage').textContent = page + ' / ' + pages;
        el('conditionPrevious').disabled = page <= 1;
        el('conditionNext').disabled = page >= pages;
        clearAlert();
    } catch (e) {
        if (seq === sequence) { empty(words('Unable to load copies.', 'কপি লোড করা যায়নি।')); alertMessage(e.message); }
    }
}
async function save(event) {
    event.preventDefault();
    if (!selected || !currentSession() || !el('conditionForm').reportValidity()) return;
    const button = el('conditionSave'); button.disabled = true;
    try {
        const data = await api('/api/library/copies/' + selected.copyId + '/condition', {
            method: 'POST', body: JSON.stringify({
                clientRequestId: requestId, targetState: Number(el('conditionTarget').value),
                reason: el('conditionReason').value.trim(), rowVersion: selected.rowVersion,
                openIssueRowVersion: selected.openIssueRowVersion
            })
        });
        if (!data) throw Error('No response.');
        el('conditionDialog').close(); selected = null; requestId = null;
        await load();
        alertMessage(words('Condition recorded.', 'অবস্থা সংরক্ষিত হয়েছে।'), true);
    } catch (e) { alertMessage(e.message); }
    finally { button.disabled = false; }
}
async function exportPage() {
    const button = el('conditionExport'); button.disabled = true;
    try {
        const params = query();
        params.set('language', bn ? 'bn-BD' : 'en-BD');
        const blob = await api('/api/library/reports/copy-conditions/export?' + params, { raw: true });
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url; link.download = 'library-copy-conditions.csv';
        document.body.append(link); link.click(); link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (e) { alertMessage(e.message); }
    finally { button.disabled = false; }
}
function clearSensitive() {
    sequence++;
    for (const controller of active) controller.abort();
    active.clear(); selected = null; requestId = null;
    empty(''); el('conditionCount').textContent = ''; el('conditionPage').textContent = '';
    el('conditionForm').reset(); clearAlert();
    if (el('conditionDialog').open) el('conditionDialog').close();
    if (el('conditionHistoryDialog').open) el('conditionHistoryDialog').close();
    el('conditionHistoryRows').replaceChildren();
}
el('conditionFilters').addEventListener('submit', e => { e.preventDefault(); page = 1; load(); });
el('conditionPrevious').addEventListener('click', () => { if (page > 1) { page--; load(); } });
el('conditionNext').addEventListener('click', () => { if (page < pages) { page++; load(); } });
el('conditionExport').addEventListener('click', exportPage);
el('conditionForm').addEventListener('submit', save);
el('conditionCancel').addEventListener('click', () => el('conditionDialog').close());
el('conditionHistoryClose').addEventListener('click', () => el('conditionHistoryDialog').close());
el('conditionDialog').addEventListener('close', () => { selected = null; requestId = null; });
const observer = new MutationObserver(() => { if (!currentSession()) { clearSensitive(); observer.disconnect(); } });
observer.observe(document.body, { attributes: true,
    attributeFilter: ['data-auth-session-reference', 'data-auth-tenant-id'] });
window.addEventListener('pagehide', clearSensitive);
window.addEventListener('focus', () => { if (!currentSession()) clearSensitive(); });
if (currentSession()) load(); else clearSensitive();
})();
