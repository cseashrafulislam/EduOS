(() => {
    'use strict';
    const vm = window.eduosAcademicManagement;
    if (!vm) return;
    const { state, el, api, notice } = vm;
    let page = 1, totalPages = 0, visible = [], pending = false, requestId = null;
    const selectedBatch = () => state.catalog?.batches.find(x => x.id === Number(el('offeringBatch').value));
    const status = message => { el('offeringMessage').textContent = message; };
    const cell = (tr, value) => { const td = document.createElement('td'); td.textContent = String(value ?? ''); tr.append(td); return td; };
    const reset = () => {
        el('offeringForm').reset(); el('offeringReference').value = ''; el('offeringVersion').value = '';
        el('offeringSave').textContent = 'Create Offering'; requestId = null;
        el('offeringCode').disabled = false; el('offeringCurriculumSubject').disabled = false;
        el('offeringDelivery').disabled = false;
    };
    const chooseMapping = () => {
        const batch = selectedBatch(), select = el('offeringCurriculumSubject'), previous = select.value;
        select.replaceChildren(new Option('Select curriculum subject', ''));
        if (!batch) return;
        const curricula = state.catalog.curricula.filter(c =>
            c.isActive && c.academicProgramId === batch.academicProgramId &&
            (c.academicTrackId || null) === (batch.academicTrackId || null) &&
            (c.mediumId || null) === (batch.mediumId || null));
        const ids = new Set(curricula.map(c => c.id));
        state.catalog.curriculumSubjects.filter(x => x.isActive && x.academicLevelId === batch.academicLevelId &&
            ids.has(x.academicCurriculumId)).forEach(x => {
            const subject = state.catalog.subjects.find(s => s.id === x.subjectId);
            if (subject) select.add(new Option(subject.name + ' (' + subject.code + ') • Curriculum #' + x.academicCurriculumId, String(x.id)));
        });
        if ([...select.options].some(x => x.value === previous)) select.value = previous;
    };
    async function refresh() {
        const batch = selectedBatch();
        if (!batch) { visible = []; totalPages = 0; el('offeringRows').replaceChildren(); status('Select a batch.'); return; }
        const result = await api('/api/academic-setup/batches/' + batch.id + '/offerings?page=' + page +
            '&pageSize=20&search=' + encodeURIComponent(el('offeringSearch').value.trim()));
        visible = result.items || []; totalPages = result.totalPages || 0;
        const body = el('offeringRows'); body.replaceChildren();
        for (const row of visible) {
            const tr = document.createElement('tr');
            cell(tr, row.code); cell(tr, row.subjectName + ' (' + row.subjectCode + ')');
            cell(tr, row.capacity); cell(tr, ['','On campus','Online live','Self-paced','Hybrid'][row.deliveryMode] || row.deliveryMode);
            cell(tr, row.isActive ? 'Active' : 'Inactive');
            const action = cell(tr, '');
            const edit = document.createElement('button'); edit.type = 'button';
            edit.className = 'btn btn-sm btn-outline-primary'; edit.textContent = 'Edit';
            edit.addEventListener('click', () => {
                reset(); el('offeringReference').value = row.reference; el('offeringVersion').value = row.rowVersion;
                el('offeringCurriculumSubject').value = String(row.curriculumSubjectId);
                el('offeringCode').value = row.code; el('offeringCapacity').value = row.capacity;
                el('offeringDelivery').value = row.deliveryMode; el('offeringActive').checked = row.isActive;
                el('offeringCode').disabled = true; el('offeringCurriculumSubject').disabled = true;
                el('offeringDelivery').disabled = true; el('offeringSave').textContent = 'Update Offering';
                el('offeringForm').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            });
            action.append(edit); body.append(tr);
        }
        if (!visible.length) { const tr = document.createElement('tr'); const td = cell(tr, 'No offerings found.'); td.colSpan = 6; body.append(tr); }
        el('offeringPrevious').disabled = page <= 1;
        el('offeringNext').disabled = page >= totalPages;
        el('offeringPage').textContent = 'Page ' + page + ' of ' + Math.max(1, totalPages) + ' • ' + result.totalCount + ' offerings';
        status('Results are tenant-scoped. Exports include only the visible page.');
    }
    const safeRefresh = async () => { try { await refresh(); } catch (e) { notice(e.message, true); } };
    window.addEventListener('eduos:academic-reloaded', () => {
        const previous = el('offeringBatch').value;
        const select = el('offeringBatch'); select.replaceChildren(new Option('Select batch', ''));
        (state.catalog?.batches || []).forEach(x => select.add(new Option(x.name + ' (' + x.code + ')', String(x.id))));
        if ([...select.options].some(x => x.value === previous)) select.value = previous;
        else if (state.catalog?.batches?.length === 1) select.value = String(state.catalog.batches[0].id);
        chooseMapping(); page = 1; safeRefresh();
    });
    el('offeringBatch').addEventListener('change', () => { reset(); chooseMapping(); page = 1; safeRefresh(); });
    el('offeringSearchButton').addEventListener('click', () => { page = 1; safeRefresh(); });
    el('offeringSearch').addEventListener('keydown', event => {
        if (event.key === 'Enter') { event.preventDefault(); page = 1; safeRefresh(); }
    });
    el('offeringPrevious').addEventListener('click', () => { if (page > 1) { page--; safeRefresh(); } });
    el('offeringNext').addEventListener('click', () => { if (page < totalPages) { page++; safeRefresh(); } });
    el('offeringCancel').addEventListener('click', reset);
    el('offeringForm').addEventListener('submit', async event => {
        event.preventDefault();
        if (pending) return;
        const batch = selectedBatch(), reference = el('offeringReference').value;
        if (!batch) { notice('Select an academic batch.', true); return; }
        const capacity = Number(el('offeringCapacity').value);
        if (!Number.isInteger(capacity) || capacity < 1 || capacity > batch.capacity) {
            notice('Offering capacity must be between 1 and batch capacity (' + batch.capacity + ').', true); return;
        }
        if (!reference && !el('offeringCurriculumSubject').value) { notice('Select a curriculum subject.', true); return; }
        if (!requestId) requestId = crypto.randomUUID();
        const body = reference ? {
            capacity, isActive: el('offeringActive').checked, rowVersion: el('offeringVersion').value
        } : {
            clientRequestId: requestId, academicBatchId: batch.id,
            curriculumSubjectId: Number(el('offeringCurriculumSubject').value),
            academicYearId: batch.academicYearId, academicTermId: batch.academicTermId || null,
            code: el('offeringCode').value.trim().toUpperCase(), capacity,
            deliveryMode: Number(el('offeringDelivery').value), isActive: el('offeringActive').checked
        };
        pending = true; el('offeringSave').disabled = true;
        try {
            await api(reference ? '/api/academic-setup/offerings/' + encodeURIComponent(reference) :
                '/api/academic-setup/offerings', { method: reference ? 'PUT' : 'POST', body: JSON.stringify(body) });
            reset(); page = 1; await refresh(); notice('Subject offering saved.');
        } catch (e) { notice(e.message, true); }
        finally { pending = false; el('offeringSave').disabled = false; }
    });
    el('offeringCsv').addEventListener('click', () => {
        if (!visible.length) return;
        const escape = value => '"' + String(value ?? '').replace(/"/g, '""') + '"';
        const rows = [['Code','Subject','Capacity','Mode','Active'], ...visible.map(x =>
            [x.code,x.subjectName,x.capacity,x.deliveryMode,x.isActive])];
        const csv = '\uFEFF' + rows.map(x => x.map(escape).join(',')).join('\r\n');
        const link = document.createElement('a'), url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
        link.href = url; link.download = 'subject-offerings-page-' + page + '.csv'; link.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    });
})();
