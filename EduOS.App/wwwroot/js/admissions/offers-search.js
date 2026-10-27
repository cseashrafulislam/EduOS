import { el, api, notice, load } from './offers-core.js';

const pageSize = 10;
const statuses = { 1: 'Draft', 2: 'Submitted', 3: 'Under review', 4: 'Documents pending',
    5: 'Assessment pending', 6: 'Qualified', 7: 'Rejected', 8: 'Admitted', 9: 'Withdrawn' };
let page = 1, lastPage = 1, query = '', latestSearch = 0;

function renderPage(data) {
    const rows = Array.isArray(data?.items) ? data.items : [];
    const total = Math.max(0, Number(data?.totalCount) || 0);
    lastPage = Math.max(1, Math.ceil(total / pageSize));
    const container = el('offerSearchResults');
    container.replaceChildren();
    if (!rows.length) {
        const empty = document.createElement('p');
        empty.className = 'text-muted small mb-0';
        empty.textContent = 'No applicants match this search.';
        container.append(empty);
    }
    for (const applicant of rows) {
        const item = document.createElement('div');
        item.className = 'd-flex flex-wrap gap-2 justify-content-between align-items-center border-bottom py-2';
        const detail = document.createElement('div');
        const title = document.createElement('strong');
        title.textContent = applicant.applicantName || applicant.applicationNumber || 'Applicant';
        const info = document.createElement('div');
        info.className = 'small text-muted';
        info.textContent = [applicant.applicationNumber, applicant.campusName,
            statuses[Number(applicant.state)] || 'Status unknown'].filter(Boolean).join(' · ');
        detail.append(title, info);
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'btn btn-sm btn-outline-primary';
        button.textContent = 'Open offer';
        button.dataset.reference = applicant.reference;
        item.append(detail, button);
        container.append(item);
    }
    el('offerSearchPrevious').disabled = page <= 1;
    el('offerSearchNext').disabled = page >= lastPage;
    el('offerSearchPage').textContent = `Page ${page} of ${lastPage} · ${total} applicants`;
}

async function search() {
    const token = ++latestSearch;
    const params = new URLSearchParams({ search: query, page: String(page), pageSize: String(pageSize) });
    const results = el('offerSearchResults');
    results.setAttribute('aria-busy', 'true');
    try {
        const data = await api('/api/admission-applications?' + params);
        if (token !== latestSearch) return;
        renderPage(data);
    } catch (error) {
        if (token !== latestSearch) return;
        results.replaceChildren();
        el('offerSearchPage').textContent = '';
        el('offerSearchPrevious').disabled = el('offerSearchNext').disabled = true;
        notice(error.message || 'Applicant search failed.', true);
    } finally {
        if (token === latestSearch) results.removeAttribute('aria-busy');
    }
}

export function initializeOfferSearch() {
    if (!el('offerSearchForm')) return;
    el('offerSearchForm').addEventListener('submit', event => {
        event.preventDefault();
        const term = el('offerSearchTerm').value.trim();
        if (term.length < 2 || term.length > 100) {
            notice('Search by at least two characters of name or application number.', true);
            return;
        }
        query = term; page = 1; search();
    });
    el('offerSearchPrevious').addEventListener('click', () => { if (page > 1) { page--; search(); } });
    el('offerSearchNext').addEventListener('click', () => { if (page < lastPage) { page++; search(); } });
    el('offerSearchResults').addEventListener('click', event => {
        const button = event.target.closest('button[data-reference]');
        if (!button) return;
        el('offerApplicantReference').value = button.dataset.reference;
        load({ preventDefault() {} });
    });
    const reference = new URLSearchParams(window.location.search).get('reference');
    if (reference && /^[\da-f]{8}(-[\da-f]{4}){3}-[\da-f]{12}$/i.test(reference)) {
        el('offerApplicantReference').value = reference;
        load({ preventDefault() {} });
    }
}
