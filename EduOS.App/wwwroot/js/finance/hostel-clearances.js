(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('hostelFinanceRoot')) return;
    const apiRoot = '/api/finance/adjustments/hostel';
    const stays = new Map(), beds = new Map();
    let stayPage = 1, clearancePage = 1, clearancePages = 1, policyVersion = null;
    let selected = null, review = null, lastPayload = null, lastRequestId = null;
    let working = false;
    const today = () => {
        const d = new Date();
        return [d.getFullYear(), String(d.getMonth() + 1).padStart(2, '0'),
            String(d.getDate()).padStart(2, '0')].join('-');
    };
    function notice(message, ok = false) {
        const node = el('hfAlert'); node.className = 'alert alert-' + (ok ? 'success' : 'danger');
        node.textContent = message; node.focus();
    }
    async function api(path, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        if (body !== undefined) {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw Error('Security token unavailable. Reload page.');
            headers.RequestVerificationToken = token;
            headers['Content-Type'] = 'application/json';
        }
        const response = await fetch(path, { method, cache: 'no-store', credentials: 'same-origin', headers,
            ...(body !== undefined ? { body: JSON.stringify(body) } : {}) });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success) throw Error(result?.message || 'Finance request failed.');
        return result.data;
    }
    const numeric = id => { const s = el(id).value.trim(); return s ? Number(s) : null; };
    const signed = id => { const s = el(id).value.trim();
        if (s && !/^-?\d{1,12}(\.\d{1,2})?$/.test(s)) throw Error('Signed adjustment must have at most 2 decimal places.');
        return s || null; };
    const money = value => Number(value || 0).toFixed(2);
    function option(select, label, value) { select.append(new Option(label, value)); }
    async function loadPolicy() {
        const campusId = numeric('hfCampus');
        if (!Number.isSafeInteger(campusId) || campusId <= 0) throw Error('Choose a valid campus ID.');
        const data = await api(`${apiRoot}/billing-policy/${campusId}`);
        el('hfMode').value = String(data.calculationMode);
        el('hfOverrideAllowed').checked = data.allowManualOverride;
        policyVersion = data.rowVersion;
    }
    async function savePolicy(event) {
        event.preventDefault();
        const campusId = numeric('hfCampus');
        if (!Number.isSafeInteger(campusId) || campusId <= 0) throw Error('Campus ID is required.');
        const data = await api(`${apiRoot}/billing-policy`, 'PUT', {
            campusId, calculationMode: Number(el('hfMode').value),
            allowManualOverride: el('hfOverrideAllowed').checked, rowVersion: policyVersion
        });
        policyVersion = data.rowVersion;
        notice('Campus billing policy saved. Existing clearances keep their original calculation snapshot.', true);
    }
    async function loadStays() {
        const data = await api('/api/hostel/active-allocations?page=' + stayPage + '&pageSize=50');
        const select = el('hfSource');
        if (stayPage === 1) { stays.clear(); select.replaceChildren(new Option('Select active stay', '')); }
        for (const item of data.items || []) {
            stays.set(String(item.id), item);
            option(select, `#${item.id} · ${item.studentName} · ${item.hostelName} ${item.roomNumber}/${item.bedNumber}`, String(item.id));
        }
        stayPage = stayPage < Number(data.totalPages || 1) ? stayPage + 1 : 1;
        el('hfLoadStays').textContent = stayPage === 1 ? 'Reload first 50 stays' : 'Load next 50 stays';
    }
    async function chooseStay() {
        selected = stays.get(el('hfSource').value) || null;
        beds.clear(); el('hfTarget').replaceChildren(new Option('Not selected', ''));
        if (!selected) return;
        const preflight = await api(`/api/finance/adjustments/reservations/preflight?kind=hostel&id=${selected.id}`);
        el('hfCampus').value = preflight.campusId;
        await loadPolicy();
        el('hfRequestInfo').textContent = `Stay ${selected.id} · Rent ${money(preflight.monthlyCharge)} BDT · Deposit ${money(preflight.securityDeposit)} BDT. Check student Finance records before approval.`;
        await updateTarget();
    }
    async function updateTarget() {
        const transfer = el('hfOperation').value === '1';
        const period = el('hfOperation').value === '3';
        el('hfManualRent').disabled = period; el('hfManualDeposit').disabled = period;
        el('hfManualNet').disabled = !period;
        const box = el('hfTarget'); box.disabled = !transfer;
        beds.clear(); box.replaceChildren(new Option(transfer ? 'Select destination' : 'Not needed', ''));
        if (!transfer || !selected) return;
        const data = await api(`/api/hostel/allocations/${selected.id}/occupied-transfer-beds?page=1&pageSize=50`);
        for (const bed of data.items || []) {
            beds.set(String(bed.id), bed);
            option(box, `${bed.hostelName} · ${bed.roomNumber}/${bed.bedNumber}`, String(bed.id));
        }
        if (Number(data.totalPages) > 1) el('hfRequestInfo').textContent += ' Only first 50 available beds shown; narrow availability in Hostel Operations if needed.';
    }
    async function requestClearance(event) {
        event.preventDefault();
        if (!selected) throw Error('Choose an active stay.');
        const transfer = el('hfOperation').value === '1';
        const target = beds.get(el('hfTarget').value);
        if (transfer && !target) throw Error('Choose a valid destination bed.');
        const input = {
            sourceAllocationId: selected.id, operation: Number(el('hfOperation').value),
            destinationBedId: transfer ? target.id : null,
            destinationBedRowVersion: transfer ? target.rowVersion : null,
            effectiveDate: el('hfEffective').value,
            sourceRowVersion: selected.rowVersion, reason: el('hfReason').value.trim(),
            manualRentCredit: transfer || el('hfOperation').value === '2' ? numeric('hfManualRent') : null,
            manualDepositRefund: transfer || el('hfOperation').value === '2' ? numeric('hfManualDeposit') : null,
            manualNetAdjustment: el('hfOperation').value === '3' ? signed('hfManualNet') : null,
            overrideReason: el('hfOverrideReason').value.trim() || null
        };
        const fingerprint = JSON.stringify(input);
        if (lastPayload !== fingerprint) { lastPayload = fingerprint; lastRequestId = crypto.randomUUID(); }
        const result = await api(`${apiRoot}/clearances`, 'POST', { clientRequestId: lastRequestId, ...input });
        notice(result.operation === 3
            ? `Clearance #${result.id} requested. Selected signed period adjustment ${money(result.approvedNetAdjustment)} BDT. Awaiting independent Finance approval.`
            : `Clearance #${result.id} requested. Proposed rent credit ${money(result.suggestedRentCredit)} BDT; deposit refund ${money(result.suggestedDepositRefund)} BDT. Awaiting independent Finance approval.`, true);
        clearancePage = 1; await loadClearances();
    }
    function td(value) { const node = document.createElement('td'); node.textContent = value == null ? '' : String(value); return node; }
    async function loadClearances() {
        const data = await api(`${apiRoot}/clearances?page=${clearancePage}&pageSize=25`);
        clearancePages = Math.max(1, Number(data.totalPages) || 1);
        el('hfPageInfo').textContent = `Page ${clearancePage} of ${clearancePages} · ${data.totalCount}`;
        el('hfPrevious').disabled = clearancePage <= 1;
        el('hfNext').disabled = clearancePage >= clearancePages;
        const body = el('hfRows'); body.replaceChildren();
        for (const entry of data.items || []) {
            const tr = document.createElement('tr');
            [ `#${entry.id} / ${entry.sourceAllocationId}`, `${entry.operation === 1 ? 'Transfer → bed ' + entry.destinationBedId : entry.operation === 3 ? 'Upcoming reschedule' : 'Checkout'} / ${entry.effectiveDate}`,
                ['?', 'Daily prorated', 'Fixed monthly', 'Manual'][entry.calculationMode] || 'Unknown',
                entry.operation === 3 ? money(entry.suggestedNetAdjustment) : `${money(entry.suggestedRentCredit)} / ${money(entry.suggestedDepositRefund)}`,
                entry.operation === 3 ? money(entry.approvedNetAdjustment) : `${money(entry.approvedRentCredit)} / ${money(entry.approvedDepositRefund)}`,
                ['?', 'Pending', 'Approved', 'Rejected', 'Consumed'][entry.state] || 'Unknown'
            ].forEach(v => tr.append(td(v)));
            const action = document.createElement('td');
            if (entry.state === 1) {
                const button = document.createElement('button'); button.type = 'button';
                button.className = 'btn btn-sm btn-outline-primary'; button.textContent = 'Review';
                button.addEventListener('click', () => beginReview(entry)); action.append(button);
            } else {
                action.textContent = entry.state === 2 ? `Finance cleared · journal ${entry.settlementJournalId ?? 'none'}` : (entry.reconciliationNote || '—');
            }
            tr.append(action); body.append(tr);
        }
        if (!body.children.length) { const tr = document.createElement('tr'); const cell = td('No Finance clearances found.'); cell.colSpan = 7; tr.append(cell); body.append(tr); }
    }
    function beginReview(entry) {
        review = entry;
        el('hfReviewTitle').textContent = `#${entry.id} · ${entry.operation === 1 ? 'transfer' : entry.operation === 3 ? 'upcoming reschedule' : 'checkout'}`;
        el('hfReviewRent').value = ''; el('hfReviewDeposit').value = '';
        el('hfReviewNet').value = '';
        el('hfReviewRent').disabled = entry.operation === 3;
        el('hfReviewDeposit').disabled = entry.operation === 3;
        el('hfReviewNet').disabled = entry.operation !== 3;
        el('hfReviewOverrideReason').value = ''; el('hfReviewJournal').value = '';
        el('hfNoAdjustment').checked = entry.operation === 3 ?
            Number(entry.approvedNetAdjustment) === 0 : entry.approvedRentCredit + entry.approvedDepositRefund === 0;
        el('hfAutoInvoice').checked = false;
        el('hfAutoInvoice').disabled = entry.operation !== 3;
        toggleAutoInvoice();
        if (entry.operation === 3) run(() => loadAutoInvoiceOptions('hfAutoPeriod', 'hfAutoHead'));
        el('hfReviewNote').value = '';
        el('hfReviewForm').classList.remove('d-none');
        el('hfReviewForm').scrollIntoView({ block: 'nearest', behavior: 'smooth' });
    }
    function toggleAutoInvoice() {
        const active = el('hfAutoInvoice').checked;
        el('hfAutoPeriod').disabled = !active;
        el('hfAutoHead').disabled = !active;
        el('hfReviewJournal').disabled = active;
        el('hfNoAdjustment').disabled = active;
        if (active) { el('hfNoAdjustment').checked = false; el('hfReviewJournal').value = ''; }
    }
    async function loadAutoInvoiceOptions(periodId, headId) {
        const [periods, billing] = await Promise.all([
            api('/api/finance/invoice-posting?unpostedOnly=true&pageSize=1'),
            api('/api/finance/fees/options')
        ]);
        const periodBox = el(periodId), headBox = el(headId);
        periodBox.replaceChildren(new Option('Choose open period', ''));
        headBox.replaceChildren(new Option('Choose configured fee head', ''));
        for (const item of periods.openPeriods || []) option(periodBox, item.name, String(item.id));
        for (const item of billing.feeHeads || []) option(headBox, item.name, String(item.id));
    }
    async function finishReview(approve) {
        if (!review || working) return;
        const note = el('hfReviewNote').value.trim();
        if (note.length < 20 || note.length > 1000) throw Error('Enter independently verified reconciliation evidence (20–1000 characters).');
        const auto = approve && el('hfAutoInvoice').checked;
        if (auto && (!numeric('hfAutoPeriod') || !numeric('hfAutoHead') ||
            Number(el('hfReviewNet').value || review.approvedNetAdjustment) <= 0))
            throw Error('Automatic invoice requires a positive charge, open accounting period and fee head.');
        if (!confirm(`${approve ? 'Approve' : 'Reject'} clearance #${review.id}? This decision is audited.`)) return;
        working = true;
        try {
            await api(`${apiRoot}/clearances/${review.id}/review`, 'POST', {
                rowVersion: review.rowVersion, approve,
                noAdjustmentRequired: el('hfNoAdjustment').checked,
                settlementJournalId: numeric('hfReviewJournal'),
                autoPostInvoice: auto, accountingPeriodId: auto ? numeric('hfAutoPeriod') : null,
                autoFeeHeadId: auto ? numeric('hfAutoHead') : null,
                manualRentCredit: numeric('hfReviewRent'), manualDepositRefund: numeric('hfReviewDeposit'),
                manualNetAdjustment: review.operation === 3 ? signed('hfReviewNet') : null,
                overrideReason: el('hfReviewOverrideReason').value.trim() || null,
                reconciliationNote: note
            });
            notice(`Clearance #${review.id} ${approve ? auto ? 'approved; draft invoice prepared (posting occurs with successful Operations change)' : 'approved' : 'rejected'}. Cash payout is not automatic.`, true);
            review = null; el('hfReviewForm').classList.add('d-none'); await loadClearances();
        } finally { working = false; }
    }
    function run(task) { Promise.resolve().then(task).catch(error => notice(error.message)); }
    el('hfEffective').value = today();
    el('hfPolicyLoad').addEventListener('click', () => run(loadPolicy));
    el('hfPolicyForm').addEventListener('submit', e => { e.preventDefault(); run(() => savePolicy(e)); });
    el('hfLoadStays').addEventListener('click', () => run(loadStays));
    el('hfSource').addEventListener('change', () => run(chooseStay));
    el('hfOperation').addEventListener('change', () => run(updateTarget));
    el('hfRequestForm').addEventListener('submit', e => { e.preventDefault(); run(() => requestClearance(e)); });
    el('hfRefresh').addEventListener('click', () => run(loadClearances));
    el('hfPrevious').addEventListener('click', () => run(() => { if (clearancePage > 1) { clearancePage--; return loadClearances(); } }));
    el('hfNext').addEventListener('click', () => run(() => { if (clearancePage < clearancePages) { clearancePage++; return loadClearances(); } }));
    el('hfReviewForm').addEventListener('submit', e => { e.preventDefault(); run(() => finishReview(true)); });
    el('hfAutoInvoice').addEventListener('change', toggleAutoInvoice);
    el('hfReject').addEventListener('click', () => run(() => finishReview(false)));
    el('hfReviewCancel').addEventListener('click', () => { review = null; el('hfReviewForm').classList.add('d-none'); });
    run(async () => { await Promise.all([loadStays(), loadClearances()]); });
})();
