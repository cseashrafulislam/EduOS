(() => {
    'use strict';
    if (!document.getElementById('discountAwardRegister')) return;
    const el = id => document.getElementById(id);
    const state = { report: null, filters: null, page: 1, generation: 0 };
    const cell = value => { const td = document.createElement('td'); td.textContent = String(value ?? ''); return td; };
    function notice(message, error = false) {
        const n = el('awardNotice');
        n.className = 'alert ' + (error ? 'alert-danger' : 'alert-info');
        n.textContent = message;
    }
    function clear() {
        state.report = null;
        for (const id of ['awardExport', 'awardPrev', 'awardNext']) el(id).disabled = true;
        el('awardTotals').textContent = 'No register loaded';
        el('awardPage').textContent = '—';
        const tr = document.createElement('tr'), td = cell('Run the register.');
        td.colSpan = 8; tr.append(td); el('awardRows').replaceChildren(tr);
    }
    function filters() {
        const campusId = el('awardCampus').value.trim(), ruleId = el('awardRule').value.trim();
        if ([campusId, ruleId].some(v => v && (!/^\d+$/.test(v) || !Number.isSafeInteger(Number(v)) || Number(v) < 1)))
            throw Error('Enter valid positive campus and rule IDs.');
        return { campusId, ruleId, active: el('awardState').value,
            search: el('awardSearch').value.trim(), pageSize: el('awardSize').value };
    }
    function render(data) {
        const rows = data.awards.items || [], body = el('awardRows');
        body.replaceChildren();
        for (const x of rows) {
            const tr = document.createElement('tr');
            [x.studentCode, x.campusName, x.ruleName, String(x.ruleValue) + (x.isPercentage ? '%' : ' BDT'),
                String(x.effectiveFrom).slice(0, 10), x.effectiveTo ? String(x.effectiveTo).slice(0, 10) : 'Open',
                x.isActive ? 'Enabled' : 'Deactivated', x.approvedByUserId]
                .forEach(v => tr.append(cell(v)));
            body.append(tr);
        }
        if (!rows.length) { const tr = document.createElement('tr'), td = cell('No matching awards.');
            td.colSpan = 8; tr.append(td); body.append(tr); }
        state.report = data; state.page = data.awards.page;
        const pages = Math.max(1, data.awards.totalPages);
        el('awardTotals').textContent = data.totalCount + ' awards (' + data.activeCount +
            ' enabled, ' + data.inactiveCount + ' deactivated)';
        el('awardPage').textContent = 'Page ' + state.page + ' of ' + pages;
        el('awardPrev').disabled = state.page <= 1;
        el('awardNext').disabled = state.page >= pages;
        el('awardExport').disabled = rows.length === 0;
    }
    async function load(page) {
        const generation = ++state.generation; clear(); el('awardRun').disabled = true;
        try {
            const q = new URLSearchParams({ page: String(page), pageSize: state.filters.pageSize });
            for (const key of ['campusId', 'ruleId', 'active', 'search'])
                if (state.filters[key]) q.set(key, state.filters[key]);
            const response = await fetch('/api/finance/adjustments/discount-awards?' + q,
                { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
            const result = await response.json();
            if (generation !== state.generation) return;
            if (!response.ok || !result.success) throw Error(result.message || 'Register unavailable.');
            render(result.data); notice('Awarded policy, not posted invoice discount.');
        } catch (error) {
            if (generation === state.generation) notice(error.message || 'Register unavailable.', true);
        } finally { if (generation === state.generation) el('awardRun').disabled = false; }
    }
    function csv(value) {
        let s = String(value ?? '');
        if (/^[\s\u0000-\u001f]*[=+\-@]/.test(s)) s = "'" + s;
        return '"' + s.replace(/"/g, '""') + '"';
    }
    function exportPage() {
        if (!state.report) return;
        const lines = [['Student code','Campus','Rule','Value','Unit','From','To','Status','Approver ID']];
        for (const x of state.report.awards.items || [])
            lines.push([x.studentCode, x.campusName, x.ruleName, x.ruleValue,
                x.isPercentage ? 'Percent' : 'BDT', x.effectiveFrom, x.effectiveTo || '',
                x.isActive ? 'Enabled' : 'Deactivated', x.approvedByUserId]);
        const blob = new Blob(['\uFEFF' + lines.map(row => row.map(csv).join(',')).join('\r\n') + '\r\n'],
            { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob), a = document.createElement('a');
        a.href = url; a.download = 'discount-awards-page-' + state.page + '.csv';
        document.body.append(a); a.click(); a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    el('awardFilters').addEventListener('submit', e => {
        e.preventDefault();
        try { state.filters = filters(); load(1); }
        catch (error) { ++state.generation; clear(); notice(error.message, true); }
    });
    for (const id of ['awardCampus','awardRule','awardState','awardSearch','awardSize'])
        el(id).addEventListener('input', () => { ++state.generation; clear(); });
    el('awardPrev').addEventListener('click', () => { if (state.report && state.page > 1) load(state.page - 1); });
    el('awardNext').addEventListener('click', () => {
        if (state.report && state.page < state.report.awards.totalPages) load(state.page + 1);
    });
    el('awardExport').addEventListener('click', exportPage);
    clear();
})();
