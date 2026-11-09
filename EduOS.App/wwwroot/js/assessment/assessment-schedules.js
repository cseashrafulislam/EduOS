(() => {
    'use strict';
    const root = document.getElementById('assessmentScheduleWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const form = el('scheduleForm');
    const formGuard = window.EduOSFormGuard?.create(form);
    const state = { rows: [], scopes: [], page: 1, pageSize: 20, total: 0, editId: null, rowVersion: null,
        busy: false, editorLoading: false, roomReady: false, roomGeneration: 0, editGeneration: 0, listGeneration: 0 };
    let lastSubject = '';
    const cell = value => { const td = document.createElement('td'); td.textContent = value == null ? '' : String(value); return td; };
    function notice(message, error = false) {
        const box = el('scheduleNotice'); box.className = 'alert ' + (error ? 'alert-danger' : 'alert-success');
        box.textContent = message;
    }
    async function api(path, method = 'GET', body) {
        const response = await fetch(path, {
            method, credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json',
                ...(body === undefined ? {} : { 'Content-Type': 'application/json' }),
                ...(method === 'GET' ? {} : { RequestVerificationToken: token }) },
            ...(body === undefined ? {} : { body: JSON.stringify(body) })
        });
        const result = await response.json().catch(() => null);
        if (!response.ok || result?.success !== true) throw new Error(result?.message || 'Request failed.');
        return result.data;
    }
    const selected = () => state.scopes.find(x => String(x.assessmentSubjectId) === el('scheduleSubject').value);
    function updateSave() {
        el('scheduleSave').disabled = state.busy || state.editorLoading || !state.roomReady;
    }
    async function rooms() {
        const generation = ++state.roomGeneration;
        const subject = selected();
        const room = el('scheduleRoom');
        state.roomReady = false; room.disabled = true;
        room.replaceChildren(new Option('No room assigned', '')); updateSave();
        if (!subject) return false;
        let data;
        try {
            data = await api('/api/assessment-administration/schedules/rooms?assessmentSubjectId=' + encodeURIComponent(subject.assessmentSubjectId));
        } catch (error) {
            if (generation !== state.roomGeneration || selected()?.assessmentSubjectId !== subject.assessmentSubjectId)
                return false;
            throw error;
        }
        if (generation !== state.roomGeneration || selected()?.assessmentSubjectId !== subject.assessmentSubjectId) return false;
        for (const x of data || []) room.add(new Option(x.name + ' (' + x.code + ')', String(x.id)));
        state.roomReady = true; room.disabled = state.busy || state.editorLoading;
        updateSave();
        return true;
    }
    async function scopes() {
        const data = await api('/api/assessment-administration/scopes');
        state.scopes = data || [];
        const select = el('scheduleSubject'); select.replaceChildren(new Option('Choose assessment / batch / subject', ''));
        for (const x of state.scopes) {
            const label = x.assessmentName + ' / ' + x.academicBatchName + ' / ' + x.subjectName;
            select.add(new Option(label, String(x.assessmentSubjectId)));
        }
        if (!state.scopes.length) notice('No assessment subjects available. Configure assessment subjects first.', true);
    }
    function render() {
        const body = el('scheduleRows'); body.replaceChildren();
        for (const row of state.rows) {
            const tr = document.createElement('tr');
            const time = row.startTime && row.endTime ? row.startTime.slice(0, 5) + '–' + row.endTime.slice(0, 5) : 'Unspecified';
            tr.append(cell(row.assessmentDate), cell(time), cell(row.assessmentName),
                cell(row.batchName), cell(row.subjectName), cell(row.roomName || '—'), cell(row.instructions || ''));
            const action = document.createElement('td'); action.className = 'schedule-action';
            const button = document.createElement('button'); button.type = 'button';
            button.className = 'btn btn-sm btn-outline-primary'; button.textContent = 'Edit';
            button.disabled = state.busy || ![1, 2, 'Draft', 'Scheduled'].includes(row.state);
            button.addEventListener('click', () => edit(row));
            action.append(button); tr.append(action); body.append(tr);
        }
        if (!state.rows.length) { const tr = document.createElement('tr'), td = cell('No schedules found.'); td.colSpan = 8; tr.append(td); body.append(tr); }
        el('scheduleCount').textContent = state.total + ' schedule(s) · Page ' + state.page;
        el('schedulePrev').disabled = state.page <= 1;
        el('scheduleNext').disabled = state.page * state.pageSize >= state.total;
    }
    async function load() {
        const generation = ++state.listGeneration;
        const query = new URLSearchParams({ page: String(state.page), pageSize: String(state.pageSize) });
        const subject = selected();
        if (subject) query.set('assessmentSubjectId', String(subject.assessmentSubjectId));
        if (el('scheduleFrom').value) query.set('fromDate', el('scheduleFrom').value);
        if (el('scheduleTo').value) query.set('toDate', el('scheduleTo').value);
        try {
            const data = await api('/api/assessment-administration/schedules?' + query);
            if (generation !== state.listGeneration) return;
            state.rows = data.items || []; state.total = data.totalCount || 0; render();
            return true;
        } catch (error) { if (generation === state.listGeneration) notice(error.message, true); }
        return false;
    }
    async function edit(row) {
        if (state.busy || (formGuard && !formGuard.confirmDiscard())) return;
        if (!state.scopes.some(x => x.assessmentSubjectId === row.assessmentSubjectId)) {
            notice('This subject is not in the available assessment scopes.', true); return;
        }
        const generation = ++state.editGeneration;
        state.editorLoading = true;
        form.querySelectorAll('input, select').forEach(field => { field.disabled = true; });
        state.editId = null; state.rowVersion = null; updateSave();
        el('scheduleSubject').value = String(row.assessmentSubjectId);
        try {
            if (!await rooms() || generation !== state.editGeneration) return;
            if (row.roomId != null && ![...el('scheduleRoom').options].some(x => x.value === String(row.roomId)))
                throw new Error('The saved room is not available in this assessment scope. Edit cancelled to protect the booking.');
        } catch (error) {
            if (generation === state.editGeneration) { clear(true); notice(error.message, true); }
            return;
        }
        if (generation !== state.editGeneration) return;
        state.editId = row.id; state.rowVersion = row.rowVersion;
        state.editorLoading = false;
        form.querySelectorAll('input, select').forEach(field => { field.disabled = false; });
        el('scheduleSubject').disabled = true;
        el('scheduleDate').value = row.assessmentDate;
        el('scheduleStart').value = row.startTime?.slice(0, 5) || '';
        el('scheduleEnd').value = row.endTime?.slice(0, 5) || '';
        el('scheduleRoom').value = row.roomId == null ? '' : String(row.roomId);
        el('scheduleInstructions').value = row.instructions || '';
        el('scheduleFormTitle').textContent = 'Edit schedule #' + row.id;
        el('scheduleSave').textContent = 'Update schedule';
        lastSubject = el('scheduleSubject').value;
        formGuard?.markClean(); updateSave();
        form.scrollIntoView({ behavior: 'smooth' });
    }
    function clear(force = false) {
        if (state.busy && !force) return false;
        if (!force && formGuard && !formGuard.confirmDiscard()) return false;
        ++state.editGeneration;
        state.editorLoading = false;
        state.editId = null; state.rowVersion = null;
        form.querySelectorAll('input, select').forEach(field => { field.disabled = false; });
        form.reset(); el('scheduleSubject').disabled = false;
        el('scheduleFormTitle').textContent = 'New schedule';
        el('scheduleSave').textContent = 'Save schedule';
        lastSubject = el('scheduleSubject').value;
        rooms().catch(error => notice(error.message, true));
        formGuard?.markClean();
        return true;
    }
    async function save(event) {
        event.preventDefault();
        if (state.busy || state.editorLoading || !state.roomReady) return;
        const subject = selected(), start = el('scheduleStart').value, end = el('scheduleEnd').value;
        if (!subject || !el('scheduleDate').value || (!!start !== !!end) ||
            (start && start >= end) || (el('scheduleRoom').value && !start)) {
            notice('Choose a subject, date and valid time interval; room bookings require both times.', true); return;
        }
        const payload = {
            assessmentSubjectId: subject.assessmentSubjectId,
            assessmentDate: el('scheduleDate').value,
            startTime: start || null, endTime: end || null,
            roomId: el('scheduleRoom').value ? Number(el('scheduleRoom').value) : null,
            instructions: el('scheduleInstructions').value.trim() || null,
            rowVersion: state.rowVersion
        };
        state.busy = true;
        const fields = [...form.querySelectorAll('input, select, button')];
        const disabledBefore = fields.map(field => field.disabled);
        fields.forEach(field => { field.disabled = true; });
        updateSave();
        try {
            await api('/api/assessment-administration/schedules' +
                (state.editId == null ? '' : '/' + encodeURIComponent(state.editId)),
                state.editId == null ? 'POST' : 'PUT', payload);
            clear(true);
            if (await load()) notice('Assessment schedule saved.');
            else notice('Schedule saved, but the register could not refresh. Search before another save.', true);
        } catch (error) {
            await load();
            notice(error.message + ' Check the register before retrying; the write may already have succeeded.', true);
        } finally {
            state.busy = false;
            fields.forEach((field, index) => { field.disabled = disabledBefore[index]; });
            el('scheduleSubject').disabled = state.editId != null;
            el('scheduleRoom').disabled = !state.roomReady;
            updateSave();
        }
    }
    function csv() {
        const safe = value => /^[=+@\-\t\r]/.test(String(value ?? '')) ? "'" + String(value) : value;
        const quote = value => '"' + String(safe(value) ?? '').replace(/"/g, '""') + '"';
        const rows = [['Date', 'Start', 'End', 'Assessment', 'Batch', 'Subject', 'Room', 'Instructions'],
            ...state.rows.map(x => [x.assessmentDate, x.startTime, x.endTime, x.assessmentName,
                x.batchName, x.subjectName, x.roomName, x.instructions])];
        const data = rows.map(x => x.map(quote).join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob(['\uFEFF' + data], { type: 'text/csv;charset=utf-8' }));
        const a = document.createElement('a'); a.href = url; a.download = 'assessment-schedules-page-' + state.page + '.csv';
        document.body.append(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    el('scheduleSubject').addEventListener('change', async () => {
        const subjectId = el('scheduleSubject').value;
        const hasEnteredData = !!(el('scheduleDate').value || el('scheduleStart').value ||
            el('scheduleEnd').value || el('scheduleRoom').value || el('scheduleInstructions').value);
        if ((hasEnteredData || state.editId != null) && formGuard && !formGuard.confirmDiscard()) {
            el('scheduleSubject').value = lastSubject; return;
        }
        clear(true);
        el('scheduleSubject').value = subjectId;
        lastSubject = subjectId;
        formGuard?.markClean();
        try { await rooms(); state.page = 1; await load(); } catch (error) { notice(error.message, true); }
    });
    form.addEventListener('submit', save);
    el('scheduleClear').addEventListener('click', () => clear());
    el('scheduleSearch').addEventListener('click', () => { state.page = 1; load(); });
    el('schedulePrev').addEventListener('click', () => { if (state.page > 1) { state.page--; load(); } });
    el('scheduleNext').addEventListener('click', () => { if (state.page * state.pageSize < state.total) { state.page++; load(); } });
    el('scheduleCsv').addEventListener('click', csv);
    el('schedulePrint').addEventListener('click', () => window.print());
    scopes().then(() => { clear(true); return load(); }).catch(error => notice(error.message, true));
})();
