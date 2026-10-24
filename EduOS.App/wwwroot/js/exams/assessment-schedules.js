(() => {
    'use strict';
    const root = document.getElementById('assessmentScheduleWorkspace');
    if (!root) return;
    const el = id => document.getElementById(id);
    const token = root.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    const state = { rows: [], scopes: [], page: 1, pageSize: 20, total: 0, editId: null, rowVersion: null, busy: false };
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
    async function rooms() {
        const subject = selected();
        const room = el('scheduleRoom'); room.replaceChildren(new Option('No room assigned', ''));
        if (!subject) return;
        const data = await api('/api/exams/workflow/schedules/rooms?assessmentSubjectId=' + encodeURIComponent(subject.assessmentSubjectId));
        for (const x of data || []) room.add(new Option(x.name + ' (' + x.code + ')', String(x.id)));
    }
    async function scopes() {
        const data = await api('/api/exams/workflow/scopes');
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
        const query = new URLSearchParams({ page: String(state.page), pageSize: String(state.pageSize) });
        const subject = selected();
        if (subject) query.set('assessmentSubjectId', String(subject.assessmentSubjectId));
        if (el('scheduleFrom').value) query.set('fromDate', el('scheduleFrom').value);
        if (el('scheduleTo').value) query.set('toDate', el('scheduleTo').value);
        try {
            const data = await api('/api/exams/workflow/schedules?' + query);
            state.rows = data.items || []; state.total = data.totalCount || 0; render();
        } catch (error) { notice(error.message, true); }
    }
    async function edit(row) {
        if (!state.scopes.some(x => x.assessmentSubjectId === row.assessmentSubjectId)) {
            notice('This subject is not in the available assessment scopes.', true); return;
        }
        el('scheduleSubject').value = String(row.assessmentSubjectId);
        try { await rooms(); } catch (error) { notice(error.message, true); return; }
        state.editId = row.id; state.rowVersion = row.rowVersion;
        el('scheduleSubject').disabled = true;
        el('scheduleDate').value = row.assessmentDate;
        el('scheduleStart').value = row.startTime?.slice(0, 5) || '';
        el('scheduleEnd').value = row.endTime?.slice(0, 5) || '';
        el('scheduleRoom').value = row.roomId == null ? '' : String(row.roomId);
        el('scheduleInstructions').value = row.instructions || '';
        el('scheduleFormTitle').textContent = 'Edit schedule #' + row.id;
        el('scheduleSave').textContent = 'Update schedule';
        el('scheduleForm').scrollIntoView({ behavior: 'smooth' });
    }
    function clear() {
        state.editId = null; state.rowVersion = null;
        el('scheduleForm').reset(); el('scheduleSubject').disabled = false;
        el('scheduleFormTitle').textContent = 'New schedule';
        el('scheduleSave').textContent = 'Save schedule';
        rooms().catch(error => notice(error.message, true));
    }
    async function save(event) {
        event.preventDefault();
        if (state.busy) return;
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
        state.busy = true; el('scheduleSave').disabled = true;
        try {
            await api('/api/exams/workflow/schedules' +
                (state.editId == null ? '' : '/' + encodeURIComponent(state.editId)),
                state.editId == null ? 'POST' : 'PUT', payload);
            notice('Assessment schedule saved.'); clear(); await load();
        } catch (error) {
            notice(error.message + ' Check the register before retrying; the write may already have succeeded.', true);
            await load();
        } finally { state.busy = false; el('scheduleSave').disabled = false; }
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
        try { await rooms(); state.page = 1; await load(); } catch (error) { notice(error.message, true); }
    });
    el('scheduleForm').addEventListener('submit', save);
    el('scheduleClear').addEventListener('click', clear);
    el('scheduleSearch').addEventListener('click', () => { state.page = 1; load(); });
    el('schedulePrev').addEventListener('click', () => { if (state.page > 1) { state.page--; load(); } });
    el('scheduleNext').addEventListener('click', () => { if (state.page * state.pageSize < state.total) { state.page++; load(); } });
    el('scheduleCsv').addEventListener('click', csv);
    el('schedulePrint').addEventListener('click', () => window.print());
    scopes().then(load).catch(error => notice(error.message, true));
})();
