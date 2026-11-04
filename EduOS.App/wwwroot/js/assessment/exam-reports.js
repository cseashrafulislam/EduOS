(() => {
    'use strict';
    const root = document.getElementById('examReportsWorkspace');
    if (!root) return;
    const $ = id => document.getElementById(id);
    const bn = root.dataset.lang === 'bn';
    const t = (en, bangla) => bn ? bangla : en;
    const session = {
        reference: document.body?.dataset.authSessionReference || '',
        tenant: document.body?.dataset.authTenantId || ''
    };
    const shield = document.getElementById('eduosSessionShield');
    const state = { scopePage: 1, scopeTotal: 0, subjectPage: 1, subjectTotal: 0,
        scopeSeq: 0, subjectSeq: 0, resultSeq: 0, report: null, page: 1,
        busyExport: false, locked: false };
    const labels = {
        rollNo: t('Roll', 'রোল'), studentCode: t('Code', 'কোড'),
        studentName: t('Student', 'শিক্ষার্থী'), subjectName: t('Subject', 'বিষয়'),
        subjectCode: t('Subject code', 'বিষয় কোড'), groupName: t('Group', 'গ্রুপ'),
        meritPosition: t('Position', 'অবস্থান'), subjectPosition: t('Subject position', 'বিষয়ে অবস্থান'),
        obtainedMarks: t('Obtained', 'প্রাপ্ত নম্বর'), totalMarks: t('Full marks', 'পূর্ণমান'),
        percentage: t('Percentage', 'শতকরা'), gpa: 'GPA', cgpa: 'CGPA',
        gradeLetter: t('Grade', 'গ্রেড'), outcome: t('Result', 'ফলাফল'),
        failedSubjects: t('Failed subjects', 'অনুত্তীর্ণ বিষয়'),
        studentCount: t('Students', 'শিক্ষার্থী'), passedCount: t('Passed', 'উত্তীর্ণ'),
        failedCount: t('Failed', 'অনুত্তীর্ণ'), withheldCount: t('Withheld', 'স্থগিত'),
        blankMark: t('Mark (blank)', 'নম্বর (ফাঁকা)')
    };
    const summary = ['rollNo', 'studentCode', 'studentName', 'obtainedMarks',
        'totalMarks', 'percentage', 'gpa', 'gradeLetter', 'outcome'];
    const columns = {
        1: ['rollNo', 'studentCode', 'studentName', 'gpa', 'gradeLetter', 'outcome'],
        3: ['meritPosition', ...summary, 'cgpa'],
        4: summary,
        5: ['meritPosition', ...summary],
        6: summary, 7: summary,
        8: ['groupName', 'studentCount', 'passedCount', 'failedCount', 'withheldCount'],
        9: ['subjectPosition', 'rollNo', 'studentCode', 'studentName', 'subjectName',
            'obtainedMarks', 'totalMarks', 'gradeLetter'],
        10: ['rollNo', 'studentCode', 'studentName', 'subjectName', 'obtainedMarks',
            'totalMarks', 'gradeLetter'],
        11: ['rollNo', 'studentCode', 'studentName', 'subjectName', 'obtainedMarks',
            'totalMarks', 'gradeLetter'],
        12: ['rollNo', 'studentCode', 'studentName', 'failedSubjects', 'gpa', 'gradeLetter'],
        13: ['rollNo', 'studentCode', 'studentName', 'failedSubjects', 'gpa', 'gradeLetter'],
        14: ['rollNo', 'studentCode', 'studentName', 'failedSubjects', 'gpa', 'gradeLetter'],
        16: ['rollNo', 'studentCode', 'studentName', 'gpa', 'cgpa', 'gradeLetter', 'outcome'],
        17: ['rollNo', 'studentCode', 'studentName', 'subjectCode', 'subjectName',
            'obtainedMarks', 'totalMarks', 'gpa', 'gradeLetter', 'outcome'],
        18: summary,
        19: ['rollNo', 'studentCode', 'studentName', 'subjectName', 'totalMarks', 'blankMark']
    };
    const needsSubject = new Set([9, 10, 11, 19]);
    const subjectKinds = new Set([9, 10, 11, 17, 19]);
    const currentSession = () => !state.locked && !!session.reference && !!session.tenant &&
        !window.EduOSSessionTransitionPending?.() &&
        document.body?.dataset.authSessionReference === session.reference &&
        document.body?.dataset.authTenantId === session.tenant;
    function lockSession() {
        if (state.locked) return;
        state.locked = true;
        ++state.scopeSeq; ++state.subjectSeq; invalidate();
        $('erTitle').textContent = t('Report unavailable', 'রিপোর্ট অনুপলব্ধ');
        $('erScope').replaceChildren(); $('erVersion').replaceChildren();
        $('erSubject').replaceChildren(); $('erSearch').value = '';
        for (const el of root.querySelectorAll('button,select,input')) el.disabled = true;
        notice(t('Institution session changed. Reload or sign in again.',
            'প্রতিষ্ঠানের সেশন পরিবর্তিত হয়েছে। রিলোড বা আবার লগইন করুন।'), true);
    }
    async function verifySession() {
        if (!currentSession() || !shield || !window.EduOSVerifySessionContext) {
            lockSession(); throw new Error('session-changed');
        }
        try { await window.EduOSVerifySessionContext(); }
        catch { lockSession(); throw new Error('session-changed'); }
        if (!currentSession() || shield.hidden !== true) {
            lockSession(); throw new Error('session-changed');
        }
    }
    const responseMatches = response => currentSession() &&
        /^[0-9A-F]{64}$/.test(session.reference) && /^[1-9][0-9]*$/.test(session.tenant) &&
        response.headers.get('X-EduOS-Session-Reference') === session.reference &&
        response.headers.get('X-EduOS-Tenant-Id') === session.tenant;
    const api = async path => {
        await verifySession();
        const response = await fetch('/api/assessment-administration/' + path, {
            credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' }
        });
        if (response.status === 401 || response.status === 403) {
            lockSession(); throw new Error('session-changed');
        }
        await verifySession();
        if (path.startsWith('results/reports') && !responseMatches(response)) {
            lockSession(); throw new Error('session-changed');
        }
        const json = await response.json().catch(() => null);
        await verifySession();
        if (path.startsWith('results/reports') && !responseMatches(response)) {
            lockSession(); throw new Error('session-changed');
        }
        if (!response.ok || json?.success !== true)
            throw new Error(json?.message || t('Request failed.', 'অনুরোধ ব্যর্থ হয়েছে।'));
        return json.data;
    };
    const cell = value => {
        const td = document.createElement('td');
        td.textContent = value == null || value === '' ? '—' : String(value);
        return td;
    };
    function notice(message, error = false) {
        $('erNotice').className = 'alert ' + (error ? 'alert-danger' : 'alert-info');
        $('erNotice').textContent = message;
    }
    function clearNotice() {
        $('erNotice').className = 'd-none'; $('erNotice').textContent = '';
    }
    function invalidate() {
        state.resultSeq++; state.report = null; state.page = 1;
        $('erCsv').disabled = true; $('erPrint').disabled = true;
        $('erPrev').disabled = true; $('erNext').disabled = true;
        $('erHead').replaceChildren();
        const row = document.createElement('tr');
        row.append(cell(t('Select a finalized version and load a report.',
            'প্রকাশিত সংস্করণ ও রিপোর্ট বেছে লোড করুন।')));
        $('erRows').replaceChildren(row); $('erInfo').textContent = ''; $('erPage').textContent = '';
    }
    function selectedKind() { return Number($('erKind').value); }
    function selectedTitle() {
        return $('erKind').selectedOptions[0]?.textContent?.trim() || '';
    }
    function enable() { $('erLoad').disabled = selectedKind() !== 2 && !$('erVersion').value; }
    async function scopes() {
        const seq = ++state.scopeSeq; ++state.subjectSeq;
        invalidate(); clearNotice();
        $('erScope').replaceChildren(new Option(t('Loading…', 'লোড হচ্ছে…'), ''));
        $('erScope').disabled = true; $('erVersion').disabled = true;
        $('erVersion').replaceChildren(); $('erLoad').disabled = true;
        try {
            const data = await api('results/reports/scopes?' + new URLSearchParams({
                search: $('erScopeTerm').value.trim(), page: String(state.scopePage)
            }));
            if (seq !== state.scopeSeq) return;
            state.scopeTotal = data.totalCount || 0;
            const seen = new Set();
            $('erScope').replaceChildren(new Option(t('Select assessment / batch',
                'মূল্যায়ন / ব্যাচ নির্বাচন করুন'), ''));
            for (const item of data.items || []) {
                const key = item.assessmentId + ':' + item.academicBatchId;
                if (seen.has(key)) continue;
                seen.add(key);
                $('erScope').add(new Option(item.assessmentName + ' / ' + item.academicBatchName, key));
            }
            $('erScope').disabled = false;
            $('erScopePrev').disabled = state.scopePage <= 1;
            $('erScopeNext').disabled = state.scopePage * 30 >= state.scopeTotal;
        } catch (e) { if (seq === state.scopeSeq) notice(e.message, true); }
    }
    async function versions() {
        const seq = ++state.scopeSeq; ++state.subjectSeq;
        invalidate(); clearNotice();
        $('erVersion').replaceChildren(new Option(t('Loading…', 'লোড হচ্ছে…'), ''));
        $('erVersion').disabled = true; $('erLoad').disabled = true;
        $('erSubject').replaceChildren();
        const selected = $('erScope').value.split(':').map(Number);
        if (selected.length !== 2 || selected.some(x => !Number.isSafeInteger(x) || x <= 0)) return;
        try {
            const data = await api('results/publications?' + new URLSearchParams({
                assessmentId: String(selected[0]), academicBatchId: String(selected[1]),
                page: '1', pageSize: '100'
            }));
            if (seq !== state.scopeSeq) return;
            $('erVersion').replaceChildren(new Option(t('Select version', 'সংস্করণ নির্বাচন করুন'), ''));
            for (const row of data.items || []) {
                if (![2, 3, 'Published', 'Withdrawn'].includes(row.state)) continue;
                const withdrawn = [3, 'Withdrawn'].includes(row.state);
                $('erVersion').add(new Option(t('Version ', 'সংস্করণ ') + row.versionNo +
                    (withdrawn ? t(' (withdrawn)', ' (প্রত্যাহার)') : ''), String(row.id)));
            }
            $('erVersion').disabled = $('erVersion').options.length <= 1;
        } catch (e) { if (seq === state.scopeSeq) notice(e.message, true); }
    }
    async function subjects() {
        const publication = $('erVersion').value;
        const seq = ++state.subjectSeq;
        $('erSubject').replaceChildren(new Option(t('Loading subjects…', 'বিষয় লোড হচ্ছে…'), ''));
        $('erSubjectPrev').disabled = true; $('erSubjectNext').disabled = true;
        if (!publication || !subjectKinds.has(selectedKind())) return;
        try {
            const data = await api('results/reports/subjects?' + new URLSearchParams({
                publicationId: publication, search: $('erSubjectTerm').value.trim(),
                page: String(state.subjectPage)
            }));
            if (seq !== state.subjectSeq || publication !== $('erVersion').value ||
                !subjectKinds.has(selectedKind())) return;
            state.subjectTotal = data.totalCount || 0;
            $('erSubject').replaceChildren(new Option(needsSubject.has(selectedKind()) ?
                t('Select subject', 'বিষয় নির্বাচন করুন') : t('All subjects', 'সব বিষয়'), ''));
            for (const item of data.items || [])
                $('erSubject').add(new Option(item.subjectName + ' (' + item.subjectCode + ')',
                    String(item.assessmentSubjectId)));
            $('erSubjectPrev').disabled = state.subjectPage <= 1;
            $('erSubjectNext').disabled = state.subjectPage * 30 >= state.subjectTotal;
        } catch (e) { if (seq === state.subjectSeq) notice(e.message, true); }
    }
    function onKind() {
        invalidate(); clearNotice();
        $('erSubjectWrap').hidden = !subjectKinds.has(selectedKind());
        state.subjectPage = 1;
        if (subjectKinds.has(selectedKind())) subjects();
        if (selectedKind() === 1)
            notice(t('This is a referral-candidate screening of failed/withheld outcomes, not a record of completed counselling.',
                'এটি অনুত্তীর্ণ/স্থগিত শিক্ষার্থীর সম্ভাব্য কাউন্সেলিং তালিকা; সম্পন্ন কাউন্সেলিংয়ের রেকর্ড নয়।'));
        if (selectedKind() === 2)
            notice(t('Formal transcripts use the existing Transcript Issue Register.',
                'আনুষ্ঠানিক ট্রান্সক্রিপ্ট বিদ্যমান ইস্যুর তালিকায় পাওয়া যাবে।'));
        enable();
    }
    function reportQuery(page) {
        const params = new URLSearchParams({
            publicationId: $('erVersion').value, kind: $('erKind').value,
            search: $('erSearch').value.trim(),
            page: String(page), pageSize: '25'
        });
        if (subjectKinds.has(selectedKind()) && $('erSubject').value)
            params.set('assessmentSubjectId', $('erSubject').value);
        return 'results/reports?' + params;
    }
    function value(row, key) {
        if (key === 'blankMark') return '';
        if (key === 'outcome') return row.isWithheld ? t('Withheld', 'স্থগিত') :
            row.isPassed ? t('Passed', 'উত্তীর্ণ') : t('Failed', 'অনুত্তীর্ণ');
        return row[key] ?? '';
    }
    function render(report) {
        const fields = columns[selectedKind()] || summary;
        const header = document.createElement('tr');
        for (const key of fields) {
            const th = document.createElement('th');
            th.scope = 'col'; th.textContent = labels[key]; header.append(th);
        }
        $('erHead').replaceChildren(header);
        const body = $('erRows'); body.replaceChildren();
        for (const item of report.items || []) {
            const tr = document.createElement('tr');
            for (const key of fields)
                tr.append(key === 'blankMark' ? document.createElement('td') : cell(value(item, key)));
            body.append(tr);
        }
        if (!body.children.length) {
            const tr = document.createElement('tr'), td = cell(t('No matching results.', 'কোনো ফলাফল পাওয়া যায়নি।'));
            td.colSpan = fields.length; tr.append(td); body.append(tr);
        }
        $('erTitle').textContent = selectedTitle() + ' · ' + report.assessmentName +
            ' / ' + report.academicBatchName + ' · ' + t('Version ', 'সংস্করণ ') + report.versionNo;
        $('erInfo').textContent = t('Matching rows: ', 'মোট সারি: ') + report.totalCount +
            (report.isWithdrawn ? t(' · Withdrawn historical version', ' · প্রত্যাহারকৃত ইতিহাস') : '');
        $('erPage').textContent = t('Page ', 'পৃষ্ঠা ') + report.page;
        $('erPrev').disabled = report.page <= 1;
        $('erNext').disabled = report.page * report.pageSize >= report.totalCount;
        $('erCsv').disabled = !(report.items || []).length;
        $('erPrint').disabled = !(report.items || []).length;
    }
    async function load(page = 1) {
        if (selectedKind() === 2) {
            window.location.assign('/AssessmentAdministration/Transcripts'); return;
        }
        if (!$('erVersion').value) return;
        if (needsSubject.has(selectedKind()) && !$('erSubject').value) {
            notice(t('Select a subject first.', 'আগে বিষয় নির্বাচন করুন।'), true); return;
        }
        invalidate(); clearNotice();
        const seq = state.resultSeq;
        const publication = $('erVersion').value, kind = selectedKind();
        $('erLoad').disabled = true;
        try {
            const data = await api(reportQuery(page));
            if (seq !== state.resultSeq || publication !== $('erVersion').value ||
                kind !== selectedKind() || data.publicationId !== Number(publication) ||
                Number(data.kind) !== kind || data.page !== page) return;
            state.report = data; state.page = page; render(data);
        } catch (e) { if (seq === state.resultSeq) notice(e.message, true); }
        finally { if (seq === state.resultSeq) enable(); }
    }
    async function verifiedPage() {
        const report = state.report, seq = state.resultSeq;
        if (!report) throw new Error(t('Reload the report.', 'রিপোর্ট আবার লোড করুন।'));
        const current = await api(reportQuery(state.page));
        if (seq !== state.resultSeq || report !== state.report ||
            report.publicationId !== current.publicationId || report.versionNo !== current.versionNo ||
            report.kind !== current.kind || report.totalCount !== current.totalCount ||
            JSON.stringify(report.items) !== JSON.stringify(current.items))
            throw new Error(t('Report changed; reload before export.', 'রিপোর্ট পরিবর্তিত হয়েছে; আবার লোড করুন।'));
        return report;
    }
    function csvSafe(value) {
        let s = String(value ?? '');
        if (/^\s*[=+@\-]/.test(s)) s = "'" + s;
        return '"' + s.replace(/"/g, '""') + '"';
    }
    async function csv() {
        if (!state.report || state.busyExport) return;
        state.busyExport = true; $('erCsv').disabled = true;
        try {
            const report = await verifiedPage();
            const fields = columns[selectedKind()] || summary;
            const rows = [fields.map(k => labels[k]), ...report.items.map(item =>
                fields.map(k => value(item, k)))];
            const blob = new Blob(['\uFEFF' + rows.map(row => row.map(csvSafe).join(',')).join('\r\n')],
                { type: 'text/csv;charset=utf-8' });
            const url = URL.createObjectURL(blob), link = document.createElement('a');
            link.href = url;
            link.download = 'exam-report-' + report.kind + '-v' + report.versionNo +
                '-page-' + report.page + '.csv';
            link.click(); setTimeout(() => URL.revokeObjectURL(url), 1000);
        } catch (e) { notice(e.message, true); }
        finally { state.busyExport = false; $('erCsv').disabled = !state.report; }
    }
    async function print() {
        if (!state.report) return;
        $('erPrint').disabled = true;
        try { await verifiedPage(); window.print(); }
        catch (e) { notice(e.message, true); }
        finally { $('erPrint').disabled = !state.report; }
    }
    $('erFilters').addEventListener('submit', e => {
        e.preventDefault(); state.scopePage = 1; scopes();
    });
    $('erScopePrev').addEventListener('click', () => {
        if (state.scopePage > 1) { state.scopePage--; scopes(); }
    });
    $('erScopeNext').addEventListener('click', () => {
        if (state.scopePage * 30 < state.scopeTotal) { state.scopePage++; scopes(); }
    });
    $('erScope').addEventListener('change', versions);
    $('erVersion').addEventListener('change', () => {
        invalidate(); enable(); state.subjectPage = 1; subjects();
    });
    $('erKind').addEventListener('change', onKind);
    $('erSubjectFind').addEventListener('click', () => {
        state.subjectPage = 1; invalidate(); subjects();
    });
    $('erSubjectPrev').addEventListener('click', () => {
        if (state.subjectPage > 1) { state.subjectPage--; invalidate(); subjects(); }
    });
    $('erSubjectNext').addEventListener('click', () => {
        if (state.subjectPage * 30 < state.subjectTotal) { state.subjectPage++; invalidate(); subjects(); }
    });
    $('erSubject').addEventListener('change', invalidate);
    $('erSearch').addEventListener('input', invalidate);
    $('erLoad').addEventListener('click', () => load(1));
    $('erPrev').addEventListener('click', () => load(state.page - 1));
    $('erNext').addEventListener('click', () => load(state.page + 1));
    $('erCsv').addEventListener('click', csv);
    $('erPrint').addEventListener('click', print);
    scopes();
})();
