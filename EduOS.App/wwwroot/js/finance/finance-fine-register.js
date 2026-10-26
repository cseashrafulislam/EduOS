(() => {
'use strict';
const root = document.getElementById('fineRegisterRoot');
if (!root) return;
const el = id => document.getElementById(id);
const token = document.querySelector('meta[name="request-verification-token"]')?.content;
let page = 1, total = 0, loading = false;
const size = 25;
const cell = value => {
    const td = document.createElement('td');
    td.textContent = value == null ? '—' : String(value);
    return td;
};
const money = value => Number(value || 0).toLocaleString('en-BD', {
    minimumFractionDigits: 2, maximumFractionDigits: 2
});
function notice(message, error = false) {
    const box = el('fineRegisterAlert');
    box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
    box.textContent = message;
}
function filters() {
    const campus = el('fineRegisterCampus').value.trim();
    const from = el('fineRegisterFrom').value, to = el('fineRegisterTo').value;
    if (campus && (!/^\d+$/.test(campus) || Number(campus) <= 0))
        throw Error('Campus ID must be a positive integer.');
    if (from && to && from > to) throw Error('Start date cannot be after end date.');
    const q = new URLSearchParams({page: String(page), pageSize: String(size)});
    if (campus) q.set('campusId', campus);
    if (from) q.set('from', from);
    if (to) q.set('to', to);
    const waived = el('fineRegisterStatus').value;
    if (waived) q.set('waived', waived);
    return q;
}
async function api(url, method = 'GET', body) {
    const headers = {Accept: 'application/json'};
    if (method !== 'GET') {
        if (!token) throw Error('Antiforgery token missing; reload the page.');
        headers.RequestVerificationToken = token;
        headers['Content-Type'] = 'application/json';
    }
    const response = await fetch(url, {method, headers, credentials: 'same-origin',
        cache: 'no-store', ...(body === undefined ? {} : {body: JSON.stringify(body)})});
    const result = await response.json().catch(() => null);
    if (!response.ok || !result?.success) throw Error(result?.message || 'Fine register request failed.');
    return result.data;
}
function inspect(row) {
    const select = el('adjustEnrollment');
    if (!select) return notice('Enrollment selector unavailable.', true);
    if (![...select.options].some(option => option.value === row.enrollmentReference))
        select.add(new Option(row.studentCode + ' — ' + row.studentName, row.enrollmentReference));
    select.value = row.enrollmentReference;
    select.dispatchEvent(new Event('change', {bubbles: true}));
    select.scrollIntoView({behavior: 'smooth', block: 'center'});
}
async function load() {
    if (loading) return;
    loading = true;
    el('fineRegisterLoad').disabled = true;
    try {
        const data = await api('/api/finance/adjustments/fines/register?' + filters());
        total = data.fines.totalCount;
        el('fineRegisterRecorded').textContent = money(data.recordedTotal);
        el('fineRegisterWaived').textContent = money(data.waivedTotal);
        el('fineRegisterOpen').textContent = money(data.outstandingAdministrativeTotal);
        const body = el('fineRegisterRows');
        body.replaceChildren();
        for (const row of data.fines.items || []) {
            const tr = document.createElement('tr');
            [row.fineDate, row.studentCode + ' — ' + row.studentName, row.campusId,
                row.ruleName, money(row.amount), row.isWaived ? 'Waived' : 'Unposted',
                row.reason || '—'].forEach(value => tr.append(cell(value)));
            const action = cell('');
            const button = document.createElement('button');
            button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary';
            button.textContent = row.isWaived ? 'Inspect' : 'Inspect / waive';
            button.addEventListener('click', () => inspect(row));
            action.append(button); tr.append(action); body.append(tr);
        }
        if (!data.fines.items?.length) {
            const tr = document.createElement('tr'), td = cell('No fine records match these filters.');
            td.colSpan = 8; tr.append(td); body.append(tr);
        }
        const pages = Math.max(1, Math.ceil(total / size));
        el('fineRegisterPage').textContent = 'Page ' + page + ' / ' + pages + ' · ' + total + ' records';
        el('fineRegisterPrev').disabled = page <= 1;
        el('fineRegisterNext').disabled = page >= pages;
        notice('Fine register loaded. These amounts are not posted revenue.');
    } catch (error) { notice(error.message, true); }
    finally { loading = false; el('fineRegisterLoad').disabled = false; }
}
el('fineRegisterLoad').addEventListener('click', () => {page = 1; load();});
el('fineRegisterPrev').addEventListener('click', () => {if (page > 1) {page--; load();}});
el('fineRegisterNext').addEventListener('click', () => {if (page * size < total) {page++; load();}});
load();
})();
