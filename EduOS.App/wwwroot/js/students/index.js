(() => {
    'use strict';

    const i18n = JSON.parse(document.getElementById('studentStrings')?.textContent || '{}');
    const state = { page: 1, pageSize: 20, totalPages: 1 };

    document.addEventListener('DOMContentLoaded', async () => {
        if (!document.getElementById('studentList')) return;
        document.getElementById('studentFilters')?.addEventListener('submit', applyFilters);
        document.getElementById('studentList')?.addEventListener('click', openStudent);
        document.getElementById('studentPrevious')?.addEventListener('click', () => changePage(-1));
        document.getElementById('studentNext')?.addEventListener('click', () => changePage(1));
        await loadStudents();
    }, { once: true });

    async function loadStudents() {
        const list = document.getElementById('studentList');
        list.setAttribute('aria-busy', 'true');
        list.replaceChildren(message(i18n.loading));
        const params = new URLSearchParams({ page: String(state.page), pageSize: String(state.pageSize) });
        if (valueOf('studentSearch')) params.set('search', valueOf('studentSearch'));
        if (valueOf('studentStatus')) params.set('status', valueOf('studentStatus'));
        try {
            const response = await fetch(`/api/students?${params}`, apiOptions());
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !Array.isArray(payload.data?.items)) throw new Error('Invalid students');
            state.totalPages = Math.max(1, Number(payload.data.totalPages) || 1);
            renderStudents(payload.data.items);
            updatePagination();
        } catch {
            const box = message(i18n.loadFailed);
            const retry = document.createElement('button');
            retry.type = 'button';
            retry.className = 'btn btn-sm btn-outline-primary';
            retry.textContent = i18n.retry || '';
            retry.addEventListener('click', loadStudents, { once: true });
            box.append(retry);
            list.replaceChildren(box);
        } finally {
            list.removeAttribute('aria-busy');
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
            card.append(heading, line(i18n.studentCode, item.studentCode), line(i18n.roll, item.roll),
                line(i18n.academicUnit, [item.academicUnit, item.section, item.academicYear].filter(Boolean).join(' · ')),
                line(i18n.fullContact, item.maskedMobile), button);
            return card;
        }));
    }

    async function openStudent(event) {
        const button = event.target.closest('button[data-reference]');
        if (!button) return;
        try {
            const response = await fetch(`/api/students/${encodeURIComponent(button.dataset.reference)}`, apiOptions());
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !payload.data) throw new Error('Invalid student');
            renderDetails(payload.data);
            bootstrap.Modal.getOrCreateInstance(document.getElementById('studentModal')).show();
        } catch { showAlert(i18n.detailsFailed); }
    }

    function renderDetails(item) {
        const rows = [
            [i18n.studentCode, item.studentCode], [i18n.roll, item.roll], [i18n.dateOfBirth, formatDate(item.dateOfBirth)],
            [i18n.gender, item.gender], [i18n.fullContact, [item.phone, item.email].filter(Boolean).join(' · ')],
            [i18n.address, item.address], [i18n.academicUnit, [item.academicUnit, item.section, item.academicYear].filter(Boolean).join(' · ')],
            [i18n.admissionDate, formatDate(item.admissionDate)], [i18n.status, item.status]
        ];
        const nodes = [];
        rows.filter(x => x[1]).forEach(x => { const dt = document.createElement('dt'); const dd = document.createElement('dd'); dt.textContent = x[0] || ''; dd.textContent = x[1] || ''; nodes.push(dt, dd); });
        document.getElementById('studentDetails').replaceChildren(...nodes);
        renderRelated('studentGuardians', item.guardians, x => [x.name, x.relation, x.phone].filter(Boolean).join(' · '));
        renderRelated('studentEnrollments', item.enrollments, x => [x.academicYear, x.academicTerm, x.academicUnit, x.section, x.group, x.roll].filter(Boolean).join(' · '));
    }

    function renderRelated(id, items, format) {
        const target = document.getElementById(id);
        const rows = Array.isArray(items) ? items : [];
        target.replaceChildren(...rows.map(item => { const box = document.createElement('p'); box.className = 'admission-card'; box.textContent = format(item); return box; }));
    }

    function applyFilters(event) { event.preventDefault(); state.page = 1; loadStudents(); }
    function changePage(delta) { const page = state.page + delta; if (page < 1 || page > state.totalPages) return; state.page = page; loadStudents(); }
    function updatePagination() {
        document.getElementById('studentPrevious').disabled = state.page <= 1;
        document.getElementById('studentNext').disabled = state.page >= state.totalPages;
        document.getElementById('studentPageStatus').textContent = `${i18n.page || ''} ${state.page} / ${state.totalPages}`;
    }
    function line(label, value) { const p = document.createElement('p'); const strong = document.createElement('strong'); strong.textContent = `${label || ''}: `; p.append(strong, value || '—'); return p; }
    function message(value) { const box = document.createElement('div'); box.className = 'admission-empty'; const p = document.createElement('p'); p.textContent = value || ''; box.append(p); return box; }
    function showAlert(value) { const alert = document.getElementById('studentAlert'); alert.className = 'alert alert-danger'; alert.textContent = value || ''; alert.focus(); }
    function valueOf(id) { return document.getElementById(id)?.value?.trim() || ''; }
    function formatDate(value) { const date = new Date(value); return Number.isNaN(date.valueOf()) ? '' : new Intl.DateTimeFormat(i18n.culture || 'en-BD', { dateStyle: 'medium' }).format(date); }
    function apiOptions() { return { method: 'GET', cache: 'no-store', credentials: 'same-origin', headers: { Accept: 'application/json' } }; }
})();
