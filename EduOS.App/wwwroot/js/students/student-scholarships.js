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
    const state = { busy: false, locked: false, page: 1, pageSize: 20, total: 0, enrollment: '', selected: null, requestId: null };
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
        state.selected = null; state.requestId = null; state.total = 0;
        for (const field of root.querySelectorAll('input,select,textarea'))
            if (field.type !== 'hidden') field.value = '';
        $('aidStudents').replaceChildren(); $('aidRows').replaceChildren();
        if (review) $('aidSelected').textContent = '';
        for (const control of root.querySelectorAll('button,input,select,textarea')) control.disabled = true;
        notice(T('Institution session changed. Reload this page.', 'প্রতিষ্ঠানের সেশন বদলেছে। পৃষ্ঠাটি রিলোড করুন।'), true);
        return false;
    }
    function refreshControls() {
        if (state.locked) return;
        $('aidSubmit').disabled = state.busy || !state.enrollment || !val('aidRule') ||
            !val('aidFrom') || !val('aidStatement');
        for (const id of ['aidRefresh', 'aidPrev', 'aidNext']) $(id).disabled = state.busy ||
            (id === 'aidPrev' && state.page <= 1) ||
            (id === 'aidNext' && state.page * state.pageSize >= state.total);
        if (!review) return;
        $('aidApprove').disabled = state.busy || state.selected?.state !== 'Submitted';
        $('aidReject').disabled = $('aidApprove').disabled;
        $('aidAward').disabled = state.busy || state.selected?.state !== 'Approved';
        $('aidCancel').disabled = state.busy ||
            !['Submitted', 'Awarded'].includes(state.selected?.state);
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
                    state.requestId = null; box.replaceChildren(); refreshControls();
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
    $('aidStudentSearchForm').addEventListener('submit', searchStudent);
    $('aidSubmitForm').addEventListener('submit', submit);
    for (const id of ['aidRule', 'aidFrom', 'aidStatement']) $(id).addEventListener('input', refreshControls);
    $('aidRefresh').addEventListener('click', () => list());
    $('aidPrev').addEventListener('click', () => { if (state.page > 1) { state.page--; list(); } });
    $('aidNext').addEventListener('click', () => { if (state.page * state.pageSize < state.total) { state.page++; list(); } });
    if (review) {
        for (const [id, kind] of [['aidApprove', 'approve'], ['aidReject', 'reject'],
            ['aidAward', 'award'], ['aidCancel', 'cancel']]) $(id).addEventListener('click', () => act(kind));
    }
    const today = new Date();
    $('aidFrom').value = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
    window.addEventListener('focus', checkSession);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) checkSession(); });
    (async () => { await rules(); await searchStudent(); await list(); })();
})();
