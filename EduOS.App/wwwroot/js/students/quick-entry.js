(() => {
    'use strict';
    const root = document.getElementById('runningStudentEntry');
    if (!root) return;
    const el = id => document.getElementById(id);
    const bn = root.dataset.language === 'bn-BD';
    const form = el('runningStudentForm');
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const identity = [document.body?.dataset.authTenantId || '',
        document.body?.dataset.authSessionReference || ''];
    const sameSession = () => identity[0] === (document.body?.dataset.authTenantId || '') &&
        identity[1] === (document.body?.dataset.authSessionReference || '') &&
        window.EduOSSessionTransitionPending?.() !== true;
    const state = { batches: [], curricula: [], saving: false, batchSeq: 0, curriculumSeq: 0,
        requestId: null, fingerprint: null };
    const value = id => (el(id)?.value || '').trim();
    const currentBatch = () => state.batches.find(x => x.id === Number(value('runningBatch')));
    const currentCurriculum = () => state.curricula.find(x => x.id === Number(value('runningCurriculum')));
    const say = (en, bd) => bn ? bd : en;

    function alertMessage(text, error) {
        const notice = el('runningStudentNotice');
        notice.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        notice.textContent = text;
        notice.focus();
    }
    function clearMessage() {
        const notice = el('runningStudentNotice');
        notice.textContent = '';
        notice.className = 'alert d-none';
    }
    async function api(url, options = {}) {
        const response = await fetch(url, {
            cache: 'no-store', credentials: 'same-origin', ...options,
            headers: {
                Accept: 'application/json',
                ...(options.body ? { 'Content-Type': 'application/json' } : {}),
                ...(options.method === 'POST' ? { RequestVerificationToken: token } : {}),
                ...(options.headers || {})
            }
        });
        const body = await response.json().catch(() => null);
        if (!response.ok || body?.success !== true)
            throw new Error(body?.message || say('Request failed. Reload and retry.', 'অনুরোধ ব্যর্থ হয়েছে। আবার চেষ্টা করুন।'));
        return body.data;
    }
    function newId() {
        if (typeof crypto.randomUUID === 'function') return crypto.randomUUID();
        const bytes = new Uint8Array(16);
        crypto.getRandomValues(bytes);
        bytes[6] = (bytes[6] & 15) | 64;
        bytes[8] = (bytes[8] & 63) | 128;
        const h = [...bytes].map(b => b.toString(16).padStart(2, '0')).join('');
        return [h.slice(0, 8), h.slice(8, 12), h.slice(12, 16), h.slice(16, 20), h.slice(20)].join('-');
    }
    function selectMenu(id, items, placeholder) {
        const select = el(id), before = select.value;
        select.replaceChildren(new Option(placeholder, ''));
        for (const item of items) select.add(new Option(item.name + (item.code ? ' · ' + item.code : ''), String(item.id)));
        if ([...select.options].some(x => x.value === before)) select.value = before;
    }
    function guardianEnabled() {
        return el('runningAddGuardian').checked;
    }
    function toggleGuardian() {
        const enabled = guardianEnabled();
        for (const id of ['runningGuardianName', 'runningGuardianRelation', 'runningGuardianPhone', 'runningGuardianEmail'])
            el(id).disabled = !enabled || state.saving;
        el('runningGuardianName').required = enabled;
        el('runningGuardianRelation').required = enabled;
    }
    async function batches() {
        if (state.saving || !sameSession()) return;
        const seq = ++state.batchSeq, search = value('runningBatchSearch');
        const button = el('runningBatchSearchButton');
        button.disabled = true;
        selectMenu('runningBatch', [], say('Loading sections...', 'শাখা লোড হচ্ছে...'));
        // A late response from the previous batch must not repopulate the
        // curriculum after the batch search has reset its dependent selector.
        ++state.curriculumSeq;
        state.curricula = [];
        selectMenu('runningCurriculum', [], say('Choose a section first', 'আগে শাখা নির্বাচন করুন'));
        try {
            const params = new URLSearchParams({ take: '100' });
            if (search) params.set('search', search);
            const catalog = await api('/api/academic-setup/catalog?' + params);
            if (seq !== state.batchSeq || !sameSession()) return;
            state.batches = (catalog?.batches || []).filter(x => x.isActive);
            selectMenu('runningBatch', state.batches,
                say('Select class / section', 'শ্রেণি / শাখা নির্বাচন করুন'));
            if (!state.batches.length) alertMessage(
                say('No active section found. Check Academic Setup or try another search.',
                    'সক্রিয় শাখা পাওয়া যায়নি। Academic Setup দেখুন অথবা অন্যভাবে খুঁজুন।'), true);
        } catch (err) {
            if (seq === state.batchSeq && sameSession()) alertMessage(err.message, true);
        } finally {
            if (seq === state.batchSeq) button.disabled = state.saving;
        }
    }
    async function curricula() {
        const seq = ++state.curriculumSeq;
        state.curricula = [];
        selectMenu('runningCurriculum', [], say('Select applicable curriculum', 'প্রযোজ্য পাঠ্যক্রম'));
        const batch = currentBatch(), date = value('runningEnrollDate');
        el('runningCurriculum').disabled = true;
        if (!batch || !date || !sameSession()) return;
        el('runningCurriculumHelp').textContent = say('Checking curriculum...', 'পাঠ্যক্রম যাচাই চলছে...');
        try {
            const rows = await api('/api/academic-enrollments/batches/' + batch.id +
                '/curricula?enrollmentDate=' + encodeURIComponent(date));
            if (seq !== state.curriculumSeq || !sameSession()) return;
            state.curricula = Array.isArray(rows) ? rows : [];
            el('runningCurriculum').disabled = !state.curricula.length;
            selectMenu('runningCurriculum', state.curricula,
                say('Select applicable curriculum', 'প্রযোজ্য পাঠ্যক্রম'));
            // Most schools have one applicable curriculum. Auto-select only when unambiguous.
            if (state.curricula.length === 1) el('runningCurriculum').value = String(state.curricula[0].id);
            el('runningCurriculumHelp').textContent = state.curricula.length
                ? say('Automatically selected when only one eligible curriculum exists.',
                    'একটিমাত্র উপযুক্ত পাঠ্যক্রম থাকলে স্বয়ংক্রিয়ভাবে নির্বাচিত হবে।')
                : say('No eligible curriculum. Check academic setup before saving.',
                    'উপযুক্ত পাঠ্যক্রম নেই। আগে Academic Setup যাচাই করুন।');
        } catch (err) {
            if (seq === state.curriculumSeq && sameSession())
                el('runningCurriculumHelp').textContent = err.message;
        }
    }
    function chooseInvalidTab() {
        const invalid = form.querySelector(':invalid');
        if (!invalid) return false;
        const tab = invalid.closest('#runningGuardianFields') ? 'runningGuardianTab' : 'runningStudentTab';
        if (window.bootstrap?.Tab) bootstrap.Tab.getOrCreateInstance(el(tab)).show();
        invalid.focus();
        invalid.reportValidity?.();
        return true;
    }
    function payload() {
        return {
            fullName: value('runningStudentName'),
            fullNameBangla: value('runningStudentNameBn') || null,
            academicBatchId: currentBatch()?.id || 0,
            academicCurriculumId: currentCurriculum()?.id || 0,
            rollNo: value('runningRoll'),
            enrollmentDate: value('runningEnrollDate'),
            admissionDate: value('runningAdmissionDate'),
            dateOfBirth: value('runningDob') || null,
            gender: value('runningGender') || null,
            phone: value('runningPhone') || null,
            address: value('runningAddress') || null,
            guardian: guardianEnabled() ? {
                fullName: value('runningGuardianName'),
                relationCode: value('runningGuardianRelation'),
                phone: value('runningGuardianPhone') || null,
                email: value('runningGuardianEmail') || null,
                canReceiveSms: Boolean(value('runningGuardianPhone')),
                canReceiveEmail: Boolean(value('runningGuardianEmail'))
            } : null
        };
    }
    async function save(event) {
        event.preventDefault();
        if (state.saving || !sameSession()) return;
        clearMessage();
        if (!token) return alertMessage(say('Security token is missing. Reload before saving.',
            'নিরাপত্তা টোকেন পাওয়া যায়নি। সংরক্ষণের আগে পেজ রিলোড করুন।'), true);
        if (chooseInvalidTab()) return;
        // Do not submit while the selected batch's curriculum is still loading
        // or when an old selection survived a dependent-field refresh.
        if (el('runningBatchSearchButton').disabled || el('runningCurriculum').disabled)
            return alertMessage(say('Wait for academic options to load.', 'শিক্ষাক্রম লোড হওয়া পর্যন্ত অপেক্ষা করুন।'), true);
        const batch = currentBatch(), curriculum = currentCurriculum();
        if (!batch || !curriculum || batch.capacity <= 0)
            return alertMessage(say('Select an available section with an eligible curriculum.',
                'খালি আসনসহ উপযুক্ত শাখা ও পাঠ্যক্রম নির্বাচন করুন।'), true);
        if (value('runningAdmissionDate') > value('runningEnrollDate'))
            return alertMessage(say('Original admission date cannot be after the selected section enrollment date.',
                'মূল ভর্তির তারিখ বর্তমান শ্রেণির ভর্তির তারিখের পরে হতে পারে না।'), true);
        if (value('runningDob') && value('runningDob') > value('runningAdmissionDate'))
            return alertMessage(say('Date of birth cannot be later than the admission date.',
                'জন্মতারিখ ভর্তির তারিখের পরে হতে পারে না।'), true);
        const data = payload();
        const fingerprint = JSON.stringify(data);
        if (!state.requestId || state.fingerprint !== fingerprint) state.requestId = newId();
        state.fingerprint = fingerprint;
        data.clientRequestId = state.requestId;
        state.saving = true;
        el('runningSave').disabled = true;
        el('runningBatchSearchButton').disabled = true;
        el('runningAddGuardian').disabled = true;
        toggleGuardian();
        try {
            const created = await api('/api/students/running', { method: 'POST', body: JSON.stringify(data) });
            if (!sameSession()) return;
            form.classList.add('d-none');
            el('runningStudentSuccess').classList.remove('d-none');
            el('runningStudentSuccessText').textContent =
                (created?.fullName || data.fullName) + ' · ' + (created?.studentCode || '') +
                ' · ' + say('Roll', 'রোল') + ': ' + (created?.rollNo || data.rollNo);
            el('runningStudentSuccess').scrollIntoView({ block: 'nearest' });
            state.requestId = null;
            state.fingerprint = null;
        } catch (err) {
            if (sameSession()) alertMessage(err.message, true);
        } finally {
            state.saving = false;
            el('runningSave').disabled = false;
            el('runningBatchSearchButton').disabled = false;
            el('runningAddGuardian').disabled = false;
            toggleGuardian();
        }
    }
    function reset() {
        if (state.saving || !sameSession()) return;
        ++state.batchSeq;
        ++state.curriculumSeq;
        state.batches = [];
        state.curricula = [];
        selectMenu('runningBatch', [], say('Select class / section', 'শ্রেণি / শাখা নির্বাচন করুন'));
        selectMenu('runningCurriculum', [], say('Choose a section first', 'আগে শাখা নির্বাচন করুন'));
        el('runningCurriculum').disabled = true;
        form.reset();
        form.classList.remove('d-none');
        el('runningStudentSuccess').classList.add('d-none');
        clearMessage();
        state.requestId = null;
        state.fingerprint = null;
        toggleGuardian();
        bootstrap.Tab.getOrCreateInstance(el('runningStudentTab')).show();
        el('runningStudentName').focus();
        batches();
    }
    el('runningBatchSearchButton').addEventListener('click', batches);
    el('runningBatchSearch').addEventListener('keydown', event => {
        if (event.key === 'Enter') { event.preventDefault(); batches(); }
    });
    el('runningBatch').addEventListener('change', curricula);
    el('runningEnrollDate').addEventListener('change', curricula);
    el('runningAddGuardian').addEventListener('change', toggleGuardian);
    form.addEventListener('submit', save);
    el('runningNextStudent').addEventListener('click', reset);
    toggleGuardian();
    batches();
})();
