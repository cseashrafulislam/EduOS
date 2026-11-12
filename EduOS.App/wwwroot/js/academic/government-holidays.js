(() => {
    'use strict';
    const root = document.getElementById('academicCalendarApp');
    const panel = document.getElementById('calendarGovernmentPanel');
    if (!root || !panel || root.dataset.canManage !== 'true') return;
    const el = id => document.getElementById(id);
    const bn = document.documentElement.lang?.toLowerCase().startsWith('bn');
    const t = (en, bd) => bn ? bd : en;
    const identity = [document.body?.dataset.authTenantId || '',
        document.body?.dataset.authSessionReference || '',
        document.body?.dataset.authUserId || ''];
    const sameSession = () => identity[0] && identity[1] &&
        identity[0] === (document.body?.dataset.authTenantId || '') &&
        identity[1] === (document.body?.dataset.authSessionReference || '') &&
        identity[2] === (document.body?.dataset.authUserId || '') &&
        window.EduOSSessionTransitionPending?.() !== true;
    const button = el('calendarGovernmentPreview');
    const save = el('calendarGovernmentImport');
    const status = el('calendarGovernmentStatus');
    const selection = el('calendarGovernmentSelection');
    const rows = el('calendarGovernmentRows');
    const confirm = el('calendarGovernmentConfirm');
    const state = { preview: null, scopeKey: null, busy: false, requestId: null, requestFingerprint: null, revision: 0 };
    const say = (message, kind = 'muted') => {
        status.className = 'small text-' + kind + ' mb-2';
        status.textContent = message;
    };
    const scope = () => {
        const academicYearId = Number(el('calendarYear')?.value);
        const campusId = el('calendarCampus')?.value ? Number(el('calendarCampus').value) : null;
        const fromDate = el('calendarFrom')?.value, toDate = el('calendarTo')?.value;
        if (!Number.isSafeInteger(academicYearId) || academicYearId <= 0 ||
            !fromDate || !toDate || fromDate > toDate)
            throw new Error(t('Select a valid academic year and date range first.',
                'প্রথমে সঠিক Academic Year ও তারিখের সীমা নির্বাচন করুন।'));
        return { academicYearId, campusId, fromDate, toDate };
    };
    const fingerprint = value => JSON.stringify(value);
    const getSelected = () => [...rows.querySelectorAll('input[data-government-code]:checked')]
        .map(box => box.dataset.governmentCode);
    const create = (tag, text, className) => {
        const n = document.createElement(tag);
        if (text != null) n.textContent = String(text);
        if (className) n.className = className;
        return n;
    };
    const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value;
    const api = async (url, method = 'GET', data) => {
        if (!sameSession()) throw new Error(t('Session changed. Refresh the page.', 'সেশন পরিবর্তিত হয়েছে। পেজ রিফ্রেশ করুন।'));
        const csrf = token();
        if (method !== 'GET' && !csrf)
            throw new Error(t('Security token is missing. Refresh the page.', 'নিরাপত্তা টোকেন পাওয়া যায়নি। পেজ রিফ্রেশ করুন।'));
        const response = await fetch(url, {
            method, credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json',
                ...(method === 'GET' ? {} : { RequestVerificationToken: csrf }),
                ...(data === undefined ? {} : { 'Content-Type': 'application/json' }) },
            ...(data === undefined ? {} : { body: JSON.stringify(data) })
        });
        const result = await response.json().catch(() => null);
        if (!sameSession()) throw new Error(t('Session changed. Refresh the page.', 'সেশন পরিবর্তিত হয়েছে। পেজ রিফ্রেশ করুন।'));
        if (!response.ok || !result?.success)
            throw new Error(result?.message || t('Holiday operation failed.', 'ছুটির অনুরোধ ব্যর্থ হয়েছে।'));
        return result.data;
    };
    const synchronize = () => {
        let key;
        try { key = fingerprint(scope()); } catch { key = null; }
        const valid = !!state.preview && !!key && key === state.scopeKey && sameSession();
        const selected = valid ? getSelected().length : 0;
        const editable = !state.busy && sameSession() && (root.dataset.tenantAdmin === 'true' ||
            !!el('calendarCampus').value);
        button.disabled = !editable;
        save.disabled = !editable || !valid || selected === 0 || !confirm.checked;
    };
    const invalidate = () => {
        ++state.revision;
        state.preview = null;
        state.scopeKey = null;
        state.requestId = null;
        state.requestFingerprint = null;
        selection.hidden = true;
        rows.replaceChildren();
        confirm.checked = false;
        say(t('Preview the official dates for the selected academic range.',
            'নির্বাচিত Academic Year-এর সরকারি ছুটির তালিকা দেখুন।'));
        synchronize();
    };
    const render = preview => {
        rows.replaceChildren();
        for (const holiday of preview.holidays || []) {
            const row = create('tr');
            const checkboxCell = create('td');
            const box = create('input');
            box.type = 'checkbox';
            box.className = 'form-check-input';
            box.dataset.governmentCode = holiday.code;
            box.disabled = holiday.status !== 'Missing';
            box.setAttribute('aria-label', t('Import ', 'সংরক্ষণ করুন ') + holiday.title);
            box.addEventListener('change', () => {
                state.requestId = null;
                state.requestFingerprint = null;
                synchronize();
            });
            checkboxCell.append(box);
            const titleCell = create('td');
            titleCell.append(create('span', holiday.title));
            if (holiday.moonDependent)
                titleCell.append(create('span', t(' • Moon sighting may change this date',
                    ' • চাঁদ দেখা অনুযায়ী তারিখ পরিবর্তন হতে পারে'), 'small text-warning d-block'));
            try {
                const source = new URL(holiday.sourceUrl);
                if (source.protocol === 'https:' && source.hostname === 'mopa.gov.bd') {
                    const link = create('a', t('Official notice', 'সরকারি প্রজ্ঞাপন'), 'small d-block');
                    link.href = source.href;
                    link.target = '_blank';
                    link.rel = 'noopener noreferrer';
                    titleCell.append(link);
                }
            } catch { /* Never output arbitrary source URLs. */ }
            const dateText = holiday.startDate +
                (holiday.endDate === holiday.startDate ? '' : ' – ' + holiday.endDate);
            row.append(checkboxCell, titleCell, create('td', dateText),
                create('td', holiday.status));
            rows.append(row);
        }
        selection.hidden = !(preview.holidays || []).length;
        say(t('Official list: ', 'সরকারি তালিকা: ') + (preview.holidays || []).length +
            t(' holiday entries. Review and tick missing entries to import.',
                'টি ছুটি। যাচাই করে Missing ছুটিগুলো নির্বাচন করুন।'));
        synchronize();
    };
    async function preview() {
        if (state.busy || !sameSession()) return;
        let chosen;
        try { chosen = scope(); } catch (error) { say(error.message, 'danger'); return; }
        const currentKey = fingerprint(chosen);
        const revision = ++state.revision;
        state.busy = true;
        button.disabled = true;
        say(t('Loading gazetted dates...', 'গেজেটের তারিখ লোড হচ্ছে...'));
        try {
            const params = new URLSearchParams({
                academicYearId: String(chosen.academicYearId),
                fromDate: chosen.fromDate, toDate: chosen.toDate
            });
            if (chosen.campusId) params.set('campusId', String(chosen.campusId));
            const data = await api('/api/academic-calendars/government-holidays/preview?' + params);
            if (revision !== state.revision || currentKey !== fingerprint(scope())) return;
            state.preview = data;
            state.scopeKey = currentKey;
            state.requestId = null;
            state.requestFingerprint = null;
            confirm.checked = false;
            render(data);
        } catch (error) {
            if (revision === state.revision && sameSession()) {
                selection.hidden = true;
                say(error.message, 'danger');
            }
        } finally {
            state.busy = false;
            synchronize();
        }
    }
    async function importSelected() {
        if (state.busy || !state.preview || !confirm.checked || !sameSession()) return;
        let chosen;
        try { chosen = scope(); } catch (error) { say(error.message, 'danger'); return; }
        if (fingerprint(chosen) !== state.scopeKey) { invalidate(); return; }
        const selectedCodes = getSelected().sort();
        if (!selectedCodes.length) return;
        const inputFingerprint = fingerprint({ ...chosen, sourceVersion: state.preview.sourceVersion, selectedCodes });
        if (state.requestFingerprint !== inputFingerprint || !state.requestId) {
            if (!window.crypto?.randomUUID) { say(t('Secure request ID unavailable. Use HTTPS.', 'নিরাপদ Request ID পাওয়া যায়নি। HTTPS ব্যবহার করুন।'), 'danger'); return; }
            state.requestId = crypto.randomUUID();
            state.requestFingerprint = inputFingerprint;
        }
        state.busy = true;
        synchronize();
        el('calendarFilters').inert = true;
        const revision = state.revision;
        try {
            const saved = await api('/api/academic-calendars/government-holidays/import', 'POST', {
                ...chosen, clientRequestId: state.requestId,
                sourceVersion: state.preview.sourceVersion, selectedCodes, confirmed: true
            });
            if (revision !== state.revision || !sameSession()) return;
            const message = t('Saved: ', 'সংরক্ষিত: ') + saved.createdCount +
                t(', already existed: ', ', আগে ছিল: ') + saved.existingCount +
                t('. Calendar updated; review dates when the government publishes amendments.',
                    '। সরকারি সংশোধন হলে তারিখ আবার যাচাই করুন।');
            invalidate();
            say(message, 'success');
            el('calendarLoad').click();
        } catch (error) {
            if (sameSession()) say(error.message + t(' Check the calendar before retrying.',
                ' আবার চেষ্টা করার আগে Calendar যাচাই করুন।'), 'danger');
        } finally {
            state.busy = false;
            el('calendarFilters').inert = false;
            synchronize();
        }
    }
    button.addEventListener('click', preview);
    save.addEventListener('click', importSelected);
    confirm.addEventListener('change', synchronize);
    el('calendarGovernmentSelectAll').addEventListener('click', () => {
        for (const box of rows.querySelectorAll('input[data-government-code]:not(:disabled)')) box.checked = true;
        state.requestId = null; state.requestFingerprint = null; synchronize();
    });
    el('calendarGovernmentClear').addEventListener('click', () => {
        for (const box of rows.querySelectorAll('input[data-government-code]:not(:disabled)')) box.checked = false;
        state.requestId = null; state.requestFingerprint = null; synchronize();
    });
    for (const id of ['calendarYear', 'calendarCampus', 'calendarFrom', 'calendarTo']) {
        el(id)?.addEventListener('change', invalidate);
        if (id === 'calendarFrom' || id === 'calendarTo') el(id)?.addEventListener('input', invalidate);
    }
    window.addEventListener('pageshow', () => { if (!sameSession()) invalidate(); });
    synchronize();
})();