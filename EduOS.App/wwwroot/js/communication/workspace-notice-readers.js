import { el, admin, api, cell, dateText, alert } from './workspace-core.js?v=1';

let reference = null, page = 1, pages = 1, generation = 0;
async function loadReaders() {
    if (!admin || !reference) return;
    const request = ++generation, selected = reference;
    try {
        const params = new URLSearchParams({ page: String(page), pageSize: '20' });
        const data = await api('/api/communication/notices/' + encodeURIComponent(selected) + '/read-receipts?' + params);
        if (request !== generation || selected !== reference) return;
        pages = Math.max(1, Number(data?.totalPages) || 1);
        const rows = el('cwReaderRows'); rows.replaceChildren();
        for (const row of data?.items || []) {
            const tr = document.createElement('tr');
            tr.append(cell(row.userName), cell(row.userId), cell(dateText(row.readAt)));
            rows.append(tr);
        }
        if (!(data?.items || []).length) {
            const tr = document.createElement('tr'), td = cell('No recorded acknowledgements.');
            td.colSpan = 3; tr.append(td); rows.append(tr);
        }
        el('cwReaderPage').textContent = 'Page ' + page + ' of ' + pages + ' · ' + (data?.totalCount || 0) + ' recorded';
        el('cwReaderPrev').disabled = page <= 1;
        el('cwReaderNext').disabled = page >= pages;
    } catch (error) { if (request === generation) alert(error.message); }
}
export function showNoticeReaders(row) {
    if (!admin || !row?.reference) return;
    reference = row.reference; page = 1; pages = 1;
    el('cwReaderTitle').textContent = 'Read receipts: ' + row.title;
    el('cwReaderPanel').hidden = false;
    loadReaders();
}
export function initNoticeReaders() {
    if (!admin) return;
    el('cwReaderClose').addEventListener('click', () => {
        reference = null; generation++; el('cwReaderPanel').hidden = true;
    });
    el('cwReaderPrev').addEventListener('click', () => {
        if (page > 1) { page--; loadReaders(); }
    });
    el('cwReaderNext').addEventListener('click', () => {
        if (page < pages) { page++; loadReaders(); }
    });
}
