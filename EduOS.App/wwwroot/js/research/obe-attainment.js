(() => {
    'use strict';
    const root = document.getElementById('obeWorkspace');
    if (!root) return;
    const get = id => document.getElementById(id);
    const bn = () => document.documentElement.lang.toLowerCase().startsWith('bn');
    const t = (en, bd) => bn() ? bd : en;
    root.querySelectorAll('[data-o-en]').forEach(el => { el.textContent = bn() ? el.dataset.oBn : el.dataset.oEn; });
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const tenant = document.body.dataset.authTenantId;
    const session = document.body.dataset.authSessionReference;
    const valid = () => Boolean(tenant && session && tenant === document.body.dataset.authTenantId &&
        session === document.body.dataset.authSessionReference);
    let busy = false, generation = 0, courses = [], plos = [], selected = null;
    let componentCatalog = null, cloRowIndex = 0, publicationOptions = [];
    let policyRetry = null, submitRetry = null, reviewRetry = null;
    const create = (tag, className, text) => {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text != null) node.textContent = String(text);
        return node;
    };
    const option = (select, value, text) => {
        const node = document.createElement('option'); node.value = String(value); node.textContent = text;
        select.appendChild(node);
    };
    const resetOptions = (select, placeholder) => { select.replaceChildren(); option(select, '', placeholder); };
    const notify = (message, error = false) => {
        const box = get('obeNotice'); box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        box.textContent = message;
    };
    const scope = () => ({ campusId: get('obeCampus').value, programId: get('obeProgram').value });
    const query = values => new URLSearchParams(values).toString();
    function invalidate() {
        generation++; selected = null; submitRetry = null; reviewRetry = null;
        get('obePreviewDetails').replaceChildren(); get('obeReportRows').replaceChildren();
        get('obeRollupDetails').replaceChildren();
        get('obeReportPanel').classList.add('d-none');
    }
    function controls() {
        const scoped = !!scope().campusId && !!scope().programId;
        get('obeCampus').disabled = busy;
        get('obeProgram').disabled = busy || !scope().campusId;
        get('obePublication').disabled = busy;
        get('obePolicy').disabled = busy;
        get('obeYear').disabled = busy;
        get('obeTerm').disabled = busy || !get('obeYear').value;
        get('obeRollup').disabled = busy || !get('obePolicy').value || !get('obeYear').value;
        for (const id of ['obeAddClo', 'obeAddLink', 'obeSavePolicy']) get(id).disabled = busy || !scoped;
        get('obePreview').disabled = busy || !get('obePolicy').value || !get('obePublication').value;
        get('obeSubmit').disabled = get('obePreview').disabled;
        get('obeReview').disabled = busy || !selected || selected.stateCode !== 'Submitted';
    }
    async function api(url, body) {
        if (!valid()) { invalidate(); throw Error(t('Session changed.', 'সেশন পরিবর্তিত।')); }
        if (typeof window.EduOSVerifySessionContext === 'function') await window.EduOSVerifySessionContext();
        const res = await fetch(url, {
            method: body === undefined ? 'GET' : 'POST', credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json', ...(body === undefined ? {} :
                { 'Content-Type': 'application/json', RequestVerificationToken: token }) },
            ...(body === undefined ? {} : { body: JSON.stringify(body) })
        });
        if (!valid() || res.headers.get('X-EduOS-Tenant-Id') !== tenant ||
            res.headers.get('X-EduOS-Session-Reference') !== session) {
            invalidate(); throw Error(t('Session validation failed.', 'সেশন যাচাই ব্যর্থ।'));
        }
        const data = await res.json().catch(() => null);
        if (!res.ok || data?.success !== true) {
            const error = Error(res.status === 409 ? t('Version or record changed; refresh.', 'সংস্করণ বা রেকর্ড বদলেছে; রিফ্রেশ করুন।') :
                res.status === 403 || res.status === 404 ? t('Access unavailable.', 'অধিকার নেই।') :
                data?.message || t('Request failed.', 'অনুরোধ ব্যর্থ।'));
            error.status = res.status; throw error;
        }
        return data.data;
    }
    function rowText(parent, cells) {
        const tr = create('tr');
        cells.forEach(value => tr.appendChild(create('td', '', value)));
        parent.appendChild(tr); return tr;
    }
    function renderAttainment(target, preview) {
        target.replaceChildren();
        if (!preview) return;
        target.appendChild(create('p', 'small mb-2', t('Learners: ', 'শিক্ষার্থী: ') + preview.learnerCount +
            ' · ' + t('Publication version: ', 'প্রকাশ সংস্করণ: ') + preview.publicationVersionNo));
        const table = create('table', 'table table-sm table-striped');
        const header = create('thead'); const hr = create('tr');
        [t('Type', 'ধরন'), t('Outcome', 'শিক্ষণফল'), t('Attainment %', 'অর্জন %')]
            .forEach(x => hr.appendChild(create('th', '', x)));
        header.appendChild(hr); table.appendChild(header);
        const body = create('tbody');
        preview.courseOutcomes.forEach(c => rowText(body, ['CLO',
            c.courseOutcomeCode + (c.componentCode ? ' · ' + c.componentCode : ' · ' + t('course grade', 'কোর্স গ্রেড')),
            Number(c.attainmentPercent).toFixed(2)]));
        preview.outcomes.forEach(p => rowText(body, ['PLO', p.programOutcomeCode, Number(p.attainmentPercent).toFixed(2)]));
        table.appendChild(body); target.appendChild(create('div', 'table-responsive')).appendChild(table);
        target.appendChild(create('p', 'small text-muted', t('Evidence digest: ', 'প্রমাণের হ্যাশ: ') + preview.evidenceSha256));
    }
    function renderProgrammeRollup(data) {
        const target = get('obeRollupDetails'); target.replaceChildren();
        if (!data.approvedPublicationCount) {
            target.appendChild(create('p', 'text-muted small',
                t('No currently published, independently approved OBE reports in this academic period.',
                    'এই শিক্ষাবর্ষে বর্তমানে প্রকাশিত স্বতন্ত্র অনুমোদিত কোনো OBE রিপোর্ট নেই।')));
            return;
        }
        target.appendChild(create('p', 'small', data.approvedPublicationCount + ' ' +
            t('approved publications', 'অনুমোদিত প্রকাশনা') + ' · ' +
            data.learnerPublicationObservations + ' ' +
            t('learner-publication observations', 'ফলাফলভিত্তিক শিক্ষার্থী পর্যবেক্ষণ')));
        const table = create('table', 'table table-sm table-striped');
        const thead = create('thead'), tr = create('tr');
        [t('Programme outcome', 'প্রোগ্রাম শিক্ষণফল'), t('Weighted attainment %', 'ওজনযুক্ত অর্জন %')]
            .forEach(x => tr.appendChild(create('th', '', x)));
        thead.appendChild(tr); table.appendChild(thead);
        const tbody = create('tbody');
        data.outcomes.forEach(x => rowText(tbody, [x.programOutcomeCode,
            Number(x.attainmentPercent).toFixed(2)]));
        table.appendChild(tbody); target.appendChild(create('div', 'table-responsive')).appendChild(table);
        target.appendChild(create('p', 'small text-muted',
            t('Source report references: ', 'উৎস রিপোর্ট রেফারেন্স: ') +
            data.sourceReportReferences.join(', ')));
    }
    function refreshTerms() {
        const selectedYear = Number(get('obeYear').value), term = get('obeTerm');
        resetOptions(term, t('All terms in the year', 'বছরের সব টার্ম'));
        const seen = new Set();
        publicationOptions.filter(x => x.academicYearId === selectedYear && x.academicTermId)
            .forEach(x => {
                if (seen.has(x.academicTermId)) return;
                seen.add(x.academicTermId);
                option(term, x.academicTermId, x.academicTermName || String(x.academicTermId));
            });
        get('obeRollupDetails').replaceChildren(); controls();
    }
    function refreshCloOptions() {
        const codes = [...get('obeCloRows').querySelectorAll('[data-role="code"]')]
            .map(input => input.value.trim().toUpperCase()).filter(Boolean);
        get('obeLinkRows').querySelectorAll('[data-role="clo"]') .forEach(select => {
            const old = select.value; resetOptions(select, t('CLO', 'CLO'));
            [...new Set(codes)].forEach(code => option(select, code, code));
            select.value = codes.includes(old) ? old : '';
        });
    }
    function refreshComponentOptions() {
        const currentPublication = get('obePublication').value;
        const message = !currentPublication
            ? t('Choose a published result to discover its frozen rubric codes.',
                'সংরক্ষিত রুব্রিক কোড দেখতে প্রকাশিত ফল নির্বাচন করুন।')
            : !componentCatalog
                ? t('Checking frozen component evidence…', 'সংরক্ষিত কম্পোনেন্ট তথ্য যাচাই হচ্ছে…')
                : !componentCatalog.hasFrozenRegister
                    ? t('This result predates component capture or has no frozen register. A component CLO cannot use it.',
                        'এই ফলাফলে অপরিবর্তনীয় কম্পোনেন্ট রেজিস্টার নেই। কম্পোনেন্ট CLO ব্যবহার করা যাবে না।')
                    : t('Only fully evidenced codes below can produce attainment for this published result.',
                        'শুধুমাত্র সম্পূর্ণ তথ্যসহ কোড দিয়ে এই ফলাফলের অর্জন হিসাব করা যাবে।');
        get('obeEvidenceNotice').textContent = message;
        get('obeCloRows').querySelectorAll('[data-role="component-list"]').forEach(list => {
            const row = list.parentElement, courseId = Number(row.querySelector('[data-role="course"]').value);
            list.replaceChildren();
            const options = componentCatalog?.components?.filter(x =>
                x.curriculumSubjectId === courseId && x.completeForCourse) || [];
            options.forEach(x => option(list, x.componentCode, x.componentCode + ' · ' +
                x.learnerCount + ' ' + t('learners', 'শিক্ষার্থী')));
            row.querySelector('[data-role="component-hint"]').textContent = options.length
                ? t('Frozen component suggestions: ', 'সংরক্ষিত কম্পোনেন্ট: ') +
                    options.map(x => x.componentCode).join(', ')
                : t('Blank uses the course-grade proxy. An unlisted code fails closed during preview.',
                    'খালি রাখলে কোর্স গ্রেড নেওয়া হবে। তালিকার বাইরের কোড প্রিভিউতে বাতিল হবে।');
        });
    }
    function addClo() {
        const box = create('div', 'border rounded p-2');
        const course = create('select', 'form-select mb-2'); course.dataset.role = 'course';
        resetOptions(course, t('Curriculum course', 'পাঠ্যক্রমের কোর্স'));
        courses.forEach(c => option(course, c.curriculumSubjectId, c.subjectCode + ' · ' + c.subjectName));
        const code = create('input', 'form-control mb-2'); code.dataset.role = 'code';
        code.required = true; code.maxLength = 40; code.placeholder = t('CLO code, e.g. CLO1', 'CLO কোড, যেমন CLO1');
        const component = create('input', 'form-control mb-2'); component.dataset.role = 'component';
        component.maxLength = 50;
        component.placeholder = t('Published assessment component code (optional; blank = course-grade proxy)',
            'প্রকাশিত মূল্যায়ন কম্পোনেন্ট কোড (ঐচ্ছিক; খালি = কোর্স গ্রেড)');
        const choices = create('datalist'); choices.id = 'obeComponentChoices' + ++cloRowIndex;
        choices.dataset.role = 'component-list'; component.setAttribute('list', choices.id);
        const help = create('p', 'small text-muted mb-2'); help.dataset.role = 'component-hint';
        const statement = create('textarea', 'form-control mb-2'); statement.dataset.role = 'statement';
        statement.required = true; statement.minLength = 10; statement.maxLength = 1000; statement.rows = 2;
        statement.placeholder = t('Measurable course learning outcome', 'পরিমাপযোগ্য কোর্স শিক্ষণফল');
        const remove = create('button', 'btn btn-sm btn-outline-danger', t('Remove', 'সরান')); remove.type = 'button';
        remove.addEventListener('click', () => { box.remove(); refreshCloOptions(); policyRetry = null; });
        code.addEventListener('input', refreshCloOptions);
        course.addEventListener('change', refreshComponentOptions);
        box.append(course, code, component, choices, help, statement, remove); get('obeCloRows').appendChild(box);
        refreshComponentOptions();
        policyRetry = null;
    }
    function addLink() {
        const box = create('div', 'border rounded p-2');
        const clo = create('select', 'form-select mb-2'); clo.dataset.role = 'clo';
        const plo = create('select', 'form-select mb-2'); plo.dataset.role = 'plo';
        resetOptions(plo, t('Program learning outcome (PLO)', 'প্রোগ্রাম শিক্ষণফল (PLO)'));
        plos.forEach(p => option(plo, p.id, p.code + ' v' + p.versionNo + ' · ' + p.statement));
        const weight = create('input', 'form-control mb-2'); weight.dataset.role = 'weight';
        weight.type = 'number'; weight.min = '0.0001'; weight.max = '100'; weight.step = '0.0001';
        weight.placeholder = t('Weight percent', 'ওজনের শতাংশ'); weight.required = true;
        const remove = create('button', 'btn btn-sm btn-outline-danger', t('Remove', 'সরান')); remove.type = 'button';
        remove.addEventListener('click', () => { box.remove(); policyRetry = null; });
        box.append(clo, plo, weight, remove); get('obeLinkRows').appendChild(box);
        refreshCloOptions(); policyRetry = null;
    }
    async function loadCampuses() {
        const campuses = await api('/api/accreditation/campuses');
        resetOptions(get('obeCampus'), t('Choose campus', 'ক্যাম্পাস নির্বাচন'));
        campuses.forEach(x => option(get('obeCampus'), x.id, x.name));
        resetOptions(get('obeProgram'), t('Choose program', 'প্রোগ্রাম নির্বাচন'));
        controls();
    }
    async function loadPrograms() {
        const campus = get('obeCampus').value, request = ++generation;
        resetOptions(get('obeProgram'), t('Choose program', 'প্রোগ্রাম নির্বাচন'));
        invalidate(); if (!campus) { controls(); return; }
        const rows = await api('/api/accreditation/programs?' + query({ campusId: campus }));
        if (request + 1 !== generation || !valid()) return;
        rows.forEach(x => option(get('obeProgram'), x.id, x.name)); controls();
    }
    async function loadScope() {
        invalidate(); policyRetry = null;
        componentCatalog = null; publicationOptions = [];
        courses = []; plos = []; get('obeCloRows').replaceChildren(); get('obeLinkRows').replaceChildren();
        resetOptions(get('obePolicy'), t('Choose policy', 'নীতি নির্বাচন'));
        resetOptions(get('obePublication'), t('Choose published result', 'প্রকাশিত ফল নির্বাচন'));
        resetOptions(get('obeYear'), t('Choose academic year', 'শিক্ষাবর্ষ নির্বাচন'));
        resetOptions(get('obeTerm'), t('All terms in the year', 'বছরের সব টার্ম'));
        const { campusId, programId } = scope(), current = generation;
        if (!campusId || !programId) { controls(); return; }
        const base = '/api/accreditation/obe';
        const params = '?' + query({ campusId, programId });
        const [c, p, policies, publications] = await Promise.all([
            api(base + '/courses' + params),
            api('/api/accreditation/program-outcomes?' + query({ campusId, programId })),
            api(base + '/policies' + params), api(base + '/publications' + params)
        ]);
        if (current !== generation || !valid()) return;
        courses = c; plos = p; publicationOptions = publications;
        policies.forEach(x => option(get('obePolicy'), x.id, 'v' + x.versionNo));
        publications.forEach(x => option(get('obePublication'), x.resultPublicationId,
            x.assessmentName + ' · ' + x.batchName + ' (v' + x.versionNo + ')'));
        const years = new Set();
        publications.forEach(x => {
            if (years.has(x.academicYearId)) return;
            years.add(x.academicYearId);
            option(get('obeYear'), x.academicYearId, x.academicYearName || String(x.academicYearId));
        });
        addClo(); addLink(); await loadReports(current); controls();
    }
    async function loadPolicies() {
        const { campusId, programId } = scope(), current = generation;
        const policies = await api('/api/accreditation/obe/policies?' + query({ campusId, programId }));
        if (current !== generation) return;
        resetOptions(get('obePolicy'), t('Choose policy', 'নীতি নির্বাচন'));
        policies.forEach(x => option(get('obePolicy'), x.id, 'v' + x.versionNo));
    }
    async function loadReports(requestGeneration = generation) {
        const { campusId, programId } = scope();
        const data = await api('/api/accreditation/obe/reports?' + query({ campusId, programId }));
        if (requestGeneration !== generation || !valid()) return;
        const rows = get('obeReportRows'); rows.replaceChildren();
        data.forEach(report => {
            const tr = rowText(rows, [report.resultPublicationId + ' · v' + report.publicationVersionNo, report.stateCode]);
            const td = create('td'); const button = create('button', 'btn btn-sm btn-outline-primary', t('Open', 'খুলুন'));
            button.type = 'button'; button.addEventListener('click', () => openReport(report.reference));
            td.appendChild(button); tr.appendChild(td);
        });
        if (!data.length) rowText(rows, [t('No reports', 'কোনো রিপোর্ট নেই'), '', '']);
    }
    function showReport(report) {
        selected = report; reviewRetry = null;
        get('obeReportPanel').classList.remove('d-none');
        get('obeReportSummary').textContent = 'v' + report.publicationVersionNo + ' · ' + report.stateCode +
            ' · ' + String(report.submittedAtUtc).slice(0, 16) + ' UTC';
        renderAttainment(get('obeReportDetails'), report.snapshot);
        get('obeReviewForm').classList.toggle('d-none', report.stateCode !== 'Submitted');
        get('obeCsv').classList.toggle('d-none', report.stateCode !== 'Approved'); controls();
    }
    async function openReport(reference) {
        try { showReport(await api('/api/accreditation/obe/reports/' + encodeURIComponent(reference))); }
        catch (e) { notify(e.message, true); }
    }
    async function run(action) {
        if (busy) return;
        busy = true; controls();
        try { await action(); }
        catch (e) { notify(e.message, true); }
        finally { busy = false; controls(); }
    }
    get('obeCampus').addEventListener('change', () => run(loadPrograms));
    get('obeProgram').addEventListener('change', () => run(loadScope));
    get('obeAddClo').addEventListener('click', addClo);
    get('obeAddLink').addEventListener('click', addLink);
    get('obePolicyForm').addEventListener('input', () => { policyRetry = null; });
    get('obePolicyForm').addEventListener('change', () => { policyRetry = null; });
    get('obePolicyForm').addEventListener('submit', event => {
        event.preventDefault(); run(async () => {
            const { campusId, programId } = scope();
            const courseOutcomes = [...get('obeCloRows').children].map(row => ({
                curriculumSubjectId: Number(row.querySelector('[data-role="course"]').value),
                code: row.querySelector('[data-role="code"]').value.trim().toUpperCase(),
                componentCode: row.querySelector('[data-role="component"]').value.trim().toUpperCase() || null,
                statement: row.querySelector('[data-role="statement"]').value.trim()
            }));
            const contributions = [...get('obeLinkRows').children].map(row => ({
                courseOutcomeCode: row.querySelector('[data-role="clo"]').value,
                programOutcomeId: Number(row.querySelector('[data-role="plo"]').value),
                weightPercent: Number(row.querySelector('[data-role="weight"]').value)
            }));
            const saved = await api('/api/accreditation/obe/policies', {
                clientRequestId: policyRetry ||= crypto.randomUUID(), campusId: Number(campusId),
                academicProgramId: Number(programId), versionNo: Number(get('obeVersion').value),
                courseOutcomes, contributions
            });
            policyRetry = null; await loadPolicies(); get('obePolicy').value = String(saved.policy.id);
            notify(t('Immutable policy saved.', 'অপরিবর্তনীয় নীতি সংরক্ষিত।'));
        });
    });
    get('obePolicy').addEventListener('change', () => {
        submitRetry = null; get('obePreviewDetails').replaceChildren();
        get('obeRollupDetails').replaceChildren(); controls();
    });
    get('obeYear').addEventListener('change', refreshTerms);
    get('obeTerm').addEventListener('change', () => get('obeRollupDetails').replaceChildren());
    get('obeRollup').addEventListener('click', () => run(async () => {
        const data = await api('/api/accreditation/obe/policies/' +
            encodeURIComponent(get('obePolicy').value) + '/programme-rollup?' + query({
                academicYearId: get('obeYear').value,
                ...(get('obeTerm').value ? { academicTermId: get('obeTerm').value } : {})
            }));
        renderProgrammeRollup(data);
    }));
    get('obePublication').addEventListener('change', () => {
        submitRetry = null; componentCatalog = null;
        get('obePreviewDetails').replaceChildren(); refreshComponentOptions();
        const publicationId = get('obePublication').value, request = generation;
        if (!publicationId) return;
        run(async () => {
            const { campusId, programId } = scope();
            const catalog = await api('/api/accreditation/obe/publications/' +
                encodeURIComponent(publicationId) + '/component-options?' + query({ campusId, programId }));
            if (request !== generation || get('obePublication').value !== publicationId || !valid()) return;
            componentCatalog = catalog; refreshComponentOptions();
        });
    });
    get('obePreview').addEventListener('click', () => run(async () => {
        const policy = get('obePolicy').value, publication = get('obePublication').value;
        const preview = await api('/api/accreditation/obe/policies/' + encodeURIComponent(policy) +
            '/preview?' + query({ publicationId: publication }));
        renderAttainment(get('obePreviewDetails'), preview);
        notify(t('Read-only attainment computed from published snapshots.', 'প্রকাশিত ফলাফলের অপরিবর্তনীয় তথ্য থেকে হিসাব হয়েছে।'));
    }));
    get('obeSubmit').addEventListener('click', () => run(async () => {
        const policy = get('obePolicy').value;
        const report = await api('/api/accreditation/obe/policies/' + encodeURIComponent(policy) + '/reports', {
            clientRequestId: submitRetry ||= crypto.randomUUID(),
            resultPublicationId: Number(get('obePublication').value)
        });
        submitRetry = null; showReport(report); await loadReports();
        notify(t('Frozen report submitted for independent review.', 'অপরিবর্তনীয় রিপোর্ট স্বতন্ত্র পর্যালোচনায় জমা হয়েছে।'));
    }));
    get('obeReviewForm').addEventListener('input', () => { reviewRetry = null; });
    get('obeReviewForm').addEventListener('submit', event => {
        event.preventDefault(); if (!selected) return;
        run(async () => {
            const report = await api('/api/accreditation/obe/reports/' + encodeURIComponent(selected.reference) + '/review', {
                clientRequestId: reviewRetry ||= crypto.randomUUID(),
                decision: get('obeDecision').value, reason: get('obeReason').value.trim()
            });
            showReport(report); await loadReports();
            notify(t('Independent decision recorded.', 'স্বতন্ত্র সিদ্ধান্ত সংরক্ষিত।'));
        });
    });
    get('obeCsv').addEventListener('click', () => run(async () => {
        if (!selected || selected.stateCode !== 'Approved' || !valid()) return;
        if (typeof window.EduOSVerifySessionContext === 'function') await window.EduOSVerifySessionContext();
        const res = await fetch('/api/accreditation/obe/reports/' + encodeURIComponent(selected.reference) +
            '/approved.csv', { credentials: 'same-origin', cache: 'no-store' });
        if (!valid() || res.headers.get('X-EduOS-Tenant-Id') !== tenant ||
            res.headers.get('X-EduOS-Session-Reference') !== session || !res.ok)
            throw Error(t('CSV access failed.', 'CSV ডাউনলোড ব্যর্থ।'));
        const blob = await res.blob(), url = URL.createObjectURL(blob), a = document.createElement('a');
        a.href = url; a.download = 'obe-attainment-' + selected.reference + '.csv'; a.click();
        URL.revokeObjectURL(url);
    }));
    get('obePrint').addEventListener('click', () => { if (selected) window.print(); });
    document.addEventListener('visibilitychange', () => { if (!valid()) invalidate(); });
    window.addEventListener('pageshow', () => { if (!valid()) invalidate(); });
    run(loadCampuses);
})();
