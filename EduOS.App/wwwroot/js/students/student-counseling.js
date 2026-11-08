(() => {
    'use strict';
    const root = document.getElementById('counselingApp');
    if (!root) return;
    const $ = id => document.getElementById(id);
    const bn = (document.documentElement.lang || '').toLowerCase().startsWith('bn');
    const T = (en, bd) => bn ? bd : en;
    const officer = root.dataset.officer === 'true';
    const staff = officer || root.dataset.counselor === 'true';
    const admin = root.dataset.admin === 'true';
    const aggregateAccess = root.dataset.aggregate === 'true';
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value ||
        document.querySelector('meta[name="request-verification-token"]')?.content;
    const session = [document.body.dataset.authTenantId, document.body.dataset.authSessionReference];
    const safeSession = () => session[0] && session[1] &&
        session[0] === document.body.dataset.authTenantId &&
        session[1] === document.body.dataset.authSessionReference;
    const state = { student: '', selected: null, busy: false, locked: false, page: 1, total: 0,
        requestId: null, policy: null, policyRequestId: null, redactionRequestId: null, notificationRequestId: null };
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
        state.policy = null; state.policyRequestId = null; state.redactionRequestId = null;
        for (const field of root.querySelectorAll('input,select,textarea'))
            if (field.type !== 'hidden') field.value = '';
        $('caseStudents').replaceChildren(); $('myCases').replaceChildren();
        if (aggregateAccess) $('caseAggregates').replaceChildren();
        if (admin || staff) {
            $('casePolicyDetails').textContent = '';
            $('casePolicyCampus').replaceChildren();
        }
        if (admin) {
            $('casePolicyPending').replaceChildren(); $('caseRedactionPending').replaceChildren();
        }
        if (staff) {
            $('caseRows').replaceChildren(); $('caseSelected').textContent = '';
            $('casePrivateSummary').textContent = ''; $('casePrivateEvents').replaceChildren();
            $('casePrivatePanel').classList.add('d-none');
            if (officer) $('caseCounselorId').replaceChildren();
        }
        for (const control of root.querySelectorAll('button,input,select,textarea')) control.disabled = true;
        notice(T('Institution session changed. Reload this page.', 'প্রতিষ্ঠানের সেশন বদলেছে। পৃষ্ঠাটি রিলোড করুন।'), true);
        return false;
    }
    function controls() {
        if (state.locked) return;
        $('caseSubmit').disabled = state.busy || !state.student || val('caseSummary').length < 10;
        $('caseMineRefresh').disabled = state.busy;
        if (aggregateAccess) $('caseAggregateLoad').disabled = state.busy || !val('caseAggregateMonth');
        if (admin || staff) $('casePolicyLoad').disabled = state.busy || !val('casePolicyCampus');
        if (admin) {
            $('casePolicySubmit').disabled = state.busy || !val('casePolicyCampus');
            $('casePolicyPendingLoad').disabled = state.busy;
            $('caseRedactionPendingLoad').disabled = state.busy;
        }
        if (!staff) return;
        $('caseRefresh').disabled = state.busy;
        $('casePrev').disabled = state.busy || state.page <= 1;
        $('caseNext').disabled = state.busy || state.page * 20 >= state.total;
        const canOpen = !!state.selected && (root.dataset.counselor === 'true' ||
            (officer && state.selected.isUrgent &&
                ['Requested', 'Escalated'].includes(state.selected.state)));
        $('casePrivate').disabled = state.busy || !canOpen || state.selected?.isRedacted;
        $('caseAct').disabled = state.busy || !canOpen || state.selected.state === 'Closed' || state.selected?.isRedacted;
        $('caseNotifyGuardian').disabled = state.busy || !state.selected ||
            !state.selected.guardianContactConsented || state.selected.state === 'Closed' || state.selected.isRedacted;
        $('caseRedactRequest').disabled = state.busy || !state.selected ||
            state.selected.state !== 'Closed' || state.selected.redactionPending || state.selected.isRedacted ||
            val('caseRedactReason').length < 10;
        if (officer) $('caseAssign').disabled = state.busy || state.selected?.state !== 'Requested' ||
            $('caseCounselorId').disabled || !val('caseCounselorId');
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
            state.redactionRequestId = null; state.notificationRequestId = null;
            $('caseSelected').textContent = item.reference + ' · ' + item.state;
            $('casePrivatePanel').classList.add('d-none');
            $('casePrivateSummary').textContent = '';
            $('casePrivateEvents').replaceChildren();
            controls();
            if (officer) loadCounselors(item);
        });
        return button;
    }
    async function loadCounselors(item) {
        const chooser = $('caseCounselorId'); chooser.replaceChildren();
        const placeholder = node('option', T('Select an eligible counselor', 'অনুমোদিত কাউন্সেলর নির্বাচন করুন'));
        placeholder.value = ''; chooser.append(placeholder); chooser.disabled = true;
        if (item.state !== 'Requested') return;
        await run(async () => {
            const result = await api('/api/student-support-directory/counselors?' +
                new URLSearchParams({ campusId: String(item.campusId) }));
            if (state.selected?.reference !== item.reference) return;
            for (const counselor of result || []) {
                const option = node('option', counselor.fullName ||
                    T('Counselor', 'কাউন্সেলর'));
                option.value = String(counselor.userId); chooser.append(option);
            }
            chooser.disabled = !result?.length;
            if (!result?.length) notice(T('No active counselor is assigned to this campus.',
                'এই ক্যাম্পাসে কোনো সক্রিয় কাউন্সেলর নেই।'), true);
        });
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
            for (const item of result?.items || []) {
                const entry = node('div', item.reference.slice(0, 8) + ' · ' + item.state +
                    (item.isUrgent ? ' · ' + T('Urgent', 'জরুরি') : ''), 'list-group-item');
                if (item.guardianContactConsented) {
                    const withdraw = node('button', T('Withdraw guardian contact consent',
                        'অভিভাবকের সঙ্গে যোগাযোগের সম্মতি প্রত্যাহার'), 'btn btn-outline-danger btn-sm d-block mt-2');
                    withdraw.type = 'button';
                    let requestId = null;
                    withdraw.addEventListener('click', async () => {
                        if (!window.confirm(T('Withdraw permission for future guardian contact on this case?',
                            'এই কেসে ভবিষ্যতে অভিভাবকের সঙ্গে যোগাযোগের অনুমতি প্রত্যাহার করবেন?'))) return;
                        await run(async () => {
                            const changed = await api('/api/student-counseling/' +
                                encodeURIComponent(item.reference) + '/guardian-contact-consent', 'POST', {
                                requestId: requestId || (requestId = guid()),
                                rowVersion: item.rowVersion, consent: false
                            });
                            item.rowVersion = changed.rowVersion;
                            item.guardianContactConsented = false;
                            requestId = null; withdraw.remove();
                            notice(T('Guardian contact permission withdrawn and recorded.',
                                'অভিভাবকের যোগাযোগের অনুমতি প্রত্যাহার ও অডিটে সংরক্ষিত।'));
                        });
                    });
                    entry.append(withdraw);
                }
                container.append(entry);
            }
        });
    }
    async function aggregates() {
        await run(async () => {
            const month = val('caseAggregateMonth');
            if (!/^\d{4}-(0[1-9]|1[0-2])$/.test(month))
                throw Error(T('Choose a valid UTC month.', 'সঠিক UTC মাস নির্বাচন করুন।'));
            const result = await api('/api/student-counseling/aggregates?' + new URLSearchParams({
                year: month.slice(0, 4), month: String(Number(month.slice(5)))
            }));
            const container = $('caseAggregates'); container.replaceChildren();
            for (const row of result?.campuses || [])
                container.append(node('div', T('Campus ', 'ক্যাম্পাস ') + row.campusId +
                    ' · ' + row.caseCount + ' ' + T('requests', 'আবেদন'), 'list-group-item'));
            if (!result?.campuses?.length)
                container.append(node('div', T('No publishable groups (groups under five are suppressed).',
                    'প্রকাশযোগ্য গ্রুপ নেই (পাঁচটির কম হলে তথ্য গোপন রাখা হয়)।'), 'list-group-item text-muted'));
        });
    }
    function currentCampus() { return Number(val('casePolicyCampus')); }
    async function loadPolicyRaw() {
        const campus = currentCampus();
        if (!Number.isSafeInteger(campus) || campus <= 0) return;
        state.policy = null;
        $('casePolicyDetails').textContent = T('No approved campus policy. Independent approval required.',
            'অনুমোদিত নীতি নেই। স্বতন্ত্র অনুমোদন আবশ্যক।');
        const policy = await api('/api/student-counseling/policy/' + campus);
        state.policy = policy;
        const info = $('casePolicyDetails'); info.replaceChildren();
        info.append(node('strong', T('Retention after closure: ', 'বন্ধের পর সংরক্ষণ: ') +
            policy.retentionDays + T(' days', ' দিন')));
        info.append(node('p', policy.legalHold ? T('LEGAL HOLD: redaction suspended.', 'আইনি সংরক্ষণ: তথ্য অপসারণ স্থগিত।') :
            T('No legal hold is currently recorded.', 'বর্তমানে আইনি সংরক্ষণ নেই।'), 'mt-2 mb-1'));
        info.append(node('p', T('Emergency guidance: ', 'জরুরি নির্দেশনা: ') +
            (bn ? policy.emergencyGuidanceBn : policy.emergencyGuidanceEn), 'mb-0 text-break'));
        if (admin) {
            $('caseRetentionDays').value = String(policy.retentionDays);
            $('caseLegalHold').checked = policy.legalHold;
            $('caseEmergencyEn').value = policy.emergencyGuidanceEn;
            $('caseEmergencyBn').value = policy.emergencyGuidanceBn;
        }
    }
    async function loadPolicy() {
        await run(async () => {
            try { await loadPolicyRaw(); }
            catch (error) {
                if (!error.message.includes('not been independently approved')) throw error;
                state.policy = null;
            }
        });
    }
    function decisionRow(item, kind) {
        const entry = node('div', '', 'list-group-item');
        const title = kind === 'policy' ?
            T('Campus ', 'ক্যাম্পাস ') + item.campusId + ' · ' + item.retentionDays +
                T(' days', ' দিন') + (item.legalHold ? T(' · LEGAL HOLD', ' · আইনি সংরক্ষণ') : '') :
            T('Closed case reference ', 'বন্ধ কেস রেফারেন্স ') + item.reference.slice(0, 12) +
                T(' · campus ', ' · ক্যাম্পাস ') + item.campusId;
        entry.append(node('strong', title, 'd-block text-break'));
        if (kind === 'policy') entry.append(node('p', T('English: ', 'ইংরেজি: ') + item.emergencyGuidanceEn +
            T(' · Bangla: ', ' · বাংলা: ') + item.emergencyGuidanceBn, 'small text-break mb-1'));
        const input = node('input', '', 'form-control form-control-sm mb-2');
        input.placeholder = T('Independent decision reason (10–500 chars)', 'স্বতন্ত্র সিদ্ধান্তের কারণ (১০–৫০০ অক্ষর)');
        input.type = 'text'; input.minLength = 10; input.maxLength = 500;
        entry.append(input);
        for (const approve of [true, false]) {
            const button = node('button', approve ? T('Approve', 'অনুমোদন') : T('Reject', 'বাতিল'),
                'btn btn-sm me-2 ' + (approve ? 'btn-outline-success' : 'btn-outline-danger'));
            button.type = 'button';
            let id = null;
            button.addEventListener('click', () => run(async () => {
                const reason = input.value.trim();
                if (reason.length < 10) throw Error(T('Provide at least 10 characters of reasoning.',
                    'কমপক্ষে ১০ অক্ষরের কারণ দিন।'));
                if (approve && kind === 'redaction' && !window.confirm(T(
                    'Permanently clear confidential narratives for this expired case? This cannot be undone.',
                    'এই মেয়াদোত্তীর্ণ কেসের গোপন বিবরণ স্থায়ীভাবে অপসারণ করবেন? ফিরিয়ে আনা যাবে না।'))) return;
                const path = kind === 'policy' ? '/policy-requests/' + item.reference + '/review' :
                    '/' + item.reference + '/redaction/review';
                await api('/api/student-counseling' + path, 'POST', {
                    requestId: id || (id = guid()), rowVersion: item.rowVersion,
                    reason, approve
                });
                entry.remove(); id = null;
                notice(T('Independent decision recorded.', 'স্বতন্ত্র সিদ্ধান্ত সংরক্ষিত।'));
                if (kind === 'policy' && currentCampus() === item.campusId) {
                    try { await loadPolicyRaw(); } catch (_) { state.policy = null; }
                }
                if (kind === 'redaction' && state.selected?.reference === item.reference)
                    state.selected = await api('/api/student-counseling/' + item.reference);
            }));
            entry.append(button);
        }
        return entry;
    }
    async function pendingPolicies() {
        await run(async () => {
            const items = await api('/api/student-counseling/policy-requests');
            const box = $('casePolicyPending'); box.replaceChildren();
            if (!items?.length) box.append(node('div', T('No pending campus policies.', 'অপেক্ষমাণ ক্যাম্পাস নীতি নেই।'), 'small text-muted'));
            for (const item of items || []) box.append(decisionRow(item, 'policy'));
        });
    }
    async function pendingRedactions() {
        await run(async () => {
            const items = await api('/api/student-counseling/redaction-requests');
            const box = $('caseRedactionPending'); box.replaceChildren();
            if (!items?.length) box.append(node('div', T('No pending redactions.', 'অপেক্ষমাণ অপসারণ আবেদন নেই।'), 'small text-muted'));
            for (const item of items || []) box.append(decisionRow(item, 'redaction'));
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
            if (officer && result.state !== 'Requested') $('caseCounselorId').disabled = true;
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
        $('caseNotifyGuardian').addEventListener('click', () => {
            if (!state.selected || !window.confirm(T(
                'Send a neutral in-app message only to the original consenting guardian? No case details will be included.',
                'শুধু মূল সম্মত অভিভাবককে সাধারণ অ্যাপ বার্তা পাঠাবেন? কেস তথ্য থাকবে না।'))) return;
            selectedAction('/notify-guardian', {
                requestId: state.notificationRequestId || (state.notificationRequestId = guid()),
                rowVersion: state.selected.rowVersion
            }).then(() => { state.notificationRequestId = null; });
        });
        $('caseRedactReason').addEventListener('input', controls);
        $('caseRedactForm').addEventListener('submit', event => {
            event.preventDefault();
            if (!state.selected || val('caseRedactReason').length < 10) return;
            selectedAction('/redaction', {
                requestId: state.redactionRequestId || (state.redactionRequestId = guid()),
                rowVersion: state.selected.rowVersion, reason: val('caseRedactReason'), approve: false
            }).then(() => { if (!state.busy) $('caseRedactReason').value = ''; });
        });
        if (officer) $('caseAssign').addEventListener('click', () => {
            const id = Number(val('caseCounselorId'));
            if (!Number.isSafeInteger(id) || id <= 0) return notice(T('Valid counselor ID required.', 'সঠিক কাউন্সেলর আইডি দিন।'), true);
            selectedAction('/assign', { requestId: guid(), rowVersion: state.selected.rowVersion, counselorUserId: id });
        });
        if (officer) $('caseCounselorId').addEventListener('change', controls);
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
    if (aggregateAccess) {
        const now = new Date();
        $('caseAggregateMonth').value = now.getUTCFullYear() + '-' +
            String(now.getUTCMonth() + 1).padStart(2, '0');
        $('caseAggregateLoad').addEventListener('click', aggregates);
    }
    async function bootstrapPolicy() {
        await run(async () => {
            const campuses = await api('/api/student-counseling/policy-campuses');
            for (const campus of campuses || []) {
                const option = node('option', campus.campusName);
                option.value = String(campus.campusId);
                $('casePolicyCampus').append(option);
            }
        });
        if (admin) { await pendingPolicies(); await pendingRedactions(); }
    }
    if (admin || staff) {
        $('casePolicyLoad').addEventListener('click', loadPolicy);
        $('casePolicyCampus').addEventListener('change', () => {
            state.policy = null; state.policyRequestId = null;
            $('casePolicyDetails').textContent = T('Select Load to read the approved policy.',
                'অনুমোদিত নীতি দেখতে লোড চাপুন।');
            controls();
        });
    }
    if (admin) {
        $('casePolicyPendingLoad').addEventListener('click', pendingPolicies);
        $('caseRedactionPendingLoad').addEventListener('click', pendingRedactions);
        $('casePolicyForm').addEventListener('submit', event => {
            event.preventDefault();
            run(async () => {
                const submitted = await api('/api/student-counseling/policy-requests', 'POST', {
                    requestId: state.policyRequestId || (state.policyRequestId = guid()),
                    campusId: currentCampus(), expectedPolicyRowVersion: state.policy?.rowVersion || '',
                    retentionDays: Number(val('caseRetentionDays')), legalHold: $('caseLegalHold').checked,
                    emergencyGuidanceEn: val('caseEmergencyEn'), emergencyGuidanceBn: val('caseEmergencyBn'),
                    reason: val('casePolicyReason')
                });
                state.policyRequestId = null;
                $('casePolicyReason').value = '';
                notice(T('Policy change submitted for independent review: ', 'নীতি পরিবর্তন স্বতন্ত্র যাচাইয়ে পাঠানো হয়েছে: ') +
                    submitted.reference.slice(0, 12));
            }).then(pendingPolicies);
        });
    }
    window.addEventListener('focus', checkSession);
    document.addEventListener('visibilitychange', () => { if (!document.hidden) checkSession(); });
    (async () => { if (admin || staff) await bootstrapPolicy(); await lookup(); await mine(); })();
})();
