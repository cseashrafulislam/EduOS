(() => {
    'use strict';
    const root = document.getElementById('dailyCashCollections');
    if (!root) return;
    const el = id => document.getElementById(id);
    const state = { filters: null, report: null, page: 1, generation: 0 };
    const moneyKeys = /("(?:grossCollected|amount)"\s*:\s*)(-?\d+(?:\.\d+)?)/g;
    function money(value) {
        const raw = String(value ?? '0');
        if (!/^-?\d+(?:\.\d{1,2})?$/.test(raw)) throw Error('Invalid amount precision.');
        const negative = raw.startsWith('-');
        const parts = (negative ? raw.slice(1) : raw).split('.');
        const cents = BigInt(parts[0]) * 100n + BigInt((parts[1] || '').padEnd(2, '0'));
        return (negative ? '-' : '') + (cents / 100n).toString() + '.' +
            (cents % 100n).toString().padStart(2, '0');
    }
    function notice(message, error = false) {
        const n = el('dailyCashNotice');
        n.className = 'alert ' + (error ? 'alert-danger' : 'alert-info');
        n.textContent = message;
    }
    function cell(value) {
        const td = document.createElement('td');
        td.textContent = String(value ?? '');
        return td;
    }
    function empty(target, columns, message) {
        const row = document.createElement('tr'), td = cell(message);
        td.colSpan = columns; row.append(td); el(target).replaceChildren(row);
    }
    function filters() {
        const fromDate = el('dailyCashFrom').value, toDate = el('dailyCashTo').value;
        const campusId = el('dailyCashCampus').value;
        if (!/^\d{4}-\d{2}-\d{2}$/.test(fromDate) || !/^\d{4}-\d{2}-\d{2}$/.test(toDate) ||
            fromDate > toDate || Number(fromDate.slice(0, 4)) < 2000 ||
            Number(toDate.slice(0, 4)) > 2200)
            throw Error('Choose valid collection dates.');
        const start = Date.parse(fromDate + 'T00:00:00Z');
        const end = Date.parse(toDate + 'T00:00:00Z');
        if ((end - start) / 86400000 > 30) throw Error('Maximum reporting range is 31 days.');
        return { fromDate, toDate, campusId };
    }
    function clear() {
        state.report = null;
        el('dailyCashExport').disabled = true;
        el('dailyCashPrev').disabled = true;
        el('dailyCashNext').disabled = true;
        empty('dailyCashSummary', 4, 'Run report.');
        empty('dailyCashRows', 8, 'Run report.');
        el('dailyCashPage').textContent = '—';
    }
    function render(data) {
        const selected = el('dailyCashCampus').value;
        const select = el('dailyCashCampus');
        select.replaceChildren();
        const all = document.createElement('option');
        all.value = ''; all.textContent = 'All accessible campuses'; select.append(all);
        for (const campus of data.campuses || []) {
            const option = document.createElement('option');
            option.value = String(campus.id); option.textContent = campus.name; select.append(option);
        }
        if ([...select.options].some(x => x.value === selected)) select.value = selected;
        el('dailyCashSummary').replaceChildren();
        for (const item of data.summary || []) {
            const tr = document.createElement('tr');
            [item.campusName, item.currencyCode, item.receiptCount, money(item.grossCollected)]
                .forEach(x => tr.append(cell(x)));
            el('dailyCashSummary').append(tr);
        }
        if (!el('dailyCashSummary').children.length) empty('dailyCashSummary', 4, 'No eligible receipts.');
        el('dailyCashRows').replaceChildren();
        const receipts = data.receipts;
        for (const item of receipts.items || []) {
            const tr = document.createElement('tr');
            [String(item.paymentDate).slice(0, 10), item.receiptNumber, item.studentCode,
                item.campusName, item.receivedByUserId, money(item.amount),
                item.paymentState, item.hasJournalLink ? 'Linked (not verified)' : 'Not linked']
                .forEach(x => tr.append(cell(x)));
            el('dailyCashRows').append(tr);
        }
        if (!el('dailyCashRows').children.length) empty('dailyCashRows', 8, 'No receipts on this page.');
        state.page = receipts.page;
        el('dailyCashPage').textContent = 'Page ' + state.page + ' of ' + Math.max(1, receipts.totalPages) +
            ' (' + receipts.totalCount + ' receipts)';
        el('dailyCashPrev').disabled = state.page <= 1;
        el('dailyCashNext').disabled = state.page >= receipts.totalPages;
        el('dailyCashExport').disabled = !(receipts.items || []).length;
        state.report = data;
    }
    async function load(page) {
        const generation = ++state.generation;
        clear();
        el('dailyCashRun').disabled = true;
        try {
            const query = new URLSearchParams({ fromDate: state.filters.fromDate,
                toDate: state.filters.toDate, page: String(page), pageSize: '25' });
            if (state.filters.campusId) query.set('campusId', state.filters.campusId);
            const response = await fetch('/api/finance/receivables-aging/daily-cash-collections?' + query,
                { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
            const raw = await response.text();
            const result = JSON.parse(raw.replace(moneyKeys, '$1"$2"'));
            if (generation !== state.generation) return;
            if (!response.ok || !result.success) throw Error(result.message || 'Report unavailable.');
            render(result.data);
            notice('Gross allocated cash receipts. Refunds and GL settlement require separate reconciliation.');
        } catch (error) {
            if (generation === state.generation) notice(error.message || 'Report failed.', true);
        } finally {
            if (generation === state.generation) el('dailyCashRun').disabled = false;
        }
    }
    function csvField(value) {
        let s = String(value ?? '');
        if (/^[\s\u0000-\u001f]*[=+\-@]/.test(s)) s = "'" + s;
        return '"' + s.replace(/"/g, '""') + '"';
    }
    function exportPage() {
        if (!state.report) return;
        const rows = [['Date','Receipt','Student code','Campus','Cashier user ID',
            'Gross BDT','Payment state','Journal link']];
        for (const x of state.report.receipts.items || []) rows.push([
            x.paymentDate, x.receiptNumber, x.studentCode, x.campusName,
            x.receivedByUserId, money(x.amount), x.paymentState,
            x.hasJournalLink ? 'Linked (not verified)' : 'Not linked']);
        const csv = '\uFEFF' + rows.map(r => r.map(csvField).join(',')).join('\r\n') + '\r\n';
        const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
        const link = document.createElement('a');
        link.href = url;
        link.download = 'daily-cash-collections-' + state.filters.fromDate + '-' +
            state.filters.toDate + '-page-' + state.page + '.csv';
        document.body.append(link); link.click(); link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    el('dailyCashFilters').addEventListener('submit', event => {
        event.preventDefault();
        try { state.filters = filters(); load(1); }
        catch (error) { clear(); notice(error.message, true); }
    });
    for (const id of ['dailyCashFrom', 'dailyCashTo', 'dailyCashCampus']) {
        el(id).addEventListener('change', () => { ++state.generation; clear(); });
    }
    el('dailyCashPrev').addEventListener('click', () => {
        if (state.report && state.page > 1) load(state.page - 1);
    });
    el('dailyCashNext').addEventListener('click', () => {
        if (state.report && state.page < state.report.receipts.totalPages) load(state.page + 1);
    });
    el('dailyCashExport').addEventListener('click', exportPage);
    const now = new Date();
    const today = [now.getFullYear(), String(now.getMonth() + 1).padStart(2, '0'),
        String(now.getDate()).padStart(2, '0')].join('-');
    el('dailyCashFrom').value = today;
    el('dailyCashTo').value = today;
    clear();
})();
