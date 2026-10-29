(() => {
    'use strict';
    const root = document.getElementById('hostelResidentRosterPanel');
    if (!root) return;
    const el = id => document.getElementById(id);
    const size = 25;
    let page = 1, pages = 1, sequence = 0, applied = null;
    const status = (message, error = false) => {
        const node = el('hostelResidentRosterStatus');
        node.textContent = message;
        node.className = error ? 'small text-danger mb-2' : 'small text-muted mb-2';
    };
    const params = (paged = true) => {
        const query = new URLSearchParams();
        if (paged) { query.set('page', String(page)); query.set('pageSize', String(size)); }
        for (const [id, name] of [
            ['hostelResidentRosterDate', 'operatingDate'],
            ['hostelResidentRosterCampus', 'campusId'],
            ['hostelResidentRosterSearch', 'search']
        ]) {
            const value = el(id).value.trim();
            if (value) query.set(name, value);
        }
        return query;
    };
    const invalidate = () => {
        ++sequence; applied = null;
        el('hostelResidentRosterRows').replaceChildren();
        el('hostelResidentRosterPage').textContent = '';
        for (const id of ['hostelResidentRosterPrev','hostelResidentRosterNext','hostelResidentRosterCsv'])
            el(id).disabled = true;
    };
    async function load() {
        invalidate();
        const date = el('hostelResidentRosterDate').value;
        if (!/^\d{4}-\d{2}-\d{2}$/.test(date)) {
            status('Choose a date to view scheduled occupants.', true); return;
        }
        const ticket = sequence, requestedPage = page;
        status('Loading scheduled residents…');
        try {
            const response = await fetch('/api/hostel/resident-roster?' + params(), {
                credentials:'same-origin', cache:'no-store', headers:{Accept:'application/json'}
            });
            const result = await response.json().catch(() => null);
            if (ticket !== sequence) return;
            if (!response.ok || !result?.success) throw Error(result?.message || 'Roster unavailable.');
            const data = result.data || {}, items = Array.isArray(data.items) ? data.items : [];
            pages = Math.max(1, Math.ceil((Number(data.totalCount) || 0) / size));
            const body = el('hostelResidentRosterRows');
            for (const item of items) {
                const tr = document.createElement('tr');
                for (const value of [
                    item.campusName, item.hostelName,
                    (item.roomNumber || '') + ' / ' + (item.bedNumber || ''),
                    item.studentName, item.studentCode, item.startDate,
                    item.endDate || '—', item.currentState
                ]) {
                    const cell = document.createElement('td');
                    cell.textContent = String(value ?? '—'); tr.append(cell);
                }
                body.append(tr);
            }
            if (!items.length) {
                const tr = document.createElement('tr'), cell = document.createElement('td');
                cell.colSpan = 8; cell.textContent = 'No scheduled occupants on this date.';
                tr.append(cell); body.append(tr);
            }
            el('hostelResidentRosterPage').textContent =
                'Page ' + requestedPage + ' of ' + pages + ' · ' +
                (Number(data.totalCount) || 0) + ' stays';
            el('hostelResidentRosterPrev').disabled = requestedPage <= 1;
            el('hostelResidentRosterNext').disabled = requestedPage >= pages;
            applied = params(false).toString();
            el('hostelResidentRosterCsv').disabled = !items.length;
            status('Scheduled occupancy; not a physical check-in record.');
        } catch (error) {
            if (ticket === sequence) { invalidate(); status(error.message, true); }
        }
    }
    el('hostelResidentRosterFilter').addEventListener('submit', event => {
        event.preventDefault(); page = 1; load();
    });
    for (const id of ['hostelResidentRosterDate','hostelResidentRosterCampus','hostelResidentRosterSearch']) {
        el(id).addEventListener('change', () => {
            page = 1; invalidate(); status('Filters changed. Select Filter to refresh.');
        });
    }
    el('hostelResidentRosterSearch').addEventListener('input', () => {
        page = 1; invalidate(); status('Filters changed. Select Filter to refresh.');
    });
    el('hostelResidentRosterPrev').addEventListener('click', () => {
        page = Math.max(1, page - 1); load();
    });
    el('hostelResidentRosterNext').addEventListener('click', () => {
        page = Math.min(pages, page + 1); load();
    });
    el('hostelResidentRosterCsv').addEventListener('click', async () => {
        const snapshot = params(false).toString(), ticket = sequence;
        if (applied === null || snapshot !== applied) return;
        const button = el('hostelResidentRosterCsv'); button.disabled = true;
        try {
            const response = await fetch('/api/hostel/resident-roster/export?' + snapshot, {
                credentials:'same-origin', cache:'no-store', headers:{Accept:'text/csv'}
            });
            if (!response.ok) {
                const result = await response.json().catch(() => null);
                throw Error(result?.message || 'Roster export failed.');
            }
            const blob = await response.blob();
            if (ticket !== sequence || applied !== snapshot || params(false).toString() !== snapshot)
                throw Error('Filters changed during export. Apply filters and retry.');
            const url = URL.createObjectURL(blob), link = document.createElement('a');
            link.href = url; link.download = 'hostel-resident-roster.csv';
            document.body.append(link); link.click(); link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        } catch (error) { status(error.message, true); }
        finally { button.disabled = applied === null || applied !== params(false).toString(); }
    });
    async function campuses() {
        try {
            const response = await fetch('/api/hostel/facility-campuses', {
                credentials:'same-origin', cache:'no-store', headers:{Accept:'application/json'}
            });
            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.success || !Array.isArray(result.data))
                throw Error(result?.message || 'Campus list unavailable.');
            const select = el('hostelResidentRosterCampus');
            select.replaceChildren(new Option('All permitted campuses', ''));
            for (const campus of result.data) {
                const id = Number(campus.id);
                if (Number.isSafeInteger(id) && id > 0)
                    select.add(new Option(String(campus.name || 'Campus'), String(id)));
            }
        } catch (error) { status(error.message, true); }
    }
    campuses();
})();
