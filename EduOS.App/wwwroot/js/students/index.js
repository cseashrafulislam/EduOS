(() => {
    'use strict';

    const i18n = JSON.parse(document.getElementById('studentStrings')?.textContent || '{}');
    const state = { page: 1, pageSize: 20, totalPages: 1, totalCount: 0, currentPageCount: 0,
        search: '', status: '', listRequest: 0, detailRequest: 0 };
    const initialTenant = document.body?.dataset.authTenantId || '';
    const initialSession = document.body?.dataset.authSessionReference || '';
    const sameSession = () => (document.body?.dataset.authTenantId || '') === initialTenant &&
        (document.body?.dataset.authSessionReference || '') === initialSession;
    let listController = null, detailController = null;

    document.addEventListener('DOMContentLoaded', async () => {
        if (!document.getElementById('studentList')) return;
        document.getElementById('studentFilters')?.addEventListener('submit', applyFilters);
        document.getElementById('studentPageSize')?.addEventListener('change', changePageSize);
        document.getElementById('studentResetFilters')?.addEventListener('click', resetFilters);
        document.getElementById('studentList')?.addEventListener('click', openStudent);
        document.getElementById('studentPrevious')?.addEventListener('click', () => changePage(-1));
        document.getElementById('studentNext')?.addEventListener('click', () => changePage(1));
        document.getElementById('studentExportPage')?.addEventListener('click', exportPage);
        document.getElementById('studentModal')?.addEventListener('hidden.bs.modal', () => {
            ++state.detailRequest;
            detailController?.abort();
            state.currentReference = '';
        });
        const help = document.getElementById('studentDirectoryHelp');
        if (help && window.bootstrap?.Popover) new bootstrap.Popover(help, { trigger: 'focus', container: 'body' });
        await loadStudents();
    }, { once: true });

    async function loadStudents() {
        if (!sameSession()) return;
        const request = ++state.listRequest;
        listController?.abort();
        const controller = new AbortController();
        listController = controller;
        const list = document.getElementById('studentList');
        list.setAttribute('aria-busy', 'true');
        const exportButton = document.getElementById('studentExportPage');
        if (exportButton) exportButton.disabled = true;
        document.getElementById('studentPrevious').disabled = true;
        document.getElementById('studentNext').disabled = true;
        state.currentPageCount = 0;
        list.replaceChildren(message(i18n.loading));
        const params = directoryParams();
        try {
            const response = await fetch(`/api/students?${params}`, { ...apiOptions(), signal: controller.signal });
            const payload = await response.json().catch(() => null);
            if (request !== state.listRequest || controller.signal.aborted || !sameSession()) return;
            if (!response.ok || !payload?.success || !Array.isArray(payload.data?.items)) throw new Error('Invalid students');
            state.totalPages = Math.max(1, Number(payload.data.totalPages) || 1);
            if (state.page > state.totalPages) {
                state.page = state.totalPages;
                return loadStudents();
            }
            state.totalCount = Math.max(0, Number(payload.data.totalCount) || 0);
            state.currentPageCount = payload.data.items.length;
            renderStudents(payload.data.items);
            updatePagination();
            if (exportButton) exportButton.disabled = state.currentPageCount === 0;
        } catch {
            if (request !== state.listRequest || controller.signal.aborted || !sameSession()) return;
            const box = message(i18n.loadFailed);
            const retry = document.createElement('button');
            retry.type = 'button';
            retry.className = 'btn btn-sm btn-outline-primary';
            retry.textContent = i18n.retry || '';
            retry.addEventListener('click', loadStudents, { once: true });
            box.append(retry);
            list.replaceChildren(box);
            document.getElementById('studentPageStatus').textContent = i18n.loadFailed || '';
        } finally {
            if (request === state.listRequest) {
                listController = null;
                list.removeAttribute('aria-busy');
            }
        }
    }

    function renderStudents(items) {
        const list = document.getElementById('studentList');
        if (!items.length) { list.replaceChildren(message(i18n.empty)); return; }
        list.replaceChildren(...items.map(item => {
            const card = document.createElement('article');
            card.className = 'admission-card';
            const heading = document.createElement('div');
            heading.className = 'admission-card-heading';
            const title = document.createElement('h4');
            title.textContent = item.fullNameBangla || item.fullName || '';
            const badge = document.createElement('span');
            badge.className = `admission-status ${item.status === 'Active' ? 'status-7' : 'status-3'}`;
            badge.textContent = item.status || '';
            heading.append(title, badge);
            const button = document.createElement('button');
            button.type = 'button';
            button.className = 'btn btn-sm btn-outline-primary';
            button.dataset.reference = item.reference;
            button.textContent = i18n.view || '';
            card.append(heading, line(i18n.studentCode, item.studentCode), line(i18n.roll, item.rollNo),
                line(i18n.academicUnit, [item.academicLevelName, item.academicBatchName, item.academicYear].filter(Boolean).join(' · ')),
                line(i18n.fullContact, item.maskedMobile), button);
            if (!item.rollNo) {
                const notice = document.createElement('p');
                notice.className = 'small text-muted';
                notice.textContent = i18n.noCurrentEnrollment || 'No current active enrollment; see the student profile for historical rolls.';
                card.append(notice);
            }
            const promotionLink = document.querySelector('[data-student-promotion-link]');
            if (promotionLink && item.status === 'Active') {
                const promote = document.createElement('a');
                promote.className = 'btn btn-sm btn-outline-secondary';
                promote.href = promotionLink.href + (promotionLink.href.includes('?') ? '&' : '?') +
                    'studentReference=' + encodeURIComponent(item.reference);
                promote.textContent = i18n.promote || 'Promote / Repeat';
                card.append(promote);
            }
            return card;
        }));
    }

    async function openStudent(event) {
        const button = event.target.closest('button[data-reference]');
        if (!button || !sameSession()) return;
        const reference = button.dataset.reference;
        const request = ++state.detailRequest;
        detailController?.abort();
        const controller = new AbortController();
        detailController = controller;
        try {
            const response = await fetch(`/api/students/${encodeURIComponent(reference)}`,
                { ...apiOptions(), signal: controller.signal });
            const payload = await response.json().catch(() => null);
            if (request !== state.detailRequest || controller.signal.aborted || !sameSession()) return;
            if (!response.ok || !payload?.success || !payload.data) throw new Error('Invalid student');
            state.currentReference = reference;
            renderDetails(payload.data);
            bootstrap.Modal.getOrCreateInstance(document.getElementById('studentModal')).show();
            document.dispatchEvent(new CustomEvent('student:opened',
                { detail: { reference, rowVersion: payload.data.rowVersion, photoUrl: payload.data.photoUrl } }));
        } catch {
            if (request === state.detailRequest && !controller.signal.aborted && sameSession())
                showAlert(i18n.detailsFailed);
        } finally {
            if (request === state.detailRequest) detailController = null;
        }
    }

    document.addEventListener('student:contact-updated', async event => {
        const reference = event.detail?.reference;
        if (!reference || state.currentReference !== reference) return;
        const request = state.detailRequest;
        try {
            const response = await fetch('/api/students/' + encodeURIComponent(reference), apiOptions());
            const payload = await response.json().catch(() => null);
            if (response.ok && payload?.success && payload.data && state.currentReference === reference &&
                request === state.detailRequest && sameSession())
                renderDetails(payload.data);
        } catch { /* Contact form already reports its own save status. */ }
    });

    document.addEventListener('student:status-updated', async event => {
        const reference = event.detail?.reference;
        if (!reference || state.currentReference !== reference) return;
        const request = state.detailRequest;
        try {
            const response = await fetch('/api/students/' + encodeURIComponent(reference), apiOptions());
            const payload = await response.json().catch(() => null);
            if (response.ok && payload?.success && payload.data && state.currentReference === reference &&
                request === state.detailRequest && sameSession()) {
                renderDetails(payload.data);
                document.dispatchEvent(new CustomEvent('student:opened', {
                    detail: { reference, rowVersion: payload.data.rowVersion, photoUrl: payload.data.photoUrl }
                }));
                await loadStudents();
            }
        } catch { /* The status form reports success; the list can be refreshed manually. */ }
    });

    function renderDetails(item) {
        const rows = [
            [i18n.studentCode, item.studentCode], [i18n.roll, item.rollNo], [i18n.dateOfBirth, formatDate(item.dateOfBirth)],
            [i18n.gender, item.gender], [i18n.fullContact, [item.phone, item.email].filter(Boolean).join(' · ')],
            [i18n.address, item.address], [i18n.academicUnit, [item.academicLevelName, item.academicBatchName, item.academicYear].filter(Boolean).join(' · ')],
            [i18n.admissionDate, formatDate(item.admissionDate)], [i18n.status, item.status]
        ];
        const nodes = [];
        rows.filter(x => x[1]).forEach(x => { const dt = document.createElement('dt'); const dd = document.createElement('dd'); dt.textContent = x[0] || ''; dd.textContent = x[1] || ''; nodes.push(dt, dd); });
        document.getElementById('studentDetails').replaceChildren(...nodes);
        document.getElementById('studentCurrentEnrollmentNotice').classList.toggle('d-none', Boolean(item.rollNo));
        renderRelated('studentGuardians', item.guardians, x => [x.name, x.relation, x.phone].filter(Boolean).join(' · '));
        renderRelated('studentEnrollments', item.enrollments, x => [x.academicYear, x.academicTerm, x.academicLevelName, x.academicBatchName, x.academicTrackName, x.rollNo].filter(Boolean).join(' · '));
    }

    function renderRelated(id, items, format) {
        const target = document.getElementById(id);
        const rows = Array.isArray(items) ? items : [];
        target.replaceChildren(...rows.map(item => { const box = document.createElement('p'); box.className = 'admission-card'; box.textContent = format(item); return box; }));
    }

    function directoryParams() {
        const params = new URLSearchParams({ page: String(state.page), pageSize: String(state.pageSize) });
        if (state.search) params.set('search', state.search);
        if (state.status) params.set('status', state.status);
        return params;
    }
    async function exportPage() {
        const button = document.getElementById('studentExportPage');
        if (!button || button.disabled || !sameSession()) return;
        const request = state.listRequest;
        button.disabled = true;
        try {
            const response = await fetch('/api/students/export-page?' + directoryParams(), {
                method: 'GET', cache: 'no-store', credentials: 'same-origin',
                headers: { Accept: 'text/csv' }
            });
            if (request !== state.listRequest || !sameSession()) return;
            if (!response.ok || !response.headers.get('content-type')?.includes('text/csv'))
                throw new Error('Student directory export failed');
            const blob = await response.blob();
            if (request !== state.listRequest || !sameSession()) return;
            if (!blob.size || blob.size > 2000000) throw new Error('Invalid export size');
            const url = URL.createObjectURL(blob);
            const link = document.createElement('a');
            link.href = url;
            link.download = 'students-page-' + state.page + '.csv';
            document.body.append(link);
            link.click();
            link.remove();
            setTimeout(() => URL.revokeObjectURL(url), 1000);
        } catch {
            if (request === state.listRequest && sameSession()) showAlert(i18n.exportFailed);
        } finally {
            if (request === state.listRequest && sameSession())
                button.disabled = state.currentPageCount === 0;
        }
    }
    function applyFilters(event) {
        event.preventDefault();
        state.search = valueOf('studentSearch');
        state.status = valueOf('studentStatus');
        state.page = 1;
        loadStudents();
    }
    function resetFilters() {
        document.getElementById('studentSearch').value = '';
        document.getElementById('studentStatus').value = '';
        state.search = '';
        state.status = '';
        state.page = 1;
        loadStudents();
    }
    function changePageSize(event) {
        const size = Number(event.target.value);
        if (![20, 50, 100].includes(size)) return;
        state.pageSize = size;
        state.page = 1;
        loadStudents();
    }
    function changePage(delta) { const page = state.page + delta; if (page < 1 || page > state.totalPages) return; state.page = page; loadStudents(); }
    function updatePagination() {
        document.getElementById('studentPrevious').disabled = state.page <= 1;
        document.getElementById('studentNext').disabled = state.page >= state.totalPages;
        document.getElementById('studentPageStatus').textContent =
            `${i18n.page || ''} ${state.page} / ${state.totalPages} · ${state.totalCount} ${i18n.countNoun || ''}`;
    }
    function line(label, value) { const p = document.createElement('p'); const strong = document.createElement('strong'); strong.textContent = `${label || ''}: `; p.append(strong, value || '—'); return p; }
    function message(value) { const box = document.createElement('div'); box.className = 'admission-empty'; const p = document.createElement('p'); p.textContent = value || ''; box.append(p); return box; }
    function showAlert(value) { const alert = document.getElementById('studentAlert'); alert.className = 'alert alert-danger'; alert.textContent = value || ''; alert.focus(); }
    function valueOf(id) { return document.getElementById(id)?.value?.trim() || ''; }
    function formatDate(value) { const date = new Date(value); return Number.isNaN(date.valueOf()) ? '' : new Intl.DateTimeFormat(i18n.culture || 'en-BD', { dateStyle: 'medium' }).format(date); }
    function apiOptions() { return { method: 'GET', cache: 'no-store', credentials: 'same-origin', headers: { Accept: 'application/json' } }; }
})();
