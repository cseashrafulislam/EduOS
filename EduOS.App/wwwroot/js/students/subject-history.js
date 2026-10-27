(() => {
'use strict';
const root = document.getElementById('studentOptionalSubjects');
const host = document.getElementById('subjectsHistory');
if (!root || !host) return;
const el = id => document.getElementById(id);
let studentReference = null, page = 1, sequence = 0, loading = false, totalPages = 0;
const pageSize = 20;
function updateControls() {
    el('subjectsHistoryPrev').disabled = loading || !studentReference || page <= 1;
    el('subjectsHistoryNext').disabled = loading || !studentReference || page >= totalPages;
    el('subjectsHistoryPage').textContent = studentReference
        ? 'Page ' + page + ' of ' + Math.max(1, totalPages) : 'Select a student';
}
function status(message, error = false) {
    const element = el('subjectsHistoryStatus');
    element.textContent = message;
    element.className = 'small mb-2 ' + (error ? 'text-danger' : 'text-muted');
}
function cell(row, value) {
    const td = document.createElement('td');
    td.textContent = value == null || value === '' ? '—' : String(value);
    row.append(td);
}
async function load() {
    if (!studentReference) return;
    const ticket = ++sequence;
    loading = true; updateControls(); status('Loading historical registrations…');
    try {
        const response = await fetch('/api/student-subjects/students/' +
            encodeURIComponent(studentReference) + '/history?' +
            new URLSearchParams({page: String(page), pageSize: String(pageSize)}), {
            credentials: 'same-origin', cache: 'no-store',
            headers: {Accept: 'application/json'}
        });
        const payload = await response.json().catch(() => null);
        if (ticket !== sequence) return;
        if (!response.ok || payload?.success !== true)
            throw Error(payload?.message || 'History could not be loaded.');
        const result = payload.data;
        totalPages = Math.ceil(result.totalCount / pageSize);
        const body = el('subjectsHistoryRows');
        body.replaceChildren();
        for (const item of result.items || []) {
            const row = document.createElement('tr');
            cell(row, item.rollNo);
            cell(row, item.campusId);
            cell(row, item.academicBatchId);
            cell(row, item.enrollmentState + (item.isCurrentEnrollment ? ' (current)' : ''));
            cell(row, item.subjectName || item.subjectCode || 'Catalog unavailable');
            cell(row, item.creditHoursSnapshot);
            cell(row, item.registrationState);
            cell(row, item.registeredAt);
            cell(row, item.approvedAt);
            body.append(row);
        }
        if (!body.children.length) {
            const row = document.createElement('tr');
            const td = document.createElement('td');
            td.colSpan = 9; td.textContent = 'No historical subject registrations.';
            row.append(td); body.append(row);
        }
        status(result.totalCount + ' registration(s). Original enrollment roll and credit snapshots are preserved.');
    } catch (error) {
        if (ticket !== sequence) return;
        totalPages = 0;
        el('subjectsHistoryRows').replaceChildren();
        status(error.message || 'History could not be loaded.', true);
    } finally {
        if (ticket === sequence) { loading = false; updateControls(); }
    }
}
root.addEventListener('student-subjects:selected', event => {
    studentReference = event.detail?.reference || null;
    page = 1; totalPages = 0; ++sequence; loading = false;
    el('subjectsHistoryRows').replaceChildren();
    status(studentReference ? 'Loading…' : 'Choose a student to view historical registrations.');
    updateControls();
    if (studentReference) void load();
});
el('subjectsHistoryPrev').addEventListener('click', () => {
    if (!loading && page > 1) { page--; void load(); }
});
el('subjectsHistoryNext').addEventListener('click', () => {
    if (!loading && page < totalPages) { page++; void load(); }
});
updateControls();
})();
