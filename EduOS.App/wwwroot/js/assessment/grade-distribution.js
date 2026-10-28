(() => {
    'use strict';
    const root = document.getElementById('gradeDistributionWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const state = { page: 1, pageSize: 30, total: 0, scopes: [], versions: [], report: null, comparison: null, busy: false, sequence: 0 };
    const cell = (value, right = false) => {
        const td = document.createElement('td');
        td.textContent = value == null ? '—' : String(value);
        if (right) td.className = 'text-end';
        return td;
    };
    function notice(message, error = false) {
        const box = el('gradeDistributionNotice');
        box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        box.textContent = message;
    }
    async function api(path) {
        const response = await fetch(path, { credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json' } });
        const body = await response.json().catch(() => null);
        if (!response.ok || body?.success !== true) throw new Error(body?.message || 'Report request failed.');
        return body.data;
    }
    function clearReport() {
        state.report = null; state.comparison = null;
        el('gradeSingle').classList.remove('d-none');
        el('gradeCompare').classList.add('d-none');
        el('gradeCompareRows').replaceChildren();
        el('gradeReportScope').textContent = '';
        el('gradeTotals').textContent = 'Select a finalized publication.';
        const tbody = el('gradeRows');
        tbody.replaceChildren();
        const tr = document.createElement('tr'), td = cell('No report loaded.');
        td.colSpan = 6; tr.append(td); tbody.append(tr);
        el('gradeCsv').disabled = true; el('gradePrint').disabled = true;
    }
    async function scopes() {
        const sequence = ++state.sequence;
        clearReport();
        try {
            const params = new URLSearchParams({ page: String(state.page),
                pageSize: String(state.pageSize), search: el('gradeScopeTerm').value.trim() });
            const data = await api('/api/assessment-administration/scopes/page?' + params);
            if (sequence !== state.sequence) return;
            state.scopes = data.items || []; state.total = data.totalCount || 0;
            const select = el('gradeScope');
            select.replaceChildren(new Option('Select authorized assessment / batch', ''));
            const seen = new Set();
            for (const item of state.scopes) {
                const key = item.assessmentId + ':' + item.academicBatchId;
                if (seen.has(key)) continue;
                seen.add(key);
                select.add(new Option(item.assessmentName + ' / ' + item.academicBatchName, key));
            }
            el('gradeScopePage').textContent = state.total + ' scopes · Page ' + state.page;
            el('gradeScopePrev').disabled = state.page <= 1;
            el('gradeScopeNext').disabled = state.page * state.pageSize >= state.total;
            el('gradeVersion').replaceChildren(new Option('Select scope first', ''));
            el('gradeVersion').disabled = true; el('gradeLoad').disabled = true;
            el('gradeBaseline').replaceChildren(new Option('Single version', ''));
            el('gradeBaseline').disabled = true;
        } catch (error) { if (sequence === state.sequence) notice(error.message, true); }
    }
    async function versions() {
        clearReport();
        const select = el('gradeVersion');
        select.replaceChildren(new Option('Loading versions…', ''));
        select.disabled = true; el('gradeLoad').disabled = true;
        el('gradeBaseline').replaceChildren(new Option('Single version', ''));
        el('gradeBaseline').disabled = true;
        const value = el('gradeScope').value.split(':').map(Number);
        if (value.length !== 2 || value.some(x => !Number.isSafeInteger(x) || x <= 0)) return;
        const sequence = ++state.sequence;
        try {
            const params = new URLSearchParams({ assessmentId: String(value[0]),
                academicBatchId: String(value[1]), page: '1', pageSize: '100' });
            const data = await api('/api/assessment-administration/results/publications?' + params);
            if (sequence !== state.sequence) return;
            state.versions = (data.items || []).filter(x => x.state === 2 || x.state === 3 ||
                x.state === 'Published' || x.state === 'Withdrawn');
            select.replaceChildren(new Option('Select finalized version', ''));
            for (const version of state.versions)
                select.add(new Option('Version ' + version.versionNo + ' · ' +
                    (version.state === 3 || version.state === 'Withdrawn' ? 'Withdrawn' : 'Published'), String(version.id)));
            select.disabled = !state.versions.length;
            const baseline = el('gradeBaseline');
            baseline.replaceChildren(new Option('Single version', ''));
            for (const version of state.versions)
                baseline.add(new Option('Version ' + version.versionNo, String(version.id)));
            baseline.disabled = !state.versions.length;
        } catch (error) { if (sequence === state.sequence) notice(error.message, true); }
    }
    async function load() {
        const id = Number(el('gradeVersion').value);
        const earlier = el('gradeBaseline').value ? Number(el('gradeBaseline').value) : null;
        if (!Number.isSafeInteger(id) || id <= 0 || state.busy) return;
        if (earlier != null) {
            const before = state.versions.find(x => x.id === earlier);
            const after = state.versions.find(x => x.id === id);
            if (!before || !after || before.versionNo >= after.versionNo) {
                notice('Choose an earlier finalized version for comparison.', true);
                return;
            }
        }
        state.busy = true; el('gradeLoad').disabled = true;
        clearReport();
        const sequence = ++state.sequence;
        try {
            const path = earlier == null
                ? '/api/assessment-administration/results/grade-distribution?publicationId=' + encodeURIComponent(String(id))
                : '/api/assessment-administration/results/grade-distribution/compare?' +
                    new URLSearchParams({ baselinePublicationId: String(earlier), targetPublicationId: String(id) });
            const data = await api(path);
            if (sequence !== state.sequence || String(id) !== el('gradeVersion').value ||
                (earlier == null ? '' : String(earlier)) !== el('gradeBaseline').value) return;
            if (earlier != null) {
                state.comparison = data;
                el('gradeSingle').classList.add('d-none');
                el('gradeCompare').classList.remove('d-none');
                el('gradeCompareScope').textContent = data.assessmentName + ' / ' +
                    data.academicBatchName + ' · Version ' + data.baselineVersionNo +
                    ' → ' + data.targetVersionNo;
                el('gradeCompareTotals').textContent = 'Students: ' + data.baselineStudentCount +
                    ' → ' + data.targetStudentCount + ' (change ' + data.studentCountChange + ')' +
                    ' · Passed: ' + data.baselinePassedCount + ' → ' + data.targetPassedCount +
                    ' · Failed: ' + data.baselineFailedCount + ' → ' + data.targetFailedCount +
                    ' · Withheld: ' + data.baselineWithheldCount + ' → ' + data.targetWithheldCount;
                const tbody = el('gradeCompareRows'); tbody.replaceChildren();
                for (const row of data.grades || []) {
                    const tr = document.createElement('tr');
                    tr.append(cell(row.gradeLetter), cell(row.isWithheld ? 'Withheld' : row.isPassed ? 'Passed' : 'Failed'),
                        cell(row.baselineCount, true), cell(row.targetCount, true),
                        cell(row.countChange, true), cell(row.obtainedMarksChange, true),
                        cell(row.averagePercentageChange, true),
                        cell(row.baselineAverageGpa, true), cell(row.targetAverageGpa, true));
                    tbody.append(tr);
                }
                if (!tbody.children.length) {
                    const tr = document.createElement('tr'), td = cell('No saved result rows.');
                    td.colSpan = 9; tr.append(td); tbody.append(tr);
                }
            } else {
                state.report = data;
                el('gradeReportScope').textContent = data.assessmentName + ' / ' +
                    data.academicBatchName + ' · Version ' + data.versionNo;
                el('gradeTotals').textContent = 'Students: ' + data.studentCount +
                    ' · Passed: ' + data.passedCount + ' · Failed: ' + data.failedCount +
                    ' · Withheld: ' + data.withheldCount;
                const tbody = el('gradeRows'); tbody.replaceChildren();
                for (const row of data.grades || []) {
                    const tr = document.createElement('tr');
                    tr.append(cell(row.gradeLetter), cell(row.isWithheld ? 'Withheld' : row.isPassed ? 'Passed' : 'Failed'),
                        cell(row.studentCount, true), cell(row.totalObtainedMarks, true),
                        cell(row.averagePercentage, true), cell(row.averageGpa, true));
                    tbody.append(tr);
                }
                if (!tbody.children.length) {
                    const tr = document.createElement('tr'), td = cell('No saved result rows.');
                    td.colSpan = 6; tr.append(td); tbody.append(tr);
                }
            }
            el('gradeCsv').disabled = false; el('gradePrint').disabled = false;
        } catch (error) { if (sequence === state.sequence) notice(error.message, true); }
        finally { state.busy = false; el('gradeLoad').disabled = !el('gradeVersion').value; }
    }
    function exportCsv() {
        const report = state.report;
        const comparison = state.comparison;
        if (!report && !comparison) return;
        const rows = comparison ? [['Grade', 'Outcome', 'Earlier', 'Later', 'Change', 'Obtained marks change', 'Average percentage change', 'Earlier GPA', 'Later GPA'],
            ...(comparison.grades || []).map(x => [x.gradeLetter,
                x.isWithheld ? 'Withheld' : x.isPassed ? 'Passed' : 'Failed',
                x.baselineCount, x.targetCount, x.countChange, x.obtainedMarksChange,
                x.averagePercentageChange, x.baselineAverageGpa, x.targetAverageGpa])]
            : [['Grade', 'Outcome', 'Students', 'Total obtained marks', 'Average %', 'Average GPA'],
            ...(report.grades || []).map(x => [x.gradeLetter,
                x.isWithheld ? 'Withheld' : x.isPassed ? 'Passed' : 'Failed',
                x.studentCount, x.totalObtainedMarks, x.averagePercentage, x.averageGpa])];
        const safe = value => /^[=+@\-\t\r]/.test(String(value ?? '')) ? "'" + value : value;
        const content = rows.map(row => row.map(value =>
            '"' + String(safe(value) ?? '').replace(/"/g, '""') + '"').join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob(['\uFEFF', content], { type: 'text/csv;charset=utf-8' }));
        const link = document.createElement('a');
        link.href = url; link.download = comparison
            ? 'grade-distribution-versions-' + comparison.baselineVersionNo + '-' + comparison.targetVersionNo + '.csv'
            : 'grade-distribution-version-' + report.versionNo + '.csv'; link.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    el('gradeScopeSearch').addEventListener('submit', event => {
        event.preventDefault(); state.page = 1; scopes();
    });
    el('gradeScopePrev').addEventListener('click', () => {
        if (state.page > 1) { state.page--; scopes(); }
    });
    el('gradeScopeNext').addEventListener('click', () => {
        if (state.page * state.pageSize < state.total) { state.page++; scopes(); }
    });
    el('gradeScope').addEventListener('change', versions);
    el('gradeVersion').addEventListener('change', () => {
        clearReport(); el('gradeLoad').disabled = !el('gradeVersion').value;
    });
    el('gradeBaseline').addEventListener('change', clearReport);
    el('gradeLoad').addEventListener('click', load);
    el('gradeCsv').addEventListener('click', exportCsv);
    el('gradePrint').addEventListener('click', () => window.print());
    scopes();
})();
