// Read-only bank-statement comparison. Local CSV contents are sent only to the authorized
// reconciliation preview API, never stored in the browser or posted to the ledger.
const panel = document.getElementById('bankStatementPanel');
if (panel) {
    const el = id => document.getElementById(id);
    const escapeHtml = v => String(v ?? '').replace(/[&<>"']/g, c =>
        ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    const decimal = /^-?\d{1,12}(?:\.\d{1,2})?$/;
    const cents = value => {
        const s = String(value ?? '').trim();
        if (!decimal.test(s)) throw new Error('Use BDT values with up to two decimal places.');
        const negative = s.startsWith('-');
        const [whole, fraction = ''] = (negative ? s.slice(1) : s).split('.');
        const n = BigInt(whole) * 100n + BigInt(fraction.padEnd(2, '0'));
        return negative ? -n : n;
    };
    const money = value => {
        const n = cents(value), absolute = n < 0n ? -n : n;
        return (n < 0n ? '-' : '') + (absolute / 100n).toString() + '.' +
            String(absolute % 100n).padStart(2, '0');
    };
    const cell = v => {
        let text = String(v ?? '');
        if (/^[\s\u0000-\u001f]*[=+\-@]/.test(text)) text = "'" + text;
        return '"' + text.replace(/"/g, '""') + '"';
    };
    const show = (message, error = false) => {
        el('statementNotice').className = 'alert ' + (error ? 'alert-danger' : 'alert-info');
        el('statementNotice').textContent = message;
    };
    const parseCsvRow = line => {
        const fields = []; let value = '', quoted = false;
        for (let i = 0; i < line.length; i++) {
            const c = line[i];
            if (c === '"') {
                if (quoted && line[i + 1] === '"') { value += '"'; i++; }
                else if (!quoted && value.length === 0) quoted = true;
                else if (quoted) quoted = false;
                else throw new Error('Invalid CSV quoting.');
            } else if (c === ',' && !quoted) { fields.push(value); value = ''; }
            else value += c;
        }
        if (quoted) throw new Error('Multiline CSV fields are not supported.');
        fields.push(value);
        return fields.map(x => x.trim());
    };
    function parseRows(csv, from, to) {
        const lines = csv.replace(/^\uFEFF/, '').split(/\r?\n/).filter(x => x.trim());
        if (lines.length < 2 || lines.length > 501) throw new Error('CSV must contain 1–500 movements.');
        if (parseCsvRow(lines.shift()).map(x => x.toLowerCase()).join(',') !== 'date,reference,deposit,withdrawal')
            throw new Error('CSV header must be date,reference,deposit,withdrawal.');
        return lines.map((line, i) => {
            const x = parseCsvRow(line);
            if (x.length !== 4 || !/^\d{4}-\d{2}-\d{2}$/.test(x[0]) ||
                x[0] < from || x[0] > to || x[1].length > 150)
                throw new Error('Invalid CSV date, reference or column count at row ' + (i + 2) + '.');
            const deposit = cents(x[2]), withdrawal = cents(x[3]);
            if ((deposit > 0n) === (withdrawal > 0n) || deposit < 0n || withdrawal < 0n)
                throw new Error('Each row needs one positive deposit or withdrawal at row ' + (i + 2) + '.');
            return {date:x[0], reference:x[1], deposit:money(x[2]), withdrawal:money(x[3])};
        });
    }
    async function get(url, options = {}) {
        const response = await fetch(url, {credentials:'same-origin',cache:'no-store',
            headers:{Accept:'application/json',...(options.headers || {})},...options});
        const raw = await response.text();
        const safe = raw.replace(/("(?:deposit|withdrawal|ledgerOpeningBalance|ledgerClosingBalance|statementOpeningBalance|statementClosingBalance|closingVariance)"\s*:\s*)(-?\d+(?:\.\d+)?)/g, '$1"$2"');
        let payload; try { payload = JSON.parse(safe); } catch { throw new Error('Unexpected bank response.'); }
        if (!response.ok || !payload.success) throw new Error(payload.message || 'Comparison failed.');
        return payload.data;
    }
    let report = null;
    const today = new Date().toLocaleDateString('en-CA');
    el('statementFrom').value = today.slice(0, 4) + '-01-01';
    el('statementTo').value = today;
    get('/api/finance/accounting/setup/banks').then(banks => {
        el('statementBank').innerHTML = '<option value="">Select bank</option>' +
            banks.map(x => '<option value="' + escapeHtml(x.id) + '">' +
                escapeHtml(x.bankName + ' — ' + x.accountName) + '</option>').join('');
    }).catch(e => show(e.message, true));
    el('statementFile').addEventListener('change', async event => {
        try {
            const file = event.target.files?.[0];
            if (!file) return;
            if (file.size > 80000) throw new Error('CSV must be 80 KB or smaller.');
            el('statementCsv').value = await file.text();
        } catch (e) { show(e.message, true); }
    });
    el('bankStatementForm').addEventListener('submit', async event => {
        event.preventDefault(); report = null; el('statementExport').disabled = true;
        el('statementSummary').textContent = '';
        el('statementMatchCount').textContent = '0';
        el('statementMatches').replaceChildren();
        el('statementExceptions').replaceChildren();
        const bank = el('statementBank').value, from = el('statementFrom').value, to = el('statementTo').value;
        if (!/^\d+$/.test(bank) || BigInt(bank) <= 0n || !Number.isSafeInteger(Number(bank)) || !from || !to || from > to)
            return show('Choose a bank and valid date range.', true);
        el('statementRun').disabled = true;
        try {
            const opening = money(el('statementOpening').value), closing = money(el('statementClosing').value);
            const movements = parseRows(el('statementCsv').value, from, to);
            const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value ||
                document.querySelector('meta[name="request-verification-token"]')?.content || '';
            const result = await get('/api/finance/accounting/reports/bank-statement-preview', {
                method:'POST', headers:{'Content-Type':'application/json',RequestVerificationToken:token},
                body:JSON.stringify({bankAccountId:Number(bank),fromDate:from,toDate:to,
                    openingBalance:opening,closingBalance:closing,movements})
            });
            report = result;
            el('statementMatchCount').textContent = String((result.matches || []).length);
            el('statementMatches').innerHTML = (result.matches || []).map(x =>
                '<tr><td>' + escapeHtml(x.date) + '</td><td>' + escapeHtml(x.journalNumber) +
                '</td><td>' + escapeHtml(x.reference) + '</td><td class="text-end">' +
                money(x.deposit) + '</td><td class="text-end">' + money(x.withdrawal) +
                '</td></tr>').join('') ||
                '<tr><td colspan="5">No unique exact matches.</td></tr>';
            el('statementSummary').textContent = result.bankName + ': ' + result.matchedCount +
                ' matched; ' + result.unmatchedLedgerCount + ' unmatched ledger; ' +
                result.unmatchedStatementCount + ' unmatched statement. Opening variance BDT ' +
                money(result.openingVariance) + '; movement variance BDT ' +
                money(result.movementVariance) + '; closing variance BDT ' +
                money(result.closingVariance) + '. ' +
                (result.isExactMatch ? 'All movements match exactly (not cleared).' :
                    'Exceptions require manual review (not cleared).');
            el('statementExceptions').innerHTML = (result.exceptions || []).map(x =>
                '<tr><td>' + escapeHtml(x.source) + '</td><td>' + escapeHtml(x.date) +
                '</td><td>' + escapeHtml(x.journalNumber) + '</td><td>' +
                escapeHtml(x.reference) + '</td><td class="text-end">' + money(x.deposit) +
                '</td><td class="text-end">' + money(x.withdrawal) +
                '</td><td>' + escapeHtml(x.reason) + '</td></tr>').join('') ||
                '<tr><td colspan="7">No unmatched movements in this exact-match preview.</td></tr>';
            el('statementExport').disabled = false;
            show('Read-only comparison complete. Differences are not approved or cleared.');
        } catch (e) { show(e.message, true); } finally { el('statementRun').disabled = false; }
    });
    el('statementExport').addEventListener('click', () => {
        if (!report) return;
        const rows = [['Status','Source','Date','Journal','Reference','Deposit','Withdrawal','Reason'],
            ['Summary','Bank',el('statementFrom').value,'',report.bankName,'','','Comparison only; not approved or cleared'],
            ['Summary','Opening variance','','','','', '',money(report.openingVariance)],
            ['Summary','Movement variance','','','','', '',money(report.movementVariance)],
            ['Summary','Closing variance','','','','', '',money(report.closingVariance)],
            ['Summary','Exact match','','','','','',report.isExactMatch ? 'Yes; not cleared' : 'No; manual review'],
            ...(report.matches || []).map(x => ['Matched','Ledger + Statement',x.date,x.journalNumber,
                x.reference,money(x.deposit),money(x.withdrawal),'Unique exact match; not cleared']),
            ...(report.exceptions || []).map(x => ['Unmatched',x.source,x.date,x.journalNumber,x.reference,
                money(x.deposit),money(x.withdrawal),x.reason])];
        const csv = '\uFEFF' + rows.map(x => x.map(cell).join(',')).join('\r\n') + '\r\n';
        const url = URL.createObjectURL(new Blob([csv],{type:'text/csv;charset=utf-8'}));
        const a = document.createElement('a'); a.href = url;
        a.download = 'bank-statement-comparison-' + el('statementFrom').value + '.csv';
        document.body.appendChild(a); a.click(); a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    });
}
