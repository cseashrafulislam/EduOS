(() => {
    'use strict';
    const root = document.getElementById('teacherPublicCatalogue');
    if (!root) return;
    const el = id => document.getElementById(id);
    const bn = document.documentElement.lang.toLowerCase().startsWith('bn');
    const tr = (en, bd) => bn ? bd : en;
    const tenant = document.body?.dataset.authTenantId || '';
    const session = document.body?.dataset.authSessionReference || '';
    const actor = document.body?.dataset.authUserId || '';
    let generation = 0, page = 1, total = 0, controller = null;
    const current = () => document.body?.dataset.authTenantId === tenant &&
        document.body?.dataset.authSessionReference === session &&
        document.body?.dataset.authUserId === actor &&
        window.EduOSSessionTransitionPending?.() !== true;

    function message(value, isError = false) {
        const box = el('publicResourceMessage');
        box.className = isError ? 'alert alert-danger' : 'alert alert-info';
        box.textContent = value;
    }
    function cell(row, value) {
        const td = document.createElement('td');
        td.textContent = value || '—'; row.append(td); return td;
    }
    function render(items) {
        const tbody = el('publicResourceRows'); tbody.replaceChildren();
        if (!items.length) {
            const trRow = document.createElement('tr');
            const td = document.createElement('td'); td.colSpan = 4;
            td.textContent = tr('No approved public source links found.', 'অনুমোদিত উন্মুক্ত উৎস-লিংক পাওয়া যায়নি।');
            trRow.append(td); tbody.append(trRow);
        }
        for (const item of items) {
            const row = document.createElement('tr');
            cell(row, `${item.title || ''} · ${item.kindCode || ''}${item.topic ? ' · ' + item.topic : ''}`);
            cell(row, [item.level, item.subject].filter(Boolean).join(' / '));
            cell(row, [item.institution, item.rightsHolder, item.licenceCode].filter(Boolean).join(' · '));
            const linkCell = document.createElement('td');
            try {
                const url = new URL(item.sourceUrl);
                if (url.protocol !== 'https:' || url.username || url.password ||
                    url.hostname === 'localhost' || url.hostname.endsWith('.local')) throw Error();
                const link = document.createElement('a');
                link.href = url.href; link.target = '_blank'; link.rel = 'noopener noreferrer';
                link.className = 'btn btn-sm btn-outline-primary';
                link.textContent = tr('Open source', 'উৎস খুলুন'); linkCell.append(link);
            } catch { linkCell.textContent = '—'; }
            row.append(linkCell); tbody.append(row);
        }
        el('publicResourcePrev').disabled = page === 1;
        el('publicResourceNext').disabled = page * 20 >= total;
        el('publicResourcePage').textContent = `${page} · ${total}`;
    }
    async function load() {
        ++generation;
        controller?.abort(); controller = new AbortController();
        const nonce = generation, signal = controller.signal;
        const query = new URLSearchParams({ page: String(page), pageSize: '20' });
        const term = el('publicResourceTerm').value.trim();
        if (term) query.set('search', term);
        try {
            const response = await fetch('/api/lms/teacher-resources/public?' + query, {
                cache: 'no-store', credentials: 'same-origin', signal
            });
            const payload = await response.json();
            if (!response.ok || !payload?.success || !payload.data) throw Error(payload?.message || 'Access denied.');
            if (nonce !== generation || !current()) return;
            total = payload.data.totalCount || 0;
            render(payload.data.items || []);
            el('publicResourceMessage').className = 'd-none';
        } catch (error) {
            if (signal.aborted || nonce !== generation || !current()) return;
            message(error.message || tr('Unable to load resources.', 'শিক্ষাসামগ্রী লোড করা যায়নি।'), true);
        }
    }
    el('publicResourceSearch').addEventListener('submit', event => {
        event.preventDefault(); page = 1; load();
    });
    el('publicResourcePrev').addEventListener('click', () => { if (page > 1) { page--; load(); } });
    el('publicResourceNext').addEventListener('click', () => {
        if (page * 20 < total) { page++; load(); }
    });
    const observer = new MutationObserver(() => {
        if (current()) return;
        ++generation; controller?.abort(); root.replaceChildren(); observer.disconnect();
    });
    observer.observe(document.body, { attributes: true,
        attributeFilter: ['data-auth-session-reference', 'data-auth-tenant-id', 'data-auth-user-id'] });
    load();
})();
