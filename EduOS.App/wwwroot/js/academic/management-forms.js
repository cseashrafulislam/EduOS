(() => {
    'use strict';
    const vm = window.eduosAcademicManagement;
    if (!vm) return;
    const { state, el, field, idOf, find, findYear, notice, api, reload, checked,
        sameSession, guard, mayReplace } = vm;
    const pending = new WeakSet();
    async function submit(event, operation, successText) {
        event.preventDefault();
        const form = event.currentTarget, button = form.querySelector('button[type="submit"]');
        if (pending.has(form) || !sameSession() || !form.reportValidity()) return;
        pending.add(form); button.disabled = true; form.inert = true;
        let committed = false;
        try {
            await operation();
            committed = true;
            if (!sameSession()) return;
            form.reset();
            if (form.id === 'yearForm' || form.id === 'termForm') {
                const kind = form.id.slice(0, -4);
                el(kind + 'Id').value = '';
                el(kind + 'Version').value = '';
            }
            delete form.dataset.requestId;
            delete form.dataset.requestFingerprint;
            guard(form.id)?.markClean();
            try { await reload(); notice(successText); }
            catch { notice('Saved successfully, but the academic directory could not refresh. Reload before making another change.', true); }
        } catch (error) {
            if (sameSession()) notice(committed ? 'Saved successfully, but the display could not refresh.' : error.message, true);
        } finally { pending.delete(form); button.disabled = false; form.inert = false; }
    }
    function requestId(form) {
        const fingerprint = window.EduOSFormGuard?.fingerprint(form) ||
            JSON.stringify([...form.elements].map(x => [x.name || x.id, x.value, x.checked]));
        if (form.dataset.requestFingerprint !== fingerprint) {
            delete form.dataset.requestId;
            form.dataset.requestFingerprint = fingerprint;
        }
        if (!form.dataset.requestId) {
            if (typeof crypto.randomUUID === 'function') form.dataset.requestId = crypto.randomUUID();
            else {
                // getRandomValues is available in HTTP development contexts too.
                // Never downgrade idempotency tokens to Math.random() or timestamps.
                const bytes = new Uint8Array(16);
                crypto.getRandomValues(bytes);
                bytes[6] = (bytes[6] & 15) | 64;
                bytes[8] = (bytes[8] & 63) | 128;
                const hex = [...bytes].map(x => x.toString(16).padStart(2, '0')).join('');
                form.dataset.requestId = [
                    hex.slice(0, 8), hex.slice(8, 12), hex.slice(12, 16),
                    hex.slice(16, 20), hex.slice(20)
                ].join('-');
            }
        }
        return form.dataset.requestId;
    }
    function validDates(start, end) {
        if (!start || !end || start >= end) throw new Error('End date must be after start date.');
    }
    function reset(kind) {
        const form = el(kind + 'Form');
        if (!mayReplace(form.id)) return;
        form.reset();
        el(kind + 'Id').value = '';
        el(kind + 'Version').value = '';
        delete form.dataset.requestId;
        delete form.dataset.requestFingerprint;
        guard(form.id)?.markClean();
    }
    el('yearForm').addEventListener('submit', async event => submit(event, async () => {
        const startDate = field('yearStart'), endDate = field('yearEnd');
        validDates(startDate, endDate);
        await api('/api/institution-onboarding/academic-year', { method: 'POST',
            body: JSON.stringify({
                id: idOf('yearId'), rowVersion: field('yearVersion') || null,
                name: field('yearName'), code: field('yearCode').toUpperCase(),
                campusId: idOf('yearCampus'), cycleType: Number(field('yearCycle')),
                startDate, endDate, isCurrent: checked('yearCurrent'), isActive: true
            })
        });
    }, 'Academic year saved.'));
    el('termForm').addEventListener('submit', async event => submit(event, async () => {
        const startDate = field('termStart'), endDate = field('termEnd');
        validDates(startDate, endDate);
        const year = findYear(idOf('termYear'));
        if (!year || startDate < year.startDate || endDate > year.endDate)
            throw new Error('Term dates must be within the selected academic year.');
        await api('/api/institution-onboarding/academic-term', { method: 'POST',
            body: JSON.stringify({
                id: idOf('termId'), rowVersion: field('termVersion') || null,
                academicYearId: year.id, name: field('termName'),
                code: field('termCode') || null, startDate, endDate,
                isCurrent: checked('termCurrent'), isActive: true, displayOrder: 0
            })
        });
    }, 'Academic term saved.'));
    for (const kind of ['year', 'term'])
        document.querySelector('button[data-reset="' + kind + '"]')
            .addEventListener('click', () => reset(kind));

    // Include the other six editors in the shared unsaved-navigation guard.
    // Reuse the same guard instance on resets and successful submissions.
    for (const id of ['yearForm', 'termForm', 'programForm', 'levelForm', 'sectionForm',
        'subjectForm', 'curriculumForm', 'curriculumSubjectForm']) guard(id);

    el('programForm').addEventListener('submit', event => submit(event, async () => {
        const campusIds = [...el('programCampus').selectedOptions]
            .map(x => Number(x.value)).filter(x => x > 0);
        await api('/api/academic-setup/programmes', { method: 'POST', body: JSON.stringify({
            name: field('programName'), code: field('programCode').toUpperCase(),
            durationInMonths: Number(field('programDuration')) || 0,
            academicDepartmentId: idOf('programDepartment'), campusIds,
            isAdmissionOpen: true, isActive: true, displayOrder: 0
        }) });
    }, 'Programme created.'));
    el('levelForm').addEventListener('submit', event => submit(event, async () => {
        await api('/api/academic-setup/levels', { method: 'POST', body: JSON.stringify({
            academicProgramId: idOf('levelProgram'),
            name: field('levelName'), code: field('levelCode').toUpperCase(),
            levelNo: Number(field('levelNumber')),
            isPromotable: true, isTerminalLevel: false, isActive: true, displayOrder: 0
        }) });
    }, 'Class / grade created.'));
    el('sectionForm').addEventListener('submit', event => submit(event, async () => {
        const form = event.currentTarget;
        const year = findYear(idOf('sectionYear')), term = state.terms.find(x => x.id === idOf('sectionTerm'));
        const program = find('programs', idOf('sectionProgram'));
        const classLevel = find('levels', idOf('sectionLevel'));
        const campusId = idOf('sectionCampus');
        if (!year || !classLevel || !program ||
            classLevel.academicProgramId !== program.id)
            throw new Error('Select a valid academic year, programme and class.');
        if (program.campusIds?.length && !program.campusIds.includes(campusId))
            throw new Error('Programme is unavailable at the chosen campus.');
        const startDate = field('sectionStart'), endDate = field('sectionEnd');
        validDates(startDate, endDate);
        if (startDate < year.startDate || endDate > year.endDate ||
            (term && (startDate < term.startDate || endDate > term.endDate)))
            throw new Error('Section dates must stay within the selected year / term.');
        await api('/api/academic-setup/batches', { method: 'POST', body: JSON.stringify({
            clientRequestId: requestId(form), campusId, academicYearId: year.id,
            academicTermId: term?.id || null, academicProgramId: program.id,
            academicLevelId: classLevel.id, name: field('sectionName'),
            code: field('sectionCode').toUpperCase(),
            capacity: Number(field('sectionCapacity')) || 0,
            startDate, endDate, deliveryMode: 1,
            isDefault: false, isActive: true, displayOrder: 0
        }) });
    }, 'Section created.'));
    el('subjectForm').addEventListener('submit', event => submit(event, async () => {
        await api('/api/academic-setup/subjects', { method: 'POST', body: JSON.stringify({
            name: field('subjectName'), code: field('subjectCode').toUpperCase(),
            defaultCreditHours: Number(field('subjectCredits')) || 0, isActive: true
        }) });
    }, 'Subject created.'));
    el('curriculumForm').addEventListener('submit', event => submit(event, async () => {
        await api('/api/academic-setup/curricula', { method: 'POST', body: JSON.stringify({
            clientRequestId: requestId(event.currentTarget),
            academicProgramId: idOf('curriculumProgram'),
            name: field('curriculumName'), code: field('curriculumCode').toUpperCase(),
            versionNo: Number(field('curriculumVersion')),
            effectiveFrom: field('curriculumFrom'),
            isCurrent: checked('curriculumCurrent'), isActive: true, subjects: []
        }) });
    }, 'Curriculum version created; add subjects using the form below.'));
    el('curriculumSubjectForm').addEventListener('submit', event => submit(event, async () => {
        const curriculum = find('curricula', idOf('curriculumSelection'));
        const level = find('levels', idOf('mappingLevel'));
        const subject = find('subjects', idOf('mappingSubject'));
        const fullMarks = Number(field('mappingMarks'));
        const passMarks = Number(field('mappingPassMarks'));
        if (!curriculum || !level || !subject || level.academicProgramId !== curriculum.academicProgramId)
            throw new Error('Curriculum, class or subject does not match.');
        if (fullMarks <= 0 || passMarks < 0 || passMarks > fullMarks)
            throw new Error('Pass marks must be between zero and full marks.');
        await api('/api/academic-setup/curricula/' + curriculum.id + '/subjects', { method: 'POST',
            body: JSON.stringify({
                academicLevelId: level.id, subjectId: subject.id,
                fullMarks, passMarks, creditHours: subject.defaultCreditHours,
                isOptional: false, hasPractical: false, isActive: true, displayOrder: 0
            })
        });
    }, 'Curriculum subject mapped.'));
    el('sectionYear').addEventListener('change', updateSectionDates);
    el('sectionTerm').addEventListener('change', updateSectionDates);
    function updateSectionDates() {
        const term = state.terms.find(x => x.id === idOf('sectionTerm'));
        const year = findYear(idOf('sectionYear'));
        if (term || year) {
            el('sectionStart').value = (term || year).startDate;
            el('sectionEnd').value = (term || year).endDate;
        }
    }
})();
