(() => {
    'use strict';
    const vm = window.eduosAcademicManagement;
    if (!vm) return;
    const { state, el, api, notice, sameSession, guard, mayReplace } = vm;
    const formGuard = guard('offeringForm');
    let page = 1, totalPages = 0, visible = [], pending = false, requestId = null;
    let requestFingerprint = null, activeBatch = el('offeringBatch').value, readGeneration = 0;
    let sections = [], sectionRequest = 0;
    const selectedSection = () => sections.find(x => String(x.id) === el('offeringSection').value);
    async function loadSections() {
        const batch = selectedBatch(), seq = ++sectionRequest, select = el('offeringSection');
        const previous = select.value;
        if (!batch) { select.replaceChildren(new Option('Select batch first', '')); return; }
        const result = await api('/api/academic-sections?academicBatchId=' + batch.id +
            '&pageSize=100&page=1');
        if (!sameSession() || seq !== sectionRequest || selectedBatch()?.id !== batch.id) return;
        sections = (result.items || []).filter(x => x.isActive);
        select.replaceChildren(new Option('Select academic section', ''));
        sections.forEach(x => select.add(new Option(x.name + ' (' + x.code + ')', String(x.id))));
        if (sections.some(x => String(x.id) === previous)) select.value = previous;
        else if (sections.length === 1) select.value = String(sections[0].id);
        select.disabled = !!el('offeringReference').value;
        if (!formGuard?.isDirty()) formGuard?.markClean();
    }
    const selectedBatch = () => state.catalog?.batches.find(x => x.id === Number(el('offeringBatch').value));
    const status = message => { el('offeringMessage').textContent = message; };
    const cell = (tr, value) => { const td = document.createElement('td'); td.textContent = String(value ?? ''); tr.append(td); return td; };
    const reset = (force = false) => {
        if (!force && (pending || !mayReplace('offeringForm'))) return false;
        el('offeringForm').reset(); el('offeringReference').value = ''; el('offeringVersion').value = '';
        el('offeringSave').textContent = 'Create Offering'; requestId = null; requestFingerprint = null;
        el('offeringCode').disabled = false; el('offeringCurriculumSubject').disabled = false;
        el('offeringDelivery').disabled = false;
        el('offeringSection').disabled = false;
        formGuard?.markClean();
        return true;
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
        const generation = ++readGeneration;
        const batch = selectedBatch();
        if (!batch) { visible = []; totalPages = 0; el('offeringRows').replaceChildren(); status('Select a batch.'); return; }
        const result = await api('/api/academic-setup/batches/' + batch.id + '/offerings?page=' + page +
            '&pageSize=20&search=' + encodeURIComponent(el('offeringSearch').value.trim()) +
            (el('offeringSection').value ? '&academicSectionId=' + encodeURIComponent(el('offeringSection').value) : ''));
        if (generation !== readGeneration || !sameSession() || selectedBatch()?.id !== batch.id) return;
        visible = result.items || []; totalPages = result.totalPages || 0;
        const body = el('offeringRows'); body.replaceChildren();
        for (const row of visible) {
            const tr = document.createElement('tr');
            cell(tr, row.code); cell(tr, sections.find(x => x.id === row.academicSectionId)?.name ||
                (row.academicSectionId ? '#' + row.academicSectionId : 'Batch-wide'));
            cell(tr, row.subjectName + ' (' + row.subjectCode + ')');
            cell(tr, row.capacity);
            cell(tr, row.pendingSeats); cell(tr, row.allocatedSeats); cell(tr, row.reservedSeats);
            const available = cell(tr, row.seatsRemaining);
            if (row.isOverCapacity) {
                available.classList.add('text-danger', 'fw-bold');
                available.title = 'Existing reservations exceed configured capacity; investigate before allocating seats.';
            } else if (!row.seatsRemaining) {
                available.classList.add('text-warning', 'fw-bold');
                available.title = 'No seats available.';
            }
            cell(tr, ['','On campus','Online live','Self-paced','Hybrid'][row.deliveryMode] || row.deliveryMode);
            cell(tr, row.isActive ? 'Active' : 'Inactive');
            const action = cell(tr, '');
            const edit = document.createElement('button'); edit.type = 'button';
            edit.className = 'btn btn-sm btn-outline-primary'; edit.textContent = 'Edit';
            edit.addEventListener('click', () => {
                if (!sameSession() || !reset()) return;
                el('offeringReference').value = row.reference; el('offeringVersion').value = row.rowVersion;
                el('offeringCurriculumSubject').value = String(row.curriculumSubjectId);
                el('offeringCode').value = row.code; el('offeringCapacity').value = row.capacity;
                el('offeringDelivery').value = row.deliveryMode; el('offeringActive').checked = row.isActive;
                el('offeringCode').disabled = true; el('offeringCurriculumSubject').disabled = true;
                if (row.academicSectionId) el('offeringSection').value = String(row.academicSectionId);
                el('offeringSection').disabled = true;
                el('offeringDelivery').disabled = true; el('offeringSave').textContent = 'Update Offering';
                formGuard?.markClean();
                el('offeringForm').scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            });
            action.append(edit); body.append(tr);
        }
        if (!visible.length) { const tr = document.createElement('tr'); const td = cell(tr, 'No offerings found.'); td.colSpan = 11; body.append(tr); }
        el('offeringPrevious').disabled = page <= 1;
        el('offeringNext').disabled = page >= totalPages;
        el('offeringPage').textContent = 'Page ' + page + ' of ' + Math.max(1, totalPages) + ' • ' + result.totalCount + ' offerings';
        status('Seat counts include pending, approved and completed registrations. Rejected/dropped seats are released. CSV exports include only the visible page.');
    }
    const safeRefresh = async () => { try { await refresh(); } catch (e) { notice(e.message, true); } };
    window.addEventListener('eduos:academic-reloaded', () => {
        if (!sameSession()) return;
        const clean = !formGuard?.isDirty();
        const previous = el('offeringBatch').value;
        const select = el('offeringBatch'); select.replaceChildren(new Option('Select batch', ''));
        (state.catalog?.batches || []).forEach(x => select.add(new Option(x.name + ' (' + x.code + ')', String(x.id))));
        if ([...select.options].some(x => x.value === previous)) select.value = previous;
        else if (state.catalog?.batches?.length === 1) select.value = String(state.catalog.batches[0].id);
        activeBatch = select.value;
        chooseMapping(); loadSections().then(() => { page = 1; safeRefresh(); }).catch(e => notice(e.message, true));
        if (clean) formGuard?.markClean();
    });
    el('offeringBatch').addEventListener('change', () => {
        if (!sameSession() || !reset()) { el('offeringBatch').value = activeBatch; return; }
        activeBatch = el('offeringBatch').value;
        chooseMapping(); formGuard?.markClean(); page = 1;
        loadSections().then(safeRefresh).catch(e => notice(e.message, true));
    });
    el('offeringSection').addEventListener('change', () => {
        if (!sameSession()) return;
        const sectionId = el('offeringSection').value;
        if (!reset()) return;
        el('offeringSection').value = sectionId;
        page = 1; safeRefresh();
    });
    el('offeringSearchButton').addEventListener('click', () => { page = 1; safeRefresh(); });
    el('offeringSearch').addEventListener('keydown', event => {
        if (event.key === 'Enter') { event.preventDefault(); page = 1; safeRefresh(); }
    });
    el('offeringPrevious').addEventListener('click', () => { if (page > 1) { page--; safeRefresh(); } });
    el('offeringNext').addEventListener('click', () => { if (page < totalPages) { page++; safeRefresh(); } });
    el('offeringCancel').addEventListener('click', () => reset());
    el('offeringForm').addEventListener('submit', async event => {
        event.preventDefault();
        if (pending || !sameSession() || !event.currentTarget.reportValidity()) return;
        const batch = selectedBatch(), reference = el('offeringReference').value;
        if (!batch) { notice('Select an academic batch.', true); return; }
        const section = selectedSection();
        if (!reference && !section) { notice('Select an active section before offering subjects.', true); return; }
        const capacity = Number(el('offeringCapacity').value);
        if (!Number.isInteger(capacity) || capacity < 1 || capacity > Math.min(batch.capacity, section?.capacity || batch.capacity)) {
            notice('Offering capacity must fit the academic section and batch.', true); return;
        }
        if (!reference && !el('offeringCurriculumSubject').value) { notice('Select a curriculum subject.', true); return; }
        const body = reference ? {
            capacity, isActive: el('offeringActive').checked, rowVersion: el('offeringVersion').value
        } : {
            academicBatchId: batch.id, academicSectionId: section.id,
            curriculumSubjectId: Number(el('offeringCurriculumSubject').value),
            academicYearId: batch.academicYearId, academicTermId: batch.academicTermId || null,
            code: el('offeringCode').value.trim().toUpperCase(), capacity,
            deliveryMode: Number(el('offeringDelivery').value), isActive: el('offeringActive').checked
        };
        if (!reference) {
            const fingerprint = JSON.stringify(body);
            if (fingerprint !== requestFingerprint) { requestId = null; requestFingerprint = fingerprint; }
            if (!requestId) requestId = crypto.randomUUID();
            body.clientRequestId = requestId;
        }
        pending = true; el('offeringSave').disabled = true; event.currentTarget.inert = true;
        let committed = false;
        try {
            await api(reference ? '/api/academic-setup/offerings/' + encodeURIComponent(reference) :
                '/api/academic-setup/offerings', { method: reference ? 'PUT' : 'POST', body: JSON.stringify(body) });
            committed = true;
            if (!sameSession()) return;
            reset(true); page = 1;
            try { await refresh(); notice('Subject offering saved.'); }
            catch { notice('Offering saved, but the list did not refresh. Reload before changing it again.', true); }
        } catch (e) {
            if (sameSession()) notice(committed ? 'Offering saved, but the display failed.' : e.message, true);
        } finally { pending = false; el('offeringSave').disabled = false; event.currentTarget.inert = false; }
    });
    el('offeringCsv').addEventListener('click', () => {
        if (!visible.length) return;
        const escape = value => {
            const text = String(value ?? '');
            // Prevent spreadsheet formula execution from tenant-authored subject codes/names.
            const safe = /^[\s]*[=+@-]/.test(text) ? "'" + text : text;
            return '"' + safe.replace(/"/g, '""') + '"';
        };
        const rows = [['Code','Section','Subject','Capacity','Pending','Allocated','Reserved','Available','Over Capacity','Mode','Active'], ...visible.map(x =>
            [x.code,sections.find(s => s.id === x.academicSectionId)?.name || 'Batch-wide',
                x.subjectName,x.capacity,x.pendingSeats,x.allocatedSeats,x.reservedSeats,
                x.seatsRemaining,x.isOverCapacity,x.deliveryMode,x.isActive])];
        const csv = '\uFEFF' + rows.map(x => x.map(escape).join(',')).join('\r\n');
        const link = document.createElement('a'), url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
        link.href = url; link.download = 'subject-offerings-page-' + page + '.csv'; link.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    });
})();
