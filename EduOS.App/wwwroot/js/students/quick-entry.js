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
    const state = { campuses: [], years: [], levels: [], programs: [], sections: [],
        curricula: [], saving: false, setupLoading: false,
        setupSeq: 0, sectionSeq: 0, curriculumSeq: 0,
        requestId: null, fingerprint: null };
    const value = id => (el(id)?.value || '').trim();
    const currentCampus = () => state.campuses.find(x => x.id === Number(value('runningCampus')));
    const currentYear = () => state.years.find(x => x.id === Number(value('runningYear')));
    const currentLevel = () => state.levels.find(x => x.id === Number(value('runningLevel')));
    const currentSection = () => state.sections.find(x => x.id === Number(value('runningSection')));
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
    const selectSingle = id => {
        const dropdown = el(id);
        if (dropdown.options.length === 2) dropdown.selectedIndex = 1;
    };
    function filterYearAndLevel() {
        const campus = currentCampus();
        const years = campus ? state.years.filter(x =>
            x.isActive !== false && (x.campusId == null || x.campusId === campus.id)) : [];
        selectMenu('runningYear', years, say('Select academic year', 'শিক্ষাবর্ষ নির্বাচন করুন'));
        if (!years.some(x => String(x.id) === value('runningYear'))) el('runningYear').value = '';
        selectSingle('runningYear');
        const permitted = new Set(state.programs.filter(p =>
            !Array.isArray(p.campusIds) || !p.campusIds.length ||
            p.campusIds.includes(campus?.id)).map(p => p.id));
        const levels = campus ? state.levels.filter(x => x.isActive !== false &&
            permitted.has(x.academicProgramId)) : [];
        selectMenu('runningLevel', levels, say('Select class / grade', 'শ্রেণি নির্বাচন করুন'));
        if (!levels.some(x => String(x.id) === value('runningLevel'))) el('runningLevel').value = '';
        selectSingle('runningLevel');
    }
    async function setup() {
        if (state.saving || !sameSession()) return;
        const seq = ++state.setupSeq;
        state.setupLoading = true;
        ++state.sectionSeq;
        ++state.curriculumSeq;
        state.sections = [];
        state.curricula = [];
        el('runningSection').disabled = true;
        el('runningCurriculum').disabled = true;
        selectMenu('runningSection', [], say('Select campus and class first', 'ক্যাম্পাস ও শ্রেণি নির্বাচন করুন'));
        selectMenu('runningCurriculum', [], say('Choose a section first', 'আগে শাখা নির্বাচন করুন'));
        try {
            const [campuses, years, catalog] = await Promise.all([
                api('/api/institution-onboarding/campus-list'),
                api('/api/institution-onboarding/academic-years'),
                api('/api/academic-setup/catalog?take=100')
            ]);
            if (seq !== state.setupSeq || !sameSession()) return;
            state.campuses = (campuses || []).filter(x => x.isActive !== false);
            state.years = (years || []).filter(x => x.isActive !== false);
            state.programs = catalog?.programs || [];
            state.levels = catalog?.levels || [];
            selectMenu('runningCampus', state.campuses, say('Select campus', 'ক্যাম্পাস নির্বাচন করুন'));
            selectSingle('runningCampus');
            filterYearAndLevel();
            await sections();
            if (!state.campuses.length || !state.years.length || !state.levels.length)
                alertMessage(say('Complete campus, academic year and class setup before saving.',
                    'সংরক্ষণের আগে ক্যাম্পাস, শিক্ষাবর্ষ ও শ্রেণি সেটআপ করুন।'), true);
        } catch (err) {
            if (seq === state.setupSeq && sameSession()) alertMessage(err.message, true);
        } finally {
            if (seq === state.setupSeq) state.setupLoading = false;
        }
    }
    async function sections() {
        const seq = ++state.sectionSeq;
        ++state.curriculumSeq;
        state.sections = [];
        state.curricula = [];
        const select = el('runningSection');
        select.disabled = true;
        el('runningCurriculum').disabled = true;
        selectMenu('runningSection', [], say('Select academic section', 'শাখা নির্বাচন করুন'));
        selectMenu('runningCurriculum', [], say('Choose a section first', 'আগে শাখা নির্বাচন করুন'));
        const campus = currentCampus(), level = currentLevel();
        if (!campus || !level || !sameSession()) return;
        try {
            const query = new URLSearchParams({
                campusId: String(campus.id), academicLevelId: String(level.id),
                page: '1', pageSize: '100'
            });
            const result = await api('/api/academic-sections?' + query);
            if (seq !== state.sectionSeq || !sameSession() ||
                currentCampus()?.id !== campus.id || currentLevel()?.id !== level.id) return;
            state.sections = (result?.items || []).filter(x => x.isActive && x.capacity > 0);
            selectMenu('runningSection', state.sections,
                say('Select academic section', 'শাখা নির্বাচন করুন'));
            select.disabled = !state.sections.length;
            selectSingle('runningSection');
            if (state.sections.length === 1) await curricula();
            if (!state.sections.length)
                alertMessage(say('No active section for this class. Create one in Academic Sections.',
                    'এই শ্রেণিতে সক্রিয় শাখা নেই। Academic Sections-এ তৈরি করুন।'), true);
        } catch (error) {
            if (seq === state.sectionSeq && sameSession()) alertMessage(error.message, true);
        }
    }
    async function curricula() {
        const seq = ++state.curriculumSeq;
        state.curricula = [];
        selectMenu('runningCurriculum', [], say('Select applicable curriculum', 'প্রযোজ্য পাঠ্যক্রম'));
        const section = currentSection(), year = currentYear(), date = value('runningEnrollDate');
        el('runningCurriculum').disabled = true;
        if (!section || !year || !date || !sameSession()) return;
        el('runningCurriculumHelp').textContent = say('Checking curriculum...', 'পাঠ্যক্রম যাচাই চলছে...');
        try {
            const query = new URLSearchParams({ academicYearId: String(year.id), enrollmentDate: date });
            const rows = await api('/api/academic-enrollments/sections/' + section.id + '/curricula?' + query);
            if (seq !== state.curriculumSeq || !sameSession() ||
                currentSection()?.id !== section.id || currentYear()?.id !== year.id) return;
            state.curricula = Array.isArray(rows) ? rows.filter(x => x.isActive && x.isCurrent) : [];
            el('runningCurriculum').disabled = !state.curricula.length;
            selectMenu('runningCurriculum', state.curricula,
                say('Select applicable curriculum', 'প্রযোজ্য পাঠ্যক্রম'));
            selectSingle('runningCurriculum');
            el('runningCurriculumHelp').textContent = state.curricula.length
                ? say('Select the effective curriculum for this section and year.',
                    'এই শাখা ও শিক্ষাবর্ষের প্রযোজ্য পাঠ্যক্রম নির্বাচন করুন।')
                : say('No eligible curriculum. Complete academic setup before saving.',
                    'উপযুক্ত পাঠ্যক্রম নেই। আগে Academic Setup সম্পন্ন করুন।');
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
            academicBatchId: null,
            academicYearId: currentYear()?.id || null,
            academicSectionId: currentSection()?.id || 0,
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
        // Never submit while academic scopes are loading or a dependent selection is stale.
        if (state.setupLoading || el('runningSection').disabled || el('runningCurriculum').disabled)
            return alertMessage(say('Wait for academic options to load.', 'শিক্ষাক্রম লোড হওয়া পর্যন্ত অপেক্ষা করুন।'), true);
        const campus = currentCampus(), year = currentYear(), level = currentLevel();
        const section = currentSection(), curriculum = currentCurriculum();
        if (!campus || !year || !level || !section || !curriculum ||
            section.campusId !== campus.id || section.academicLevelId !== level.id)
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
             el('runningAddGuardian').disabled = false;
            toggleGuardian();
        }
    }
    function reset() {
        if (state.saving || !sameSession()) return;
        ++state.setupSeq;
        ++state.sectionSeq;
        ++state.curriculumSeq;
        state.campuses = []; state.years = []; state.levels = [];
        state.programs = []; state.sections = []; state.curricula = [];
        form.reset();
        el('runningSection').disabled = true;
        el('runningCurriculum').disabled = true;
        selectMenu('runningSection', [], say('Select campus and class first', 'ক্যাম্পাস ও শ্রেণি নির্বাচন করুন'));
        selectMenu('runningCurriculum', [], say('Choose a section first', 'আগে শাখা নির্বাচন করুন'));
        form.classList.remove('d-none');
        el('runningStudentSuccess').classList.add('d-none');
        clearMessage();
        state.requestId = null;
        state.fingerprint = null;
        toggleGuardian();
        bootstrap.Tab.getOrCreateInstance(el('runningStudentTab')).show();
        el('runningStudentName').focus();
        setup();
    }
    el('runningCampus').addEventListener('change', () => {
        filterYearAndLevel();
        sections().catch(error => alertMessage(error.message, true));
    });
    el('runningLevel').addEventListener('change', () => {
        sections().catch(error => alertMessage(error.message, true));
    });
    el('runningYear').addEventListener('change', () => {
        curricula().catch(error => alertMessage(error.message, true));
    });
    el('runningSection').addEventListener('change', curricula);
    el('runningEnrollDate').addEventListener('change', curricula);
    el('runningAddGuardian').addEventListener('change', toggleGuardian);
    form.addEventListener('submit', save);
    el('runningNextStudent').addEventListener('click', reset);
    toggleGuardian();
    setup();
})();
