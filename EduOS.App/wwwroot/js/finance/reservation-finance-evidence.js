(() => {
'use strict';
const root = document.getElementById('reservationFinanceRoot');
const panel = document.getElementById('reservationFinanceEvidencePanel');
if (!root || !panel) return;
const source = document.getElementById('reservationFinanceEvidenceSource');
const rows = document.getElementById('reservationFinanceEvidenceRows');
const alert = document.getElementById('reservationFinanceEvidenceAlert');
const pageLabel = document.getElementById('reservationFinanceEvidencePage');
const prev = document.getElementById('reservationFinanceEvidencePrev');
const next = document.getElementById('reservationFinanceEvidenceNext');
let scope = null, page = 1, total = 0, sequence = 0;
const pageSize = 25;
function message(value, error) {
    alert.className = 'alert alert-' + (error ? 'danger' : 'info');
    alert.textContent = value;
}
function cell(tr, value) {
    const td = document.createElement('td');
    td.textContent = value == null ? '—' : String(value);
    tr.appendChild(td);
}
function reset() {
    ++sequence;
    scope = null; page = 1; total = 0;
    rows.replaceChildren();
    panel.classList.add('d-none');
    prev.disabled = true; next.disabled = true;
    pageLabel.textContent = 'Not loaded';
}
function render(data) {
    rows.replaceChildren();
    const result = data.evidence;
    total = result.totalCount;
    for (const item of result.items) {
        const tr = document.createElement('tr');
        cell(tr, item.reference);
        cell(tr, item.activityDate);
        cell(tr, item.state);
        cell(tr, item.amount);
        cell(tr, item.paidAmount);
        cell(tr, item.dueAmount);
        cell(tr, item.journalId);
        cell(tr, item.hasPostedActivity ? 'Posted' :
            item.isUnpostedIssuedInvoice ? 'Issued, not posted' : 'No posting detected');
        rows.appendChild(tr);
    }
    if (!result.items.length) {
        const tr = document.createElement('tr');
        const td = document.createElement('td');
        td.colSpan = 8;
        td.textContent = 'No records in this enrollment/student evidence scope.';
        tr.appendChild(td); rows.appendChild(tr);
    }
    pageLabel.textContent = 'Page ' + page + ' · ' + total + ' records';
    prev.disabled = page <= 1;
    next.disabled = page * pageSize >= total;
}
async function load() {
    if (!scope) return;
    const token = ++sequence;
    const params = new URLSearchParams({
        kind: scope.kind, reservationId: String(scope.id),
        source: source.value, page: String(page), pageSize: String(pageSize)
    });
    prev.disabled = true; next.disabled = true;
    try {
        const response = await fetch('/api/finance/adjustments/reservations/evidence?' + params,
            {credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}});
        const body = await response.json().catch(() => null);
        if (token !== sequence) return;
        if (!response.ok || !body?.success || !body.data?.evidence)
            throw Error(body?.message || 'Unable to load financial evidence.');
        render(body.data);
        message(body.data.notice || 'Read-only evidence loaded.', false);
    } catch (error) {
        if (token !== sequence) return;
        rows.replaceChildren();
        total = 0; prev.disabled = true; next.disabled = true;
        pageLabel.textContent = 'Not loaded';
        message(error.message || 'Unable to load financial evidence.', true);
    }
}
root.addEventListener('reservationFinancePreflightReset', reset);
root.addEventListener('reservationFinancePreflightLoaded', event => {
    reset();
    const data = event.detail;
    scope = {kind:data.kind, id:data.reservationId};
    panel.classList.remove('d-none');
    load();
});
source.addEventListener('change', () => { page = 1; load(); });
prev.addEventListener('click', () => { if (page > 1) { --page; load(); } });
next.addEventListener('click', () => { if (page * pageSize < total) { ++page; load(); } });
})();
