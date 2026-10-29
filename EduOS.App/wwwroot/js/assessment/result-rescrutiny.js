(() => {
    'use strict';
    const root = document.getElementById('resultRescrutinyWorkspace');
    if (!root) return;
    const $ = id => document.getElementById(id);
    const bn = root.dataset.lang === 'bn';
    const tr = (en, bangla) => bn ? bangla : en;
    const state = { scopePage: 1, scopeTotal: 0, scopeSeq: 0, listSeq: 0,
        publication: 0, before: null, previous: [], hasMore: false, items: [],
        selected: null, busy: false, exportBusy: false, summarySeq: 0,
        requestKey: null, decisionKey: null, historySeq: 0, reviewSeq: 0, marksSeq: 0,
        markCase: null, markExportBusy: false, correctionCase: null,
        correctionEvidence: null, correctionKey: null, correctionSeq: 0, correctionBusy: false };
    const endpoint = '/api/assessment-administration/';
    const token = document.querySelector('#resultRescrutinyWorkspace input[name="__RequestVerificationToken"]')?.value;
    function notice(message, error = false) {
        const box = $('rescrutinyNotice');
        box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        box.textContent = message;
    }
    function cell(value) {
        const td = document.createElement('td');
        td.textContent = value == null ? '—' : String(value);
        return td;
    }
    function placeholder(id, message, span) {
        const body = $(id), row = document.createElement('tr'), td = cell(message);
        td.colSpan = span; row.append(td); body.replaceChildren(row);
    }
    async function api(path, method = 'GET', payload = null) {
        const response = await fetch(endpoint + path, {
            method, credentials: 'same-origin', cache: 'no-store',
            headers: Object.assign({ Accept: 'application/json' },
                payload ? { 'Content-Type': 'application/json', RequestVerificationToken: token || '' } : {}),
            body: payload ? JSON.stringify(payload) : undefined
        });
        const body = await response.json().catch(() => null);
        if (!response.ok || body?.success !== true)
            throw new Error(body?.message || tr('Request failed.', 'অনুরোধ ব্যর্থ হয়েছে।'));
        return body.data;
    }
    function resetPublication() {
        state.publication = 0; state.before = null; state.previous = []; state.items = [];
        state.selected = null; state.requestKey = null; state.decisionKey = null;
        $('rescrutinyStudent').replaceChildren(new Option(tr('Find a student', 'শিক্ষার্থী খুঁজুন'), ''));
        $('rescrutinyStudent').disabled = true; $('rescrutinySubmit').disabled = true;
        $('rescrutinyDecisionForm').classList.add('d-none');
        $('rescrutinyHistory').classList.add('d-none');
        state.historySeq++; state.reviewSeq++; state.marksSeq++;
        state.correctionSeq++; state.correctionCase = null;
        state.correctionEvidence = null; state.correctionKey = null;
        $('rescrutinyCorrectionForm').classList.add('d-none');
        $('rescrutinyMarkEvidence').classList.add('d-none');
        state.markCase = null; $('rescrutinyMarkCsv').disabled = true;
        $('rescrutinyReviewEvidence').classList.add('d-none');
        state.summarySeq++; $('rescrutinySummary').textContent = '';
        $('rescrutinyCsv').disabled = true; $('rescrutinyPrev').disabled = true;
        $('rescrutinyNext').disabled = true;
        placeholder('rescrutinyRows', tr('Select a published version.', 'প্রকাশিত সংস্করণ নির্বাচন করুন।'), 5);
    }
    async function loadScopes() {
        const seq = ++state.scopeSeq;
        resetPublication();
        try {
            const params = new URLSearchParams({ page: String(state.scopePage), pageSize: '30',
                search: $('rescrutinyScopeSearch').value.trim() });
            const data = await api('scopes/page?' + params);
            if (seq !== state.scopeSeq) return;
            state.scopeTotal = data.totalCount || 0;
            const select = $('rescrutinyScope');
            select.replaceChildren(new Option(tr('Select assessment / batch', 'মূল্যায়ন / ব্যাচ নির্বাচন করুন'), ''));
            const seen = new Set();
            for (const item of data.items || []) {
                const key = String(item.assessmentId) + ':' + String(item.academicBatchId);
                if (seen.has(key)) continue;
                seen.add(key);
                select.add(new Option(item.assessmentName + ' / ' + item.academicBatchName, key));
            }
            $('rescrutinyScopePrev').disabled = state.scopePage <= 1;
            $('rescrutinyScopeNext').disabled = state.scopePage * 30 >= state.scopeTotal;
            $('rescrutinyPublication').replaceChildren(new Option(tr('Select scope first', 'আগে মূল্যায়ন নির্বাচন করুন'), ''));
            $('rescrutinyPublication').disabled = true;
        } catch (e) { if (seq === state.scopeSeq) notice(e.message, true); }
    }
    async function loadVersions() {
        resetPublication();
        const scope = $('rescrutinyScope').value.split(':').map(Number);
        const select = $('rescrutinyPublication');
        select.replaceChildren(new Option(tr('Loading…', 'লোড হচ্ছে…'), ''));
        select.disabled = true;
        if (scope.length !== 2 || scope.some(x => !Number.isSafeInteger(x) || x <= 0)) return;
        const seq = ++state.scopeSeq;
        try {
            const data = await api('results/publications?' + new URLSearchParams({
                assessmentId: String(scope[0]), academicBatchId: String(scope[1]),
                page: '1', pageSize: '100'
            }));
            if (seq !== state.scopeSeq) return;
            select.replaceChildren(new Option(tr('Select published version', 'প্রকাশিত সংস্করণ নির্বাচন করুন'), ''));
            for (const item of data.items || []) {
                if (![2, 3, 'Published', 'Withdrawn'].includes(item.state)) continue;
                const previous = item.state === 3 || item.state === 'Withdrawn';
                const option = new Option(tr('Version ', 'সংস্করণ ') + item.versionNo +
                    (previous ? tr(' (withdrawn history)', ' (পূর্ববর্তী ইতিহাস)') : ''), String(item.id));
                option.dataset.withdrawn = previous ? 'true' : 'false';
                select.add(option);
            }
            select.disabled = select.options.length <= 1;
        } catch (e) { if (seq === state.scopeSeq) notice(e.message, true); }
    }
    async function loadStudents() {
        const id = state.publication;
        $('rescrutinyStudent').replaceChildren(new Option(tr('Loading…', 'লোড হচ্ছে…'), ''));
        $('rescrutinyStudent').disabled = true; $('rescrutinySubmit').disabled = true;
        if (!id) return;
        try {
            const data = await api('results/tabulation?' + new URLSearchParams({
                publicationId: String(id), page: '1', pageSize: '50',
                search: $('rescrutinyStudentSearch').value.trim()
            }));
            if (state.publication !== id) return;
            const select = $('rescrutinyStudent');
            select.replaceChildren(new Option(tr('Select student', 'শিক্ষার্থী নির্বাচন করুন'), ''));
            for (const item of data.items || [])
                select.add(new Option(item.studentCode + ' / ' + item.rollNo + ' / ' + item.studentName,
                    item.studentReference));
            select.disabled = select.options.length <= 1;
            $('rescrutinySubmit').disabled = select.disabled ||
                $('rescrutinyPublication').selectedOptions[0]?.dataset.withdrawn === 'true';
        } catch (e) { notice(e.message, true); }
    }
    function status(value) {
        if (value === 2 || value === 'Rejected') return tr('Rejected', 'প্রত্যাখ্যাত');
        if (value === 3 || value === 'AcceptedForCorrection')
            return tr('Accepted for correction review', 'সংশোধন পর্যালোচনার জন্য গৃহীত');
        return tr('Pending', 'অপেক্ষমাণ');
    }
    async function loadSummary() {
        const id = state.publication, seq = ++state.summarySeq;
        if (!id) return;
        try {
            const data = await api('results/rescrutiny/summary?' +
                new URLSearchParams({ publicationId: String(id) }));
            if (seq !== state.summarySeq || id !== state.publication) return;
            $('rescrutinySummary').textContent = [
                tr('Total', 'মোট') + ': ' + data.total,
                tr('Pending', 'অপেক্ষমাণ') + ': ' + data.pending,
                tr('Accepted for correction', 'সংশোধনের জন্য গৃহীত') + ': ' + data.acceptedForCorrection,
                tr('Rejected', 'প্রত্যাখ্যাত') + ': ' + data.rejected,
                data.oldestPendingAt ? tr('Oldest pending (UTC)', 'প্রাচীনতম অপেক্ষমাণ (UTC)') +
                    ': ' + new Date(data.oldestPendingAt).toISOString() : ''
            ].filter(Boolean).join('  ·  ');
        } catch (e) {
            if (seq === state.summarySeq) {
                $('rescrutinySummary').textContent = '';
                notice(e.message, true);
            }
        }
    }
    async function loadCases() {
        const id = state.publication, before = state.before;
        const seq = ++state.listSeq;
        if (!id) return;
        try {
            const params = new URLSearchParams({ publicationId: String(id), pageSize: '25' });
            if (before) params.set('beforeId', String(before));
            if ($('rescrutinyStatusFilter').value) params.set('status', $('rescrutinyStatusFilter').value);
            const data = await api('results/rescrutiny?' + params);
            if (seq !== state.listSeq || id !== state.publication || before !== state.before) return;
            state.items = data.items || []; state.hasMore = !!data.hasMore;
            state.nextBeforeId = data.nextBeforeId;
            const tbody = $('rescrutinyRows'); tbody.replaceChildren();
            for (const item of state.items) {
                const row = document.createElement('tr');
                row.append(cell(item.studentCode + ' / ' + item.rollNo),
                    cell(new Date(item.requestedAt).toLocaleString()),
                    cell(status(item.status)), cell(item.requestReason));
                const actions = document.createElement('td');
                const historyButton = document.createElement('button');
                historyButton.type = 'button'; historyButton.className = 'btn btn-sm btn-outline-secondary';
                historyButton.textContent = tr('History', 'ইতিহাস');
                historyButton.addEventListener('click', () => loadHistory(item));
                actions.append(historyButton);
                const marksButton = document.createElement('button');
                marksButton.type = 'button'; marksButton.className = 'btn btn-sm btn-outline-secondary ms-1';
                marksButton.textContent = tr('Mark evidence', 'নম্বরের প্রমাণ');
                marksButton.addEventListener('click', () => loadMarkEvidence(item));
                actions.append(marksButton);
                if (item.status === 1 || item.status === 'Requested') {
                    const button = document.createElement('button');
                    button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary';
                    button.textContent = tr('Review', 'পর্যালোচনা');
                    button.addEventListener('click', () => loadReview(item));
                    button.classList.add('ms-1');
                    actions.append(button);
                } else if (item.status === 3 || item.status === 'AcceptedForCorrection') {
                    const button = document.createElement('button');
                    button.type = 'button'; button.className = 'btn btn-sm btn-outline-danger ms-1';
                    button.textContent = tr('Correct', 'সংশোধন');
                    button.addEventListener('click', () => loadCorrection(item));
                    actions.append(button);
                    const reason = document.createElement('span');
                    reason.className = 'small ms-2'; reason.textContent = item.decisionReason || '—';
                    actions.append(reason);
                } else {
                    const reason = document.createElement('span');
                    reason.className = 'small ms-2'; reason.textContent = item.decisionReason || '—';
                    actions.append(reason);
                }
                row.append(actions); tbody.append(row);
            }
            if (!state.items.length)
                placeholder('rescrutinyRows', tr('No applications on this page.', 'এই পৃষ্ঠায় কোনো আবেদন নেই।'), 5);
            $('rescrutinyPrev').disabled = !state.previous.length;
            $('rescrutinyNext').disabled = !state.hasMore;
            $('rescrutinyCsv').disabled = !state.publication || state.exportBusy;
            $('rescrutinyPage').textContent = tr('Page ', 'পৃষ্ঠা ') + (state.previous.length + 1);
        } catch (e) { if (seq === state.listSeq) notice(e.message, true); }
    }
    async function loadReview(item) {
        const seq = ++state.reviewSeq;
        state.selected = null; state.decisionKey = null;
        $('rescrutinyDecisionForm').classList.add('d-none');
        $('rescrutinyReviewEvidence').classList.add('d-none');
        try {
            const data = await api('results/rescrutiny/' +
                encodeURIComponent(item.caseReference) + '/review');
            if (seq !== state.reviewSeq || state.publication !== item.publicationId) return;
            if (data.status !== 1 && data.status !== 'Requested') {
                notice(tr('Case already decided. Refresh the list.',
                    'আবেদনের সিদ্ধান্ত হয়েছে। তালিকা রিফ্রেশ করুন।'), true);
                await loadCases(); return;
            }
            state.selected = { ...item, requestRowVersion: data.requestRowVersion };
            const show = (id, value) => { $(id).textContent = value == null ? '—' : String(value); };
            show('rescrutinyReviewMarks', data.obtainedMarks + ' / ' + data.totalMarks);
            show('rescrutinyReviewPercentage', data.percentage == null ? null : data.percentage + '%');
            show('rescrutinyReviewGpa', (data.gpa ?? '—') + ' / ' + (data.cgpa ?? '—'));
            show('rescrutinyReviewGrade', data.gradeLetter);
            show('rescrutinyReviewOutcome', data.isWithheld ?
                tr('Withheld', 'স্থগিত') : data.isPassed ? tr('Passed', 'উত্তীর্ণ') : tr('Failed', 'অনুত্তীর্ণ'));
            show('rescrutinyReviewVersion', data.publicationVersionNo);
            $('rescrutinyDecisionTitle').textContent =
                tr('Review case ', 'আবেদন পর্যালোচনা ') + data.studentCode;
            $('rescrutinyDecisionReason').value = '';
            $('rescrutinyReviewEvidence').classList.remove('d-none');
            $('rescrutinyDecisionForm').classList.remove('d-none');
            $('rescrutinyDecisionReason').focus();
        } catch (e) { if (seq === state.reviewSeq) notice(e.message, true); }
    }

    async function loadMarkEvidence(item) {
        const seq = ++state.marksSeq;
        state.markCase = null; $('rescrutinyMarkCsv').disabled = true;
        $('rescrutinyMarkEvidence').classList.add('d-none');
        try {
            const data = await api('results/rescrutiny/' +
                encodeURIComponent(item.caseReference) + '/marks');
            if (seq !== state.marksSeq || state.publication !== item.publicationId) return;
            $('rescrutinyMarkEvidenceTitle').textContent =
                tr('Current marks: ', 'বর্তমান নম্বর: ') + data.studentCode;
            $('rescrutinyMarkEvidenceSummary').textContent =
                tr('Published snapshot: ', 'প্রকাশিত স্ন্যাপশট: ') +
                data.publishedObtainedMarks + ' / ' + data.publishedTotalMarks +
                ' · ' + tr('Current working marks are not a historical subject snapshot.',
                    'বর্তমান নম্বর পূর্বের প্রকাশিত বিষয়ভিত্তিক স্ন্যাপশট নয়।');
            const body = $('rescrutinyMarkEvidenceRows'); body.replaceChildren();
            for (const subject of data.subjects || []) {
                const row = document.createElement('tr');
                row.append(cell(subject.subjectName),
                    cell(subject.currentObtainedMarks == null ? '—' :
                        subject.currentObtainedMarks + ' / ' + subject.fullMarks),
                    cell(subject.isWithheld ? tr('Withheld', 'স্থগিত') :
                        subject.isAbsent ? tr('Absent', 'অনুপস্থিত') :
                            (subject.currentGradeLetter || '—')));
                body.append(row);
                for (const component of subject.components || []) {
                    const part = document.createElement('tr');
                    part.append(cell('↳ ' + component.name),
                        cell(component.currentObtainedMarks == null ? '—' :
                            component.currentObtainedMarks + ' / ' + component.fullMarks),
                        cell(component.isWithheld ? tr('Withheld', 'স্থগিত') :
                            component.isAbsent ? tr('Absent', 'অনুপস্থিত') : '—'));
                    body.append(part);
                }
            }
            if (!body.children.length)
                placeholder('rescrutinyMarkEvidenceRows',
                    tr('No registered marks found.', 'কোনো নিবন্ধিত নম্বর পাওয়া যায়নি।'), 3);
            state.markCase = item.caseReference;
            $('rescrutinyMarkCsv').disabled = false;
            $('rescrutinyMarkEvidence').classList.remove('d-none');
        } catch (e) { if (seq === state.marksSeq) notice(e.message, true); }
    }

    function correctionNumber(labelText, value, max, container) {
        const wrap = document.createElement('label');
        wrap.className = 'form-label small me-2 mb-1';
        wrap.append(document.createTextNode(labelText + ' '));
        const input = document.createElement('input');
        input.type = 'number'; input.step = '0.01'; input.min = '0';
        input.max = String(max); input.required = true;
        input.className = 'form-control form-control-sm';
        input.style.width = '8rem'; input.value = String(value ?? 0);
        wrap.append(input); container.append(wrap);
        return input;
    }
    function correctionFlag(labelText, checked, container) {
        const wrap = document.createElement('label');
        wrap.className = 'form-check form-check-inline ms-2';
        const input = document.createElement('input');
        input.type = 'checkbox'; input.className = 'form-check-input';
        input.checked = !!checked;
        const text = document.createElement('span');
        text.className = 'form-check-label'; text.textContent = labelText;
        wrap.append(input, text); container.append(wrap);
        return input;
    }
    async function loadCorrection(item) {
        if (state.correctionBusy || !state.publication) return;
        const seq = ++state.correctionSeq;
        state.correctionCase = null; state.correctionEvidence = null; state.correctionKey = null;
        $('rescrutinyCorrectionForm').classList.add('d-none');
        try {
            // Historical corrections are exposed read-only; replay must never
            // overwrite a previous immutable correction event or result version.
            try {
                const previous = await api('results/rescrutiny/' +
                    encodeURIComponent(item.caseReference) + '/correction');
                if (seq !== state.correctionSeq) return;
                notice(tr('Already corrected in version ', 'ইতিমধ্যে সংশোধিত সংস্করণ ') +
                    previous.newVersionNo + '.');
                return;
            } catch (_) { /* Nonexistent correction: the separate marks authorization still applies. */ }
            if ($('rescrutinyPublication').selectedOptions[0]?.dataset.withdrawn === 'true') {
                notice(tr('This version is withdrawn. Use the latest published version.',
                    'এই সংস্করণ প্রত্যাহার হয়েছে। সর্বশেষ প্রকাশিত সংস্করণ ব্যবহার করুন।'), true);
                return;
            }
            const evidence = await api('results/rescrutiny/' +
                encodeURIComponent(item.caseReference) + '/marks');
            if (seq !== state.correctionSeq || state.publication !== item.publicationId) return;
            if (evidence.publicationId !== item.publicationId ||
                String(evidence.caseReference).toLowerCase() !== String(item.caseReference).toLowerCase() ||
                !evidence.publicationRowVersion || !Array.isArray(evidence.subjects))
                throw new Error(tr('Correction evidence changed; refresh.',
                    'সংশোধনের প্রমাণ বদলেছে; রিফ্রেশ করুন।'));
            const container = $('rescrutinyCorrectionSubjects'); container.replaceChildren();
            const editors = [];
            for (const subject of evidence.subjects) {
                if (!subject.studentAssessmentMarkId || !subject.markRowVersion ||
                    !Array.isArray(subject.components) ||
                    subject.components.some(x => !x.markRowVersion)) continue;
                const section = document.createElement('section');
                section.className = 'border rounded p-2';
                const title = document.createElement('label');
                title.className = 'form-check d-flex gap-2 align-items-center mb-2';
                const enabled = document.createElement('input');
                enabled.type = 'checkbox'; enabled.className = 'form-check-input';
                const name = document.createElement('span');
                name.className = 'form-check-label fw-semibold';
                name.textContent = subject.subjectName + ' (' + subject.currentObtainedMarks +
                    ' / ' + subject.fullMarks + ')';
                title.append(enabled, name); section.append(title);
                const fields = document.createElement('div'); fields.className = 'ps-3';
                const marks = correctionNumber(tr('Marks', 'নম্বর'),
                    subject.currentObtainedMarks, subject.fullMarks, fields);
                const absent = correctionFlag(tr('Absent', 'অনুপস্থিত'), subject.isAbsent, fields);
                const withheld = correctionFlag(tr('Withheld', 'স্থগিত'), subject.isWithheld, fields);
                const components = [];
                for (const part of subject.components) {
                    const field = document.createElement('div');
                    field.className = 'border-start ps-2 ms-2 mt-2';
                    const partName = document.createElement('span');
                    partName.className = 'd-block small fw-semibold'; partName.textContent = part.name;
                    field.append(partName);
                    const quantity = correctionNumber(tr('Component', 'অংশ'),
                        part.currentObtainedMarks, part.fullMarks, field);
                    const isAbsent = correctionFlag(tr('Absent', 'অনুপস্থিত'), part.isAbsent, field);
                    const isWithheld = correctionFlag(tr('Withheld', 'স্থগিত'), part.isWithheld, field);
                    components.push({ data: part, quantity, isAbsent, isWithheld });
                    fields.append(field);
                }
                const enabledInputs = [marks, absent, withheld, ...components.flatMap(x =>
                    [x.quantity, x.isAbsent, x.isWithheld])];
                const toggle = () => { for (const x of enabledInputs) x.disabled = !enabled.checked; };
                enabled.addEventListener('change', toggle); toggle();
                enabledInputs.forEach(x => x.addEventListener('input', () => {
                    if (!state.correctionBusy) state.correctionKey = null;
                }));
                section.append(fields); container.append(section);
                editors.push({ subject, enabled, marks, absent, withheld, components });
            }
            if (!editors.length)
                throw new Error(tr('No complete subject-mark evidence is available.',
                    'সম্পূর্ণ বিষয়ভিত্তিক নম্বরের প্রমাণ পাওয়া যায়নি।'));
            state.correctionCase = item.caseReference;
            state.correctionEvidence = { evidence, editors, publication: state.publication };
            $('rescrutinyCorrectionTitle').textContent =
                tr('Correct accepted case: ', 'গৃহীত আবেদন সংশোধন: ') + evidence.studentCode;
            $('rescrutinyCorrectionReason').value = '';
            $('rescrutinyCorrectionForm').classList.remove('d-none');
            $('rescrutinyCorrectionReason').focus();
        } catch (e) { if (seq === state.correctionSeq) notice(e.message, true); }
    }
    async function applyCorrection(event) {
        event.preventDefault();
        const choice = state.correctionEvidence;
        if (!choice || state.correctionBusy || choice.publication !== state.publication ||
            !state.correctionCase || !$('rescrutinyCorrectionForm').reportValidity()) return;
        const selected = choice.editors.filter(x => x.enabled.checked);
        if (!selected.length || selected.length > 25) {
            notice(tr('Select between 1 and 25 changed subjects.',
                '১ থেকে ২৫টি পরিবর্তিত বিষয় নির্বাচন করুন।'), true); return;
        }
        if (!window.confirm(tr('Apply the corrections and publish a new immutable version?',
            'সংশোধন প্রয়োগ করে নতুন অপরিবর্তনীয় সংস্করণ প্রকাশ করবেন?'))) return;
        const subjects = selected.map(x => ({
            assessmentSubjectId: x.subject.assessmentSubjectId,
            rowVersion: x.subject.markRowVersion,
            obtainedMarks: Number(x.marks.value),
            isAbsent: x.absent.checked, isWithheld: x.withheld.checked,
            components: x.components.map(c => ({
                assessmentComponentId: c.data.assessmentComponentId,
                rowVersion: c.data.markRowVersion,
                obtainedMarks: Number(c.quantity.value),
                isAbsent: c.isAbsent.checked, isWithheld: c.isWithheld.checked
            }))
        }));
        state.correctionBusy = true; $('rescrutinyCorrectionApply').disabled = true;
        state.correctionKey ||= crypto.randomUUID();
        try {
            const result = await api('results/rescrutiny/' +
                encodeURIComponent(state.correctionCase) + '/correction', 'POST', {
                clientRequestId: state.correctionKey,
                publicationRowVersion: choice.evidence.publicationRowVersion,
                reason: $('rescrutinyCorrectionReason').value.trim(), subjects
            });
            state.correctionKey = null; state.correctionCase = null;
            state.correctionEvidence = null; state.correctionSeq++;
            $('rescrutinyCorrectionForm').classList.add('d-none');
            notice(tr('Correction published as version ', 'সংশোধিত ফলাফল প্রকাশিত সংস্করণ ') +
                result.newVersionNo + '.');
            await loadVersions();
        } catch (e) { notice(e.message, true); }
        finally { state.correctionBusy = false; $('rescrutinyCorrectionApply').disabled = false; }
    }

    async function loadHistory(item) {
        const seq = ++state.historySeq;
        $('rescrutinyHistory').classList.add('d-none');
        try {
            const data = await api('results/rescrutiny/' +
                encodeURIComponent(item.caseReference) + '/history');
            if (seq !== state.historySeq || state.publication !== item.publicationId) return;
            $('rescrutinyHistoryTitle').textContent = tr('Case history: ', 'আবেদনের ইতিহাস: ') + item.studentCode;
            const rows = $('rescrutinyHistoryRows'); rows.replaceChildren();
            for (const event of data.events || []) {
                const row = document.createElement('tr');
                row.append(cell(event.action === 1 || event.action === 'Requested' ?
                    tr('Application', 'আবেদন') : status(event.action)),
                    cell(new Date(event.occurredAt).toISOString()),
                    cell(event.actorUserId), cell(event.reason));
                rows.append(row);
            }
            $('rescrutinyHistory').classList.remove('d-none');
        } catch (e) { if (seq === state.historySeq) notice(e.message, true); }
    }
    async function selectPublication() {
        const id = Number($('rescrutinyPublication').value);
        resetPublication();
        if (!Number.isSafeInteger(id) || id <= 0) return;
        state.publication = id;
        $('rescrutinyCsv').disabled = false;
        await Promise.all([loadCases(), loadStudents(), loadSummary()]);
    }
    async function submitRequest(event) {
        event.preventDefault();
        if (state.busy || !state.publication || !$('rescrutinyStudent').value ||
            $('rescrutinyPublication').selectedOptions[0]?.dataset.withdrawn === 'true') return;
        if (!$('rescrutinyRequestForm').reportValidity()) return;
        state.busy = true; $('rescrutinySubmit').disabled = true;
        state.requestKey ||= crypto.randomUUID();
        try {
            await api('results/rescrutiny', 'POST', {
                clientRequestId: state.requestKey, publicationId: state.publication,
                studentReference: $('rescrutinyStudent').value,
                reason: $('rescrutinyReason').value.trim()
            });
            state.requestKey = null; $('rescrutinyReason').value = '';
            state.before = null; state.previous = [];
            notice(tr('Application recorded.', 'আবেদন সংরক্ষিত হয়েছে।'));
            await Promise.all([loadCases(), loadSummary()]);
        } catch (e) { notice(e.message, true); }
        finally { state.busy = false; $('rescrutinySubmit').disabled = !$('rescrutinyStudent').value ||
            $('rescrutinyPublication').selectedOptions[0]?.dataset.withdrawn === 'true'; }
    }
    async function decide(event) {
        event.preventDefault();
        const item = state.selected;
        if (!item || state.busy || !$('rescrutinyDecisionForm').reportValidity()) return;
        state.busy = true; $('rescrutinyDecide').disabled = true;
        state.decisionKey ||= crypto.randomUUID();
        try {
            await api('results/rescrutiny/' + encodeURIComponent(item.caseReference) + '/decision', 'POST', {
                clientRequestId: state.decisionKey, requestRowVersion: item.requestRowVersion,
                decision: Number($('rescrutinyDecision').value),
                reason: $('rescrutinyDecisionReason').value.trim()
            });
            state.decisionKey = null; state.selected = null; state.reviewSeq++;
            $('rescrutinyReviewEvidence').classList.add('d-none');
            $('rescrutinyDecisionForm').classList.add('d-none');
            notice(tr('Decision recorded. Published marks are unchanged.',
                'সিদ্ধান্ত সংরক্ষিত হয়েছে। প্রকাশিত নম্বর অপরিবর্তিত।'));
            await Promise.all([loadCases(), loadSummary()]);
        } catch (e) { notice(e.message, true); }
        finally { state.busy = false; $('rescrutinyDecide').disabled = false; }
    }
    function downloadCsv(filename, rows) {
        const safe = value => {
            let s = String(value ?? '');
            if (/^\s*[=+\-@]/.test(s)) s = "'" + s;
            return '"' + s.replace(/"/g, '""') + '"';
        };
        const blob = new Blob(['\uFEFF' + rows.map(x => x.map(safe).join(',')).join('\r\n')],
            { type: 'text/csv;charset=utf-8' });
        const url = URL.createObjectURL(blob), a = document.createElement('a');
        a.href = url; a.download = filename; a.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    async function exportMarkEvidence() {
        const caseReference = state.markCase, publication = state.publication;
        if (!caseReference || !publication || state.markExportBusy) return;
        state.markExportBusy = true; $('rescrutinyMarkCsv').disabled = true;
        try {
            // Re-fetch from the authorized, no-store API. Never export a stale
            // case after a publication or tenant navigation change.
            const data = await api('results/rescrutiny/' +
                encodeURIComponent(caseReference) + '/marks');
            if (state.markCase !== caseReference || state.publication !== publication) return;
            if (String(data.caseReference).toLowerCase() !== caseReference.toLowerCase() ||
                data.publicationId !== publication || !Array.isArray(data.subjects) ||
                data.subjects.length > 100 ||
                data.subjects.some(x => !Array.isArray(x.components) || x.components.length > 400))
                throw new Error(tr('Evidence scope mismatch; refresh.', 'প্রমাণের পরিধি মিলছে না; রিফ্রেশ করুন।'));
            const captured = data.retrievedAtUtc || '';
            const rows = [['Source', 'Retrieved UTC', 'Case', 'Version', 'Student',
                'Subject / component', 'Obtained marks', 'Full marks', 'Status', 'Grade']];
            rows.push(['PUBLISHED_AGGREGATE', captured, caseReference,
                data.publicationVersionNo, data.studentCode, 'Published total',
                data.publishedObtainedMarks, data.publishedTotalMarks, '', '']);
            for (const subject of data.subjects) {
                const subjectStatus = subject.isWithheld ? 'Withheld' :
                    subject.isAbsent ? 'Absent' : '';
                rows.push(['CURRENT_WORKING_MARKS_NOT_HISTORICAL', captured, caseReference,
                    data.publicationVersionNo, data.studentCode, subject.subjectName,
                    subject.currentObtainedMarks, subject.fullMarks, subjectStatus,
                    subject.currentGradeLetter]);
                for (const part of subject.components)
                    rows.push(['CURRENT_WORKING_MARKS_NOT_HISTORICAL', captured, caseReference,
                        data.publicationVersionNo, data.studentCode,
                        subject.subjectName + ' / ' + part.name,
                        part.currentObtainedMarks, part.fullMarks,
                        part.isWithheld ? 'Withheld' : part.isAbsent ? 'Absent' : '', '']);
            }
            downloadCsv('rescrutiny-evidence-' + caseReference + '.csv', rows);
        } catch (e) { notice(e.message, true); }
        finally {
            state.markExportBusy = false;
            $('rescrutinyMarkCsv').disabled = !state.markCase;
        }
    }
    async function csv() {
        if (!state.publication || state.exportBusy) return;
        const publication = state.publication;
        state.exportBusy = true; $('rescrutinyCsv').disabled = true;
        try {
            const params = new URLSearchParams({ publicationId: String(publication), pageSize: '50' });
            const filter = $('rescrutinyStatusFilter').value;
            if (filter) params.set('status', filter);
            const data = await api('results/rescrutiny/export?' + params);
            if (publication !== state.publication) return;
            const rows = [['Student', 'Roll', 'Status', 'Requested UTC', 'Request reason',
                'Decided UTC', 'Decision reason']];
            for (const item of data.items || [])
                rows.push([item.studentCode, item.rollNo, status(item.status), item.requestedAt,
                    item.requestReason, item.decidedAt, item.decisionReason]);
            downloadCsv('rescrutiny-publication-' + publication + '.csv', rows);
            if (data.truncated) notice(tr('Export limited to 500 cases; narrow the status filter.',
                'এক্সপোর্ট ৫০০ আবেদনে সীমিত; অবস্থা অনুযায়ী ফিল্টার করুন।'), true);
        } catch (e) { notice(e.message, true); }
        finally {
            state.exportBusy = false;
            $('rescrutinyCsv').disabled = !state.publication;
        }
    }
    $('rescrutinyScopeForm').addEventListener('submit', e => {
        e.preventDefault(); state.scopePage = 1; loadScopes();
    });
    $('rescrutinyScopePrev').addEventListener('click', () => {
        if (state.scopePage > 1) { state.scopePage--; loadScopes(); }
    });
    $('rescrutinyScopeNext').addEventListener('click', () => {
        if (state.scopePage * 30 < state.scopeTotal) { state.scopePage++; loadScopes(); }
    });
    $('rescrutinyScope').addEventListener('change', loadVersions);
    $('rescrutinyPublication').addEventListener('change', selectPublication);
    $('rescrutinyStudentFind').addEventListener('click', loadStudents);
    $('rescrutinyRequestForm').addEventListener('submit', submitRequest);
    $('rescrutinyDecisionForm').addEventListener('submit', decide);
    $('rescrutinyCorrectionForm').addEventListener('submit', applyCorrection);
    $('rescrutinyCorrectionCancel').addEventListener('click', () => {
        state.correctionSeq++; state.correctionCase = null;
        state.correctionEvidence = null; state.correctionKey = null;
        $('rescrutinyCorrectionForm').classList.add('d-none');
    });
    $('rescrutinyCorrectionReason').addEventListener('input', () => {
        if (!state.correctionBusy) state.correctionKey = null;
    });
    $('rescrutinyCancel').addEventListener('click', () => {
        state.selected = null; state.decisionKey = null; state.reviewSeq++;
        $('rescrutinyReviewEvidence').classList.add('d-none');
        $('rescrutinyDecisionForm').classList.add('d-none');
    });
    $('rescrutinyPrev').addEventListener('click', () => {
        if (!state.previous.length) return;
        state.before = state.previous.pop(); loadCases();
    });
    $('rescrutinyNext').addEventListener('click', () => {
        if (!state.hasMore || !state.nextBeforeId) return;
        state.previous.push(state.before); state.before = state.nextBeforeId; loadCases();
    });
    $('rescrutinyStatusFilter').addEventListener('change', () => {
        state.before = null; state.previous = []; state.historySeq++; state.reviewSeq++; state.marksSeq++;
        state.correctionSeq++; state.correctionCase = null;
        state.correctionEvidence = null; state.correctionKey = null;
        $('rescrutinyCorrectionForm').classList.add('d-none');
        $('rescrutinyMarkEvidence').classList.add('d-none');
        state.markCase = null; $('rescrutinyMarkCsv').disabled = true;
        state.selected = null; state.decisionKey = null;
        $('rescrutinyDecisionForm').classList.add('d-none');
        $('rescrutinyReviewEvidence').classList.add('d-none');
        $('rescrutinyHistory').classList.add('d-none');
        loadCases();
    });
    $('rescrutinyHistoryClose').addEventListener('click', () => {
        state.historySeq++;
        $('rescrutinyHistory').classList.add('d-none');
    });
    $('rescrutinyMarkEvidenceClose').addEventListener('click', () => {
        state.marksSeq++; state.markCase = null;
        $('rescrutinyMarkCsv').disabled = true;
        $('rescrutinyMarkEvidence').classList.add('d-none');
    });
    $('rescrutinyMarkCsv').addEventListener('click', exportMarkEvidence);
    $('rescrutinyCsv').addEventListener('click', csv);
    $('rescrutinyPrint').addEventListener('click', () => window.print());
    loadScopes();
})();
