(() => {
    'use strict';
    const root = document.getElementById('omrWorkspace');
    if (!root) return;
    const $ = id => document.getElementById(id);
    const bn = (document.documentElement.lang || '').toLowerCase().startsWith('bn');
    const words = {
        title: ['Smart OMR', 'স্মার্ট ওএমআর'], scope: ['Assessment / batch / subject', 'পরীক্ষা / ব্যাচ / বিষয়'],
        component: ['Assessment component', 'পরীক্ষার অংশ'], newTemplate: ['New immutable answer template', 'নতুন অপরিবর্তনীয় উত্তর টেমপ্লেট'],
        name: ['Form name', 'ফর্মের নাম'], key: ['Answer key (A–E)', 'সঠিক উত্তর (A–E)'], options: ['Options', 'অপশন'],
        correct: ['Correct', 'সঠিকের নম্বর'], penalty: ['Penalty', 'ভুলের কর্তন'], seal: ['Seal template', 'টেমপ্লেট সংরক্ষণ'],
        capture: ['Upload and recognize', 'আপলোড ও শনাক্তকরণ'], template: ['OMR template', 'ওএমআর টেমপ্লেট'],
        student: ['Registered student', 'নিবন্ধিত শিক্ষার্থী'], image: ['Straight PNG/JPEG scan (up to 5 MB)', 'সোজা PNG/JPEG স্ক্যান (সর্বোচ্চ ৫ এমবি)'],
        recognize: ['Upload and recognize', 'আপলোড ও শনাক্ত'], print: ['Download blank sheet', 'খালি উত্তরপত্র ডাউনলোড'],
        queue: ['Review queue', 'যাচাইয়ের তালিকা'], refresh: ['Refresh', 'রিফ্রেশ'], state: ['Status', 'অবস্থা'],
        uncertain: ['Uncertain', 'অনিশ্চিত'], marks: ['Proposed marks', 'প্রস্তাবিত নম্বর'], action: ['Action', 'অ্যাকশন'],
        prev: ['Previous', 'আগের'], next: ['Next', 'পরের'], review: ['Verify all choices against the original image', 'মূল ছবির সঙ্গে সব উত্তর মিলিয়ে দেখুন'],
        original: ['Open original scan', 'মূল স্ক্যান খুলুন'], suggested: ['Machine suggestions (? = uncertain)', 'যন্ত্রের প্রস্তাব (? = অনিশ্চিত)'],
        confirmed: ['Confirmed choices (- = blank)', 'যাচাইকৃত উত্তর (- = ফাঁকা)'], reason: ['Correction reason', 'সংশোধনের কারণ'],
        approve: ['Confirm independent review', 'স্বতন্ত্র যাচাই অনুমোদন'], cancel: ['Cancel', 'বাতিল'],
        post: ['Post verified marks', 'যাচাইকৃত নম্বর পোস্ট করুন'],
        export: ['Export posted marks', 'পোস্টকৃত নম্বর এক্সপোর্ট'],
        exportFormat: ['Internal preparation export', 'অভ্যন্তরীণ প্রস্তুতির রিপোর্ট'],
        identity: ['I checked the printed form and student roll/identity against the selected enrollment', 'মুদ্রিত ফর্ম ও শিক্ষার্থীর রোল/পরিচয় তালিকার সঙ্গে মিলিয়ে দেখেছি'],
        manual: ['Smart OMR page manual', 'স্মার্ট ওএমআর ব্যবহারের নিয়ম']
    };
    const t = key => (words[key] || [key, key])[bn ? 1 : 0];
    root.querySelectorAll('[data-omr-i18n]').forEach(el => { el.textContent = t(el.dataset.omrI18n); });
    const manual = bn
        ? '<p>পরীক্ষা ব্যবস্থাপক টেমপ্লেট তৈরি করেন। অনুমোদিত শিক্ষক ও ব্যবস্থাপক স্ক্যান করতে পারেন।</p><ol><li>পরীক্ষা, ব্যাচ, বিষয় ও অংশ বেছে নিন।</li><li>সঠিক উত্তর-কী দিয়ে টেমপ্লেট সিল করে ফর্ম প্রিন্ট করুন।</li><li>সোজা, যথাযথ আলোর স্ক্যান আপলোড করুন।</li><li>অন্য অনুমোদিত ব্যক্তি মূল ছবির সঙ্গে প্রতিটি উত্তর যাচাই করে সংশোধনের কারণসহ অনুমোদন দেবেন।</li><li>ব্যবস্থাপক Reviewed শিটে Post verified marks ক্লিক করে বিদ্যমান Assessment marks-এ একবার পোস্ট করবেন।</li><li>পোস্টকৃত নম্বরের Excel/PDF অভ্যন্তরীণ রিপোর্ট ডাউনলোড করতে পারবেন। এটি সরাসরি কোনো শিক্ষা বোর্ডে জমা নয়।</li></ol><p>আগে ম্যানুয়ালি দেওয়া বা অমিল নম্বর থাকলে পোস্ট ব্যর্থ হবে; বিদ্যমান marks-amendment workflow ব্যবহার করুন। capture ও review একই ব্যক্তি করতে পারবেন না। একই request পুনরায় পাঠালে duplicate হবে না।</p>'
        : '<p>Exam managers seal answer templates; assigned teachers and managers may capture sheets.</p><ol><li>Select assessment, batch, subject and component.</li><li>Seal the answer key; download the printable sheet.</li><li>Upload a straight, well-lit scan. Poor scans are rejected.</li><li>A different authorized staff member compares every choice with the original and confirms the review, recording correction reasons.</li><li>An exam manager separately posts a Reviewed sheet once to the existing Assessment marks register.</li><li>Export posted marks as internal-preparation Excel or PDF. These are not direct board submissions.</li></ol><p>Existing/manual or unreconciled marks block posting; use the approved marks-amendment workflow. The capturer cannot review their own sheet, and retries must reuse request IDs.</p>';
    $('omrHelpText').innerHTML = manual;
    const help = $('omrHelp');
    $('omrHelpOpen').addEventListener('click', () => { help.classList.remove('d-none'); $('omrHelpOpen').setAttribute('aria-expanded', 'true'); help.focus(); });
    const closeHelp = () => { help.classList.add('d-none'); $('omrHelpOpen').setAttribute('aria-expanded', 'false'); $('omrHelpOpen').focus(); };
    $('omrHelpClose').addEventListener('click', closeHelp);
    help.addEventListener('keydown', e => { if (e.key === 'Escape') closeHelp(); });
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { scopes: [], components: [], students: [], templates: [], sheets: [], page: 1, total: 0, selectedSheet: null, uploadKey: null, captureKey: null, reviewKey: null, postKey: null };
    function notice(message, error = false) {
        const el = $('omrNotice'); el.textContent = message; el.className = `alert ${error ? 'alert-danger' : 'alert-success'}`;
    }
    async function api(path, options = {}) {
        const response = await fetch(`/api/${path}`, { credentials: 'same-origin', cache: 'no-store',
            ...options, headers: { Accept: 'application/json', 'RequestVerificationToken': token, ...(options.headers || {}) } });
        const result = await response.json().catch(() => ({ success: false, message: `HTTP ${response.status}` }));
        if (!response.ok || result.success === false) throw new Error(result.message || `HTTP ${response.status}`);
        return result.data;
    }
    function option(select, value, label) {
        const opt = document.createElement('option'); opt.value = String(value); opt.textContent = label; select.append(opt);
    }
    function clear(select) { select.replaceChildren(); option(select, '', '—'); }
    function currentScope() { return state.scopes.find(x => String(x.assessmentSubjectId) === $('omrScope').value); }
    async function scopes() {
        const data = await api('assessment-administration/scopes/page?page=1&pageSize=100');
        state.scopes = data.items || [];
        clear($('omrScope'));
        state.scopes.forEach(x => option($('omrScope'), x.assessmentSubjectId,
            `${x.assessmentName} / ${x.academicBatchName} / ${x.subjectName}`));
    }
    async function selectScope() {
        state.components = []; state.students = []; clear($('omrComponent')); clear($('omrStudent'));
        const s = currentScope(); if (!s) return;
        const qs = new URLSearchParams({ AssessmentId: s.assessmentId, AcademicBatchId: s.academicBatchId,
            AssessmentSubjectId: s.assessmentSubjectId, Page: 1, PageSize: 100 });
        const roster = await api(`assessment-administration/component-marks/roster?${qs}`);
        state.components = roster.components || []; state.students = roster.students || [];
        state.components.forEach(x => option($('omrComponent'), x.id, `${x.name} (${x.fullMarks})`));
        state.students.forEach(x => option($('omrStudent'), x.studentSubjectRegistrationId,
            `${x.rollNo || x.studentCode} - ${x.studentName}`));
        if (state.components.length) { $('omrComponent').value = String(state.components[0].id); await selectComponent(); }
    }
    async function selectComponent() {
        const id = Number($('omrComponent').value); clear($('omrTemplate')); state.templates = [];
        if (!id) return;
        state.templates = await api(`assessment-omr/templates?componentId=${id}`);
        state.templates.forEach(x => option($('omrTemplate'), x.reference, `${x.name} (${x.questionCount} Q)`));
        if (state.templates.length) { $('omrTemplate').value = state.templates[0].reference; await listSheets(); }
        else { state.sheets = []; renderRows(); }
    }
    async function listSheets() {
        const reference = $('omrTemplate').value; if (!reference) return;
        const data = await api(`assessment-omr/sheets?templateReference=${encodeURIComponent(reference)}&page=${state.page}&pageSize=30`);
        state.sheets = data.items || []; state.total = data.totalCount || 0; renderRows();
    }
    function renderRows() {
        const tbody = $('omrRows'); tbody.replaceChildren();
        state.sheets.forEach(sheet => {
            const tr = document.createElement('tr');
            const label = state.students.find(x => x.studentSubjectRegistrationId === sheet.studentSubjectRegistrationId);
            [label ? `${label.rollNo || label.studentCode} - ${label.studentName}` : `#${sheet.studentSubjectRegistrationId}`,
                sheet.state, String(sheet.uncertainCount), sheet.proposedMarks == null ? '—' : String(sheet.proposedMarks)]
                .forEach(value => { const td = document.createElement('td'); td.textContent = value; tr.append(td); });
            const td = document.createElement('td'), button = document.createElement('button');
            button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary'; button.textContent = t('review');
            button.addEventListener('click', () => openReview(sheet)); td.append(button); tr.append(td); tbody.append(tr);
        });
        $('omrPrev').disabled = state.page <= 1; $('omrNext').disabled = state.page * 30 >= state.total;
    }
    function openReview(sheet) {
        state.selectedSheet = sheet; state.reviewKey = null; state.postKey = null;
        $('omrReviewForm').classList.remove('d-none');
        $('omrSuggested').value = sheet.suggestedAnswers;
        $('omrConfirmed').value = sheet.confirmedAnswers || sheet.suggestedAnswers.replaceAll('?', '-');
        $('omrConfirmed').maxLength = sheet.suggestedAnswers.length;
        $('omrReason').value = '';
        const student = state.students.find(x => x.studentSubjectRegistrationId === sheet.studentSubjectRegistrationId);
        $('omrIdentity').textContent = student ?
            `${student.rollNo || student.studentCode} — ${student.studentName} (registration ${sheet.studentSubjectRegistrationId})` :
            `Registration ${sheet.studentSubjectRegistrationId}; verify the original printed roll before approving.`;
        $('omrIdentityConfirmed').checked = false;
        $('omrOriginal').href = `/api/assessment-omr/sheets/${encodeURIComponent(sheet.reference)}/image`;
        $('omrReviewForm').querySelector('button[type="submit"]').disabled = sheet.state !== 'Captured';
        $('omrConfirmed').readOnly = sheet.state !== 'Captured';
        $('omrReason').readOnly = sheet.state !== 'Captured';
        if ($('omrPost')) $('omrPost').disabled = sheet.state !== 'Reviewed';
        $('omrReviewForm').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
    }
    $('omrScope').addEventListener('change', () => selectScope().catch(e => notice(e.message, true)));
    $('omrComponent').addEventListener('change', () => { state.page = 1; selectComponent().catch(e => notice(e.message, true)); });
    $('omrTemplate').addEventListener('change', () => { state.page = 1; listSheets().catch(e => notice(e.message, true)); });
    $('omrRefresh').addEventListener('click', () => listSheets().catch(e => notice(e.message, true)));
    $('omrPrev').addEventListener('click', () => { if (state.page > 1) { state.page--; listSheets().catch(e => notice(e.message, true)); } });
    $('omrNext').addEventListener('click', () => { if (state.page * 30 < state.total) { state.page++; listSheets().catch(e => notice(e.message, true)); } });
    $('omrCancel').addEventListener('click', () => { state.selectedSheet = null; state.reviewKey = null; state.postKey = null; $('omrReviewForm').classList.add('d-none'); });
    $('omrPrint').addEventListener('click', () => {
        if (!$('omrTemplate').value) { notice('Select a template.', true); return; }
        window.location.assign(`/api/assessment-omr/templates/${encodeURIComponent($('omrTemplate').value)}/print`);
    });
    const templateForm = $('omrTemplateForm');
    if (templateForm) templateForm.addEventListener('submit', async e => {
        e.preventDefault(); const button = templateForm.querySelector('button[type="submit"]'); button.disabled = true;
        try {
            const created = await api('assessment-omr/templates', { method: 'POST',
                headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({
                    assessmentComponentId: Number($('omrComponent').value), name: $('omrName').value,
                    answerKey: $('omrKey').value, optionsPerQuestion: Number($('omrOptions').value),
                    correctAnswerMarks: Number($('omrCorrect').value), incorrectAnswerMarks: Number($('omrPenalty').value)
                }) });
            await selectComponent(); $('omrTemplate').value = created.reference; await listSheets();
            templateForm.reset(); notice('Template sealed. Download its printable form.');
        } catch (error) { notice(error.message, true); } finally { button.disabled = false; }
    });
    $('omrCaptureForm').addEventListener('submit', async e => {
        e.preventDefault(); const button = $('omrUpload'); button.disabled = true;
        try {
            const file = $('omrImage').files?.[0];
            if (!file || file.size > 5_242_880 || !['image/png', 'image/jpeg'].includes(file.type))
                throw new Error('Choose a PNG/JPEG scan up to 5 MB.');
            if (!state.uploadKey) state.uploadKey = crypto.randomUUID();
            if (!state.captureKey) state.captureKey = crypto.randomUUID();
            const form = new FormData(); form.append('file', file); form.append('requestId', state.uploadKey);
            const uploaded = await api('assessment-omr/images', { method: 'POST', body: form });
            const captured = await api('assessment-omr/sheets', { method: 'POST',
                headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({
                    requestId: state.captureKey, templateReference: $('omrTemplate').value,
                    studentSubjectRegistrationId: Number($('omrStudent').value), fileReference: uploaded.reference
                }) });
            state.uploadKey = null; state.captureKey = null; $('omrImage').value = '';
            await listSheets(); openReview(captured); notice('Sheet captured. An independent reviewer must verify it.');
        } catch (error) { notice(error.message, true); } finally { button.disabled = false; }
    });
    $('omrImage').addEventListener('change', () => { state.uploadKey = null; state.captureKey = null; });
    $('omrReviewForm').addEventListener('submit', async e => {
        e.preventDefault(); const sheet = state.selectedSheet;
        if (!sheet || sheet.state !== 'Captured') return;
        const button = $('omrReviewForm').querySelector('button[type="submit"]'); button.disabled = true;
        try {
            await api(`assessment-omr/sheets/${encodeURIComponent(sheet.reference)}/review`, {
                method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({
                    requestId: state.reviewKey ||= crypto.randomUUID(), confirmedAnswers: $('omrConfirmed').value,
                    reason: $('omrReason').value, rowVersion: sheet.rowVersion,
                    identityConfirmed: $('omrIdentityConfirmed').checked
                })
            });
            $('omrReviewForm').classList.add('d-none'); state.selectedSheet = null;
            await listSheets(); notice('Independent review saved. Exam management can now post marks.');
        } catch (error) { notice(error.message, true); } finally { button.disabled = false; }
    });
    if ($('omrPost')) $('omrPost').addEventListener('click', async () => {
        const sheet = state.selectedSheet; if (!sheet || sheet.state !== 'Reviewed') return;
        if (!confirm(`Post ${sheet.proposedMarks} marks to the existing Assessment register? This cannot overwrite manual marks.`)) return;
        const button = $('omrPost'); button.disabled = true;
        try {
            await api(`assessment-omr/sheets/${encodeURIComponent(sheet.reference)}/post`, {
                method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({
                    requestId: state.postKey ||= crypto.randomUUID(), rowVersion: sheet.rowVersion
                })
            });
            $('omrReviewForm').classList.add('d-none'); state.selectedSheet = null; state.postKey = null;
            await listSheets(); notice('Verified OMR marks posted to the canonical Assessment register.');
        } catch (error) { notice(error.message, true); } finally { button.disabled = false; }
    });
    if ($('omrExport')) $('omrExport').addEventListener('click', async () => {
        const reference = $('omrTemplate').value; if (!reference) return notice('Select an OMR template.', true);
        const button = $('omrExport'); button.disabled = true;
        try {
            const format = $('omrExportFormat').value;
            const response = await fetch('/api/assessment-omr/exports', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                body: JSON.stringify({ templateReference: reference, format,
                    columns: ['RollNo', 'StudentCode', 'StudentName', 'ComponentMarks', 'SubjectMarks', 'PostedAt'] })
            });
            if (!response.ok) {
                const data = await response.json().catch(() => ({}));
                throw new Error(data.message || `Export failed: HTTP ${response.status}`);
            }
            const blob = await response.blob(), url = URL.createObjectURL(blob);
            const anchor = document.createElement('a'); anchor.href = url;
            anchor.download = `EduOS-OMR-internal-${reference}.${format}`;
            document.body.append(anchor); anchor.click(); anchor.remove();
            window.setTimeout(() => URL.revokeObjectURL(url), 5000);
            notice('Internal-preparation report downloaded. This is not a board submission.');
        } catch (error) { notice(error.message, true); } finally { button.disabled = false; }
    });
    scopes().catch(e => notice(e.message, true));
})();
