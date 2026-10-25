(() => {
    'use strict';
    const root = document.getElementById('admissionCashWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value;
    const endpoint = '/api/admission-cash-receipts';
    let workspace = null, selected = null, requestId = '', fingerprint = '';
    function notice(message, error = false) {
        const box = el('admissionCashNotice');
        box.textContent = message;
        box.className = 'alert ' + (error ? 'alert-danger' : 'alert-info');
    }
    async function api(url, method = 'GET', payload) {
        const headers = { Accept: 'application/json' };
        if (method !== 'GET') {
            if (!token) throw new Error('Antiforgery token missing; reload the page.');
            headers.RequestVerificationToken = token;
            headers['Content-Type'] = 'application/json';
        }
        const response = await fetch(url, { method, headers, credentials: 'same-origin',
            cache: 'no-store', body: payload === undefined ? undefined : JSON.stringify(payload) });
        const result = await response.json().catch(() => null);
        if (!response.ok || !result?.success) throw new Error(result?.message || 'Request failed.');
        return result.data;
    }
    function option(value, label) {
        const o = document.createElement('option'); o.value = String(value); o.textContent = label; return o;
    }
    function select(id, rows, placeholder) {
        const control = el(id), previous = control.value;
        control.replaceChildren(option('', placeholder), ...rows.map(x => option(x.id, x.name)));
        if ([...control.options].some(x => x.value === previous)) control.value = previous;
    }
    function cell(value) {
        const td = document.createElement('td'); td.textContent = value == null ? '' : String(value); return td;
    }
    function money(value, currency = 'BDT') {
        return Number(value).toLocaleString('en-BD', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) + ' ' + currency;
    }
    function showReceipt(row) {
        const values = {
            cashReceiptNumber: row.receiptNumber,
            cashReceiptApplication: row.applicationNumber,
            cashReceiptApplicant: row.applicantName,
            cashReceiptCampus: row.campusName,
            cashReceiptDate: row.paymentDate,
            cashReceiptAmount: money(row.amount, row.currencyCode),
            cashReceiptJournal: row.journalNumber
        };
        Object.entries(values).forEach(([id, value]) => { el(id).textContent = value || '—'; });
        el('admissionCashReceiptPrint').hidden = false;
    }
    async function receipt(reference) {
        const row = await api(endpoint + '/' + encodeURIComponent(reference));
        showReceipt(row);
        notice('Posted receipt ' + row.receiptNumber + ' loaded.');
    }
    function choose(row) {
        selected = row; requestId = ''; fingerprint = '';
        el('admissionCashReceiptPrint').hidden = true;
        el('admissionCashCollectPanel').hidden = false;
        el('admissionCashSelected').textContent = row.applicationNumber + ' · ' + row.applicantName +
            ' · ' + row.campusName + ' · ' + money(row.applicationFee, row.currencyCode);
        notice('Select a period and ledgers, then confirm cash collection.');
    }
    function render(data) {
        workspace = data;
        select('admissionCashPeriod', data.openPeriods || [], 'Select open period');
        select('admissionCashLedger', data.cashAccounts || [], 'Select cash-in-hand ledger');
        select('admissionIncomeLedger', data.incomeAccounts || [], 'Select income ledger');
        if (!el('admissionCashDate').value) el('admissionCashDate').value = data.today;
        const body = el('admissionCashApplicants'); body.replaceChildren();
        for (const row of data.applicants || []) {
            const tr = document.createElement('tr');
            for (const value of [row.applicationNumber, row.applicantName, row.campusName,
                money(row.applicationFee, row.currencyCode), row.receiptNumber || 'Not collected'])
                tr.append(cell(value));
            const action = document.createElement('td'), button = document.createElement('button');
            button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary';
            button.textContent = row.receiptReference ? 'View receipt' : 'Collect cash';
            const eligible = [2, 3, 4, 5, 6, 8].includes(Number(row.state));
            button.disabled = !row.receiptReference &&
                (!eligible || row.currencyCode !== 'BDT' || Number(row.applicationFee) <= 0);
            button.addEventListener('click', async () => {
                try {
                    if (row.receiptReference) await receipt(row.receiptReference);
                    else choose(row);
                } catch (error) { notice(error.message, true); }
            });
            action.append(button); tr.append(action); body.append(tr);
        }
        if (!(data.applicants || []).length) {
            const tr = document.createElement('tr'), td = cell('No matching applications.');
            td.colSpan = 6; tr.append(td); body.append(tr);
        }
    }
    async function load() {
        const search = el('admissionCashSearch').value.trim();
        render(await api(endpoint + '/workspace' + (search ? '?search=' + encodeURIComponent(search) : '')));
    }
    async function collect(event) {
        event.preventDefault();
        if (!selected) return;
        const date = el('admissionCashDate').value, periodId = Number(el('admissionCashPeriod').value);
        const cashId = Number(el('admissionCashLedger').value), incomeId = Number(el('admissionIncomeLedger').value);
        const period = (workspace.openPeriods || []).find(x => x.id === periodId);
        if (!period || date < period.startDate || date > period.endDate || !cashId || !incomeId) {
            notice('Choose an open period covering the date, cash ledger and income ledger.', true); return;
        }
        const key = [selected.applicantReference, selected.applicationFee, date, periodId, cashId, incomeId].join('|');
        if (key !== fingerprint) { fingerprint = key; requestId = crypto.randomUUID(); }
        if (!confirm('Cash received? This will immediately post a balanced admission-fee journal.')) return;
        const button = el('admissionCashCollect'); button.disabled = true;
        try {
            const result = await api(endpoint, 'POST', {
                clientRequestId: requestId, applicantReference: selected.applicantReference,
                paymentDate: date, accountingPeriodId: periodId, cashAccountId: cashId,
                incomeAccountId: incomeId, expectedAmount: selected.applicationFee,
                applicantRowVersion: selected.rowVersion
            });
            showReceipt(result);
            el('admissionCashCollectPanel').hidden = true;
            selected = null;
            notice(result.alreadyRecorded ? 'Existing receipt recovered.' : 'Cash collected and GL journal posted.');
            await load();
        } catch (error) { notice(error.message, true); }
        finally { button.disabled = false; }
    }
    el('admissionCashSearchForm').addEventListener('submit', async event => {
        event.preventDefault();
        el('admissionCashCollectPanel').hidden = true;
        selected = null;
        try { await load(); notice('Applications loaded.'); }
        catch (error) { notice(error.message, true); }
    });
    el('admissionCashCollectForm').addEventListener('submit', collect);
    el('admissionCashPrintButton').addEventListener('click', () => window.print());
    load().catch(error => notice(error.message, true));
})();
