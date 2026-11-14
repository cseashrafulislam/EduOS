(() => {
    'use strict';
    const root = document.getElementById('admissionCashWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const bn = document.documentElement.lang.toLowerCase().startsWith('bn');
    const t = (en, bd) => bn ? bd : en;
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value;
    const endpoint = '/api/admission-cash-receipts';
    let workspace = null, selected = null, requestId = '', fingerprint = '';
    let collecting = false, loadSequence = 0, receiptSequence = 0;
    const context = [document.body?.dataset.authTenantId || '', document.body?.dataset.authSessionReference || ''];
    const live = () => context[0] === (document.body?.dataset.authTenantId || '') &&
        context[1] === (document.body?.dataset.authSessionReference || '') &&
        window.EduOSSessionTransitionPending?.() !== true;
    function notice(message, error = false) {
        const box = el('admissionCashNotice');
        box.textContent = message;
        box.className = 'alert py-2 mb-2 ' + (error ? 'alert-danger' : 'alert-info');
    }
    async function api(url, method = 'GET', payload) {
        if (!live()) throw new Error(t('Institution session changed. Reload the page.', 'প্রতিষ্ঠানের সেশন বদলেছে। পেজ রিলোড করুন।'));
        const headers = { Accept: 'application/json' };
        if (method !== 'GET') {
            if (!token) throw new Error('Antiforgery token missing; reload the page.');
            headers.RequestVerificationToken = token;
            headers['Content-Type'] = 'application/json';
        }
        const response = await fetch(url, { method, headers, credentials: 'same-origin',
            cache: 'no-store', body: payload === undefined ? undefined : JSON.stringify(payload) });
        const result = await response.json().catch(() => null);
        if (!live()) throw new Error(t('Institution session changed. Reload the page.', 'প্রতিষ্ঠানের সেশন বদলেছে। পেজ রিলোড করুন।'));
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
        const sequence = ++receiptSequence;
        const row = await api(endpoint + '/' + encodeURIComponent(reference));
        if (sequence !== receiptSequence || !live()) return;
        showReceipt(row);
        notice('Posted receipt ' + row.receiptNumber + ' loaded.');
    }
    function choose(row) {
        if (collecting || !live()) return;
        ++receiptSequence;
        selected = row; requestId = ''; fingerprint = '';
        el('admissionCashReceiptPrint').hidden = true;
        el('admissionCashCollectPanel').hidden = false;
        el('admissionCashSelected').textContent = row.applicationNumber + ' · ' + row.applicantName +
            ' · ' + row.campusName + ' · ' + money(row.applicationFee, row.currencyCode);
        notice(t('Select a period and ledgers, then confirm cash collection.', 'হিসাবকাল ও লেজার নির্বাচন করে নগদ গ্রহণ নিশ্চিত করুন।'));
    }
    function render(data) {
        workspace = data;
        select('admissionCashPeriod', data.openPeriods || [], t('Select open period', 'খোলা হিসাবকাল নির্বাচন করুন'));
        select('admissionCashLedger', data.cashAccounts || [], t('Select cash-in-hand ledger', 'নগদ হিসাব নির্বাচন করুন'));
        select('admissionIncomeLedger', data.incomeAccounts || [], t('Select income ledger', 'আয়ের হিসাব নির্বাচন করুন'));
        if (!el('admissionCashDate').value) el('admissionCashDate').value = data.today;
        const body = el('admissionCashApplicants'); body.replaceChildren();
        for (const row of data.applicants || []) {
            const tr = document.createElement('tr');
            for (const value of [row.applicationNumber, row.applicantName, row.campusName,
                money(row.applicationFee, row.currencyCode), row.receiptNumber || t('Not collected', 'সংগ্রহ হয়নি')])
                tr.append(cell(value));
            const action = document.createElement('td'), button = document.createElement('button');
            button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary';
            button.textContent = row.receiptReference ? t('View receipt', 'রসিদ দেখুন') : t('Collect cash', 'নগদ গ্রহণ করুন');
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
            const tr = document.createElement('tr'), td = cell(t('No matching applications.', 'কোনো আবেদন পাওয়া যায়নি।'));
            td.colSpan = 6; tr.append(td); body.append(tr);
        }
    }
    async function load() {
        const sequence = ++loadSequence;
        const search = el('admissionCashSearch').value.trim();
        const data = await api(endpoint + '/workspace' + (search ? '?search=' + encodeURIComponent(search) : ''));
        if (sequence === loadSequence && live()) render(data);
    }
    async function collect(event) {
        event.preventDefault();
        if (!selected || collecting || !live()) return;
        const date = el('admissionCashDate').value, periodId = Number(el('admissionCashPeriod').value);
        const cashId = Number(el('admissionCashLedger').value), incomeId = Number(el('admissionIncomeLedger').value);
        const period = (workspace.openPeriods || []).find(x => x.id === periodId);
        if (!period || date < period.startDate || date > period.endDate || !cashId || !incomeId) {
            notice(t('Choose an open period covering the date, cash ledger and income ledger.', 'তারিখের সঙ্গে মিল থাকা খোলা হিসাবকাল, নগদ ও আয় হিসাব নির্বাচন করুন।'), true); return;
        }
        const key = [selected.applicantReference, selected.applicationFee, date, periodId, cashId, incomeId].join('|');
        if (key !== fingerprint) { fingerprint = key; requestId = crypto.randomUUID(); }
        if (!confirm(t('Cash received? This will immediately post a balanced admission-fee journal.', 'নগদ টাকা গ্রহণ করেছেন? নিশ্চিত করলে সুষম ভর্তি ফি জার্নাল পোস্ট হবে।'))) return;
        const applicant = selected;
        const button = el('admissionCashCollect'); collecting = true; button.disabled = true;
        try {
            const result = await api(endpoint, 'POST', {
                clientRequestId: requestId, applicantReference: applicant.applicantReference,
                paymentDate: date, accountingPeriodId: periodId, cashAccountId: cashId,
                incomeAccountId: incomeId, expectedAmount: applicant.applicationFee,
                applicantRowVersion: applicant.rowVersion
            });
            showReceipt(result);
            el('admissionCashCollectPanel').hidden = true;
            selected = null; requestId = ''; fingerprint = '';
            notice(result.alreadyRecorded ? t('Existing receipt recovered.', 'আগের রসিদ পুনরুদ্ধার করা হয়েছে।') : t('Cash collected and GL journal posted.', 'নগদ গ্রহণ ও GL জার্নাল পোস্ট হয়েছে।'));
            await load();
        } catch (error) { notice(error.message, true); }
        finally { collecting = false; button.disabled = false; }
    }
    el('admissionCashSearchForm').addEventListener('submit', async event => {
        event.preventDefault();
        if (collecting || !live()) return;
        ++receiptSequence;
        el('admissionCashCollectPanel').hidden = true;
        selected = null;
        try { await load(); notice(t('Applications loaded.', 'আবেদনের তালিকা লোড হয়েছে।')); }
        catch (error) { notice(error.message, true); }
    });
    el('admissionCashCollectForm').addEventListener('submit', collect);
    el('admissionCashPrintButton').addEventListener('click', () => window.print());
    const observer = new MutationObserver(() => {
        if (live()) return;
        ++loadSequence; ++receiptSequence; selected = null; workspace = null;
        requestId = ''; fingerprint = '';
        el('admissionCashApplicants').replaceChildren();
        el('admissionCashCollectPanel').hidden = true;
        el('admissionCashReceiptPrint').hidden = true;
        notice(t('Institution session changed. Reload the page.', 'প্রতিষ্ঠানের সেশন বদলেছে। পেজ রিলোড করুন।'), true);
        observer.disconnect();
    });
    observer.observe(document.body, { attributes: true,
        attributeFilter: ['data-auth-tenant-id', 'data-auth-session-reference'] });
    load().catch(error => notice(error.message, true));
})();
