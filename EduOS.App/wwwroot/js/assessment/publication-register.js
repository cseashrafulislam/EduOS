(() => {
    'use strict';
    const root = document.getElementById('examPublicationRegister');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = document.querySelector('#examWorkflow input[name="__RequestVerificationToken"]')?.value || '';
    const state = { history: null, historyPage: 1, historySize: 20, tabulation: null,
        tabulationPage: 1, tabulationSize: 50, integrity: null, integrityPage: 1,
        integritySize: 25, comparison: null, comparisonPage: 1, comparisonSize: 25, busy: false };
    const cell = value => {
        const td = document.createElement('td');
        td.textContent = value == null ? '—' : String(value);
        return td;
    };
    const option = (value, label) => new Option(label, String(value));
    const published = row => row && (row.state === 2 || row.state === 'Published');
    const withdrawn = row => row && (row.state === 3 || row.state === 'Withdrawn');
    const finalized = row => published(row) || withdrawn(row);
    const reconciled = row => row?.integrityStatus === 'Reconciled';
    const integrityLabel = row => row?.integrityStatus === 'RequiresReview'
        ? 'Requires reconciliation' : reconciled(row) ? 'Reconciled' : 'Not finalized';
    const stateLabel = row => published(row) ? 'Published' : withdrawn(row) ? 'Withdrawn' : 'Draft';
    function notify(message, error = false) {
        const box = el('examAlert');
        box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        box.textContent = message;
        box.focus();
    }
    async function api(path, method = 'GET', body) {
        const response = await fetch(path, { method, credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json',
                ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
                ...(method === 'GET' ? {} : { RequestVerificationToken: token }) },
            ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
        const result = await response.json().catch(() => null);
        if (!response.ok || result?.success !== true) throw new Error(result?.message || 'Request failed.');
        return result.data;
    }
    function scope() {
        const assessmentId = Number(el('examExam').value);
        const academicBatchId = Number(el('examBatch').value);
        if (!Number.isSafeInteger(assessmentId) || assessmentId <= 0 ||
            !Number.isSafeInteger(academicBatchId) || academicBatchId <= 0)
            throw new Error('Select an assessment and batch first.');
        return { assessmentId, academicBatchId };
    }
    function selectedVersion() {
        return state.history?.items?.find(row => String(row.id) === el('examHistoryVersion').value) || null;
    }
    function clearTabulation() {
        state.tabulation = null;
        el('examTabulationRows').replaceChildren();
        const tr = document.createElement('tr'), td = cell('Load a saved publication version.');
        td.colSpan = 11; td.className = 'text-center text-muted p-3'; tr.append(td);
        el('examTabulationRows').append(tr);
        el('examTabulationInfo').textContent = 'Page-level historical snapshot. Student display names are current directory values.';
        for (const id of ['examTabulationCsv', 'examTabulationPrint', 'examTabulationPrev', 'examTabulationNext'])
            el(id).disabled = true;
    }
    function versionChanged() {
        const row = selectedVersion();
        clearTabulation();
        clearIntegrity();
        clearComparison();
        el('examIntegrityLoad').disabled = !finalized(row);
        el('examTabulationLoad').disabled = !finalized(row) || !reconciled(row);
        el('examComparisonLoad').disabled = !finalized(row) || !reconciled(row) || row.versionNo <= 1;
        el('examHistoryWithdraw').disabled = !published(row);
        el('examHistoryDetails').textContent = row
            ? 'Version ' + row.versionNo + ' · ' + stateLabel(row) +
              ' · ' + row.snapshotCount + ' saved rows · ' + integrityLabel(row) + ' · ' +
              (row.publishedAt ? new Date(row.publishedAt).toLocaleString() : 'Date unavailable') +
              ' · Student access: ' + (row.visibleToStudent ? 'yes' : 'no') +
              ' · Guardian access: ' + (row.visibleToGuardian ? 'yes' : 'no') +
              (row.publishNote ? ' · Note: ' + row.publishNote : '')
            : '';
    }
    function reset() {
        state.history = null;
        state.historyPage = 1;
        el('examHistoryVersion').replaceChildren(option('', 'Load versions first'));
        el('examHistoryVersion').disabled = true;
        el('examHistoryInfo').textContent = 'Choose an assessment and batch, then load versions.';
        el('examHistoryPrev').disabled = el('examHistoryNext').disabled = true;
        versionChanged();
    }
    async function loadHistory(page) {
        if (state.busy || page < 1) return;
        let selected;
        try { selected = scope(); } catch (error) { notify(error.message, true); return; }
        state.busy = true;
        el('examHistoryLoad').disabled = true;
        try {
            const params = new URLSearchParams({ ...selected, page: String(page),
                pageSize: String(state.historySize) });
            const data = await api('/api/assessment-administration/results/publications?' + params);
            state.history = data; state.historyPage = data.page;
            const previous = el('examHistoryVersion').value;
            el('examHistoryVersion').replaceChildren(option('', 'Select version'));
            for (const row of data.items || [])
                el('examHistoryVersion').add(option(row.id,
                    'Version ' + row.versionNo + ' — ' + stateLabel(row) +
                    ' — ' + integrityLabel(row)));
            if ((data.items || []).some(row => String(row.id) === previous))
                el('examHistoryVersion').value = previous;
            else if (data.items?.length) el('examHistoryVersion').value = String(data.items[0].id);
            el('examHistoryVersion').disabled = !data.items?.length;
            const first = data.totalCount ? (data.page - 1) * data.pageSize + 1 : 0;
            const last = Math.min(data.page * data.pageSize, data.totalCount);
            el('examHistoryInfo').textContent = first + '–' + last + ' of ' + data.totalCount +
                ' retained publication versions';
            el('examHistoryPrev').disabled = data.page <= 1;
            el('examHistoryNext').disabled = data.page * data.pageSize >= data.totalCount;
            versionChanged();
        } catch (error) {
            reset();
            notify(error.message, true);
        } finally {
            state.busy = false;
            el('examHistoryLoad').disabled = false;
        }
    }
    async function loadTabulation(page) {
        if (state.busy || page < 1) return;
        const version = selectedVersion();
        if (!version) { notify('Load and select a publication version.', true); return; }
        if (!reconciled(version)) {
            notify('This saved version requires reconciliation before tabulation or export.', true);
            return;
        }
        state.busy = true;
        el('examTabulationLoad').disabled = true;
        try {
            const params = new URLSearchParams({ publicationId: String(version.id),
                page: String(page), pageSize: String(state.tabulationSize),
                search: el('examTabulationSearch').value.trim() });
            const data = await api('/api/assessment-administration/results/tabulation?' + params);
            state.tabulation = data; state.tabulationPage = data.page;
            const tbody = el('examTabulationRows');
            tbody.replaceChildren();
            for (const row of data.items || []) {
                const tr = document.createElement('tr');
                tr.append(cell(row.meritPosition), cell(row.rollNo), cell(row.studentCode),
                    cell(row.studentName), cell(row.obtainedMarks), cell(row.totalMarks),
                    cell(row.percentage), cell(row.gpa), cell(row.cgpa), cell(row.gradeLetter),
                    cell(row.isWithheld ? 'Withheld' : row.isPassed ? 'Passed' : 'Not passed'));
                tbody.append(tr);
            }
            if (!data.items?.length) {
                const tr = document.createElement('tr'), td = cell('No matching saved result rows.');
                td.colSpan = 11; td.className = 'text-center text-muted p-3'; tr.append(td); tbody.append(tr);
            }
            const first = data.totalCount ? (data.page - 1) * data.pageSize + 1 : 0;
            const last = Math.min(data.page * data.pageSize, data.totalCount);
            el('examTabulationInfo').textContent = 'Version ' + data.versionNo +
                ' · ' + first + '–' + last + ' of ' + data.totalCount +
                ' saved rows · page-only export · names reflect the current authorized directory';
            el('examTabulationPrev').disabled = data.page <= 1;
            el('examTabulationNext').disabled = data.page * data.pageSize >= data.totalCount;
            el('examTabulationCsv').disabled = !data.items?.length;
            el('examTabulationPrint').disabled = !data.items?.length;
        } catch (error) {
            clearTabulation();
            notify(error.message, true);
        } finally {
            state.busy = false;
            el('examTabulationLoad').disabled = false;
        }
    }
    async function withdraw() {
        if (state.busy) return;
        const version = selectedVersion();
        if (!published(version) || !version.rowVersion) return;
        const reason = window.prompt('Reason for withdrawing this published version (required):', '')?.trim();
        if (!reason || reason.length > 1000) return;
        if (window.prompt('This hides the published version but preserves its history. Type WITHDRAW to confirm:', '') !== 'WITHDRAW') return;
        state.busy = true;
        el('examHistoryWithdraw').disabled = true;
        let error = null;
        try {
            await api('/api/assessment-administration/results/publications/' +
                encodeURIComponent(version.id) + '/withdraw', 'POST',
                { reason, rowVersion: version.rowVersion });
        } catch (e) { error = e; }
        finally { state.busy = false; }
        await loadHistory(1);
        if (error) notify(error.message + ' Publication versions were refreshed; verify state before retrying.', true);
        else {
            window.dispatchEvent(new Event('assessment-publication-withdrawn'));
            notify('Publication withdrawn with an audit reason. The saved version remains available to authorized reviewers.');
        }
    }
    function exportPage() {
        const data = state.tabulation;
        if (!data?.items?.length) return;
        const safe = value => /^[=+@\-\t\r]/.test(String(value ?? '')) ? "'" + String(value) : value;
        const quote = value => '"' + String(safe(value) ?? '').replace(/"/g, '""') + '"';
        const rows = [['Version', 'Position', 'Roll', 'Student code', 'Student', 'Obtained', 'Maximum',
            'Percentage', 'GPA', 'CGPA', 'Grade', 'Passed', 'Withheld'],
            ...data.items.map(row => [data.versionNo, row.meritPosition, row.rollNo, row.studentCode,
                row.studentName, row.obtainedMarks, row.totalMarks, row.percentage, row.gpa,
                row.cgpa, row.gradeLetter, row.isPassed, row.isWithheld])];
        const output = rows.map(row => row.map(quote).join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob(['\uFEFF' + output], { type: 'text/csv;charset=utf-8' }));
        const link = document.createElement('a');
        link.href = url; link.download = 'assessment-publication-' + data.publicationId +
            '-page-' + data.page + '.csv';
        document.body.append(link); link.click(); link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }

    function clearIntegrity() {
        state.integrity = null;
        state.integrityPage = 1;
        const tr = document.createElement('tr');
        const td = cell('Select a finalized version to inspect saved evidence.');
        td.colSpan = 3; td.className = 'text-center text-muted';
        tr.append(td);
        el('examIntegrityRows').replaceChildren(tr);
        el('examIntegrityInfo').textContent = 'Select a finalized version to inspect saved evidence.';
        for (const id of ['examIntegrityCsv', 'examIntegrityPrev', 'examIntegrityNext'])
            el(id).disabled = true;
    }
    async function loadIntegrity(page) {
        if (state.busy || page < 1) return;
        const version = selectedVersion();
        if (!finalized(version)) { notify('Select a finalized publication version.', true); return; }
        state.busy = true;
        el('examIntegrityLoad').disabled = true;
        try {
            const params = new URLSearchParams({ page: String(page),
                pageSize: String(state.integritySize) });
            const data = await api('/api/assessment-administration/results/publications/' +
                encodeURIComponent(version.id) + '/integrity?' + params);
            state.integrity = data; state.integrityPage = data.page;
            const tbody = el('examIntegrityRows');
            tbody.replaceChildren();
            for (const row of data.items || []) {
                const tr = document.createElement('tr');
                tr.append(cell(row.summaryId), cell(row.studentEnrollmentId), cell(row.issueCode));
                tbody.append(tr);
            }
            if (!data.items?.length) {
                const tr = document.createElement('tr');
                const td = cell(data.isEmptySnapshot ? 'No saved snapshot rows.' :
                    data.isReconciled ? 'All saved snapshot rows reconcile.' : 'No issues on this page.');
                td.colSpan = 3; td.className = 'text-center text-muted'; tr.append(td); tbody.append(tr);
            }
            const first = data.invalidCount ? (data.page - 1) * data.pageSize + 1 : 0;
            const last = Math.min(data.page * data.pageSize, data.invalidCount);
            el('examIntegrityInfo').textContent = 'Version ' + data.versionNo +
                ' · ' + data.snapshotCount + ' snapshot rows · ' +
                (data.isReconciled ? 'Reconciled' : 'Requires reconciliation') +
                ' · issues ' + first + '–' + last + ' of ' + data.invalidCount;
            el('examIntegrityPrev').disabled = data.page <= 1;
            el('examIntegrityNext').disabled = data.page * data.pageSize >= data.invalidCount;
            el('examIntegrityCsv').disabled = !data.items?.length;
        } catch (error) {
            clearIntegrity();
            notify(error.message, true);
        } finally {
            state.busy = false;
            el('examIntegrityLoad').disabled = !finalized(selectedVersion());
        }
    }
    function exportIntegrity() {
        const data = state.integrity;
        if (!data?.items?.length) return;
        const safe = value => /^[=+@\-\t\r]/.test(String(value ?? '')) ? "'" + String(value) : value;
        const quote = value => '"' + String(safe(value) ?? '').replace(/"/g, '""') + '"';
        const rows = [['Publication', 'Version', 'Snapshot ID', 'Enrollment ID', 'Issue'],
            ...data.items.map(row => [data.publicationId, data.versionNo,
                row.summaryId, row.studentEnrollmentId, row.issueCode])];
        const csv = rows.map(row => row.map(quote).join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob(['\uFEFF' + csv],
            { type: 'text/csv;charset=utf-8' }));
        const link = document.createElement('a');
        link.href = url;
        link.download = 'publication-integrity-' + data.publicationId +
            '-page-' + data.page + '.csv';
        document.body.append(link); link.click(); link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }

    function clearComparison() {
        state.comparison = null;
        state.comparisonPage = 1;
        const tr = document.createElement('tr'), td = cell('Select a finalized version to compare.');
        td.colSpan = 12; td.className = 'text-center text-muted p-3'; tr.append(td);
        el('examComparisonRows').replaceChildren(tr);
        el('examComparisonInfo').textContent = 'Select a finalized version.';
        for (const id of ['examComparisonCsv', 'examComparisonPrint', 'examComparisonPrev',
            'examComparisonNext']) el(id).disabled = true;
    }
    async function loadComparison(page) {
        if (state.busy || page < 1) return;
        const version = selectedVersion();
        if (!finalized(version) || !reconciled(version) || version.versionNo <= 1) {
            notify('Select a reconciled finalized version with an earlier version.', true);
            return;
        }
        state.busy = true;
        el('examComparisonLoad').disabled = true;
        try {
            const params = new URLSearchParams({
                publicationId: String(version.id), page: String(page),
                pageSize: String(state.comparisonSize),
                search: el('examComparisonSearch').value.trim(),
                changesOnly: String(el('examComparisonChangedOnly').checked)
            });
            const data = await api('/api/assessment-administration/results/publications/compare?' + params);
            state.comparison = data; state.comparisonPage = data.page;
            const tbody = el('examComparisonRows');
            tbody.replaceChildren();
            const resultText = (passed, withheld) => withheld == null ? '—' :
                withheld ? 'Withheld' : passed ? 'Passed' : 'Not passed';
            for (const row of data.items || []) {
                const tr = document.createElement('tr');
                tr.append(cell(row.rollNo), cell(row.studentCode), cell(row.studentName),
                    cell(row.change), cell(row.beforeObtainedMarks), cell(row.afterObtainedMarks),
                    cell(row.beforeGpa), cell(row.afterGpa), cell(row.beforeGrade),
                    cell(row.afterGrade), cell(resultText(row.beforePassed, row.beforeWithheld)),
                    cell(resultText(row.afterPassed, row.afterWithheld)));
                tbody.append(tr);
            }
            if (!data.items?.length) {
                const tr = document.createElement('tr'), td = cell('No matching version differences.');
                td.colSpan = 12; td.className = 'text-center text-muted p-3'; tr.append(td);
                tbody.append(tr);
            }
            const first = data.totalCount ? (data.page - 1) * data.pageSize + 1 : 0;
            const last = Math.min(data.page * data.pageSize, data.totalCount);
            el('examComparisonInfo').textContent = 'Version ' + data.baselineVersionNo +
                ' → ' + data.versionNo + ' · ' + first + '–' + last + ' of ' +
                data.totalCount + ' rows · page-only export';
            el('examComparisonPrev').disabled = data.page <= 1;
            el('examComparisonNext').disabled = data.page * data.pageSize >= data.totalCount;
            el('examComparisonCsv').disabled = !data.items?.length;
            el('examComparisonPrint').disabled = !data.items?.length;
        } catch (error) {
            clearComparison();
            notify(error.message, true);
        } finally {
            state.busy = false;
            const selected = selectedVersion();
            el('examComparisonLoad').disabled = !finalized(selected) ||
                !reconciled(selected) || selected.versionNo <= 1;
        }
    }
    function exportComparison() {
        const data = state.comparison;
        if (!data?.items?.length) return;
        const safe = value => /^[=+@\-\t\r]/.test(String(value ?? '')) ? "'" + String(value) : value;
        const quote = value => '"' + String(safe(value) ?? '').replace(/"/g, '""') + '"';
        const rows = [['Baseline version', 'Target version', 'Roll', 'Student code', 'Student',
            'Change', 'Marks before', 'Marks after', 'Maximum before', 'Maximum after',
            'Percentage before', 'Percentage after', 'GPA before', 'GPA after',
            'CGPA before', 'CGPA after', 'Grade before', 'Grade after',
            'Merit before', 'Merit after', 'Passed before', 'Passed after',
            'Withheld before', 'Withheld after'],
            ...data.items.map(row => [data.baselineVersionNo, data.versionNo,
                row.rollNo, row.studentCode, row.studentName, row.change,
                row.beforeObtainedMarks, row.afterObtainedMarks,
                row.beforeTotalMarks, row.afterTotalMarks,
                row.beforePercentage, row.afterPercentage, row.beforeGpa, row.afterGpa,
                row.beforeCgpa, row.afterCgpa, row.beforeGrade, row.afterGrade,
                row.beforeMerit, row.afterMerit, row.beforePassed, row.afterPassed,
                row.beforeWithheld, row.afterWithheld])];
        const csv = rows.map(row => row.map(quote).join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob(['\uFEFF' + csv],
            { type: 'text/csv;charset=utf-8' }));
        const link = document.createElement('a');
        link.href = url;
        link.download = 'comparison-' + data.baselinePublicationId + '-' + data.publicationId +
            '-page-' + data.page + '.csv';
        document.body.append(link); link.click(); link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    function printComparison() {
        if (!state.comparison?.items?.length) return;
        document.body.classList.add('exam-comparison-print');
        window.addEventListener('afterprint', () =>
            document.body.classList.remove('exam-comparison-print'), { once: true });
        window.print();
    }

    el('examComparisonFilters').addEventListener('submit', event => { event.preventDefault(); loadComparison(1); });
    el('examComparisonPrev').addEventListener('click', () => loadComparison(state.comparisonPage - 1));
    el('examComparisonNext').addEventListener('click', () => loadComparison(state.comparisonPage + 1));
    el('examComparisonCsv').addEventListener('click', exportComparison);
    el('examComparisonPrint').addEventListener('click', printComparison);
    el('examComparisonChangedOnly').addEventListener('change', clearComparison);
    el('examComparisonSearch').addEventListener('input', clearComparison);
    el('examHistoryFilters').addEventListener('submit', event => { event.preventDefault(); loadHistory(1); });
    el('examHistoryPrev').addEventListener('click', () => loadHistory(state.historyPage - 1));
    el('examHistoryNext').addEventListener('click', () => loadHistory(state.historyPage + 1));
    el('examHistoryVersion').addEventListener('change', versionChanged);
    el('examHistoryWithdraw').addEventListener('click', withdraw);
    el('examIntegrityLoad').addEventListener('click', () => loadIntegrity(1));
    el('examIntegrityPrev').addEventListener('click', () => loadIntegrity(state.integrityPage - 1));
    el('examIntegrityNext').addEventListener('click', () => loadIntegrity(state.integrityPage + 1));
    el('examIntegrityCsv').addEventListener('click', exportIntegrity);
    el('examTabulationFilters').addEventListener('submit', event => { event.preventDefault(); loadTabulation(1); });
    el('examTabulationPrev').addEventListener('click', () => loadTabulation(state.tabulationPage - 1));
    el('examTabulationNext').addEventListener('click', () => loadTabulation(state.tabulationPage + 1));
    el('examTabulationCsv').addEventListener('click', exportPage);
    el('examTabulationPrint').addEventListener('click', () => window.print());
    el('examExam').addEventListener('change', reset);
    el('examBatch').addEventListener('change', reset);
})();
