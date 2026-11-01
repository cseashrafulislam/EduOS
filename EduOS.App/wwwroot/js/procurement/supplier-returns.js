(() => {
    'use strict';
    const root = document.getElementById('supplierReturns'); if (!root) return;
    const el = id => document.getElementById(id);
    const maker = root.dataset.maker === 'true', reviewer = root.dataset.reviewer === 'true';
    const bn = (document.documentElement.lang || '').toLowerCase().startsWith('bn');
    if (bn) root.querySelectorAll('[data-bn]').forEach(x => { x.textContent = x.dataset.bn; });
    const token = document.querySelector('meta[name="request-verification-token"]')?.content;
    let reference = '', invoice = null, page = 1, totalPages = 1, seq = 0,
        pending = false, requestKey = null;
    function status(message, isError = false) {
        const box = el('srStatus'); box.className = 'alert alert-' + (isError ? 'danger' : 'info');
        box.textContent = message;
    }
    async function api(path, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        if (method !== 'GET' && token) headers.RequestVerificationToken = token;
        const response = await fetch('/api/procurement/supplier-invoices' + path,
            { method, credentials: 'same-origin', cache: 'no-store', headers,
                ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
        const data = await response.json().catch(() => null);
        if (!response.ok || !data?.success) throw new Error(data?.message || 'Request failed / অনুরোধ ব্যর্থ');
        return data.data;
    }
    function setOptions(id, entries, label, empty = 'Choose / বাছুন') {
        const select = el(id); if (!select) return;
        select.replaceChildren(new Option(empty, ''));
        for (const row of entries) select.add(new Option(label(row), String(row.id)));
    }
    function addCell(tr, value) {
        const td = document.createElement('td'); td.textContent = String(value ?? '—'); tr.append(td); return td;
    }
    function setAccounts(options) {
        setOptions('srPeriod', options?.periods || [], x => x.name + ' · ' + x.startDate + '—' + x.endDate);
        setOptions('srVariance', (options?.accounts || []).filter(x => x.type === 'Expense'),
            x => x.code + ' · ' + x.name, 'Optional unless variance');
        setOptions('srRefund', (options?.accounts || []).filter(x => x.type === 'Asset'),
            x => x.code + ' · ' + x.name, 'Required if payment exceeds net credit-adjusted AP');
    }
    function nextAction(cell, label, callback) {
        const button = document.createElement('button'); button.type = 'button';
        button.className = 'btn btn-sm btn-outline-primary'; button.textContent = label;
        button.addEventListener('click', () => execute(callback)); cell.append(button);
    }
    async function loadReturns() {
        const current = reference, currentSeq = ++seq;
        const result = await api('/' + current + '/returns?page=' + page + '&pageSize=25');
        if (current !== reference || currentSeq !== seq) return;
        totalPages = result.totalPages || 1;
        const body = el('srRows'); body.replaceChildren();
        for (const row of result.items || []) {
            const tr = document.createElement('tr');
            addCell(tr, row.supplierReturnReference); addCell(tr, row.quantity);
            addCell(tr, row.creditAmount); addCell(tr, row.unpaidCredit);
            addCell(tr, row.refundReceivable); addCell(tr, row.state);
            addCell(tr, (row.stockMovementReference || '—') + ' / ' +
                (row.financeJournalReference || '—'));
            const actions = addCell(tr, '');
            if (reviewer && row.state === 'Submitted') nextAction(actions,
                'Independent review / যাচাই', async () => {
                    const decision = prompt('Type APPROVE or REJECT after verifying supplier dispatch and GL mapping:');
                    if (decision === null) return;
                    const word = decision.trim().toUpperCase();
                    if (!['APPROVE', 'REJECT'].includes(word)) throw new Error('Type APPROVE or REJECT.');
                    const reason = prompt('Signed Finance/physical return evidence, 10+ characters:');
                    if (!reason || reason.trim().length < 10) throw new Error('Independent reason required.');
                    await api('/' + reference + '/returns/' + row.reference + '/review', 'POST',
                        { rowVersion: row.rowVersion, approve: word === 'APPROVE', reason: reason.trim() });
                });
            body.append(tr);
        }
        if (!(result.items || []).length) {
            const tr = document.createElement('tr'), td = addCell(tr,
                'No return credit notes / কোনো ফেরত ক্রেডিট নেই');
            td.colSpan = 8; body.append(tr);
        }
        el('srPage').textContent = page + ' / ' + totalPages;
        el('srPrev').disabled = page <= 1; el('srNext').disabled = page >= totalPages;
    }
    async function execute(callback) {
        if (pending) return; pending = true;
        try { await callback(); if (reference) await loadReturns();
            status('Updated source-linked credit evidence / ক্রেডিট তথ্য হালনাগাদ হয়েছে'); }
        catch (error) { status(error.message, true); }
        finally { pending = false; }
    }
    el('srHelpToggle').addEventListener('click', () => {
        const help = el('srHelp'); help.classList.toggle('d-none');
        el('srHelpToggle').setAttribute('aria-expanded', String(!help.classList.contains('d-none')));
    });
    el('srLoad').addEventListener('click', () => execute(async () => {
        const value = el('srInvoice').value.trim();
        if (!/^[0-9a-f]{8}-[0-9a-f-]{27,36}$/i.test(value)) throw new Error('Valid invoice GUID required.');
        reference = value; invoice = null; ++seq; page = 1; requestKey = null;
        el('srWorkspace').classList.add('d-none');
        const [source, options] = await Promise.all([api('/' + value), api('/options')]);
        if (reference !== value) return;
        if (source.stateCode !== 'Posted') throw new Error('Only an approved, posted supplier invoice can be returned.');
        invoice = source;
        el('srInvoiceSummary').textContent = source.supplierInvoiceNumber + ' · Campus ' +
            source.campusId + ' · Gross payable BDT ' + source.totalAmount;
        setOptions('srLine', source.lines || [], x => 'Line ' + x.lineNo + ' · Item ' +
            x.inventoryItemId + ' · Lot ' + x.lotOrBatchNo + ' · Qty ' + x.quantity +
            ' · BDT ' + x.invoiceUnitPrice);
        setAccounts(options);
        el('srWorkspace').classList.remove('d-none'); await loadReturns();
    }));
    el('srForm')?.addEventListener('submit', event => {
        event.preventDefault(); execute(async () => {
            if (!invoice) throw new Error('Load a posted source invoice first.');
            requestKey ||= crypto.randomUUID();
            await api('/' + reference + '/returns', 'POST', {
                clientRequestId: requestKey, invoiceRowVersion: invoice.rowVersion,
                supplierInvoiceLineId: Number(el('srLine').value),
                quantity: Number(el('srQuantity').value), mode: el('srMode').value,
                manualFinalCredit: Number(el('srManual').value),
                manualCreditCorrection: Number(el('srCorrection').value),
                accountingPeriodId: Number(el('srPeriod').value), returnDate: el('srDate').value,
                varianceAccountId: el('srVariance').value ? Number(el('srVariance').value) : null,
                refundReceivableAccountId: el('srRefund').value ? Number(el('srRefund').value) : null,
                supplierReturnReference: el('srReference').value.trim(),
                reason: el('srReason').value.trim()
            });
            requestKey = null;
        });
    });
    for (const id of ['srLine', 'srQuantity', 'srDate', 'srPeriod', 'srMode',
        'srManual', 'srCorrection', 'srVariance', 'srRefund', 'srReference', 'srReason'])
        el(id)?.addEventListener('input', () => { requestKey = null; });
    el('srPrev').addEventListener('click', () => execute(async () => {
        if (page > 1) { --page; await loadReturns(); }
    }));
    el('srNext').addEventListener('click', () => execute(async () => {
        if (page < totalPages) { ++page; await loadReturns(); }
    }));
    const now = new Date();
    el('srDate').value = [now.getFullYear(), String(now.getMonth() + 1).padStart(2, '0'),
        String(now.getDate()).padStart(2, '0')].join('-');
    const fromQuery = new URLSearchParams(location.search).get('invoice');
    if (fromQuery) { el('srInvoice').value = fromQuery; el('srLoad').click(); }
})();
