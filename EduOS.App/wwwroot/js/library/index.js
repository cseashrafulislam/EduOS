(() => {
    'use strict';
    const form = document.getElementById('librarySearch');
    const query = document.getElementById('libraryQuery');
    const catalogRows = document.getElementById('catalogRows');
    const issueRows = document.getElementById('issueRows');
    if (!form || !query || !catalogRows || !issueRows) return;
    form.addEventListener('submit', e => { e.preventDefault(); loadCatalog(); });
    document.getElementById('clearLibrarySearch')?.addEventListener('click', () => { query.value = ''; loadCatalog(); });
    document.getElementById('printLibrary')?.addEventListener('click', () => window.print());
    loadCatalog(); loadIssues();

    async function loadCatalog() {
        clearAlert();
        try {
            const q = query.value.trim();
            const payload = await getJson(`/api/library/catalog${q ? `?search=${encodeURIComponent(q)}` : ''}`);
            const rows = Array.isArray(payload.data) ? payload.data : [];
            renderCatalog(rows); document.getElementById('catalogCount').textContent = `${rows.length} item${rows.length === 1 ? '' : 's'}`;
        } catch (e) { renderEmpty(catalogRows, 6, 'Catalog could not be loaded.'); showAlert(e.message); }
    }
    async function loadIssues() {
        try {
            const payload = await getJson('/api/library/my-issues');
            const rows = Array.isArray(payload.data) ? payload.data : [];
            renderIssues(rows); document.getElementById('issueCount').textContent = `${rows.length} record${rows.length === 1 ? '' : 's'}`;
        } catch (e) { renderEmpty(issueRows, 7, 'Issue history could not be loaded.'); showAlert(e.message); }
    }
    async function getJson(url) {
        const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store', headers: { Accept: 'application/json' } });
        const payload = await response.json().catch(() => null);
        if (!response.ok || !payload?.success) throw new Error(payload?.message || 'Request failed.');
        return payload;
    }
    function renderCatalog(items) {
        catalogRows.replaceChildren(); if (!items.length) return renderEmpty(catalogRows, 6, 'No matching books found.');
        items.forEach(x => { const tr = document.createElement('tr'); tr.append(cell(x.title), cell(x.author || '—'), cell(x.isbn || '—'), cell(x.category || '—'), cell(x.shelfNo || '—')); const available = cell(`${x.availableCopies ?? 0} / ${x.totalCopies ?? 0}`); available.className = 'text-end'; tr.append(available); catalogRows.append(tr); });
    }
    function renderIssues(items) {
        issueRows.replaceChildren(); if (!items.length) return renderEmpty(issueRows, 7, 'No borrowing records found.');
        items.forEach(x => { const tr = document.createElement('tr'); tr.append(cell(x.bookTitle), cell(x.borrowerName || x.borrowerType || '—'), cell(date(x.issueDate)), cell(date(x.dueDate)), cell(x.actualReturnDate ? date(x.actualReturnDate) : '—'), cell(x.status || '—')); const fine = cell(formatMoney(x.fineAmount)); fine.className = 'text-end'; tr.append(fine); issueRows.append(tr); });
    }
    function renderEmpty(target, span, message) { target.replaceChildren(); const tr = document.createElement('tr'); const td = cell(message); td.colSpan = span; td.className = 'text-center text-muted py-4'; tr.append(td); target.append(tr); }
    function cell(value) { const td = document.createElement('td'); td.textContent = value ?? ''; return td; }
    function date(value) { const d = new Date(value); return Number.isNaN(d.valueOf()) ? '—' : d.toLocaleDateString(); }
    function formatMoney(value) { const n = Number(value || 0); return Number.isFinite(n) ? n.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }) : '0.00'; }
    function clearAlert() { const box = document.getElementById('libraryAlert'); if (box) { box.className = 'd-none'; box.textContent = ''; } }
    function showAlert(message) { const box = document.getElementById('libraryAlert'); if (!box) return; box.className = 'alert alert-danger'; box.textContent = message || 'Library request failed.'; box.focus(); }
})();
