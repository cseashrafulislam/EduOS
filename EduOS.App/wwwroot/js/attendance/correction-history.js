(() => {
    'use strict';
    const modalElement = document.getElementById('attendanceCorrectionHistoryModal');
    if (!modalElement) return;
    const rows = document.getElementById('attendanceCorrectionHistoryRows');
    const status = document.getElementById('attendanceCorrectionHistoryStatus');
    const previous = document.getElementById('attendanceHistoryPrevious');
    const next = document.getElementById('attendanceHistoryNext');
    const pageLabel = document.getElementById('attendanceHistoryPage');
    const studentLabel = document.getElementById('attendanceCorrectionHistoryStudent');
    const state = { attendanceId: 0, page: 1, totalPages: 1, sequence: 0 };
    const names = { 1: 'Present', 2: 'Absent', 3: 'Late', 4: 'Leave', 5: 'Excused' };

    function cell(value) {
        const td = document.createElement('td');
        td.textContent = value === null || value === undefined ? '' : String(value);
        return td;
    }
    function setStatus(message, error = false) {
        status.textContent = message;
        status.className = error ? 'small text-danger' : 'small text-muted';
    }
    async function load() {
        const id = state.attendanceId, page = state.page, sequence = ++state.sequence;
        previous.disabled = true; next.disabled = true;
        rows.replaceChildren();
        setStatus('Loading correction history...');
        try {
            const response = await fetch('/api/student-attendance/' + encodeURIComponent(id) +
                '/corrections?page=' + page + '&pageSize=20', {
                credentials: 'same-origin', cache: 'no-store', referrerPolicy: 'no-referrer',
                headers: { Accept: 'application/json' }
            });
            const payload = await response.json().catch(() => null);
            if (sequence !== state.sequence) return;
            if (!response.ok || !payload?.success || !payload.data ||
                !Array.isArray(payload.data.items))
                throw new Error(payload?.message || 'Could not load correction history.');
            state.totalPages = Math.max(1, Number(payload.data.totalPages) || 1);
            for (const item of payload.data.items) {
                const tr = document.createElement('tr');
                tr.append(
                    cell(item.changedAt ? new Date(item.changedAt).toISOString() : ''),
                    cell(names[item.previousState] || item.previousState),
                    cell(names[item.newState] || item.newState),
                    cell(item.reason), cell(item.changedByUserId)
                );
                rows.append(tr);
            }
            setStatus(payload.data.items.length ? '' : 'No corrections recorded for this attendance.');
            pageLabel.textContent = 'Page ' + page + ' of ' + state.totalPages +
                ' · ' + Number(payload.data.totalCount || 0) + ' corrections';
            previous.disabled = page <= 1;
            next.disabled = page >= state.totalPages;
        } catch (error) {
            if (sequence === state.sequence) {
                setStatus(error.message || 'Could not load correction history.', true);
                pageLabel.textContent = '';
            }
        }
    }
    document.addEventListener('attendance:corrections-open', event => {
        const id = Number(event.detail?.attendanceId);
        if (!Number.isSafeInteger(id) || id <= 0) return;
        state.attendanceId = id; state.page = 1;
        studentLabel.textContent = event.detail?.studentName || '';
        bootstrap.Modal.getOrCreateInstance(modalElement).show();
        void load();
    });
    previous.addEventListener('click', () => {
        if (state.page > 1) { state.page--; void load(); }
    });
    next.addEventListener('click', () => {
        if (state.page < state.totalPages) { state.page++; void load(); }
    });
    modalElement.addEventListener('hidden.bs.modal', () => {
        state.sequence++; state.attendanceId = 0;
    });
})();
