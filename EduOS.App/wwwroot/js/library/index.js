(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const catalogRows = el('catalogRows'), issueRows = el('issueRows'), query = el('libraryQuery');
    if (!catalogRows || !query) return;
    const canManage = !!el('libraryBookForm');
    let books = [], categories = [], selectedCopyBook = null, copyPage = 1, copyRequest = 0;
    let branchPage = 1, branchRequest = 0;
    const branchLabels = new Map();
    const paging = { catalog: 1, issues: 1, pageSize: 20 };
    let issueRequest = 0;
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
        if (!['GET', 'HEAD', 'OPTIONS'].includes(method.toUpperCase())) {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw new Error('Security token missing. Reload the page.');
            headers.RequestVerificationToken = token;
        }
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
                actions.append(edit, archive);
                const copies = document.createElement('button');
                copies.type = 'button'; copies.className = 'btn btn-outline-secondary btn-sm ms-2';
                copies.textContent = 'Copies'; copies.addEventListener('click', () => selectCopyBook(book));
                actions.append(copies); tr.append(actions);
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
    function issueQuery() {
        return new URLSearchParams({ page: String(paging.issues), pageSize: String(paging.pageSize),
            status: el('issueStatus').value });
    }
    function loanState(issue) {
        const state = Number(issue.state);
        if (state === 1) return issue.dueDate < new Date().toISOString().slice(0, 10) ? 'Overdue' : 'Issued';
        return state === 2 ? 'Returned' : state === 3 ? 'Lost' : state === 4 ? 'Damaged' : 'Other';
    }
    async function loadIssues() {
        const request = ++issueRequest;
        try {
            const result = await api('/api/library/my-issues?' + issueQuery());
            if (request !== issueRequest) return;
            const issues = Array.isArray(result?.items) ? result.items : [];
            issueRows.replaceChildren();
            if (!issues.length) renderEmpty(issueRows, 7, 'No matching loans.');
            issues.forEach(issue => {
                const tr = document.createElement('tr');
                [issue.bookTitle, issue.accessionNumber, issue.issueDate, issue.dueDate,
                    issue.returnDate, loanState(issue), formatMoney(issue.fineAmount)]
                    .forEach((v, index) => { const td = cell(v); if (index === 6) td.className = 'text-end'; tr.append(td); });
                issueRows.append(tr);
            });
            el('issueCount').textContent = (result?.totalCount ?? 0) + ' records';
            el('issuePrevious').disabled = !result?.hasPreviousPage;
            el('issueNext').disabled = !result?.hasNextPage;
            el('issuePage').textContent = 'Page ' + paging.issues + ' of ' + Math.max(1, result?.totalPages ?? 1);
        } catch (error) {
            if (request !== issueRequest) return;
            renderEmpty(issueRows, 7, 'Loan history could not be loaded.'); message(error.message);
        }
    }
    async function exportMyIssues() {
        const button = el('issueCsv');
        if (button.disabled) return;
        button.disabled = true;
        try {
            const params = issueQuery();
            params.set('language', document.documentElement.lang?.toLowerCase().startsWith('bn') ? 'bn-BD' : 'en-BD');
            const response = await fetch('/api/library/my-issues/export?' + params, {
                credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'text/csv' }
            });
            if (!response.ok) {
                const error = await response.json().catch(() => null);
                throw Error(error?.message || 'Loan statement export denied.');
            }
            const url = URL.createObjectURL(await response.blob());
            try {
                const link = document.createElement('a');
                link.href = url; link.download = 'my-library-loans-page.csv';
                document.body.append(link); link.click(); link.remove();
            } finally { setTimeout(() => URL.revokeObjectURL(url), 1000); }
        } catch (error) { message(error.message); }
        finally { button.disabled = false; }
    }
    function formatMoney(value) {
        const n = Number(value || 0);
        return Number.isFinite(n) ? n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }) : '0.00';
    }
    function resetForm() {
        if (!canManage) return;
        el('libraryBookForm').reset(); el('bookReference').value = ''; el('bookRowVersion').value = '';
        el('bookSave').textContent = 'Save book';
        el('bookCategory').value = '';
    }
    function populate(book) {
        if (!canManage) return;
        const pairs = { bookReference: book.reference, bookRowVersion: book.rowVersion, bookTitle: book.title,
            bookAuthor: book.author, bookIsbn: book.isbn, bookPublisher: book.publisher, bookEdition: book.edition,
            bookCover: book.coverImageUrl, bookReplacement: book.replacementPrice, bookCategory: book.bookCategoryId };
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
                bookCategoryId: el('bookCategory').value ? Number(el('bookCategory').value) : null,
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

    function resetCopyForm() {
        el('libraryCopyForm').reset(); el('copyId').value = ''; el('copyRowVersion').value = '';
        el('copyBranch').value = '';
        el('copySave').textContent = 'Save copy';
    }
    function chooseCopyBranch(id) {
        const select = el('copyBranch'), value = id == null ? '' : String(id);
        if (value && ![...select.options].some(option => option.value === value)) {
            select.add(new Option(branchLabels.get(value) || ('Branch #' + value + ' (current)'), value));
        }
        select.value = value;
    }
    async function loadCopyBranches() {
        const sequence = ++branchRequest;
        try {
            const data = await api('/api/library/copy-branches?' + new URLSearchParams({
                search: el('copyBranchSearch').value.trim(), page: branchPage, pageSize: 20
            }));
            if (sequence !== branchRequest) return;
            const select = el('copyBranch'), selected = select.value;
            select.replaceChildren(new Option('Select a branch', ''));
            for (const branch of (data?.items || [])) {
                const name = branch.name + ' (' + branch.code + ', campus ' + branch.campusId + ')';
                branchLabels.set(String(branch.id), name);
                select.add(new Option(name, String(branch.id)));
            }
            chooseCopyBranch(selected);
            el('copyBranchPrevious').disabled = !data?.hasPreviousPage;
            el('copyBranchNext').disabled = !data?.hasNextPage;
            el('copyBranchPage').textContent = 'Page ' + branchPage + ' of ' + Math.max(1, data?.totalPages ?? 1);
        } catch (error) {
            if (sequence === branchRequest) message(error.message);
        }
    }
    function editCopy(copy) {
        el('copyId').value = copy.id; el('copyRowVersion').value = copy.rowVersion || '';
        el('copyAccession').value = copy.accessionNumber; el('copyBarcode').value = copy.barcode || '';
        el('copyShelf').value = copy.shelfNumber || '';
        chooseCopyBranch(copy.libraryBranchId);
        el('copySave').textContent = 'Update copy'; el('copyAccession').focus();
    }
    function selectCopyBook(book) {
        if (!canManage) return;
        selectedCopyBook = book; copyPage = 1; ++copyRequest; resetCopyForm();
        el('copyBookTitle').textContent = book.title;
        el('libraryCopiesPanel').classList.remove('d-none');
        el('libraryCopiesPanel').scrollIntoView({ behavior: 'smooth', block: 'start' });
        loadCopies();
        loadCopyBranches();
    }
    async function loadCopies() {
        if (!canManage || !selectedCopyBook) return;
        const sequence = ++copyRequest;
        try {
            const data = await api('/api/library/books/' + encodeURIComponent(selectedCopyBook.reference) +
                '/copies?' + new URLSearchParams({ page: copyPage, pageSize: paging.pageSize }));
            if (sequence !== copyRequest) return;
            const rows = el('copyRows'); rows.replaceChildren();
            const copies = Array.isArray(data?.items) ? data.items : [];
            copies.forEach(copy => {
                const tr = document.createElement('tr');
                [copy.accessionNumber, copy.barcode, copy.shelfNumber,
                    copy.libraryBranchId ? (branchLabels.get(String(copy.libraryBranchId)) || 'Branch #' + copy.libraryBranchId) : 'Unassigned',
                    copy.state].forEach(v => tr.append(cell(v)));
                const td = document.createElement('td');
                if (copy.state === 1 || copy.state === 'Available') {
                    const edit = document.createElement('button');
                    edit.type = 'button'; edit.className = 'btn btn-outline-primary btn-sm'; edit.textContent = 'Edit';
                    edit.addEventListener('click', () => editCopy(copy)); td.append(edit);
                }
                tr.append(td); rows.append(tr);
            });
            if (!copies.length) renderEmpty(rows, 6, 'No registered copies.');
            el('copyPrevious').disabled = !data?.hasPreviousPage;
            el('copyNext').disabled = !data?.hasNextPage;
            el('copyPage').textContent = 'Page ' + copyPage + ' of ' + Math.max(1, data?.totalPages ?? 1);
        } catch (error) { if (sequence === copyRequest) message(error.message); }
    }
    async function saveCopy(event) {
        event.preventDefault();
        if (!selectedCopyBook || !el('libraryCopyForm').reportValidity()) return;
        const button = el('copySave'); button.disabled = true;
        try {
            const id = el('copyId').value;
            const branchId = Number(el('copyBranch').value);
            if (!Number.isSafeInteger(branchId) || branchId <= 0)
                throw new Error('Select an active library branch before saving.');
            const payload = { bookReference: selectedCopyBook.reference, libraryBranchId: branchId,
                accessionNumber: el('copyAccession').value.trim(),
                barcode: el('copyBarcode').value.trim() || null,
                shelfNumber: el('copyShelf').value.trim() || null,
                rowVersion: el('copyRowVersion').value || null };
            await api(id ? '/api/library/copies/' + encodeURIComponent(id) : '/api/library/copies',
                id ? 'PUT' : 'POST', payload);
            resetCopyForm(); message('Book copy saved.', true);
            await Promise.all([loadCopies(), loadCatalog()]);
        } catch (error) { message(error.message); }
        finally { button.disabled = false; }
    }
    el('librarySearch').addEventListener('submit', e => { e.preventDefault(); paging.catalog = 1; clearMessage(); loadCatalog(); });
    el('clearLibrarySearch').addEventListener('click', () => { query.value = ''; paging.catalog = 1; loadCatalog(); });
    el('catalogPrevious').addEventListener('click', () => { if (paging.catalog > 1) { paging.catalog--; loadCatalog(); } });
    el('catalogNext').addEventListener('click', () => { paging.catalog++; loadCatalog(); });
    if (issueRows) {
        el('issuePrevious').addEventListener('click', () => { if (paging.issues > 1) { paging.issues--; loadIssues(); } });
        el('issueNext').addEventListener('click', () => { paging.issues++; loadIssues(); });
        el('issueStatus').addEventListener('change', () => { paging.issues = 1; loadIssues(); });
        el('issueCsv').addEventListener('click', exportMyIssues);
    }
    el('printLibrary')?.addEventListener('click', () => window.print());
    if (canManage) { el('libraryBookForm').addEventListener('submit', saveBook); el('bookReset').addEventListener('click', resetForm); }

    let categoryLoadVersion = 0;
    async function loadCategories() {
        if (!canManage) return;
        const version = ++categoryLoadVersion;
        try {
            const rows = await api('/api/library/categories');
            if (version !== categoryLoadVersion) return;
            categories = Array.isArray(rows) ? rows : [];
            const select = el('bookCategory'), previous = select.value;
            select.replaceChildren(new Option('Uncategorized', ''));
            for (const row of categories.filter(x => x.isActive))
                select.append(new Option(row.name + ' (' + row.code + ')', String(row.id)));
            if ([...select.options].some(x => x.value === previous)) select.value = previous;
            const body = el('categoryRows'); body.replaceChildren();
            for (const row of categories) {
                const tr = document.createElement('tr');
                [row.name, row.code, row.isActive ? 'Active' : 'Inactive'].forEach(x => tr.append(cell(x)));
                const td = document.createElement('td'), button = document.createElement('button');
                button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary'; button.textContent = 'Edit';
                button.addEventListener('click', () => {
                    el('categoryId').value = row.id; el('categoryRowVersion').value = row.rowVersion;
                    el('categoryName').value = row.name; el('categoryCode').value = row.code;
                    el('categoryActive').checked = !!row.isActive; el('categoryName').focus();
                });
                td.append(button); tr.append(td); body.append(tr);
            }
            if (!categories.length) { const tr = document.createElement('tr'), td = cell('No categories.'); td.colSpan = 4; tr.append(td); body.append(tr); }
        } catch (error) { message(error.message); }
    }
    function resetCategory() {
        el('libraryCategoryForm').reset(); el('categoryId').value = ''; el('categoryRowVersion').value = '';
        el('categoryActive').checked = true;
    }
    async function saveCategory(event) {
        event.preventDefault();
        if (!el('libraryCategoryForm').reportValidity()) return;
        const id = el('categoryId').value, button = el('categorySave');
        const body = { name: el('categoryName').value.trim(), code: el('categoryCode').value.trim(),
            isActive: el('categoryActive').checked, rowVersion: el('categoryRowVersion').value || null };
        if (!body.name || !body.code) return message('Category name and code are required.');
        button.disabled = true;
        try {
            await api(id ? '/api/library/categories/' + encodeURIComponent(id) : '/api/library/categories',
                id ? 'PUT' : 'POST', body);
            resetCategory(); await loadCategories(); await loadCatalog();
            message('Category saved.', true);
        } catch (error) { message(error.message); }
        finally { button.disabled = false; }
    }
    if (canManage) {
        el('libraryCategoryForm').addEventListener('submit', saveCategory);
        el('categoryReset').addEventListener('click', resetCategory);
        loadCategories();
    }
    if (canManage) {
        el('libraryCopyForm').addEventListener('submit', saveCopy);
        el('copyReset').addEventListener('click', resetCopyForm);
        el('copyBranchFind').addEventListener('click', () => { branchPage = 1; loadCopyBranches(); });
        el('copyBranchSearch').addEventListener('keydown', event => {
            if (event.key === 'Enter') { event.preventDefault(); branchPage = 1; loadCopyBranches(); }
        });
        el('copyBranchPrevious').addEventListener('click', () => { if (branchPage > 1) { branchPage--; loadCopyBranches(); } });
        el('copyBranchNext').addEventListener('click', () => { branchPage++; loadCopyBranches(); });
        el('copyPrevious').addEventListener('click', () => { if (copyPage > 1) { copyPage--; loadCopies(); } });
        el('copyNext').addEventListener('click', () => { copyPage++; loadCopies(); });
    }
    loadCatalog(); if (issueRows) loadIssues();
})();
