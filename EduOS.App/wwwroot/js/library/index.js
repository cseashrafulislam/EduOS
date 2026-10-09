(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const catalogRows = el('catalogRows'), issueRows = el('issueRows'), query = el('libraryQuery');
    if (!catalogRows || !issueRows || !query) return;
    const canManage = !!el('libraryBookForm');
    let books = [];
    const paging = { catalog: 1, issues: 1, pageSize: 20 };
    const catalogColumns = canManage ? 6 : 5;
    function message(text, success = false) {
        const node = el('libraryAlert');
        node.className = 'alert alert-' + (success ? 'success' : 'danger');
        node.textContent = text;
        node.focus();
    }
    function clearMessage() { const node = el('libraryAlert'); node.className = 'd-none'; node.textContent = ''; }
    async function api(url, method = 'GET', data) {
        const headers = { Accept: 'application/json' };
        if (data !== undefined) headers['Content-Type'] = 'application/json';
        const response = await fetch(url, { method, cache: 'no-store', credentials: 'same-origin', headers,
            ...(data !== undefined ? { body: JSON.stringify(data) } : {}) });
        const payload = await response.json().catch(() => null);
        if (!response.ok || !payload?.success) throw new Error(payload?.message || 'Library request failed.');
        return payload.data;
    }
    function cell(value) { const td = document.createElement('td'); td.textContent = value === null || value === undefined || value === '' ? '—' : String(value); return td; }
    function renderEmpty(target, columns, text) { target.replaceChildren(); const tr = document.createElement('tr'), td = cell(text); td.colSpan = columns; td.className = 'text-center text-muted py-4'; tr.append(td); target.append(tr); }
    function renderCatalog() {
        catalogRows.replaceChildren();
        if (!books.length) { renderEmpty(catalogRows, catalogColumns, 'No matching books.'); return; }
        books.forEach(book => {
            const tr = document.createElement('tr');
            [book.title, book.author, book.isbn, book.bookCategoryName, (book.availableCopies ?? 0) + ' / ' + (book.totalCopies ?? 0)].forEach((v, index) => {
                const td = cell(v); if (index === 4) td.className = 'text-end'; tr.append(td);
            });
            if (canManage) {
                const actions = document.createElement('td');
                const edit = document.createElement('button');
                edit.type = 'button'; edit.className = 'btn btn-outline-primary btn-sm me-2'; edit.textContent = 'Edit';
                edit.addEventListener('click', () => populate(book));
                const archive = document.createElement('button');
                archive.type = 'button'; archive.className = 'btn btn-outline-danger btn-sm'; archive.textContent = 'Archive';
                archive.addEventListener('click', () => archiveBook(book, archive));
                actions.append(edit, archive); tr.append(actions);
            }
            catalogRows.append(tr);
        });
    }
    async function loadCatalog() {
        try {
            const term = query.value.trim();
            const result = await api('/api/library/catalog?' + new URLSearchParams({ search: term, page: paging.catalog, pageSize: paging.pageSize }));
            books = Array.isArray(result?.items) ? result.items : [];
            renderCatalog();
            el('catalogCount').textContent = (result?.totalCount ?? 0) + ' matching books';
            el('catalogPrevious').disabled = !result?.hasPreviousPage;
            el('catalogNext').disabled = !result?.hasNextPage;
            el('catalogPage').textContent = 'Page ' + paging.catalog + ' of ' + Math.max(1, result?.totalPages ?? 1);
        } catch (error) { renderEmpty(catalogRows, catalogColumns, 'Catalog could not be loaded.'); message(error.message); }
    }
    async function loadIssues() {
        try {
            const result = await api('/api/library/my-issues?' + new URLSearchParams({ page: paging.issues, pageSize: paging.pageSize }));
            const issues = Array.isArray(result?.items) ? result.items : [];
            issueRows.replaceChildren();
            if (!issues.length) renderEmpty(issueRows, 7, 'No borrowing history.');
            issues.forEach(issue => {
                const tr = document.createElement('tr');
                [issue.bookTitle, issue.accessionNumber, issue.issueDate, issue.dueDate, issue.returnDate, issue.state, formatMoney(issue.fineAmount)]
                    .forEach((v, index) => { const td = cell(v); if (index === 6) td.className = 'text-end'; tr.append(td); });
                issueRows.append(tr);
            });
            el('issueCount').textContent = (result?.totalCount ?? 0) + ' records';
            el('issuePrevious').disabled = !result?.hasPreviousPage;
            el('issueNext').disabled = !result?.hasNextPage;
            el('issuePage').textContent = 'Page ' + paging.issues + ' of ' + Math.max(1, result?.totalPages ?? 1);
        } catch (error) { renderEmpty(issueRows, 7, 'Issue history could not be loaded.'); message(error.message); }
    }
    function formatMoney(value) {
        const n = Number(value || 0);
        return Number.isFinite(n) ? n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }) : '0.00';
    }
    function resetForm() {
        if (!canManage) return;
        el('libraryBookForm').reset(); el('bookReference').value = ''; el('bookRowVersion').value = '';
        el('bookSave').textContent = 'Save book';
    }
    function populate(book) {
        if (!canManage) return;
        const pairs = { bookReference: book.reference, bookRowVersion: book.rowVersion, bookTitle: book.title,
            bookAuthor: book.author, bookIsbn: book.isbn, bookPublisher: book.publisher, bookEdition: book.edition,
            bookCover: book.coverImageUrl, bookReplacement: book.replacementPrice };
        Object.entries(pairs).forEach(([key, value]) => { el(key).value = value ?? ''; });
        el('bookSave').textContent = 'Update book';
        el('libraryBookForm').scrollIntoView({ behavior: 'smooth', block: 'start' });
        el('bookTitle').focus();
    }
    async function saveBook(event) {
        event.preventDefault();
        if (!el('libraryBookForm').reportValidity()) return;
        const submit = el('bookSave');
        submit.disabled = true;
        try {
            const price = el('bookReplacement').value.trim();
            const payload = { reference: el('bookReference').value || null, rowVersion: el('bookRowVersion').value || null,
                title: el('bookTitle').value.trim(), author: el('bookAuthor').value.trim() || null,
                isbn: el('bookIsbn').value.trim() || null, publisher: el('bookPublisher').value.trim() || null,
                edition: el('bookEdition').value.trim() || null, coverImageUrl: el('bookCover').value.trim() || null,
                replacementPrice: price ? Number(price) : null, isActive: true };
            if (!payload.title || price && !Number.isFinite(payload.replacementPrice)) throw new Error('Check book title and price.');
            await api('/api/library/books', 'POST', payload);
            resetForm(); clearMessage(); message('Book metadata saved.', true); await loadCatalog();
        } catch (error) { message(error.message); }
        finally { submit.disabled = false; }
    }
    async function archiveBook(book, button) {
        if (!window.confirm('Archive this book? It will be hidden from the active catalog.')) return;
        button.disabled = true;
        try {
            await api('/api/library/books/' + encodeURIComponent(book.reference) + '?rowVersion=' + encodeURIComponent(book.rowVersion), 'DELETE');
            if (el('bookReference')?.value === book.reference) resetForm();
            message('Book archived.', true); await loadCatalog();
        } catch (error) { message(error.message); button.disabled = false; }
    }
    el('librarySearch').addEventListener('submit', e => { e.preventDefault(); paging.catalog = 1; clearMessage(); loadCatalog(); });
    el('clearLibrarySearch').addEventListener('click', () => { query.value = ''; paging.catalog = 1; loadCatalog(); });
    el('catalogPrevious').addEventListener('click', () => { if (paging.catalog > 1) { paging.catalog--; loadCatalog(); } });
    el('catalogNext').addEventListener('click', () => { paging.catalog++; loadCatalog(); });
    el('issuePrevious').addEventListener('click', () => { if (paging.issues > 1) { paging.issues--; loadIssues(); } });
    el('issueNext').addEventListener('click', () => { paging.issues++; loadIssues(); });
    el('printLibrary')?.addEventListener('click', () => window.print());
    if (canManage) { el('libraryBookForm').addEventListener('submit', saveBook); el('bookReset').addEventListener('click', resetForm); }
    loadCatalog(); loadIssues();
})();
