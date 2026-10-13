(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const root = el('reservationApp');
    if (!root) return;
    const staff = root.dataset.staff === 'true', state = { mine: 1, queue: 1, book: '', copies: [], key: crypto.randomUUID(), search: 0, queueRequest: 0 };
    const stateNames = { 1: 'Pending', 2: 'Fulfilled', 3: 'Cancelled', 4: 'Expired' };
    function alertUser(text, ok = false) {
        const target = el('reservationAlert');
        target.className = 'alert alert-' + (ok ? 'success' : 'danger');
        target.textContent = text;
        target.focus();
    }
    async function api(url, method = 'GET', body) {
        const headers = { Accept: 'application/json' };
        if (body !== undefined) headers['Content-Type'] = 'application/json';
        if (method !== 'GET') {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw new Error('Security token missing. Reload the page.');
            headers.RequestVerificationToken = token;
        }
        const response = await fetch(url, { method, credentials: 'same-origin', cache: 'no-store', headers,
            ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
        const payload = await response.json().catch(() => null);
        if (!response.ok || !payload?.success) throw new Error(payload?.message || 'Reservation request failed.');
        return payload.data;
    }
    function td(value) {
        const cell = document.createElement('td');
        cell.textContent = value === undefined || value === null || value === '' ? '—' : String(value);
        return cell;
    }
    function empty(id, columns, message) {
        const row = document.createElement('tr'), cell = td(message);
        cell.colSpan = columns; cell.className = 'text-center text-muted p-3'; row.append(cell);
        el(id).replaceChildren(row);
    }
    function date(value) { return value ? new Date(value).toLocaleString() : '—'; }
    function pageButtons(prefix, data, page) {
        el(prefix + 'Previous').disabled = !data?.hasPreviousPage;
        el(prefix + 'Next').disabled = !data?.hasNextPage;
        el(prefix + 'Page').textContent = 'Page ' + page + ' of ' + Math.max(1, data?.totalPages || 1);
    }
    async function searchBooks() {
        const sequence = ++state.search;
        try {
            const data = await api('/api/library/catalog?' + new URLSearchParams({
                search: el('reservationQuery').value.trim(), page: 1, pageSize: 50
            }));
            if (sequence !== state.search) return;
            const select = el('reservationBook'), previous = select.value;
            select.replaceChildren(new Option('Select a book', ''));
            (data?.items || []).forEach(book => select.add(new Option(
                book.title + ' (' + book.availableCopies + ' available / ' + book.totalCopies + ' copies)', book.reference)));
            const deepLink = new URLSearchParams(location.search).get('book');
            if ([...select.options].some(option => option.value === previous || option.value === deepLink))
                select.value = [...select.options].some(x => x.value === previous) ? previous : deepLink;
            state.book = select.value;
            if (staff) await loadQueue();
        } catch (error) { alertUser(error.message); }
    }
    async function loadMine() {
        try {
            const data = await api('/api/library/reservations/mine?' + new URLSearchParams({ page: state.mine, pageSize: 20 }));
            const rows = el('reservationMineRows'); rows.replaceChildren();
            (data?.items || []).forEach(item => {
                const row = document.createElement('tr');
                [item.bookTitle, date(item.reservedAt), date(item.expiresAt), stateNames[item.state] || item.state]
                    .forEach(value => row.append(td(value)));
                const action = td('');
                if (item.state === 1 || item.state === 'Pending') {
                    const button = document.createElement('button');
                    button.type = 'button'; button.className = 'btn btn-outline-danger btn-sm'; button.textContent = 'Cancel';
                    button.addEventListener('click', async () => {
                        if (!confirm('Cancel this reservation?')) return;
                        button.disabled = true;
                        try {
                            await api('/api/library/reservations/' + item.id + '/cancel', 'POST', { rowVersion: item.rowVersion });
                            alertUser('Reservation cancelled.', true); await Promise.all([loadMine(), staff ? loadQueue() : Promise.resolve()]);
                        } catch (error) { alertUser(error.message); button.disabled = false; }
                    });
                    action.append(button);
                }
                row.append(action); rows.append(row);
            });
            if (!data?.items?.length) empty('reservationMineRows', 5, 'No reservations.');
            pageButtons('reservationMine', data, state.mine);
        } catch (error) { empty('reservationMineRows', 5, 'Could not load reservations.'); alertUser(error.message); }
    }
    async function loadQueue() {
        if (!staff) return;
        const seq = ++state.queueRequest, book = el('reservationBook').value;
        state.book = book;
        if (!book) { empty('reservationQueueRows', 5, 'Select a book to view its queue.'); return; }
        try {
            const [queue, copies] = await Promise.all([
                api('/api/library/reservations/books/' + encodeURIComponent(book) + '/queue?' +
                    new URLSearchParams({ page: state.queue, pageSize: 20 })),
                api('/api/library/books/' + encodeURIComponent(book) + '/copies?' +
                    new URLSearchParams({ page: 1, pageSize: 100 }))
            ]);
            if (seq !== state.queueRequest || el('reservationBook').value !== book) return;
            state.copies = (copies?.items || []).filter(x => x.state === 1 || x.state === 'Available');
            const target = el('reservationQueueRows'); target.replaceChildren();
            (queue?.items || []).forEach(item => {
                const row = document.createElement('tr');
                [item.studentName, date(item.reservedAt), date(item.expiresAt)].forEach(x => row.append(td(x)));
                const cell = td(''), select = document.createElement('select');
                select.className = 'form-select form-select-sm'; select.setAttribute('aria-label', 'Available copy');
                select.add(new Option('Select copy', ''));
                state.copies.forEach(copy => select.add(new Option(copy.accessionNumber, String(copy.id))));
                cell.append(select); row.append(cell);
                const actions = td(''), button = document.createElement('button');
                button.type = 'button'; button.className = 'btn btn-success btn-sm'; button.textContent = 'Issue copy';
                button.addEventListener('click', async () => {
                    if (!select.value) { alertUser('Select an available copy.'); return; }
                    button.disabled = true;
                    try {
                        await api('/api/library/reservations/' + item.id + '/fulfill', 'POST',
                            { bookCopyId: Number(select.value), rowVersion: item.rowVersion });
                        alertUser('Reservation fulfilled and copy issued.', true);
                        await Promise.all([loadQueue(), loadMine()]);
                    } catch (error) { alertUser(error.message); button.disabled = false; }
                });
                actions.append(button); row.append(actions); target.append(row);
            });
            if (!queue?.items?.length) empty('reservationQueueRows', 5, 'No active reservations for this book.');
            pageButtons('reservationQueue', queue, state.queue);
        } catch (error) {
            if (seq === state.queueRequest) { empty('reservationQueueRows', 5, 'Queue unavailable.'); alertUser(error.message); }
        }
    }
    el('reservationSearch').addEventListener('submit', e => { e.preventDefault(); searchBooks(); });
    el('reservationBook').addEventListener('change', () => { state.queue = 1; state.key = crypto.randomUUID(); loadQueue(); });
    el('reservationStudent')?.addEventListener('input', () => { state.key = crypto.randomUUID(); });
    el('reservationCreate').addEventListener('submit', async e => {
        e.preventDefault();
        const book = el('reservationBook').value;
        if (!book) { alertUser('Choose a book.'); return; }
        const button = el('reservationCreateButton'); button.disabled = true;
        try {
            await api('/api/library/reservations', 'POST', {
                clientRequestId: state.key, bookReference: book,
                studentReference: staff ? (el('reservationStudent').value.trim() || '00000000-0000-0000-0000-000000000000')
                    : '00000000-0000-0000-0000-000000000000'
            });
            state.key = crypto.randomUUID(); alertUser('Reservation placed.', true);
            await Promise.all([loadMine(), staff ? loadQueue() : Promise.resolve()]);
        } catch (error) { alertUser(error.message); }
        finally { button.disabled = false; }
    });
    el('reservationMineRefresh').addEventListener('click', loadMine);
    for (const [direction, delta] of [['Previous', -1], ['Next', 1]]) {
        el('reservationMine' + direction).addEventListener('click', () => { state.mine = Math.max(1, state.mine + delta); loadMine(); });
        if (staff) el('reservationQueue' + direction).addEventListener('click', () => { state.queue = Math.max(1, state.queue + delta); loadQueue(); });
    }
    if (staff) el('reservationQueueRefresh').addEventListener('click', loadQueue);
    searchBooks(); loadMine();
})();