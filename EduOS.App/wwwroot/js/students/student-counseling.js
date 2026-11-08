(() => {
    'use strict';
    const root = document.getElementById('counselingApp');
    if (!root) return;
    const $ = id => document.getElementById(id);
    const bn = (document.documentElement.lang || '').toLowerCase().startsWith('bn');
    const T = (en, bd) => bn ? bd : en;
    const officer = root.dataset.officer === 'true';
    const staff = officer || root.dataset.counselor === 'true';
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value ||
        document.querySelector('meta[name="request-verification-token"]')?.content;
    const session = [document.body.dataset.authTenantId, document.body.dataset.authSessionReference];
    const safeSession = () => session[0] && session[1] &&
        session[0] === document.body.dataset.authTenantId &&
        session[1] === document.body.dataset.authSessionReference;
    const state = { student: '', selected: null, busy: false, locked: false, page: 1, total: 0, requestId: null };
    const val = id => $(id)?.value?.trim() || '';
    const guid = () => crypto.randomUUID();
    function notice(text, danger = false) {
        const box = $('caseNotice'); box.textContent = text;
        box.className = 'alert ' + (danger ? 'alert-danger' : 'alert-info');
    }
    function checkSession() {
        if (state.locked) return false;
        if (safeSession()) return true;
        state.locked = true; state.busy = true;
        state.student = ''; state.selected = null; state.requestId = null;
        for (const field of root.querySelectorAll('input,select,textarea'))
            if (field.type !== 'hidden') field.value = '';
        $('caseStudents').replaceChildren(); $('myCases').replaceChildren();
        if (staff) {
            $('caseRows').replaceChildren(); $('caseSelected').textContent = '';
            $('casePrivateSummary').textContent = ''; $('casePrivateEvents').replaceChildren();
            $('casePrivatePanel').classList.add('d-none');
        }
        for (const control of root.querySelectorAll('button,input,select,textarea')) control.disabled = true;
        notice(T('Institution session changed. Reload this page.', 'প্রতিষ্ঠানের সেশন বদলেছে। পৃষ্ঠাটি রিলোড করুন।'), true);
        return false;
    }
    function controls() {
        if (state.locked) return;
        $('caseSubmit').disabled = state.busy || !state.student || val('caseSummary').length < 10;
        $('caseMineRefresh').disabled = state.busy;
        if (!staff) return;
        $('caseRefresh').disabled = state.busy;
        $('casePrev').disabled = state.busy || state.page <= 1;
        $('caseNext').disabled = state.busy || state.page * 20 >= state.total;
        const canOpen = !!state.selected && (root.dataset.counselor === 'true' ||
            (officer && state.selected.isUrgent &&
                ['Requested', 'Escalated'].includes(state.selected.state)));
        $('casePrivate').disabled = state.busy || !canOpen;
        $('caseAct').disabled = state.busy || !canOpen || state.selected.state === 'Closed';
        if (officer) $('caseAssign').disabled = state.busy || state.selected?.state !== 'Requested';
    }
    async function api(path, method = 'GET', payload) {
        if (!checkSession()) throw Error(T('Institution session changed. Reload.', 'প্রতিষ্ঠানের সেশন বদলেছে। রিলোড করুন।'));
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
            throw Error(json?.message || T('Request failed. Check access and refresh.', 'অনুরোধ ব্যর্থ। অনুমতি যাচাই করে রিফ্রেশ করুন।'));
        return json.data;
    }
    async function run(work) {
        if (state.busy) return;
        state.busy = true; controls();
        try { await work(); }
        catch (error) { notice(error.message, true); }
        finally { state.busy = false; controls(); }
    }
    function node(tag, value, css = '') {
        const n = document.createElement(tag); n.textContent = String(value ?? '');
        n.className = css; return n;
    }
    function caseButton(item, label) {
        const button = node('button', label, 'btn btn-sm btn-outline-primary');
        button.type = 'button';
        button.addEventListener('click', () => {
            state.selected = item;
            $('caseSelected').textContent = item.reference + ' · ' + item.state;
            $('casePrivatePanel').classList.add('d-none');
            $('casePrivateSummary').textContent = '';
            $('casePrivateEvents').replaceChildren();
            controls();
        });
        return button;
    }
    async function lookup(event) {
        event?.preventDefault();
        await run(async () => {
            const records = await api('/api/student-support-directory/enrollments?' +
                new URLSearchParams({ search: val('caseStudentSearch'), financialOnly: 'false' }));
            const box = $('caseStudents'); box.replaceChildren();
            if (!records?.length)
                box.append(node('div', T('No accessible active students.', 'অনুমোদিত সক্রিয় শিক্ষার্থী নেই।'), 'small text-muted'));
            for (const record of records || []) {
                const btn = node('button', record.studentName + ' · ' + record.studentCode,
                    'list-group-item list-group-item-action');
                btn.type = 'button'; btn.addEventListener('click', () => {
                    state.student = record.studentReference;
                    state.requestId = null; $('caseStudent').value = record.studentName;
                    box.replaceChildren(); controls();
                }); box.append(btn);
            }
        });
    }
    async function mine() {
        await run(async () => {
            const result = await api('/api/student-counseling/mine?page=1&pageSize=25');
            const container = $('myCases'); container.replaceChildren();
            if (!result?.items?.length)
                container.append(node('div', T('No requests by this account.', 'এই অ্যাকাউন্টের কোনো আবেদন নেই।'), 'text-muted small'));
            for (const item of result?.items || [])
                container.append(node('div', item.reference.slice(0, 8) + ' · ' + item.state +
                    (item.isUrgent ? ' · ' + T('Urgent', 'জরুরি') : ''), 'list-group-item'));
        });
    }
    async function listRaw() {
        const data = await api('/api/student-counseling?' + new URLSearchParams({
            page: String(state.page), pageSize: '20'
        }));
        state.total = data?.totalCount || 0;
        const tbody = $('caseRows'); tbody.replaceChildren();
        for (const item of data?.items || []) {
            const tr = document.createElement('tr');
            for (const text of [item.reference.slice(0, 8), item.state,
                item.isUrgent ? T('Urgent', 'জরুরি') : T('Normal', 'সাধারণ')])
                tr.append(node('td', text));
            const td = document.createElement('td');
            td.append(caseButton(item, T('Select', 'বাছুন'))); tr.append(td); tbody.append(tr);
        }
        if (!data?.items?.length) {
            const tr = document.createElement('tr'); const td = node('td', T('No assigned cases.', 'বরাদ্দকৃত কেস নেই।'));
            td.colSpan = 4; tr.append(td); tbody.append(tr);
        }
        $('casePage').textContent = `${state.page} · ${state.total} ${T('cases', 'কেস')}`;
    }
    async function submit(event) {
        event.preventDefault();
        if (!state.student) return;
        await run(async () => {
            const result = await api('/api/student-counseling', 'POST', {
                requestId: state.requestId || (state.requestId = guid()),
                studentReference: state.student, privateSummary: val('caseSummary'),
                urgent: $('caseUrgent').checked,
                guardianContactConsented: $('caseConsent').checked
            });
            state.requestId = null;
            $('caseSummary').value = ''; $('caseUrgent').checked = false; $('caseConsent').checked = false;
            notice(T('Confidential support requested: ', 'গোপন সহায়তার আবেদন: ') + result.reference);
        });
        await mine(); if (staff) await list();
    }
    const list = () => run(listRaw);
    async function selectedAction(action, payload) {
        if (!state.selected) return;
        await run(async () => {
            const result = await api('/api/student-counseling/' +
                encodeURIComponent(state.selected.reference) + action, 'POST', payload);
            state.selected = result;
            $('caseSelected').textContent = result.reference + ' · ' + result.state;
            $('casePrivatePanel').classList.add('d-none');
            notice(T('Case action saved.', 'কেস কার্যক্রম সংরক্ষিত হয়েছে।'));
            await listRaw();
        });
    }
    function appointmentUtc() {
        if (val('caseAction') !== 'Appointment') return null;
        const time = val('caseAppointment');
        if (!time) throw Error(T('Appointment time required.', 'অ্যাপয়েন্টমেন্টের সময় দিন।'));
        const d = new Date(time);
        if (Number.isNaN(d.getTime()) || d.getTime() <= Date.now())
            throw Error(T('Choose a future appointment time.', 'ভবিষ্যতের সময় নির্বাচন করুন।'));
        return d.toISOString();
    }
    if (staff) {
        $('caseRefresh').addEventListener('click', list);
        $('casePrev').addEventListener('click', () => { if (state.page > 1) { state.page--; list(); } });
        $('caseNext').addEventListener('click', () => { if (state.page * 20 < state.total) { state.page++; list(); } });
        $('casePrivate').addEventListener('click', () => {
            if (!state.selected) return;
            run(async () => {
                const result = await api('/api/student-counseling/' +
                    encodeURIComponent(state.selected.reference) + '/private-view', 'POST', { requestId: guid() });
                $('casePrivatePanel').classList.remove('d-none');
                $('casePrivateSummary').textContent = result.privateSummary;
                $('casePrivateEvents').replaceChildren();
                for (const entry of result.events || [])
                    $('casePrivateEvents').append(node('div', entry.action + ' · ' +
                        new Date(entry.occurredAtUtc).toLocaleString() + ' · ' + (entry.note || ''),
                        'list-group-item text-break'));
                notice(T('Private access recorded.', 'গোপন তথ্য দেখার ঘটনা অডিটে সংরক্ষিত।'));
            });
        });
        if (officer) $('caseAssign').addEventListener('click', () => {
            const id = Number(val('caseCounselorId'));
            if (!Number.isSafeInteger(id) || id <= 0) return notice(T('Valid counselor ID required.', 'সঠিক কাউন্সেলর আইডি দিন।'), true);
            selectedAction('/assign', { requestId: guid(), rowVersion: state.selected.rowVersion, counselorUserId: id });
        });
        $('caseActionForm').addEventListener('submit', event => {
            event.preventDefault();
            if (!state.selected || !val('caseNote')) return;
            let time;
            try { time = appointmentUtc(); } catch (error) { return notice(error.message, true); }
            selectedAction('/action', {
                requestId: guid(), rowVersion: state.selected.rowVersion,
                action: val('caseAction'), privateNote: val('caseNote'), appointmentAtUtc: time
            }).then(() => { if (!state.busy) $('caseNote').value = ''; });
        });
        list();
    }
    $('caseStudentSearchForm').addEventListener('submit', lookup);
    $('caseSubmitForm').addEventListener('submit', submit);
    $('caseSummary').addEventListener('input', controls);
    $('caseMineRefresh').addEventListener('click', mine);
    window.addEventListener('focus', checkSession);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) checkSession(); });
    (async () => { await lookup(); await mine(); })();
})();
