(() => {
    'use strict';
    const root = document.getElementById('scholarshipsApp');
    if (!root) return;
    const $ = id => document.getElementById(id);
    const bn = (document.documentElement.lang || '').toLowerCase().startsWith('bn');
    const T = (en, bd) => bn ? bd : en;
    const review = root.dataset.reviewer === 'true';
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value ||
        document.querySelector('meta[name="request-verification-token"]')?.content;
    const session = [document.body.dataset.authTenantId, document.body.dataset.authSessionReference];
    const safeSession = () => session[0] && session[1] &&
        session[0] === document.body.dataset.authTenantId &&
        session[1] === document.body.dataset.authSessionReference;
    const state = { busy: false, locked: false, page: 1, pageSize: 20, total: 0, enrollment: '', selected: null, requestId: null, evidenceUploadId: null,
        reconcilePage: 1, reconcileTotal: 0 };
    const guid = () => crypto.randomUUID();
    const val = id => $(id)?.value?.trim() || '';
    const date = value => value ? String(value).slice(0, 10) : '';
    function notice(message, danger = false) {
        const box = $('aidNotice');
        box.className = 'alert ' + (danger ? 'alert-danger' : 'alert-info');
        box.textContent = message;
    }
    function checkSession() {
        if (state.locked) return false;
        if (safeSession()) return true;
        state.locked = true; state.busy = true; state.enrollment = '';
        state.selected = null; state.requestId = null; state.evidenceUploadId = null; state.total = 0;
        for (const field of root.querySelectorAll('input,select,textarea'))
            if (field.type !== 'hidden') field.value = '';
        $('aidStudents').replaceChildren(); $('aidRows').replaceChildren();
        $('aidEvidence').value = '';
        if (review) {
            $('aidSelected').textContent = '';
            $('aidReconciliationRows').replaceChildren();
        }
        for (const control of root.querySelectorAll('button,input,select,textarea')) control.disabled = true;
        notice(T('Institution session changed. Reload this page.', 'প্রতিষ্ঠানের সেশন বদলেছে। পৃষ্ঠাটি রিলোড করুন।'), true);
        return false;
    }
    function refreshControls() {
        if (state.locked) return;
        $('aidSubmit').disabled = state.busy || !state.enrollment || !val('aidRule') ||
            !val('aidFrom') || !val('aidStatement') ||
            ($('aidEvidenceFile').files?.length > 0 && !val('aidEvidence'));
        $('aidEvidenceUpload').disabled = state.busy || !state.enrollment ||
            $('aidEvidenceFile').files?.length !== 1;
        for (const id of ['aidRefresh', 'aidPrev', 'aidNext']) $(id).disabled = state.busy ||
            (id === 'aidPrev' && state.page <= 1) ||
            (id === 'aidNext' && state.page * state.pageSize >= state.total);
        if (!review) return;
        $('aidApprove').disabled = state.busy || state.selected?.state !== 'Submitted';
        $('aidReject').disabled = $('aidApprove').disabled;
        $('aidAward').disabled = state.busy || state.selected?.state !== 'Approved';
        $('aidCancel').disabled = state.busy ||
            !['Submitted', 'Awarded'].includes(state.selected?.state);
        $('aidEvidenceDownload').disabled = state.busy || !state.selected?.hasEvidence;
        $('aidReconcile').disabled = state.busy;
        $('aidReconcilePrev').disabled = state.busy || state.reconcilePage <= 1;
        $('aidReconcileNext').disabled = state.busy || state.reconcilePage * 30 >= state.reconcileTotal;
    }
    async function api(path, method = 'GET', payload) {
        if (!checkSession()) throw Error(T('Institution session changed. Reload.', 'প্রতিষ্ঠানের সেশন বদলেছে। পৃষ্ঠা রিলোড করুন।'));
        const headers = { Accept: 'application/json' };
        if (method !== 'GET') {
            if (!token) throw Error(T('Security token missing. Reload.', 'নিরাপত্তা টোকেন নেই। রিলোড করুন।'));
            headers.RequestVerificationToken = token;
        }
        if (payload !== undefined) headers['Content-Type'] = 'application/json';
        const response = await fetch(path, {
            method, credentials: 'same-origin', cache: 'no-store', headers,
            ...(payload === undefined ? {} : { body: JSON.stringify(payload) })
        });
        const json = await response.json().catch(() => null);
        if (!checkSession()) throw Error(T('Institution session changed. Reload.', 'প্রতিষ্ঠানের সেশন বদলেছে। রিলোড করুন।'));
        if (!response.ok || json?.success !== true)
            throw Error(json?.message || T('Request failed. Reload and retry.', 'অনুরোধ ব্যর্থ। রিলোড করে চেষ্টা করুন।'));
        return json.data;
    }
    async function run(action) {
        if (state.busy) return;
        state.busy = true; refreshControls();
        try { await action(); }
        catch (error) { notice(error.message, true); }
        finally { state.busy = false; refreshControls(); }
    }
    const make = (tag, text, css = '') => {
        const node = document.createElement(tag); node.textContent = String(text ?? '');
        if (css) node.className = css;
        return node;
    };
    async function searchStudent(event) {
        event?.preventDefault();
        await run(async () => {
            const results = await api('/api/student-support-directory/enrollments?' +
                new URLSearchParams({ search: val('aidStudentSearch'), financialOnly: 'true' }));
            const box = $('aidStudents'); box.replaceChildren();
            if (!results?.length) box.append(make('div', T('No accessible active enrollments.', 'অনুমোদিত সক্রিয় ভর্তি নেই।'), 'text-muted small'));
            for (const row of results || []) {
                const button = make('button', row.studentName + ' · ' + row.studentCode +
                    ' · ' + row.rollNo, 'list-group-item list-group-item-action');
                button.type = 'button';
                button.addEventListener('click', () => {
                    state.enrollment = row.enrollmentReference;
                    $('aidEnrollment').value = row.studentName + ' / ' + row.rollNo;
                    state.requestId = null; state.evidenceUploadId = null;
                    $('aidEvidenceFile').value = ''; $('aidEvidence').value = '';
                    $('aidEvidenceStatus').textContent = T('No evidence uploaded for this enrollment.',
                        'এই ভর্তির জন্য কোনো প্রমাণ আপলোড হয়নি।');
                    box.replaceChildren(); refreshControls();
                });
                box.append(button);
            }
        });
    }
    async function rules() {
        await run(async () => {
            const rows = await api('/api/student-support-directory/aid-rules');
            const select = $('aidRule'); select.replaceChildren(new Option(T('Select approved fee policy', 'ফি নীতি নির্বাচন করুন'), ''));
            for (const rule of rows || []) select.add(new Option(rule.name + ' · ' +
                rule.value + (rule.isPercentage ? '%' : ' BDT'), String(rule.id)));
            if (!rows?.length) notice(T('No active fee-assistance policies. Contact Finance.', 'সক্রিয় ফি নীতি নেই। ফাইন্যান্সে যোগাযোগ করুন।'), true);
        });
    }
    function renderSelected() {
        if (!review) return;
        $('aidSelected').textContent = state.selected ? state.selected.reference + ' · ' +
            state.selected.state + ' · ' + state.selected.eligibilityStatement :
            T('Choose an application above.', 'উপরের তালিকা থেকে আবেদন বাছুন।');
        refreshControls();
    }
    async function list() {
        await run(async () => {
            const data = await api('/api/student-scholarships?' +
                new URLSearchParams({ page: String(state.page), pageSize: String(state.pageSize) }));
            state.total = data?.totalCount || 0;
            const tbody = $('aidRows'); tbody.replaceChildren();
            for (const row of data?.items || []) {
                const tr = document.createElement('tr');
                for (const value of [row.reference.slice(0, 8), row.state,
                    date(row.effectiveFrom) + ' – ' + (date(row.effectiveTo) || '∞')])
                    tr.append(make('td', value));
                const td = document.createElement('td');
                const button = make('button', T('View', 'দেখুন'), 'btn btn-outline-primary btn-sm');
                button.type = 'button';
                button.addEventListener('click', () => {
                    state.selected = row; renderSelected();
                    notice(row.reference + ' · ' + row.state + ' · ' + row.eligibilityStatement);
                });
                td.append(button); tr.append(td); tbody.append(tr);
            }
            if (!data?.items?.length) {
                const tr = document.createElement('tr');
                const td = make('td', T('No applications.', 'কোনো আবেদন নেই।'));
                td.colSpan = 4; tr.append(td); tbody.append(tr);
            }
            $('aidPage').textContent = `${state.page} · ${state.total} ${T('records', 'রেকর্ড')}`;
        });
    }
    async function submit(event) {
        event.preventDefault();
        if (!state.enrollment) return notice(T('Select current enrollment.', 'বর্তমান ভর্তি নির্বাচন করুন।'), true);
        if (val('aidTo') && val('aidTo') < val('aidFrom'))
            return notice(T('End date must follow start date.', 'শেষের তারিখ শুরুর আগে হবে না।'), true);
        if ($('aidEvidenceFile').files?.length && !val('aidEvidence'))
            return notice(T('Upload the selected evidence first.', 'নির্বাচিত প্রমাণ আগে আপলোড করুন।'), true);
        await run(async () => {
            const payload = {
                requestId: state.requestId || (state.requestId = guid()),
                enrollmentReference: state.enrollment, discountRuleId: Number(val('aidRule')),
                effectiveFrom: val('aidFrom'), effectiveTo: val('aidTo') || null,
                eligibilityStatement: val('aidStatement'),
                evidenceFileReference: val('aidEvidence') || null,
                renewalOfReference: val('aidRenewal') || null
            };
            const result = await api('/api/student-scholarships', 'POST', payload);
            state.requestId = null; $('aidStatement').value = ''; $('aidEvidence').value = '';
            $('aidRenewal').value = ''; notice(T('Submitted for review: ', 'পর্যালোচনার জন্য জমা: ') + result.reference);
            state.page = 1; await listWithoutBusy();
        });
    }
    async function uploadEvidence() {
        if (!state.enrollment) return notice(T('Choose an enrollment first.', 'আগে ভর্তি নির্বাচন করুন।'), true);
        const file = $('aidEvidenceFile').files?.[0];
        if (!file || file.size < 1 || file.size > 5242880)
            return notice(T('Choose a supported private file up to 5 MB.',
                'সর্বোচ্চ ৫ MB-এর সমর্থিত ব্যক্তিগত ফাইল নির্বাচন করুন।'), true);
        await run(async () => {
            if (!checkSession() || !token) throw Error(T('Session or security token unavailable.', 'সেশন বা নিরাপত্তা টোকেন নেই।'));
            const form = new FormData();
            form.append('enrollmentReference', state.enrollment);
            form.append('requestId', state.evidenceUploadId || (state.evidenceUploadId = guid()));
            form.append('file', file, file.name);
            const response = await fetch('/api/student-scholarships/evidence-upload', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { RequestVerificationToken: token, Accept: 'application/json' }, body: form
            });
            const result = await response.json().catch(() => null);
            if (!checkSession()) throw Error(T('Session changed; reload.', 'সেশন বদলেছে; রিলোড করুন।'));
            if (!response.ok || result?.success !== true)
                throw Error(result?.message || T('Secure upload failed.', 'নিরাপদ আপলোড ব্যর্থ হয়েছে।'));
            $('aidEvidence').value = result.data.reference;
            $('aidEvidenceFile').value = ''; state.evidenceUploadId = null;
            $('aidEvidenceStatus').textContent = T('Verified private evidence ready: ',
                'যাচাইকৃত ব্যক্তিগত প্রমাণ প্রস্তুত: ') + file.name;
            notice(T('Private evidence uploaded and scanned.', 'ব্যক্তিগত প্রমাণ আপলোড ও পরীক্ষা সম্পন্ন।'));
        });
    }
    async function listWithoutBusy() {
        const data = await api('/api/student-scholarships?' +
            new URLSearchParams({ page: String(state.page), pageSize: String(state.pageSize) }));
        state.total = data?.totalCount || 0;
        const tbody = $('aidRows'); tbody.replaceChildren();
        for (const row of data?.items || []) {
            const tr = document.createElement('tr');
            for (const value of [row.reference.slice(0, 8), row.state,
                date(row.effectiveFrom) + ' – ' + (date(row.effectiveTo) || '∞')]) tr.append(make('td', value));
            const td = document.createElement('td');
            const btn = make('button', T('View', 'দেখুন'), 'btn btn-sm btn-outline-primary');
            btn.type = 'button'; btn.addEventListener('click', () => {
                state.selected = row; renderSelected(); notice(row.reference + ' · ' + row.state + ' · ' + row.eligibilityStatement);
            }); td.append(btn); tr.append(td); tbody.append(tr);
        }
        if (!data?.items?.length) {
            const tr = document.createElement('tr'); const td = make('td', T('No applications.', 'কোনো আবেদন নেই।'));
            td.colSpan = 4; tr.append(td); tbody.append(tr);
        }
        $('aidPage').textContent = `${state.page} · ${state.total} ${T('records', 'রেকর্ড')}`;
    }
    async function act(kind) {
        if (!state.selected) return;
        const target = state.selected;
        const reason = val('aidReason');
        if (kind !== 'award' && !reason) return notice(T('Document a reason first.', 'আগে কারণ লিখুন।'), true);
        await run(async () => {
            const path = '/api/student-scholarships/' + encodeURIComponent(target.reference);
            const data = kind === 'award' ? await api(path + '/award', 'POST') :
                await api(path + (kind === 'cancel' ? '/cancel' : '/decision'), 'POST',
                    kind === 'cancel' ? { requestId: guid(), rowVersion: target.rowVersion, reason } :
                        { requestId: guid(), rowVersion: target.rowVersion, reason, approve: kind === 'approve' });
            state.selected = data; $('aidReason').value = ''; renderSelected();
            notice(T('Saved state: ', 'সংরক্ষিত অবস্থা: ') + data.state);
            await listWithoutBusy();
        });
    }
    async function evidence() {
        if (!state.selected?.hasEvidence) return;
        await run(async () => {
            const reference = state.selected.reference;
            if (!checkSession() || !token) throw Error(T('Session or security token unavailable.', 'সেশন বা নিরাপত্তা টোকেন নেই।'));
            const response = await fetch('/api/student-scholarships/' +
                encodeURIComponent(reference) + '/evidence-download', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { RequestVerificationToken: token, Accept: 'application/octet-stream,application/json' }
            });
            if (!checkSession()) throw Error(T('Session changed. Reload.', 'সেশন বদলেছে। রিলোড করুন।'));
            if (!response.ok) {
                const error = await response.json().catch(() => null);
                throw Error(error?.message || T('Private evidence unavailable.', 'ব্যক্তিগত প্রমাণ পাওয়া যায়নি।'));
            }
            const type = (response.headers.get('content-type') || '').split(';')[0].toLowerCase();
            const extension = ({ 'application/pdf': 'pdf', 'image/png': 'png',
                'image/jpeg': 'jpg', 'image/webp': 'webp' })[type] || 'bin';
            const blob = await response.blob();
            if (!checkSession()) throw Error(T('Session changed. Reload.', 'সেশন বদলেছে। রিলোড করুন।'));
            const url = URL.createObjectURL(blob);
            try {
                const link = document.createElement('a');
                link.href = url; link.download = 'scholarship-evidence-' + reference.slice(0, 8) + '.' + extension;
                link.style.display = 'none'; document.body.append(link); link.click(); link.remove();
            } finally { setTimeout(() => URL.revokeObjectURL(url), 2000); }
            notice(T('Private evidence access recorded.', 'ব্যক্তিগত প্রমাণ দেখার তথ্য অডিটে সংরক্ষিত।'));
        });
    }
    async function reconcile() {
        await run(async () => {
            const data = await api('/api/student-scholarships/reconciliation?' + new URLSearchParams({
                page: String(state.reconcilePage), pageSize: '30'
            }));
            state.reconcileTotal = data?.totalCount || 0;
            const list = $('aidReconciliationRows'); list.replaceChildren();
            for (const row of data?.items || []) {
                const entry = make('div', row.applicationReference.slice(0, 8) + ' · ' +
                    row.applicationState + ' · ' + row.lineageStatus,
                    'list-group-item text-break');
                list.append(entry);
            }
            if (!data?.items?.length) list.append(make('div',
                T('No financial awards.', 'আর্থিক বরাদ্দ নেই।'), 'list-group-item text-muted'));
            $('aidReconcilePage').textContent = state.reconcilePage + ' · ' + state.reconcileTotal;
        });
    }
    $('aidStudentSearchForm').addEventListener('submit', searchStudent);
    $('aidSubmitForm').addEventListener('submit', submit);
    $('aidEvidenceUpload').addEventListener('click', uploadEvidence);
    $('aidEvidenceFile').addEventListener('change', () => {
        $('aidEvidence').value = ''; state.evidenceUploadId = null;
        $('aidEvidenceStatus').textContent = T('Selected evidence is not uploaded yet.',
            'নির্বাচিত প্রমাণ এখনো আপলোড হয়নি।');
        refreshControls();
    });
    for (const id of ['aidRule', 'aidFrom', 'aidStatement']) $(id).addEventListener('input', refreshControls);
    $('aidRefresh').addEventListener('click', () => list());
    $('aidPrev').addEventListener('click', () => { if (state.page > 1) { state.page--; list(); } });
    $('aidNext').addEventListener('click', () => { if (state.page * state.pageSize < state.total) { state.page++; list(); } });
    if (review) {
        for (const [id, kind] of [['aidApprove', 'approve'], ['aidReject', 'reject'],
            ['aidAward', 'award'], ['aidCancel', 'cancel']]) $(id).addEventListener('click', () => act(kind));
        $('aidEvidenceDownload').addEventListener('click', evidence);
        $('aidReconcile').addEventListener('click', () => { state.reconcilePage = 1; reconcile(); });
        $('aidReconcilePrev').addEventListener('click', () => { if (state.reconcilePage > 1) { state.reconcilePage--; reconcile(); } });
        $('aidReconcileNext').addEventListener('click', () => { if (state.reconcilePage * 30 < state.reconcileTotal) { state.reconcilePage++; reconcile(); } });
    }
    const today = new Date();
    $('aidFrom').value = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
    window.addEventListener('focus', checkSession);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) checkSession(); });
    (async () => { await rules(); await searchStudent(); await list(); })();
})();
