const root = document.getElementById('institutionAccountingReports');
if (root && document.getElementById('cashBookForm')) {
    const $ = id => document.getElementById(id);
    const esc = v => String(v ?? '').replace(/[&<>"']/g, ch => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[ch]));
    const decimalKeys = /("(?:openingBalance|closingBalance|debitAmount|creditAmount|runningBalance|receipts|payments|transfersIn|transfersOut)"\s*:\s*)(-?\d+(?:\.\d+)?)/g;
    const cents = value => {
        const s = String(value ?? '0');
        if (!/^-?\d+(?:\.\d{1,2})?$/.test(s)) throw new Error('Invalid financial precision.');
        const negative = s.startsWith('-'), parts = (negative ? s.slice(1) : s).split('.');
        const amount = BigInt(parts[0]) * 100n + BigInt((parts[1] || '').padEnd(2,'0'));
        return negative ? -amount : amount;
    };
    const money = value => {
        const amount = typeof value === 'bigint' ? value : cents(value), absolute = amount < 0n ? -amount : amount;
        return (amount < 0n ? '-' : '') + (absolute / 100n).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ',') +
            '.' + String(absolute % 100n).padStart(2,'0');
    };
    const iso = date => date.toISOString().slice(0,10);
    const now = new Date();
    const from = iso(new Date(now.getFullYear(), now.getMonth(), 1, 12));
    const to = iso(new Date(now.getFullYear(), now.getMonth(), now.getDate(), 12));
    for (const id of ['cashBookFrom', 'cashMovementsFrom']) $(id).value = from;
    for (const id of ['cashBookTo', 'cashMovementsTo']) $(id).value = to;
    let book = null, movements = null;
    const get = async url => {
        const response = await fetch(url, {headers:{Accept:'application/json'}, credentials:'same-origin', cache:'no-store'});
        const payload = JSON.parse((await response.text()).replace(decimalKeys, '$1"$2"'));
        if (!response.ok || !payload.success) throw new Error(payload.message || 'Report unavailable.');
        return payload.data;
    };
    const csv = rows => {
        const data = rows.map(row => row.map(value => {
            let s = String(value ?? '');
            if (/^[\s\u0000-\u001f]*[=+\-@]/.test(s)) s = "'" + s;
            return '"' + s.replace(/"/g, '""') + '"';
        }).join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob(['\uFEFF' + data], {type:'text/csv;charset=utf-8;'}));
        const a = document.createElement('a'); a.href = url; a.download = 'institution-cash-' + to + '.csv';
        document.body.appendChild(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000);
    };
    const setFailure = (kind, error) => {
        $(kind + 'Rows').replaceChildren(); $(kind + 'Summary').textContent = error.message;
        $(kind + 'Csv').disabled = true;
        if (kind === 'cashMovements') $('cashMovementsTotals').replaceChildren();
    };
    const loadCashLedgers = async () => {
        let page = 1, pages = 1, cash = [];
        do {
            const data = await get('/api/finance/accounting/setup/accounts?' + new URLSearchParams({page, pageSize:100}));
            cash.push(...(data.items || []).filter(x => x.isCashInHand && !x.isGroup && x.isActive));
            pages = data.totalPages || 1; page++;
            if (pages > 100) throw new Error('More than 10,000 GL accounts; use a scoped cash account search.');
        } while (page <= pages);
        $('cashBookLedger').innerHTML = '<option value="">Select cash ledger</option>' + cash.map(x =>
            '<option value="' + Number(x.id) + '">' + esc(x.code + ' — ' + x.name) + '</option>').join('');
    };
    $('cashBookForm').addEventListener('submit', async event => {
        event.preventDefault(); book = null; $('cashBookCsv').disabled = true;
        try {
            const id = $('cashBookLedger').value;
            if (!id) throw new Error('Select a designated cash ledger.');
            book = await get('/api/finance/accounting/reports/cash-book/' + encodeURIComponent(id) + '?' +
                new URLSearchParams({fromDate:$('cashBookFrom').value, toDate:$('cashBookTo').value}));
            $('cashBookSummary').textContent = 'Opening BDT ' + money(book.openingBalance) +
                ' • Closing BDT ' + money(book.closingBalance);
            $('cashBookRows').innerHTML = (book.entries || []).map(x => '<tr><td>' + esc(x.date) +
                '</td><td>' + esc(x.journalNumber) + '</td><td>' + esc(x.description) +
                '</td><td class="text-end">' + money(x.debitAmount) + '</td><td class="text-end">' +
                money(x.creditAmount) + '</td><td class="text-end">' + money(x.runningBalance) + '</td></tr>').join('');
            $('cashBookCsv').disabled = false;
        } catch (error) { setFailure('cashBook', error); }
    });
    $('cashBookCsv').addEventListener('click', () => {
        if (!book) return;
        csv([['Date','Journal','Description','Cash in BDT','Cash out BDT','Running BDT'],
            ...(book.entries || []).map(x => [x.date,x.journalNumber,x.description,x.debitAmount,x.creditAmount,x.runningBalance]),
            ['Opening','','',book.openingBalance,'',''], ['Closing','','',book.closingBalance,'','']]);
    });
    $('cashMovementsForm').addEventListener('submit', async event => {
        event.preventDefault(); movements = null; $('cashMovementsCsv').disabled = true;
        try {
            movements = await get('/api/finance/accounting/reports/cash-movements?' +
                new URLSearchParams({fromDate:$('cashMovementsFrom').value, toDate:$('cashMovementsTo').value}));
            $('cashMovementsSummary').textContent = 'Opening BDT ' + money(movements.openingBalance) +
                ' • Net cash change BDT ' + money(cents(movements.receipts) - cents(movements.payments)) +
                ' • Closing BDT ' + money(movements.closingBalance);
            const columns = x => [x.openingBalance,x.receipts,x.payments,x.transfersIn,x.transfersOut,x.closingBalance];
            $('cashMovementsRows').innerHTML = (movements.accounts || []).map(x => '<tr><td>' + esc(x.kind) +
                '</td><td>' + esc(x.ledgerCode + ' — ' + x.ledgerName) + '</td>' +
                columns(x).map(n => '<td class="text-end">' + money(n) + '</td>').join('') + '</tr>').join('');
            $('cashMovementsTotals').innerHTML = '<tr><th colspan="2">Institution total</th>' +
                columns(movements).map(n => '<th class="text-end">' + money(n) + '</th>').join('') + '</tr>';
            $('cashMovementsCsv').disabled = false;
        } catch (error) { setFailure('cashMovements', error); }
    });
    $('cashMovementsCsv').addEventListener('click', () => {
        if (!movements) return;
        const fields = x => [x.openingBalance,x.receipts,x.payments,x.transfersIn,x.transfersOut,x.closingBalance];
        csv([['Kind','GL code','GL name','Opening','Receipts','Payments','Transfers in','Transfers out','Closing'],
            ...(movements.accounts || []).map(x => [x.kind,x.ledgerCode,x.ledgerName,...fields(x)]),
            ['Institution total','','',...fields(movements)]]);
    });
    loadCashLedgers().catch(error => { $('cashBookLedger').innerHTML = '<option value="">Cash ledger unavailable</option>';
        $('cashBookSummary').textContent = error.message; });
}
