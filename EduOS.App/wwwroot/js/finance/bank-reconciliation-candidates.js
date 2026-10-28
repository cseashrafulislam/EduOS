// Tenant-admin-only, server-paged bank ledger candidates. No clearing or local private cache.
const bankCandidatePanel = document.getElementById('bankCandidatePanel');
if (bankCandidatePanel) {
    const el = id => document.getElementById(id);
    const esc = value => String(value ?? '').replace(/[&<>"']/g,
        c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    const csvCell = value => {
        let s = String(value ?? '');
        if (/^[\s\u0000-\u001f]*[=+\-@]/.test(s)) s = "'" + s;
        return '"' + s.replace(/"/g, '""') + '"';
    };
    let current = null, page = 1, revision = 0;
    const invalidate = () => {
        revision++;
        current = null;
        el('bankCandidateRows').replaceChildren();
        el('bankCandidateSummary').textContent = '';
        el('bankCandidatePage').textContent = '';
        for (const id of ['bankCandidateExport','bankCandidatePrev','bankCandidateNext'])
            el(id).disabled = true;
    };
    for (const id of ['statementBank','statementFrom','statementTo','bankCandidateReference']) {
        el(id).addEventListener('input', invalidate);
        el(id).addEventListener('change', invalidate);
    }
    async function load(nextPage) {
        invalidate();
        const seq = revision;
        const bank = el('statementBank').value;
        const from = el('statementFrom').value, to = el('statementTo').value;
        const reference = el('bankCandidateReference').value.trim();
        if (!/^\d+$/.test(bank) || !Number.isSafeInteger(Number(bank)) || Number(bank) <= 0 ||
            !/^\d{4}-\d{2}-\d{2}$/.test(from) || !/^\d{4}-\d{2}-\d{2}$/.test(to) ||
            from > to || reference.length > 100)
        {
            el('bankCandidateSummary').textContent = 'Select a bank and valid dates.';
            return;
        }
        el('bankCandidateRun').disabled = true;
        try {
            const params = new URLSearchParams({fromDate:from,toDate:to,
                referencePrefix:reference,page:String(nextPage),pageSize:'25'});
            const response = await fetch('/api/finance/accounting/reports/bank-book/' +
                encodeURIComponent(bank) + '/reconciliation-candidates?' + params, {
                    credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'}
                });
            const payload = await response.json();
            if (seq !== revision) return;
            if (!response.ok || !payload.success)
                throw new Error(payload.message || 'Ledger candidate lookup failed.');
            current = payload.data;
            page = nextPage;
            const rows = current.rows || [];
            el('bankCandidateRows').innerHTML = rows.map(x =>
                '<tr><td>' + esc(x.date) + '</td><td>' + esc(x.journalNumber) +
                '</td><td>' + esc(x.journalLineId) + '</td><td>' + esc(x.reference) +
                '</td><td>' + esc(x.campusId) + '</td><td class="text-end">' +
                esc(x.deposit) + '</td><td class="text-end">' + esc(x.withdrawal) +
                '</td></tr>').join('') || '<tr><td colspan="7">No posted movements found.</td></tr>';
            el('bankCandidateSummary').textContent = current.totalCount +
                ' posted movements; BDT ' + current.totalDeposits + ' deposits, BDT ' +
                current.totalWithdrawals + ' withdrawals (filtered period). No clearance recorded.';
            el('bankCandidatePage').textContent = 'Page ' + page + ' of ' +
                Math.max(1, Math.ceil(current.totalCount / current.pageSize));
            el('bankCandidatePrev').disabled = page <= 1;
            el('bankCandidateNext').disabled = page * current.pageSize >= current.totalCount;
            el('bankCandidateExport').disabled = rows.length === 0;
        } catch (error) {
            if (seq === revision) el('bankCandidateSummary').textContent = error.message;
        } finally { el('bankCandidateRun').disabled = false; }
    }
    el('bankCandidateRun').addEventListener('click', () => load(1));
    el('bankCandidatePrev').addEventListener('click', () => { if (current && page > 1) load(page - 1); });
    el('bankCandidateNext').addEventListener('click', () => { if (current && page * current.pageSize < current.totalCount) load(page + 1); });
    el('bankCandidateExport').addEventListener('click', () => {
        if (!current || !current.rows?.length) return;
        const data = [['Date','Journal','Journal Line ID','Reference','Campus ID','Deposit BDT','Withdrawal BDT','Line Version'],
            ...current.rows.map(x => [x.date,x.journalNumber,x.journalLineId,x.reference,
                x.campusId,x.deposit,x.withdrawal,x.journalLineVersion])];
        const csv = '\uFEFF' + data.map(row => row.map(csvCell).join(',')).join('\r\n') + '\r\n';
        const url = URL.createObjectURL(new Blob([csv],{type:'text/csv;charset=utf-8'}));
        const a = document.createElement('a');
        a.href = url; a.download = 'bank-ledger-candidates-' + current.fromDate + '-page-' + page + '.csv';
        document.body.appendChild(a); a.click(); a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    });
}
