const root = document.getElementById('institutionAccountingReports');
if (root) {
    const $ = id => document.getElementById(id);
    const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    const today = new Date().toLocaleDateString('en-CA');
    $('ledgerTo').value = today; $('trialAsOf').value = today;
    $('ledgerFrom').value = today.slice(0, 4) + '-01-01';
    let accounts = [], ledger = null, trial = null;
    const notice = (message, error = false) => { const el = $('reportNotice'); el.textContent = message;
        el.className = 'alert ' + (error ? 'alert-danger' : 'alert-success'); };
    const run = async work => { try { await work(); } catch (error) { notice(error.message || 'Report failed.', true); } };
    const decimalKeys = /("(?:debitAmount|creditAmount|runningBalance|openingBalance|closingBalance|debitBalance|creditBalance|amount|totalIncome|totalExpenses|netSurplus|totalAssets|totalLiabilities|totalEquity|unclosedNetSurplus|totalLiabilitiesAndEquity)"\s*:\s*)(-?\d+(?:\.\d+)?)/g;
    async function get(url) {
        const response = await fetch(url, { credentials:'same-origin', headers:{Accept:'application/json'} });
        const raw = await response.text();
        let data; try { data = JSON.parse(raw.replace(decimalKeys, '$1"$2"')); }
        catch { throw new Error('Unexpected report response.'); }
        if (!response.ok || !data.success) throw new Error(data.message || 'Report request failed.');
        return data.data;
    }
    function cents(value) {
        const text = String(value ?? '0');
        if (!/^-?\d+(?:\.\d{1,2})?$/.test(text)) throw new Error('Invalid monetary precision returned by server.');
        const neg = text.startsWith('-'), parts = (neg ? text.slice(1) : text).split('.');
        const n = BigInt(parts[0]) * 100n + BigInt((parts[1] || '').padEnd(2, '0'));
        return neg ? -n : n;
    }
    function money(value) {
        const n = typeof value === 'bigint' ? value : cents(value), abs = n < 0n ? -n : n;
        const whole = abs / 100n, frac = String(abs % 100n).padStart(2, '0');
        return (n < 0n ? '-' : '') + whole.toString().replace(/\B(?=(\d{3})+(?!\d))/g, ',') + '.' + frac;
    }
    function csvCell(value) {
        let s = String(value ?? '');
        if (/^[\s\u0000-\u001f]*[=+\-@]/.test(s)) s = "'" + s;
        return '"' + s.replace(/"/g, '""') + '"';
    }
    function downloadCsv(name, rows) {
        const csv = '\uFEFF' + rows.map(row => row.map(csvCell).join(',')).join('\r\n') + '\r\n';
        const url = URL.createObjectURL(new Blob([csv], {type:'text/csv;charset=utf-8'}));
        const link = document.createElement('a'); link.href = url; link.download = name;
        document.body.appendChild(link); link.click(); link.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    async function loadAccounts() {
        let page = 1, total = 1, items = [];
        do {
            const data = await get('/api/finance/accounting/setup/accounts?' + new URLSearchParams({page, pageSize:100}));
            items.push(...(data.items || [])); total = data.totalPages || 1; page++;
            if (total > 100) throw new Error('More than 10,000 accounts. Narrow account selection is required.');
        } while (page <= total);
        accounts = items;
        $('ledgerAccount').innerHTML = '<option value="">Select an account</option>' + accounts
            .filter(x => !x.isGroup).map(x => '<option value="' + x.id + '">' + esc(x.code + ' — ' + x.name) + '</option>').join('');
    }
    $('ledgerForm').addEventListener('submit', event => { event.preventDefault(); run(async () => {
        const accountId = Number($('ledgerAccount').value), from = $('ledgerFrom').value, to = $('ledgerTo').value;
        if (!Number.isSafeInteger(accountId) || accountId <= 0 || !from || !to || from > to)
            throw new Error('Select an account and a valid date range.');
        $('ledgerRun').disabled = true; $('ledgerCsv').disabled = true; ledger = null;
        try {
            const data = await get('/api/finance/accounting/reports/ledger/' + accountId + '?' +
                new URLSearchParams({fromDate:from, toDate:to}));
            const account = accounts.find(x => x.id === accountId);
            $('ledgerTitle').textContent = (account ? account.code + ' — ' + account.name : 'Ledger') + ' | ' + from + ' to ' + to;
            $('ledgerRows').innerHTML = (data.entries || []).map(x => '<tr><td>' + esc(x.date) + '</td><td>' +
                esc(x.journalNumber) + '</td><td>' + esc(x.description) + '</td><td class="text-end">' +
                money(x.debitAmount) + '</td><td class="text-end">' + money(x.creditAmount) +
                '</td><td class="text-end">' + money(x.runningBalance) + '</td></tr>').join('');
            $('ledgerOpening').textContent = money(data.openingBalance);
            $('ledgerClosing').textContent = money(data.closingBalance);
            let check = cents(data.openingBalance);
            for (const x of data.entries || []) {
                check += cents(x.debitAmount) - cents(x.creditAmount);
                if (check !== cents(x.runningBalance)) throw new Error('Ledger running balance does not reconcile.');
            }
            if (check !== cents(data.closingBalance)) throw new Error('Ledger closing balance does not reconcile.');
            ledger = data; $('ledgerCsv').disabled = false; notice('Ledger loaded and reconciled.');
        } finally { $('ledgerRun').disabled = false; }
    }); });
    $('trialForm').addEventListener('submit', event => { event.preventDefault(); run(async () => {
        const date = $('trialAsOf').value; if (!date) throw new Error('Select an as-of date.');
        $('trialRun').disabled = true; $('trialCsv').disabled = true; trial = null;
        try {
            const rows = await get('/api/finance/accounting/reports/trial-balance?' + new URLSearchParams({asOfDate:date}));
            let debit = 0n, credit = 0n;
            $('trialRows').innerHTML = rows.map(x => {
                debit += cents(x.debitBalance); credit += cents(x.creditBalance);
                return '<tr><td>' + esc(x.accountCode) + '</td><td>' + esc(x.accountName) +
                    '</td><td class="text-end">' + money(x.debitBalance) + '</td><td class="text-end">' +
                    money(x.creditBalance) + '</td></tr>';
            }).join('');
            $('trialDebit').textContent = money(debit); $('trialCredit').textContent = money(credit);
            if (debit !== credit) throw new Error('Trial balance totals do not reconcile.');
            trial = rows; $('trialCsv').disabled = false; notice('Trial balance loaded and reconciled.');
        } finally { $('trialRun').disabled = false; }
    }); });
    $('ledgerCsv').addEventListener('click', () => run(async () => {
        if (!ledger) return;
        downloadCsv('institution-ledger-' + $('ledgerFrom').value + '-' + $('ledgerTo').value + '.csv',
            [['Date','Journal','Description','Debit','Credit','Running Balance'],
                ...ledger.entries.map(x => [x.date,x.journalNumber,x.description,money(x.debitAmount),money(x.creditAmount),money(x.runningBalance)]),
                ['Opening','','','','',money(ledger.openingBalance)],
                ['Closing','','','','',money(ledger.closingBalance)]]);
    }));
    $('trialCsv').addEventListener('click', () => run(async () => {
        if (!trial) return;
        downloadCsv('institution-trial-balance-' + $('trialAsOf').value + '.csv',
            [['Account Code','Account Name','Debit Balance','Credit Balance'],
                ...trial.map(x => [x.accountCode,x.accountName,money(x.debitBalance),money(x.creditBalance)]),
                ['TOTAL','',$('trialDebit').textContent,$('trialCredit').textContent]]);
    }));

    const incomeForm = $('incomeForm');
    if (incomeForm) {
        $('incomeFrom').value = $('ledgerFrom').value;
        $('incomeTo').value = today;
        let statement = null;
        incomeForm.addEventListener('submit', event => { event.preventDefault(); run(async () => {
            const from = $('incomeFrom').value, to = $('incomeTo').value;
            if (!from || !to || from > to) throw new Error('Select a valid statement period.');
            $('incomeRun').disabled = true; $('incomeCsv').disabled = true; statement = null;
            try {
                const data = await get('/api/finance/accounting/reports/income-statement?' +
                    new URLSearchParams({fromDate:from, toDate:to}));
                let revenue = 0n, expenses = 0n;
                const section = (label, rows) => '<tr class="table-secondary"><th colspan="3">' + label + '</th></tr>' +
                    rows.map(x => '<tr><td>' + esc(x.accountCode) + '</td><td>' + esc(x.accountName) +
                        '</td><td class="text-end">' + money(x.amount) + '</td></tr>').join('');
                for (const row of data.incomeRows || []) revenue += cents(row.amount);
                for (const row of data.expenseRows || []) expenses += cents(row.amount);
                if (revenue !== cents(data.totalIncome) || expenses !== cents(data.totalExpenses) ||
                    revenue - expenses !== cents(data.netSurplus))
                    throw new Error('Income statement totals do not reconcile.');
                $('incomeRows').innerHTML = section('Income', data.incomeRows || []) +
                    section('Expenses', data.expenseRows || []);
                $('incomeTotal').textContent = money(revenue);
                $('expenseTotal').textContent = money(expenses);
                $('incomeNet').textContent = money(revenue - expenses);
                statement = data; $('incomeCsv').disabled = false;
                notice('Institutional income statement loaded and reconciled.');
            } finally { $('incomeRun').disabled = false; }
        }); });
        $('incomeCsv').addEventListener('click', () => run(async () => {
            if (!statement) return;
            const rows = [['Section','Account Code','Account Name','Amount BDT']];
            for (const row of statement.incomeRows || [])
                rows.push(['Income',row.accountCode,row.accountName,money(row.amount)]);
            for (const row of statement.expenseRows || [])
                rows.push(['Expense',row.accountCode,row.accountName,money(row.amount)]);
            rows.push(['Total Income','','',money(statement.totalIncome)]);
            rows.push(['Total Expenses','','',money(statement.totalExpenses)]);
            rows.push(['Net Surplus / Deficit','','',money(statement.netSurplus)]);
            downloadCsv('institution-income-statement-' + $('incomeFrom').value + '-' + $('incomeTo').value + '.csv', rows);
        }));
    }

    const balanceForm = $('balanceForm');
    if (balanceForm) {
        $('balanceAsOf').value = today;
        let balanceSheet = null;
        balanceForm.addEventListener('submit', event => { event.preventDefault(); run(async () => {
            const date = $('balanceAsOf').value;
            if (!date) throw new Error('Select an as-of date.');
            $('balanceRun').disabled = true; $('balanceCsv').disabled = true; balanceSheet = null;
            try {
                const data = await get('/api/finance/accounting/reports/balance-sheet?' +
                    new URLSearchParams({asOfDate:date}));
                const sections = [
                    ['Assets', data.assetRows || []],
                    ['Liabilities', data.liabilityRows || []],
                    ['Equity', data.equityRows || []]
                ];
                const totals = sections.map(([, rows]) => rows.reduce((sum, row) => sum + cents(row.amount), 0n));
                const surplus = cents(data.unclosedNetSurplus);
                const right = totals[1] + totals[2] + surplus;
                if (totals[0] !== cents(data.totalAssets) ||
                    totals[1] !== cents(data.totalLiabilities) ||
                    totals[2] !== cents(data.totalEquity) ||
                    right !== cents(data.totalLiabilitiesAndEquity) || totals[0] !== right)
                    throw new Error('Balance sheet totals do not reconcile.');
                $('balanceRows').innerHTML = sections.map(([label, rows]) =>
                    '<tr class="table-secondary"><th colspan="3">' + label + '</th></tr>' +
                    rows.map(x => '<tr><td>' + esc(x.accountCode) + '</td><td>' + esc(x.accountName) +
                        '</td><td class="text-end">' + money(x.amount) + '</td></tr>').join('')).join('');
                $('balanceAssets').textContent = money(totals[0]);
                $('balanceLiabilities').textContent = money(totals[1]);
                $('balanceEquity').textContent = money(totals[2]);
                $('balanceSurplus').textContent = money(surplus);
                $('balanceRight').textContent = money(right);
                balanceSheet = data; $('balanceCsv').disabled = false;
                notice('Institutional balance sheet loaded and reconciled.');
            } finally { $('balanceRun').disabled = false; }
        }); });
        $('balanceCsv').addEventListener('click', () => run(async () => {
            if (!balanceSheet) return;
            const rows = [['Section','Account Code','Account Name','Amount BDT']];
            for (const [label, items] of [['Assets',balanceSheet.assetRows || []],
                ['Liabilities',balanceSheet.liabilityRows || []],['Equity',balanceSheet.equityRows || []]])
                for (const row of items) rows.push([label,row.accountCode,row.accountName,money(row.amount)]);
            rows.push(['Total Assets','','',money(balanceSheet.totalAssets)]);
            rows.push(['Total Liabilities','','',money(balanceSheet.totalLiabilities)]);
            rows.push(['Posted Equity','','',money(balanceSheet.totalEquity)]);
            rows.push(['Unclosed Net Surplus','','',money(balanceSheet.unclosedNetSurplus)]);
            rows.push(['Liabilities + Equity + Surplus','','',money(balanceSheet.totalLiabilitiesAndEquity)]);
            downloadCsv('institution-balance-sheet-' + $('balanceAsOf').value + '.csv', rows);
        }));
    }
    run(loadAccounts);
}
