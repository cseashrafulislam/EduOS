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
    const decimalKeys = /("(?:debitAmount|creditAmount|runningBalance|openingBalance|closingBalance|debitBalance|creditBalance|amount|totalIncome|totalExpenses|netSurplus|totalAssets|totalLiabilities|totalEquity|unclosedNetSurplus|totalLiabilitiesAndEquity|totalInflows|totalOutflows|totalDebit|totalCredit|externalInflows|externalOutflows|transferIn|transferOut)"\s*:\s*)(-?\d+(?:\.\d+)?)/g;
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

    const bankBookForm = $('bankBookForm');
    if (bankBookForm) {
        $('bankBookFrom').value = $('ledgerFrom').value;
        $('bankBookTo').value = today;
        let bankBook = null;
        run(async () => {
            const banks = await get('/api/finance/accounting/setup/banks');
            $('bankBookAccount').innerHTML = '<option value="">Select a bank account</option>' +
                banks.map(x => '<option value="' + esc(x.id) + '">' +
                    esc(x.bankName + ' — ' + x.accountName + ' (' + x.ledgerAccountName + ')') + '</option>').join('');
        });
        bankBookForm.addEventListener('submit', event => { event.preventDefault(); run(async () => {
            const bankId = $('bankBookAccount').value, from = $('bankBookFrom').value, to = $('bankBookTo').value;
            if (!/^\d+$/.test(bankId) || BigInt(bankId) <= 0n || !from || !to || from > to)
                throw new Error('Select a bank and a valid date range.');
            $('bankBookRun').disabled = true; $('bankBookCsv').disabled = true; bankBook = null;
            try {
                const data = await get('/api/finance/accounting/reports/bank-book/' + encodeURIComponent(bankId) + '?' +
                    new URLSearchParams({fromDate:from, toDate:to}));
                let running = cents(data.openingBalance), deposits = 0n, withdrawals = 0n;
                const rows = (data.entries || []).map(x => {
                    const debit = cents(x.debitAmount), credit = cents(x.creditAmount);
                    deposits += debit; withdrawals += credit; running += debit - credit;
                    if (running !== cents(x.runningBalance)) throw new Error('Bank running balance does not reconcile.');
                    return '<tr><td>' + esc(x.date) + '</td><td>' + esc(x.journalNumber) +
                        '</td><td>' + esc(x.sourceType) + '</td><td>' + esc(x.referenceNumber) +
                        '</td><td>' + esc(x.description) + '</td><td class="text-end">' + money(debit) +
                        '</td><td class="text-end">' + money(credit) + '</td><td class="text-end">' + money(running) + '</td></tr>';
                });
                if (deposits !== cents(data.totalInflows) || withdrawals !== cents(data.totalOutflows) ||
                    running !== cents(data.closingBalance))
                    throw new Error('Bank book totals do not reconcile.');
                $('bankBookRows').innerHTML = rows.join('');
                $('bankBookTitle').textContent = data.bankName + ' — ' + data.ledgerAccountCode + ' / ' +
                    data.ledgerAccountName + ' | ' + from + ' to ' + to;
                $('bankBookOpening').textContent = money(data.openingBalance);
                $('bankBookInflows').textContent = money(deposits);
                $('bankBookOutflows').textContent = money(withdrawals);
                $('bankBookClosing').textContent = money(running);
                bankBook = data; $('bankBookCsv').disabled = false;
                notice('Institutional bank book loaded and reconciled.');
            } finally { $('bankBookRun').disabled = false; }
        }); });
        $('bankBookCsv').addEventListener('click', () => run(async () => {
            if (!bankBook) return;
            const rows = [['Date','Journal','Source','Reference','Description','Deposit','Withdrawal','Balance']];
            for (const x of bankBook.entries || [])
                rows.push([x.date,x.journalNumber,x.sourceType,x.referenceNumber,x.description,
                    money(x.debitAmount),money(x.creditAmount),money(x.runningBalance)]);
            rows.push(['Opening','','','','','','',money(bankBook.openingBalance)]);
            rows.push(['Deposits','','','','',money(bankBook.totalInflows),'','']);
            rows.push(['Withdrawals','','','','','',money(bankBook.totalOutflows),'']);
            rows.push(['Closing','','','','','','',money(bankBook.closingBalance)]);
            downloadCsv('institution-bank-book-' + $('bankBookFrom').value + '-' + $('bankBookTo').value + '.csv', rows);
        }));
    }
    const bankLiquidityForm = $('bankLiquidityForm');
    if (bankLiquidityForm) {
        $('bankLiquidityFrom').value = $('ledgerFrom').value;
        $('bankLiquidityTo').value = today;
        let bankLiquidity = null;
        const fields = ['openingBalance','externalInflows','externalOutflows','transferIn','transferOut','closingBalance'];
        bankLiquidityForm.addEventListener('submit', event => { event.preventDefault(); run(async () => {
            const from = $('bankLiquidityFrom').value, to = $('bankLiquidityTo').value;
            if (!from || !to || from > to) throw new Error('Select a valid bank liquidity period.');
            $('bankLiquidityRun').disabled = true; $('bankLiquidityCsv').disabled = true; bankLiquidity = null;
            try {
                const data = await get('/api/finance/accounting/reports/bank-liquidity?' +
                    new URLSearchParams({fromDate:from, toDate:to}));
                const sums = fields.map(() => 0n);
                const rows = (data.banks || []).map(x => {
                    const amounts = fields.map(k => cents(x[k]));
                    if (amounts[0] + amounts[1] - amounts[2] + amounts[3] - amounts[4] !== amounts[5])
                        throw new Error('A bank liquidity row does not reconcile.');
                    amounts.forEach((value, index) => { sums[index] += value; });
                    return '<tr><td>' + esc(x.bankName) + ' — ' + esc(x.ledgerCode) + ' / ' + esc(x.ledgerName) +
                        '</td>' + amounts.map(v => '<td class="text-end">' + money(v) + '</td>').join('') + '</tr>';
                });
                if (fields.some((field, index) => sums[index] !== cents(data[field])) ||
                    sums[3] !== sums[4] || sums[0] + sums[1] - sums[2] !== sums[5])
                    throw new Error('Consolidated bank liquidity totals do not reconcile.');
                $('bankLiquidityRows').innerHTML = rows.join('');
                ['Opening','ExternalIn','ExternalOut','TransferIn','TransferOut','Closing']
                    .forEach((name, index) => { $('bankLiquidity' + name).textContent = money(sums[index]); });
                bankLiquidity = data; $('bankLiquidityCsv').disabled = false;
                notice('Consolidated bank movements reconciled; this is not a classified cash-flow statement.');
            } finally { $('bankLiquidityRun').disabled = false; }
        }); });
        $('bankLiquidityCsv').addEventListener('click', () => run(async () => {
            if (!bankLiquidity) return;
            const rows = [['Bank','Ledger Code','Ledger Name','Opening','Non-bank In','Non-bank Out','Transfer In','Transfer Out','Closing']];
            for (const x of bankLiquidity.banks || [])
                rows.push([x.bankName,x.ledgerCode,x.ledgerName,...fields.map(k => money(x[k]))]);
            rows.push(['INSTITUTION TOTAL','','',...fields.map(k => money(bankLiquidity[k]))]);
            downloadCsv('institution-bank-liquidity-' + $('bankLiquidityFrom').value + '-' +
                $('bankLiquidityTo').value + '.csv', rows);
        }));
    }
    const auditPanel = $('ledgerAuditPanel');
    if (auditPanel) {
        let audit = null, auditPage = 1, auditKey = '', auditRevision = 0;
        const resetAudit = () => {
            auditRevision++;
            audit = null;
            $('ledgerAuditCsv').disabled = true;
            $('ledgerAuditPrevious').disabled = true;
            $('ledgerAuditNext').disabled = true;
            $('ledgerAuditRows').replaceChildren();
            $('ledgerAuditSummary').textContent = '';
            $('ledgerAuditPage').textContent = '';
        };
        ['ledgerAccount','ledgerFrom','ledgerTo'].forEach(id => {
            $(id).addEventListener('change', resetAudit);
            $(id).addEventListener('input', resetAudit);
        });
        const fetchAudit = async page => {
            const account = $('ledgerAccount').value;
            const from = $('ledgerFrom').value, to = $('ledgerTo').value;
            if (!/^\d+$/.test(account) || BigInt(account) <= 0n || !from || !to || from > to)
                throw new Error('Select a valid account and date range.');
            const key = [account, from, to].join('|');
            const revision = ++auditRevision;
            $('ledgerAuditRun').disabled = true;
            $('ledgerAuditCsv').disabled = true;
            audit = null;
            $('ledgerAuditRows').replaceChildren();
            $('ledgerAuditSummary').textContent = 'Loading posted journal activity...';
            try {
                const result = await get('/api/finance/accounting/reports/ledger/' +
                    encodeURIComponent(account) + '/activity?' +
                    new URLSearchParams({fromDate:from,toDate:to,page:String(page),pageSize:'25'}));
                if (revision !== auditRevision || key !==
                    [$('ledgerAccount').value,$('ledgerFrom').value,$('ledgerTo').value].join('|')) return;
                if (cents(result.openingBalance) + cents(result.totalDebit) -
                    cents(result.totalCredit) !== cents(result.closingBalance))
                    throw new Error('Posted journal activity totals do not reconcile.');
                audit = result; auditPage = page; auditKey = key;
                $('ledgerAuditRows').innerHTML = (result.entries.items || []).map(x =>
                    '<tr><td>' + esc(x.date) + '</td><td>' + esc(x.journalNumber) +
                    '</td><td>' + esc(x.lineNo) + '</td><td>' + esc(x.campusId) +
                    '</td><td>' + esc(x.sourceType) + '</td><td>' + esc(x.referenceNumber) +
                    '</td><td>' + esc(x.description) + '</td><td>' +
                    (x.isReversal ? 'Reversal' : x.isReversedOriginal ? 'Reversed original' : 'Posted') +
                    '</td><td class="text-end">' + money(x.debitAmount) +
                    '</td><td class="text-end">' + money(x.creditAmount) + '</td></tr>').join('') ||
                    '<tr><td colspan="10">No posted movements in this period.</td></tr>';
                const total = result.entries.totalCount;
                $('ledgerAuditSummary').textContent = result.accountCode + ' — ' + result.accountName +
                    ': ' + total + ' posted lines; opening BDT ' + money(result.openingBalance) +
                    ', debit ' + money(result.totalDebit) + ', credit ' + money(result.totalCredit) +
                    ', closing ' + money(result.closingBalance) + '.';
                $('ledgerAuditPage').textContent = 'Page ' + page + ' / ' +
                    Math.max(1,Math.ceil(total / result.entries.pageSize));
                $('ledgerAuditPrevious').disabled = page <= 1;
                $('ledgerAuditNext').disabled = page * result.entries.pageSize >= total;
                $('ledgerAuditCsv').disabled = false;
            } finally { $('ledgerAuditRun').disabled = false; }
        };
        $('ledgerAuditRun').addEventListener('click', () => run(async () => {
            resetAudit(); await fetchAudit(1);
        }));
        $('ledgerAuditPrevious').addEventListener('click', () => run(async () => {
            if (audit && auditPage > 1) await fetchAudit(auditPage - 1);
        }));
        $('ledgerAuditNext').addEventListener('click', () => run(async () => {
            if (audit && auditPage * audit.entries.pageSize < audit.entries.totalCount)
                await fetchAudit(auditPage + 1);
        }));
        $('ledgerAuditCsv').addEventListener('click', () => run(async () => {
            if (!audit || auditKey !==
                [$('ledgerAccount').value,$('ledgerFrom').value,$('ledgerTo').value].join('|')) return;
            const rows = [['Date','Journal','Line','Campus ID','Source','Reference','Description','Status','Debit','Credit']];
            for (const x of audit.entries.items || [])
                rows.push([x.date,x.journalNumber,x.lineNo,x.campusId,x.sourceType,x.referenceNumber,
                    x.description,x.isReversal ? 'Reversal' : x.isReversedOriginal ? 'Reversed original' : 'Posted',
                    money(x.debitAmount),money(x.creditAmount)]);
            const pageDebit = (audit.entries.items || []).reduce((sum, x) => sum + cents(x.debitAmount), 0n);
            const pageCredit = (audit.entries.items || []).reduce((sum, x) => sum + cents(x.creditAmount), 0n);
            rows.push(['PAGE TOTAL','','','','','','','',money(pageDebit),money(pageCredit)]);
            downloadCsv('institution-journal-activity-page-' + auditPage + '-' + audit.fromDate +
                '-' + audit.toDate + '.csv', rows);
        }));
    }
    run(loadAccounts);
}
