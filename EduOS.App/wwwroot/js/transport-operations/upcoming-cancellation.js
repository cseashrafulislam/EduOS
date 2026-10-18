(() => {
'use strict';
const el = id => document.getElementById(id);
if (!el('transportUpcomingCancelRows')) return;
const pageSize = 25;
let page = 1, pages = 1, serial = 0, cancelling = false;
function tomorrow() {
    const d = new Date(); d.setDate(d.getDate() + 1);
    return [d.getFullYear(), String(d.getMonth() + 1).padStart(2, '0'),
        String(d.getDate()).padStart(2, '0')].join('-');
}
function notice(message, success = false) {
    const node = el('transportAlert');
    if (!node) return;
    node.className = 'alert alert-' + (success ? 'success' : 'danger');
    node.textContent = message;
    node.focus();
}
async function api(url, method = 'GET', body) {
    const headers = { Accept: 'application/json' };
    if (body !== undefined) {
        const token = document.querySelector('meta[name="request-verification-token"]')?.content;
        if (!token) throw new Error('Security token missing. Reload the page.');
        headers['Content-Type'] = 'application/json';
        headers.RequestVerificationToken = token;
    }
    const response = await fetch(url, { method, headers, credentials: 'same-origin',
        cache: 'no-store', ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
    const result = await response.json().catch(() => null);
    if (!response.ok || !result?.success)
        throw new Error(result?.message || 'Transport cancellation request failed.');
    return result.data;
}
function cell(value) {
    const td = document.createElement('td');
    td.textContent = String(value ?? '—');
    return td;
}
async function cancel(item, button) {
    if (cancelling || !item || item.startDate < tomorrow()) return;
    if (!item.rowVersion) { notice('Missing concurrency token. Reload reservations.'); return; }
    if (!confirm('Cancel the future transport reservation for ' + item.studentName +
        '? This releases the seat and does not change payment records.')) return;
    cancelling = true; button.disabled = true;
    try {
        await api('/api/transport/assignments/' + encodeURIComponent(item.reference) + '/cancel',
            'POST', { rowVersion: item.rowVersion });
        page = 1;
        window.dispatchEvent(new Event('transport:refresh-assignments'));
        notice('Upcoming transport reservation cancelled.', true);
    } catch (error) { notice(error.message); }
    finally { cancelling = false; button.disabled = false; }
}
async function load() {
    const request = ++serial;
    const query = new URLSearchParams({
        page: String(page), pageSize: String(pageSize),
        fromDate: tomorrow(), state: 'Active'
    });
    const search = el('transportUpcomingCancelTerm').value.trim();
    if (search) query.set('search', search);
    const rows = el('transportUpcomingCancelRows');
    rows.replaceChildren();
    el('transportUpcomingCancelPageInfo').textContent = 'Loading upcoming reservations…';
    el('transportUpcomingCancelPrev').disabled = true;
    el('transportUpcomingCancelNext').disabled = true;
    try {
        const data = await api('/api/transport/assignment-history?' + query);
        if (request !== serial) return;
        const items = Array.isArray(data?.items) ? data.items : [];
        pages = Math.max(1, Math.ceil((Number(data?.totalCount) || 0) / pageSize));
        for (const item of items) {
            if (!item.startDate || item.startDate < tomorrow()) continue;
            const tr = document.createElement('tr');
            for (const value of [item.studentName, item.campusName, item.routeName,
                item.vehicleNumber, item.startDate]) tr.append(cell(value));
            const action = document.createElement('td');
            const button = document.createElement('button');
            button.type = 'button'; button.className = 'btn btn-outline-danger btn-sm';
            button.textContent = 'Cancel reservation';
            button.addEventListener('click', () => cancel(item, button));
            action.append(button); tr.append(action); rows.append(tr);
        }
        if (!rows.childElementCount) {
            const tr = document.createElement('tr'), td = cell('No upcoming reservations found.');
            td.colSpan = 6; tr.append(td); rows.append(tr);
        }
        el('transportUpcomingCancelPageInfo').textContent = 'Page ' + page + ' of ' + pages +
            ' · ' + (Number(data?.totalCount) || 0) + ' reservations';
        el('transportUpcomingCancelPrev').disabled = page <= 1;
        el('transportUpcomingCancelNext').disabled = page >= pages;
    } catch (error) {
        if (request !== serial) return;
        rows.replaceChildren();
        el('transportUpcomingCancelPageInfo').textContent = 'Unable to load reservations.';
        notice(error.message);
    }
}
el('transportUpcomingCancelSearch').addEventListener('submit', event => {
    event.preventDefault(); page = 1; load();
});
el('transportUpcomingCancelPrev').addEventListener('click', () => {
    if (page <= 1 || cancelling) return;
    page--; load();
});
el('transportUpcomingCancelNext').addEventListener('click', () => {
    if (page >= pages || cancelling) return;
    page++; load();
});
window.addEventListener('transport:refresh-assignments', () => { page = 1; load(); });
load();
})();
