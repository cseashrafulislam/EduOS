(() => {
    'use strict';
    const root = document.getElementById('resultFollowUpWorkspace');
    if (!root) return;
    const $ = id => document.getElementById(id);
    const bn = root.dataset.lang === 'bn';
    const tr = (en, bangla) => bn ? bangla : en;
    const state = { scopePage: 1, scopeTotal: 0, seq: 0, reportSeq: 0,
        page: 1, report: null, exportBusy: false };
    const api = async path => {
        const response = await fetch('/api/assessment-administration/' + path, {
            credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json' }
        });
        const json = await response.json().catch(() => null);
        if (!response.ok || json?.success !== true)
            throw new Error(json?.message || tr('Request failed.', 'অনুরোধ ব্যর্থ হয়েছে।'));
        return json.data;
    };
    const cell = value => {
        const td = document.createElement('td');
        td.textContent = value == null ? '—' : String(value);
        return td;
    };
    function notice(message, error = false) {
        const box = $('followUpNotice');
        box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        box.textContent = message;
    }
    function clear() {
        state.reportSeq++; state.report = null; state.page = 1;
        $('followUpCsv').disabled = true; $('followUpPrint').disabled = true;
        $('followUpPrev').disabled = true; $('followUpNext').disabled = true;
        $('followUpInfo').textContent = ''; $('followUpSummary').textContent = ''; $('followUpPage').textContent = '';
        const row = document.createElement('tr'), td = cell(tr('Load a finalized version.',
            'প্রকাশিত সংস্করণ নির্বাচন করুন।'));
        td.colSpan = 7; row.append(td); $('followUpRows').replaceChildren(row);
    }
    async function scopes() {
        const seq = ++state.seq; clear();
        try {
            const params = new URLSearchParams({ page: String(state.scopePage),
                pageSize: '30', search: $('followUpScopeTerm').value.trim() });
            const data = await api('scopes/page?' + params);
            if (seq !== state.seq) return;
            state.scopeTotal = data.totalCount || 0;
            const select = $('followUpScope');
            select.replaceChildren(new Option(tr('Select scope', 'মূল্যায়ন নির্বাচন করুন'), ''));
            const seen = new Set();
            for (const item of data.items || []) {
                const key = item.assessmentId + ':' + item.academicBatchId;
                if (seen.has(key)) continue;
                seen.add(key);
                select.add(new Option(item.assessmentName + ' / ' + item.academicBatchName, key));
            }
            $('followUpScopePrev').disabled = state.scopePage <= 1;
            $('followUpScopeNext').disabled = state.scopePage * 30 >= state.scopeTotal;
            $('followUpVersion').replaceChildren(new Option(tr('Select scope first', 'আগে মূল্যায়ন নির্বাচন করুন'), ''));
            $('followUpVersion').disabled = true; $('followUpLoad').disabled = true;
        } catch (e) { if (seq === state.seq) notice(e.message, true); }
    }
    async function versions() {
        clear(); const seq = ++state.seq;
        const values = $('followUpScope').value.split(':').map(Number);
        $('followUpVersion').replaceChildren(new Option(tr('Loading…', 'লোড হচ্ছে…'), ''));
        $('followUpVersion').disabled = true; $('followUpLoad').disabled = true;
        if (values.length !== 2 || values.some(x => !Number.isSafeInteger(x) || x <= 0)) return;
        try {
            const data = await api('results/publications?' + new URLSearchParams({
                assessmentId: String(values[0]), academicBatchId: String(values[1]),
                page: '1', pageSize: '100'
            }));
            if (seq !== state.seq) return;
            const select = $('followUpVersion');
            select.replaceChildren(new Option(tr('Select version', 'সংস্করণ নির্বাচন করুন'), ''));
            for (const row of data.items || []) {
                if (![2, 3, 'Published', 'Withdrawn'].includes(row.state)) continue;
                select.add(new Option(tr('Version ', 'সংস্করণ ') + row.versionNo, String(row.id)));
            }
            select.disabled = select.options.length < 2;
        } catch (e) { if (seq === state.seq) notice(e.message, true); }
    }
    function query(page) {
        return 'results/follow-up?' + new URLSearchParams({
            publicationId: $('followUpVersion').value, status: $('followUpStatus').value,
            search: $('followUpSearch').value.trim(), page: String(page), pageSize: '25'
        });
    }
    async function load(page = 1) {
        const publication = $('followUpVersion').value;
        if (!publication) return;
        const seq = ++state.reportSeq;
        state.report = null; $('followUpCsv').disabled = true;
        $('followUpPrint').disabled = true;
        try {
            const summaryPath = 'results/follow-up/summary?' + new URLSearchParams({
                publicationId: publication, search: $('followUpSearch').value.trim()
            });
            const [data, summary] = await Promise.all([api(query(page)), api(summaryPath)]);
            if (seq !== state.reportSeq || $('followUpVersion').value !== publication) return;
            if (summary.publicationId !== data.publicationId ||
                summary.versionNo !== data.versionNo || summary.total < data.totalCount)
                throw new Error(tr('Outcome summary requires reconciliation.', 'ফলাফল সারসংক্ষেপ যাচাই প্রয়োজন।'));
            state.report = data; state.page = page;
            $('followUpSummary').textContent = tr('Failed: ', 'অনুত্তীর্ণ: ') + summary.failed +
                ' · ' + tr('Withheld: ', 'স্থগিত: ') + summary.withheld;
            $('followUpTitle').textContent = data.assessmentName + ' / ' +
                data.academicBatchName + ' · ' + tr('Version ', 'সংস্করণ ') + data.versionNo;
            $('followUpInfo').textContent = tr('Matching results: ', 'মোট ফলাফল: ') + data.totalCount;
            const tbody = $('followUpRows'); tbody.replaceChildren();
            for (const item of data.items || []) {
                const row = document.createElement('tr');
                row.append(cell(item.rollNo), cell(item.studentCode), cell(item.studentName),
                    cell(item.obtainedMarks + ' / ' + item.totalMarks), cell(item.gpa),
                    cell(item.gradeLetter), cell(item.isWithheld ?
                        tr('Withheld', 'স্থগিত') : tr('Failed', 'অনুত্তীর্ণ')));
                tbody.append(row);
            }
            if (!tbody.children.length) {
                const row = document.createElement('tr'), td = cell(tr('No matching results.',
                    'কোনো ফলাফল পাওয়া যায়নি।'));
                td.colSpan = 7; row.append(td); tbody.append(row);
            }
            $('followUpPrev').disabled = page <= 1;
            $('followUpNext').disabled = page * data.pageSize >= data.totalCount;
            $('followUpPage').textContent = tr('Page ', 'পৃষ্ঠা ') + page;
            $('followUpCsv').disabled = false; $('followUpPrint').disabled = false;
        } catch (e) { if (seq === state.reportSeq) notice(e.message, true); }
    }
    function matchesReport(data, report) {
        return data.publicationId === report.publicationId &&
            data.versionNo === report.versionNo &&
            data.status === report.status &&
            data.totalCount === report.totalCount;
    }
    async function csv() {
        if (!state.report || state.exportBusy) return;
        const report = state.report, seq = state.reportSeq;
        state.exportBusy = true; $('followUpCsv').disabled = true;
        try {
            const params = new URLSearchParams({
                publicationId: String(report.publicationId),
                status: String(report.status), search: $('followUpSearch').value.trim()
            });
            const data = await api('results/follow-up/export?' + params);
            if (state.reportSeq !== seq || state.report !== report ||
                !matchesReport(data, report) || data.limit !== 500 ||
                !Array.isArray(data.items) || data.items.length > 500 ||
                data.items.length !== Math.min(data.totalCount, 500) ||
                data.truncated !== (data.totalCount > 500))
                throw new Error(tr('Report changed; reload.', 'রিপোর্ট পরিবর্তিত; আবার লোড করুন।'));
            const safe = v => {
                let s = String(v ?? '');
                if (/^\s*[=+@\-]/.test(s)) s = "'" + s;
                return '"' + s.replace(/"/g, '""') + '"';
            };
            const rows = [['Roll', 'Student code', 'Student', 'Marks', 'Maximum',
                'GPA', 'Grade', 'Outcome'], ...data.items.map(x => [
                x.rollNo, x.studentCode, x.studentName, x.obtainedMarks,
                x.totalMarks, x.gpa, x.gradeLetter, x.isWithheld ? 'Withheld' : 'Failed'
            ])];
            const blob = new Blob(['\uFEFF' + rows.map(x => x.map(safe).join(',')).join('\r\n')],
                { type: 'text/csv;charset=utf-8' });
            const url = URL.createObjectURL(blob), link = document.createElement('a');
            link.href = url; link.download = 'result-follow-up-' + data.publicationId +
                '-v' + data.versionNo + '.csv'; link.click();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
            if (data.truncated) notice(tr('Export limited to the first 500 matches.',
                'প্রথম ৫০০টি ফলাফল এক্সপোর্ট হয়েছে।'));
        } catch (e) { notice(e.message, true); }
        finally { state.exportBusy = false; $('followUpCsv').disabled = !state.report; }
    }
    async function printPage() {
        if (!state.report) return;
        const report = state.report, seq = state.reportSeq, page = state.page;
        $('followUpPrint').disabled = true;
        try {
            const data = await api(query(page));
            if (state.reportSeq !== seq || state.report !== report ||
                !matchesReport(data, report) || data.page !== page ||
                data.items.length !== report.items.length)
                throw new Error(tr('Report changed; reload.', 'রিপোর্ট পরিবর্তিত; আবার লোড করুন।'));
            window.print();
        } catch (e) { notice(e.message, true); }
        finally { $('followUpPrint').disabled = !state.report; }
    }
    $('followUpScopeForm').addEventListener('submit', e => {
        e.preventDefault(); state.scopePage = 1; scopes();
    });
    $('followUpScopePrev').addEventListener('click', () => {
        if (state.scopePage > 1) { state.scopePage--; scopes(); }
    });
    $('followUpScopeNext').addEventListener('click', () => {
        if (state.scopePage * 30 < state.scopeTotal) { state.scopePage++; scopes(); }
    });
    $('followUpScope').addEventListener('change', versions);
    $('followUpVersion').addEventListener('change', () => {
        clear(); $('followUpLoad').disabled = !$('followUpVersion').value;
    });
    $('followUpStatus').addEventListener('change', clear);
    $('followUpSearch').addEventListener('input', clear);
    $('followUpLoad').addEventListener('click', () => load(1));
    $('followUpPrev').addEventListener('click', () => load(state.page - 1));
    $('followUpNext').addEventListener('click', () => load(state.page + 1));
    $('followUpCsv').addEventListener('click', csv);
    $('followUpPrint').addEventListener('click', printPage);
    scopes();
})();
