(() => {
    'use strict';
    const root = document.getElementById('aiWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const mode = root.dataset.mode;
    const bn = root.dataset.language === 'bn-BD';
    const translate = (en, bd) => bn ? bd : en;
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const session = [document.body?.dataset.authTenantId || '', document.body?.dataset.authSessionReference || ''];
    const sameSession = () => session[0] === (document.body?.dataset.authTenantId || '') &&
        session[1] === (document.body?.dataset.authSessionReference || '') &&
        window.EduOSSessionTransitionPending?.() !== true;
    const state = { busy: false, configured: false, sequence: 0, snapshot: null, snapshotKey: null };
    const val = id => (el(id)?.value || '').trim();

    function notice(message, error = false) {
        const element = el('aiWorkspaceNotice');
        element.className = 'alert ' + (error ? 'alert-danger' : 'alert-info');
        element.textContent = message;
    }
    function showOutput(data) {
        const text = typeof data?.text === 'string' ? data.text : '';
        if (!text || !sameSession()) return;
        el('aiOutputText').textContent = text;
        el('aiOutputDisclaimer').textContent = data.notice || '';
        el('aiOutputPanel').classList.remove('d-none');
    }
    async function api(url, options = {}) {
        const response = await fetch(url, {
            cache: 'no-store', credentials: 'same-origin', ...options,
            headers: {
                Accept: 'application/json',
                ...(options.body ? { 'Content-Type': 'application/json' } : {}),
                ...(options.method && options.method !== 'GET' ? { RequestVerificationToken: token } : {}),
                ...(options.headers || {})
            }
        });
        const body = await response.json().catch(() => null);
        if (!response.ok || body?.success !== true)
            throw Error(body?.message || translate('Request failed. Please retry.', 'অনুরোধ ব্যর্থ হয়েছে। আবার চেষ্টা করুন।'));
        return body.data;
    }
    function lock(buttonId, locked) {
        state.busy = locked;
        if (el(buttonId)) el(buttonId).disabled = locked;
        if (el('aiManagementInterpret'))
            el('aiManagementInterpret').disabled = locked || !state.configured || !state.snapshot;
    }
    async function runGenerate(buttonId, endpoint, payload) {
        if (state.busy || !sameSession()) return;
        const sequence = ++state.sequence;
        lock(buttonId, true);
        notice(translate('Generating a draft. Review it before use.', 'খসড়া তৈরি হচ্ছে। ব্যবহারের আগে যাচাই করুন।'));
        try {
            const data = await api(endpoint, { method: 'POST', body: JSON.stringify(payload) });
            if (sequence !== state.sequence || !sameSession()) return;
            showOutput(data);
            notice(translate('Draft ready for review.', 'যাচাইয়ের জন্য খসড়া প্রস্তুত।'));
        } catch (err) {
            if (sequence === state.sequence && sameSession()) notice(err.message, true);
        } finally {
            if (sequence === state.sequence) lock(buttonId, false);
        }
    }
    function initTeacher() {
        const form = el('aiTeacherForm');
        form.addEventListener('submit', event => {
            event.preventDefault();
            if (!form.reportValidity() || !sameSession()) return;
            runGenerate('aiTeacherGenerate', '/api/ai/workspace/teacher', {
                topic: val('aiTeacherTopic'), classLevel: val('aiTeacherLevel'),
                outputType: val('aiTeacherOutput'), durationMinutes: Number(val('aiTeacherDuration')),
                language: val('aiLanguage')
            });
        });
    }
    function initTutor() {
        const form = el('aiTutorForm');
        form.addEventListener('submit', event => {
            event.preventDefault();
            if (!form.reportValidity() || !sameSession()) return;
            runGenerate('aiTutorGenerate', '/api/ai/workspace/tutor', {
                topic: val('aiTutorTopic'), question: val('aiTutorQuestion'),
                language: val('aiLanguage')
            });
        });
    }
    const localDate = date => {
        const d = new Date(date.getFullYear(), date.getMonth(), date.getDate());
        const pad = n => String(n).padStart(2, '0');
        return [d.getFullYear(), pad(d.getMonth() + 1), pad(d.getDate())].join('-');
    };
    function filters() {
        return {
            campusId: Number(val('aiCampus')),
            fromDate: val('aiFrom'),
            toDate: val('aiTo')
        };
    }
    function filterKey(value) {
        return JSON.stringify(value);
    }
    function renderMetrics(snapshot) {
        if (!snapshot || typeof snapshot.currentActiveEnrollments !== 'number' ||
            typeof snapshot.finalizedAttendanceMarks !== 'number') throw Error('Invalid analytics result.');
        el('aiMetricActive').textContent = snapshot.currentActiveEnrollments.toLocaleString();
        el('aiMetricMarks').textContent = snapshot.finalizedAttendanceMarks.toLocaleString();
        el('aiMetricRate').textContent = snapshot.attendanceRatePercent == null
            ? translate('No data', 'তথ্য নেই') : snapshot.attendanceRatePercent.toFixed(2) + '%';
        const list = el('aiManagementFindings');
        list.replaceChildren();
        for (const finding of snapshot.findings || []) {
            const item = document.createElement('li');
            item.textContent = String(finding);
            list.append(item);
        }
        el('aiManagementMetrics').classList.remove('d-none');
    }
    function clearSnapshot() {
        state.snapshot = null;
        state.snapshotKey = null;
        el('aiManagementMetrics').classList.add('d-none');
        el('aiManagementInterpret').disabled = true;
    }
    async function loadMetrics(event) {
        event.preventDefault();
        if (state.busy || !sameSession()) return;
        const current = filters();
        clearSnapshot();
        if (!current.campusId || !current.fromDate || !current.toDate ||
            current.fromDate > current.toDate)
            return notice(translate('Choose a campus and valid dates.', 'ক্যাম্পাস ও সঠিক তারিখ নির্বাচন করুন।'), true);
        const sequence = ++state.sequence;
        lock('aiManagementLoad', true);
        try {
            const query = new URLSearchParams(current);
            const data = await api('/api/ai/workspace/management/summary?' + query);
            if (sequence !== state.sequence || !sameSession()) return;
            state.snapshot = data;
            state.snapshotKey = filterKey(current);
            renderMetrics(data);
            notice(translate('Verified metrics loaded from the institution database.',
                'প্রতিষ্ঠানের ডাটাবেস থেকে যাচাইকৃত পরিসংখ্যান দেখানো হয়েছে।'));
        } catch (err) {
            if (sequence === state.sequence && sameSession()) notice(err.message, true);
        } finally {
            if (sequence === state.sequence) lock('aiManagementLoad', false);
        }
    }
    async function interpretMetrics() {
        if (state.busy || !state.configured || !state.snapshot || !sameSession()) return;
        const current = filters();
        if (filterKey(current) !== state.snapshotKey)
            return notice(translate('Reload metrics after changing filters.', 'ফিল্টার বদলালে নতুন করে পরিসংখ্যান লোড করুন।'), true);
        runGenerate('aiManagementInterpret', '/api/ai/workspace/management/interpret', current);
    }
    async function initManagement() {
        el('aiManagementForm').addEventListener('submit', loadMetrics);
        el('aiManagementInterpret').addEventListener('click', interpretMetrics);
        ['aiCampus', 'aiFrom', 'aiTo'].forEach(id =>
            el(id).addEventListener('change', clearSnapshot));
        const today = new Date();
        const from = new Date(today.getFullYear(), today.getMonth(), today.getDate());
        from.setDate(from.getDate() - 29);
        el('aiFrom').value = localDate(from);
        el('aiTo').value = localDate(today);
        try {
            const campuses = await api('/api/ai/workspace/management/campuses');
            if (!sameSession()) return;
            const select = el('aiCampus');
            select.replaceChildren(new Option(translate('Select campus', 'ক্যাম্পাস নির্বাচন করুন'), ''));
            for (const campus of campuses || [])
                select.add(new Option(campus.name, String(campus.id)));
            if (select.options.length === 2) select.selectedIndex = 1;
            if (!(campuses || []).length)
                notice(translate('No authorized campus found.', 'অনুমোদিত ক্যাম্পাস পাওয়া যায়নি।'), true);
        } catch (err) {
            if (sameSession()) notice(err.message, true);
        }
    }
    el('aiCopy').addEventListener('click', async () => {
        const value = el('aiOutputText').textContent || '';
        if (!sameSession() || !value) return;
        try {
            await navigator.clipboard.writeText(value);
            notice(translate('Copied to clipboard.', 'কপি করা হয়েছে।'));
        } catch {
            notice(translate('Clipboard unavailable. Select and copy the output text.',
                'কপি করা যাচ্ছে না। লেখাটি নির্বাচন করে কপি করুন।'), true);
        }
    });
    api('/api/ai/workspace/status').then(data => {
        if (!sameSession()) return;
        state.configured = Boolean(data?.modelConfigured);
        if (!state.configured)
            notice(translate('The AI model is not configured. AI generation is disabled; verified management metrics remain available.',
                'AI মডেল কনফিগার করা নেই। AI খসড়া বন্ধ থাকবে; যাচাইকৃত Management পরিসংখ্যান দেখা যাবে।'), true);
    }).catch(err => { if (sameSession()) notice(err.message, true); });
    if (mode === 'Teacher') initTeacher();
    else if (mode === 'Tutor') initTutor();
    else if (mode === 'Management') initManagement();
})();
