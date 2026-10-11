(() => {
    'use strict';
    const vm = window.eduosAcademicManagement;
    if (!vm) return;
    const { state, el, field, idOf, find, findYear, notice, api, reload, checked } = vm;
    async function submit(event, operation, successText) {
        event.preventDefault();
        const form = event.currentTarget, button = form.querySelector('button[type="submit"]');
        button.disabled = true;
        try {
            await operation();
            form.reset();
            delete form.dataset.requestId;
            await reload();
            notice(successText);
        } catch (error) { notice(error.message, true); }
        finally { button.disabled = false; }
    }
    function requestId(form) {
        if (!form.dataset.requestId) form.dataset.requestId = crypto.randomUUID();
        return form.dataset.requestId;
    }
    function validDates(start, end) {
        if (!start || !end || start >= end) throw new Error('End date must be after start date.');
    }
    function reset(kind) {
        el(kind + 'Form').reset();
        el(kind + 'Id').value = '';
        el(kind + 'Version').value = '';
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
        reset('year');
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
        reset('term');
    }, 'Academic term saved.'));
    for (const kind of ['year', 'term'])
        document.querySelector('button[data-reset="' + kind + '"]')
            .addEventListener('click', () => reset(kind));

    el('programForm').addEventListener('submit', event => submit(event, async () => {
        const campusId = idOf('programCampus');
        await api('/api/academic-setup/programmes', { method: 'POST', body: JSON.stringify({
            name: field('programName'), code: field('programCode').toUpperCase(),
            durationInMonths: Number(field('programDuration')) || 0,
            campusIds: campusId ? [campusId] : [],
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
