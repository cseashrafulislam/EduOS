// Read-only institution accounting reports. All amounts stay decimal-safe through CSV and UI.
const root = document.getElementById('institutionAccountingReports');
if (root && document.getElementById('dayBookForm')) {
    const $ = id => document.getElementById(id);
    const esc = v => String(v ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
    const decimalKeys = /("(?:debitAmount|creditAmount|totalDebit|totalCredit|amount|currentAmount|comparisonAmount|variance|currentIncome|comparisonIncome|currentExpenses|comparisonExpenses|currentNetSurplus|comparisonNetSurplus|currentAssets|comparisonAssets|currentLiabilities|comparisonLiabilities|currentEquity|comparisonEquity|currentUnclosedSurplus|comparisonUnclosedSurplus|income|expenses|netSurplus|totalIncome|totalExpenses)"\s*:\s*)(-?\d+(?:\.\d+)?)/g;
    const cents = value => {
        const s = String(value ?? '0');
        if (!/^-?\d+(?:\.\d{1,2})?$/.test(s)) throw new Error('Invalid financial amount precision.');
        const negative = s.startsWith('-'), parts = (negative ? s.slice(1) : s).split('.');
        const result = BigInt(parts[0]) * 100n + BigInt((parts[1] || '').padEnd(2, '0'));
        return negative ? -result : result;
    };
    const money = value => {
        const n = typeof value === 'bigint' ? value : cents(value), abs = n < 0n ? -n : n;
        return (n < 0n ? '-' : '') + (abs / 100n).toString().replace(/\B(?=(\d{3})+(?!\d))/g, ',') + '.' + String(abs % 100n).padStart(2,'0');
    };
    const alert = (message, error = false) => {
        const el = $('reportNotice');
        el.textContent = message;
        el.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
    };
    const run = async fn => { try { await fn(); } catch (err) { alert(err.message || 'Report failed.', true); } };
    const get = async path => {
        const response = await fetch(path, {credentials:'same-origin', headers:{Accept:'application/json'}});
        let value;
        try { value = JSON.parse((await response.text()).replace(decimalKeys,'$1"$2"')); }
        catch { throw new Error('Unexpected accounting report response.'); }
        if (!response.ok || !value.success) throw new Error(value.message || 'Accounting report failed.');
        return value.data;
    };
    const csv = (name, rows) => {
        const cell = v => {
            let text = String(v ?? '');
            if (/^[\s\u0000-\u001f]*[=+\-@]/.test(text)) text = "'" + text;
            return '"' + text.replace(/"/g,'""') + '"';
        };
        const content = '\uFEFF' + rows.map(row => row.map(cell).join(',')).join('\r\n') + '\r\n';
        const url = URL.createObjectURL(new Blob([content],{type:'text/csv;charset=utf-8'}));
        const a = document.createElement('a');
        a.href = url; a.download = name; document.body.appendChild(a); a.click(); a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    };
    const today = new Date().toLocaleDateString('en-CA');
    const yearStart = today.slice(0,4) + '-01-01';
    const previousYear = date => String(Number(date.slice(0,4)) - 1) + date.slice(4);
    const validPeriod = (from, to) => !!from && !!to && from <= to;

    // Central institution-wide income/cost detail: posted GL line is the single
    // monetary truth; source metadata preserves drill-down to the original module.
    $('incomeDetailsFrom').value = yearStart;
    $('incomeDetailsTo').value = today;
    let incomeDetails = null, incomeDetailsPage = 1, incomeDetailsRevision = 0,
        incomeDetailsSignature = '', incomeDetailAccounts = [];
    const incomeDetailsKey = () => ['incomeDetailsFrom','incomeDetailsTo',
        'incomeDetailsCategory','incomeDetailsAccount','incomeDetailsSource'].map(id => $(id).value).join('|');
    const resetIncomeDetails = () => {
        ++incomeDetailsRevision;
        incomeDetails = null;
        $('incomeDetailsCsv').disabled = true;
        $('incomeDetailsPrevious').disabled = true;
        $('incomeDetailsNext').disabled = true;
        $('incomeDetailsRows').replaceChildren();
        $('incomeDetailsJournalPanel').classList.add('d-none');
        $('incomeDetailsPage').textContent = '';
        $('incomeDetailsSummary').textContent = '';
        for (const id of ['incomeDetailsIncome','incomeDetailsExpenses','incomeDetailsNet'])
            $(id).textContent = '—';
    };
    const renderIncomeAccounts = () => {
        const category = $('incomeDetailsCategory').value, selected = $('incomeDetailsAccount').value;
        const active = incomeDetailAccounts.filter(x => !x.isGroup && (x.accountType === 4 || x.accountType === 5) &&
            (category === 'all' || (category === 'income' ? x.accountType === 4 : x.accountType === 5)));
        $('incomeDetailsAccount').innerHTML = '<option value="">All income / expense accounts</option>' +
            active.map(x => '<option value="' + esc(x.id) + '">' + esc(x.code + ' — ' + x.name +
                (x.isActive ? '' : ' (inactive)')) + '</option>').join('');
        if (active.some(x => String(x.id) === selected)) $('incomeDetailsAccount').value = selected;
    };
    const loadIncomeAccounts = async () => {
        let page = 1, totalPages = 1, items = [];
        do {
            const data = await get('/api/finance/accounting/setup/accounts?' +
                new URLSearchParams({page:String(page),pageSize:'100'}));
            items.push(...(data.items || [])); totalPages = data.totalPages || 1; page++;
            if (totalPages > 100) throw new Error('More than 10,000 accounts; use the Chart of Accounts to search.');
        } while (page <= totalPages);
        incomeDetailAccounts = items;
        renderIncomeAccounts();
    };
    for (const id of ['incomeDetailsFrom','incomeDetailsTo','incomeDetailsAccount','incomeDetailsSource']) {
        $(id).addEventListener('input', resetIncomeDetails);
        $(id).addEventListener('change', resetIncomeDetails);
    }
    $('incomeDetailsCategory').addEventListener('change', () => {
        renderIncomeAccounts(); resetIncomeDetails();
    });
    const loadIncomeDetails = async page => {
        const from = $('incomeDetailsFrom').value, to = $('incomeDetailsTo').value;
        const category = $('incomeDetailsCategory').value, accountId = $('incomeDetailsAccount').value;
        const sourceType = $('incomeDetailsSource').value.trim();
        if (!validPeriod(from,to) || !['all','income','expense'].includes(category) ||
            accountId && (!/^\d+$/.test(accountId) || BigInt(accountId) <= 0n))
            throw new Error('Select valid income/cost report filters.');
        const signature = incomeDetailsKey(), revision = ++incomeDetailsRevision;
        $('incomeDetailsRun').disabled = true;
        incomeDetails = null;
        $('incomeDetailsCsv').disabled = true;
        $('incomeDetailsPrevious').disabled = true;
        $('incomeDetailsNext').disabled = true;
        $('incomeDetailsJournalPanel').classList.add('d-none');
        try {
            const query = {fromDate:from,toDate:to,category,page:String(page),pageSize:'25'};
            if (accountId) query.accountId = accountId;
            if (sourceType) query.sourceType = sourceType;
            const data = await get('/api/finance/accounting/reports/income-expense-details?' +
                new URLSearchParams(query));
            if (revision !== incomeDetailsRevision || signature !== incomeDetailsKey()) return;
            if (cents(data.totalIncome) - cents(data.totalExpenses) !== cents(data.netSurplus))
                throw new Error('Central income/cost totals do not reconcile.');
            $('incomeDetailsRows').innerHTML = (data.entries.items || []).map(x => {
                const signed = x.category === 'Income'
                    ? cents(x.creditAmount) - cents(x.debitAmount)
                    : cents(x.debitAmount) - cents(x.creditAmount);
                if (!['Income','Expense'].includes(x.category) || signed !== cents(x.amount))
                    throw new Error('Central financial line classification or amount is invalid.');
                return '<tr><td>' + esc(x.date) + '</td><td>' + esc(x.category) +
                    '</td><td>' + esc(x.accountCode + ' — ' + x.accountName) +
                    '</td><td>' + esc(x.sourceType) + '</td><td>' + esc(x.sourceId) +
                    '</td><td>' + esc(x.journalNumber) + '</td><td>' + esc(x.referenceNumber) +
                    '</td><td>' + esc(x.campusId) + '</td><td>' + esc(x.description) +
                    '</td><td>' + (x.isReversal ? 'Reversal' : x.isReversedOriginal ? 'Reversed original' : 'Posted') +
                    '</td><td class="text-end">' + money(signed) +
                    '</td><td><button type="button" class="btn btn-outline-primary btn-sm" data-central-journal="' +
                    esc(x.journalReference) + '">Journal</button></td></tr>';
            }).join('') || '<tr><td colspan="12">No posted income or expense in the selected scope.</td></tr>';
            $('incomeDetailsIncome').textContent = money(data.totalIncome);
            $('incomeDetailsExpenses').textContent = money(data.totalExpenses);
            $('incomeDetailsNet').textContent = money(data.netSurplus);
            $('incomeDetailsSummary').textContent = data.entries.totalCount +
                ' posted income/expense lines in selected scope. Totals cover all filtered pages.';
            $('incomeDetailsPage').textContent = 'Page ' + page + ' / ' + Math.max(1,data.entries.totalPages);
            $('incomeDetailsPrevious').disabled = page <= 1;
            $('incomeDetailsNext').disabled = page >= data.entries.totalPages;
            incomeDetails = data; incomeDetailsPage = page; incomeDetailsSignature = signature;
            $('incomeDetailsCsv').disabled = false;
            alert('Central income/cost details loaded from the posted institutional GL.');
        } finally { $('incomeDetailsRun').disabled = false; }
    };
    $('incomeDetailsForm').addEventListener('submit', event => { event.preventDefault(); run(async () => {
        resetIncomeDetails(); await loadIncomeDetails(1);
    }); });
    $('incomeDetailsPrevious').addEventListener('click', () => run(async () => {
        if (incomeDetails && incomeDetailsPage > 1) await loadIncomeDetails(incomeDetailsPage - 1);
    }));
    $('incomeDetailsNext').addEventListener('click', () => run(async () => {
        if (incomeDetails && incomeDetailsPage < incomeDetails.entries.totalPages)
            await loadIncomeDetails(incomeDetailsPage + 1);
    }));
    $('incomeDetailsCsv').addEventListener('click', () => {
        if (!incomeDetails || incomeDetailsSignature !== incomeDetailsKey()) return;
        const rows = [['Date','Category','Account Code','Account Name','Source Type','Source ID',
            'Journal','Reference','Campus ID','Line','Description','Status','Debit','Credit','Signed Amount']];
        for (const x of incomeDetails.entries.items)
            rows.push([x.date,x.category,x.accountCode,x.accountName,x.sourceType,x.sourceId,x.journalNumber,
                x.referenceNumber,x.campusId,x.lineNo,x.description,
                x.isReversal ? 'Reversal' : x.isReversedOriginal ? 'Reversed original' : 'Posted',
                money(x.debitAmount),money(x.creditAmount),money(x.amount)]);
        rows.push(['FILTERED PERIOD TOTAL','','','','','','','','','','','',
            money(incomeDetails.totalIncome),money(incomeDetails.totalExpenses),money(incomeDetails.netSurplus)]);
        csv('institution-central-income-cost-page-' + incomeDetailsPage + '-' +
            incomeDetails.fromDate + '-' + incomeDetails.toDate + '.csv',rows);
    });
    $('incomeDetailsRows').addEventListener('click', event => run(async () => {
        const button = event.target.closest('button[data-central-journal]');
        if (!button || !incomeDetails) return;
        const reference = button.dataset.centralJournal;
        if (!incomeDetails.entries.items.some(x => x.journalReference === reference)) return;
        const revision = incomeDetailsRevision;
        button.disabled = true;
        try {
            const detail = await get('/api/finance/accounting/journals/' + encodeURIComponent(reference));
            if (revision !== incomeDetailsRevision || !incomeDetails ||
                !incomeDetails.entries.items.some(x => x.journalReference === reference)) return;
            let debit = 0n, credit = 0n;
            $('incomeDetailsJournalRows').innerHTML = (detail.lines || []).map(x => {
                debit += cents(x.debitAmount); credit += cents(x.creditAmount);
                return '<tr><td>' + esc(x.lineNo) + '</td><td>' + esc(x.accountCode) +
                    '</td><td>' + esc(x.accountName) + '</td><td>' + esc(x.description) +
                    '</td><td class="text-end">' + money(x.debitAmount) +
                    '</td><td class="text-end">' + money(x.creditAmount) + '</td></tr>';
            }).join('');
            if (!detail.lines?.length || debit <= 0n || debit !== credit ||
                debit !== cents(detail.totalDebit) || credit !== cents(detail.totalCredit))
                throw new Error('Selected journal details are not balanced.');
            $('incomeDetailsJournalTitle').textContent = detail.journalNumber + ' — ' + detail.journalDate;
            $('incomeDetailsJournalPanel').classList.remove('d-none');
        } finally { button.disabled = false; }
    }));
    $('incomeDetailsJournalClose').addEventListener('click', () =>
        $('incomeDetailsJournalPanel').classList.add('d-none'));
    run(loadIncomeAccounts);

    $('dayBookFrom').value = yearStart;
    $('dayBookTo').value = today;
    let dayBook = null, dayPage = 1, dayRevision = 0, daySignature = '';
    const signature = () => $('dayBookFrom').value + '|' + $('dayBookTo').value;
    const clearDayBook = () => {
        ++dayRevision;
        dayBook = null;
        $('dayBookCsv').disabled = true;
        $('dayBookPrevious').disabled = true;
        $('dayBookNext').disabled = true;
        $('dayBookRows').replaceChildren();
        $('dayBookSummary').textContent = '';
        $('dayBookPage').textContent = '';
        $('dayBookDebit').textContent = '';
        $('dayBookCredit').textContent = '';
        $('dayBookDetail').classList.add('d-none');
    };
    for (const id of ['dayBookFrom','dayBookTo']) {
        $(id).addEventListener('input', clearDayBook);
        $(id).addEventListener('change', clearDayBook);
    }
    const loadDayBook = async page => {
        const from = $('dayBookFrom').value, to = $('dayBookTo').value;
        if (!validPeriod(from,to)) throw new Error('Select a valid day book period.');
        const requestSignature = signature(), revision = ++dayRevision;
        dayBook = null;
        $('dayBookDetail').classList.add('d-none');
        $('dayBookRun').disabled = true;
        $('dayBookCsv').disabled = true;
        $('dayBookPrevious').disabled = true;
        $('dayBookNext').disabled = true;
        try {
            const data = await get('/api/finance/accounting/reports/day-book?' +
                new URLSearchParams({fromDate:from,toDate:to,page:String(page),pageSize:'25'}));
            if (revision !== dayRevision || requestSignature !== signature()) return;
            if (cents(data.totalDebit) !== cents(data.totalCredit))
                throw new Error('Day book totals are not balanced.');
            for (const x of data.journals.items) {
                if (x.lineCount < 2 || cents(x.debitAmount) <= 0n || cents(x.debitAmount) !== cents(x.creditAmount))
                    throw new Error('Day book contains an invalid journal.');
            }
            $('dayBookRows').innerHTML = (data.journals.items || []).map(x =>
                '<tr><td>' + esc(x.date) + '</td><td>' + esc(x.journalNumber) +
                '</td><td>' + esc(x.campusId) + '</td><td>' + esc(x.sourceType) +
                '</td><td>' + esc(x.referenceNumber) + '</td><td>' + esc(x.description) +
                '</td><td>' + (x.isReversal ? 'Reversal' : x.isReversedOriginal ? 'Reversed original' : 'Posted') +
                '</td><td class="text-end">' + esc(x.lineCount) + '</td><td class="text-end">' +
                money(x.debitAmount) + '</td><td class="text-end">' + money(x.creditAmount) +
                '</td><td><button type="button" class="btn btn-outline-primary btn-sm" data-journal-ref="' +
                esc(x.journalReference) + '">Details</button></td></tr>').join('') ||
                '<tr><td colspan="11">No posted journals in this period.</td></tr>';
            $('dayBookDebit').textContent = money(data.totalDebit);
            $('dayBookCredit').textContent = money(data.totalCredit);
            $('dayBookSummary').textContent = data.journals.totalCount + ' posted/reversed journal records; full period: ' + from + ' to ' + to;
            $('dayBookPage').textContent = 'Page ' + page + ' / ' + Math.max(1, data.journals.totalPages);
            $('dayBookPrevious').disabled = page <= 1;
            $('dayBookNext').disabled = page >= data.journals.totalPages;
            dayPage = page; dayBook = data; daySignature = requestSignature;
            $('dayBookCsv').disabled = false;
            alert('Posted day book reconciled. CSV exports only this page; full-period totals are shown separately.');
        } finally { $('dayBookRun').disabled = false; }
    };
    $('dayBookForm').addEventListener('submit', event => { event.preventDefault(); run(async () => {
        clearDayBook(); await loadDayBook(1);
    }); });
    $('dayBookPrevious').addEventListener('click', () => run(async () => {
        if (dayBook && dayPage > 1) await loadDayBook(dayPage - 1);
    }));
    $('dayBookNext').addEventListener('click', () => run(async () => {
        if (dayBook && dayPage < dayBook.journals.totalPages) await loadDayBook(dayPage + 1);
    }));
    $('dayBookCsv').addEventListener('click', () => {
        if (!dayBook || daySignature !== signature()) return;
        const rows = [['Date','Journal','Campus ID','Source','Reference','Description','Status','Lines','Debit','Credit']];
        for (const x of dayBook.journals.items)
            rows.push([x.date,x.journalNumber,x.campusId,x.sourceType,x.referenceNumber,x.description,
                x.isReversal ? 'Reversal' : x.isReversedOriginal ? 'Reversed original' : 'Posted',
                x.lineCount,money(x.debitAmount),money(x.creditAmount)]);
        rows.push(['FULL PERIOD TOTAL','','','','','','','',money(dayBook.totalDebit),money(dayBook.totalCredit)]);
        csv('institution-day-book-page-' + dayPage + '-' + dayBook.fromDate + '-' + dayBook.toDate + '.csv',rows);
    });
    $('dayBookRows').addEventListener('click', event => run(async () => {
        const button = event.target.closest('button[data-journal-ref]');
        if (!button || !dayBook) return;
        const reference = button.dataset.journalRef;
        const summary = dayBook.journals.items.find(x => x.journalReference === reference);
        if (!summary) return;
        const revision = dayRevision;
        button.disabled = true;
        try {
            const detail = await get('/api/finance/accounting/journals/' + encodeURIComponent(reference));
            if (revision !== dayRevision || !dayBook || !dayBook.journals.items.some(x => x.journalReference === reference)) return;
            let debit = 0n, credit = 0n;
            $('dayBookDetailRows').innerHTML = (detail.lines || []).map(line => {
                debit += cents(line.debitAmount); credit += cents(line.creditAmount);
                return '<tr><td>' + esc(line.lineNo) + '</td><td>' + esc(line.accountCode) +
                    '</td><td>' + esc(line.accountName) + '</td><td>' + esc(line.description) +
                    '</td><td class="text-end">' + money(line.debitAmount) + '</td><td class="text-end">' +
                    money(line.creditAmount) + '</td></tr>';
            }).join('');
            if ((detail.lines || []).length !== summary.lineCount ||
                debit !== credit || debit !== cents(summary.debitAmount))
                throw new Error('Selected journal detail does not reconcile with the day book.');
            $('dayBookDetailTitle').textContent = summary.journalNumber + ' — ' + summary.date +
                ' | BDT ' + money(debit);
            $('dayBookDetail').classList.remove('d-none');
        } finally { button.disabled = false; }
    }));
    $('dayBookDetailClose').addEventListener('click', () => $('dayBookDetail').classList.add('d-none'));

    const compare = (kind, fieldNames, totalRows, url, parameters) => {
        const form = $('compare' + kind + 'Form'), button = $('compare' + kind + 'Run'), exportButton = $('compare' + kind + 'Csv');
        let result = null, savedSignature = '';
        const currentSignature = () => fieldNames.map(id => $(id).value).join('|');
        const reset = () => { result = null; exportButton.disabled = true;
            $('compare' + kind + 'Rows').replaceChildren(); $('compare' + kind + 'Totals').replaceChildren(); };
        for (const id of fieldNames) {
            $(id).addEventListener('input', reset);
            $(id).addEventListener('change', reset);
        }
        const verifiedTotals = data => {
            const tuples = totalRows.map(([label,currentKey,previousKey]) =>
                [label,cents(data[currentKey]),cents(data[previousKey])]);
            if (kind === 'Balance') {
                if (tuples[0][1] !== tuples[1][1] + tuples[2][1] + tuples[3][1] ||
                    tuples[0][2] !== tuples[1][2] + tuples[2][2] + tuples[3][2])
                    throw new Error('A comparative balance sheet does not reconcile.');
            } else if (tuples[0][1] - tuples[1][1] !== tuples[2][1] ||
                       tuples[0][2] - tuples[1][2] !== tuples[2][2])
                throw new Error('A comparative income statement does not reconcile.');
            return tuples;
        };
        form.addEventListener('submit', event => { event.preventDefault(); run(async () => {
            const dates = fieldNames.map(id => $(id).value);
            if (dates.some(d => !d) || (kind === 'Income' &&
                (!validPeriod(dates[0],dates[1]) || !validPeriod(dates[2],dates[3]))))
                throw new Error('Select valid comparison dates.');
            reset(); button.disabled = true;
            const revision = currentSignature();
            try {
                const data = await get(url + '?' + new URLSearchParams(parameters(dates)));
                if (revision !== currentSignature()) return;
                const totals = verifiedTotals(data);
                for (const row of data.accounts || []) {
                    if (cents(row.currentAmount) - cents(row.comparisonAmount) !== cents(row.variance))
                        throw new Error('Comparison variance does not reconcile.');
                }
                $('compare' + kind + 'Rows').innerHTML = (data.accounts || []).map(x =>
                    '<tr><td>' + esc(x.section) + '</td><td>' + esc(x.accountCode) +
                    '</td><td>' + esc(x.accountName) + '</td><td class="text-end">' + money(x.currentAmount) +
                    '</td><td class="text-end">' + money(x.comparisonAmount) +
                    '</td><td class="text-end">' + money(x.variance) + '</td></tr>').join('') ||
                    '<tr><td colspan="6">No posted account movements.</td></tr>';
                $('compare' + kind + 'Totals').innerHTML = totals.map(t =>
                    '<tr><th colspan="3">' + esc(t[0]) + '</th><th class="text-end">' + money(t[1]) +
                    '</th><th class="text-end">' + money(t[2]) + '</th><th class="text-end">' +
                    money(t[1] - t[2]) + '</th></tr>').join('');
                result = data; savedSignature = revision; exportButton.disabled = false;
                alert('Both periods have been reconciled independently.');
            } finally { button.disabled = false; }
        }); });
        exportButton.addEventListener('click', () => {
            if (!result || savedSignature !== currentSignature()) return;
            const totals = verifiedTotals(result);
            const rows = [['Section','Account Code','Account','Current BDT','Comparison BDT','Variance BDT']];
            for (const x of result.accounts || [])
                rows.push([x.section,x.accountCode,x.accountName,money(x.currentAmount),
                    money(x.comparisonAmount),money(x.variance)]);
            for (const t of totals)
                rows.push([t[0],'','',money(t[1]),money(t[2]),money(t[1]-t[2])]);
            csv('institution-comparative-' + kind.toLowerCase() + '-' + fieldNames.map(id => $(id).value).join('-') + '.csv',rows);
        });
    };
    $('compareIncomeFrom').value = yearStart;
    $('compareIncomeTo').value = today;
    $('compareIncomePreviousFrom').value = previousYear(yearStart);
    $('compareIncomePreviousTo').value = previousYear(today);
    compare('Income', ['compareIncomeFrom','compareIncomeTo','compareIncomePreviousFrom','compareIncomePreviousTo'],
        [['Total income','currentIncome','comparisonIncome'],
         ['Total expenses','currentExpenses','comparisonExpenses'],
         ['Net surplus / (deficit)','currentNetSurplus','comparisonNetSurplus']],
        '/api/finance/accounting/reports/comparative-income-statement',
        dates => ({fromDate:dates[0],toDate:dates[1],comparisonFrom:dates[2],comparisonTo:dates[3]}));
    $('compareBalanceAsOf').value = today;
    $('compareBalancePreviousAsOf').value = previousYear(today);
    compare('Balance', ['compareBalanceAsOf','compareBalancePreviousAsOf'],
        [['Total assets','currentAssets','comparisonAssets'],
         ['Total liabilities','currentLiabilities','comparisonLiabilities'],
         ['Posted equity','currentEquity','comparisonEquity'],
         ['Unclosed surplus','currentUnclosedSurplus','comparisonUnclosedSurplus']],
        '/api/finance/accounting/reports/comparative-balance-sheet',
        dates => ({asOfDate:dates[0],comparisonAsOfDate:dates[1]}));

    $('breakdownFrom').value = yearStart;
    $('breakdownTo').value = today;
    let breakdown = null, breakdownSignature = '';
    const breakdownKey = () => ['breakdownFrom','breakdownTo','breakdownGroup'].map(id => $(id).value).join('|');
    const resetBreakdown = () => { breakdown = null; $('breakdownCsv').disabled = true;
        $('breakdownRows').replaceChildren();
        for (const id of ['breakdownIncome','breakdownExpenses','breakdownNet']) $(id).textContent = ''; };
    for (const id of ['breakdownFrom','breakdownTo','breakdownGroup']) {
        $(id).addEventListener('input', resetBreakdown);
        $(id).addEventListener('change', resetBreakdown);
    }
    $('breakdownForm').addEventListener('submit', event => { event.preventDefault(); run(async () => {
        const from = $('breakdownFrom').value, to = $('breakdownTo').value, group = $('breakdownGroup').value;
        if (!validPeriod(from,to) || !['campus','source'].includes(group))
            throw new Error('Select a valid accounting period and grouping.');
        resetBreakdown(); $('breakdownRun').disabled = true;
        const signature = breakdownKey();
        try {
            const data = await get('/api/finance/accounting/reports/income-expense-breakdown?' +
                new URLSearchParams({fromDate:from,toDate:to,groupBy:group}));
            if (signature !== breakdownKey()) return;
            let income = 0n, expenses = 0n, net = 0n;
            $('breakdownRows').innerHTML = (data.rows || []).map(x => {
                const i = cents(x.income), e = cents(x.expenses), n = cents(x.netSurplus);
                if (i - e !== n) throw new Error('Grouped surplus does not reconcile.');
                income += i; expenses += e; net += n;
                return '<tr><td>' + esc(x.group) + '</td><td class="text-end">' + money(i) +
                    '</td><td class="text-end">' + money(e) + '</td><td class="text-end">' + money(n) + '</td></tr>';
            }).join('') || '<tr><td colspan="4">No posted income or expense for this period.</td></tr>';
            if (income !== cents(data.totalIncome) || expenses !== cents(data.totalExpenses) ||
                net !== cents(data.netSurplus) || income - expenses !== net)
                throw new Error('Campus/source breakdown does not match institutional P&L.');
            $('breakdownIncome').textContent = money(income);
            $('breakdownExpenses').textContent = money(expenses);
            $('breakdownNet').textContent = money(net);
            breakdown = data; breakdownSignature = signature; $('breakdownCsv').disabled = false;
            alert('Campus/source breakdown reconciled with the institutional income statement.');
        } finally { $('breakdownRun').disabled = false; }
    }); });
    $('breakdownCsv').addEventListener('click', () => {
        if (!breakdown || breakdownSignature !== breakdownKey()) return;
        const rows = [['Campus or Source','Income BDT','Expenses BDT','Net Surplus BDT']];
        for (const x of breakdown.rows || [])
            rows.push([x.group,money(x.income),money(x.expenses),money(x.netSurplus)]);
        rows.push(['INSTITUTION TOTAL',money(breakdown.totalIncome),
            money(breakdown.totalExpenses),money(breakdown.netSurplus)]);
        csv('institution-income-expense-' + breakdown.groupBy + '-' + breakdown.fromDate + '-' + breakdown.toDate + '.csv',rows);
    });
}
