(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const rows = el('digitalRows');
    if (!rows) return;
    const bn = document.documentElement.lang?.toLowerCase().startsWith('bn');
    const words = (en, bd) => bn ? bd : en;
    const admin = !!el('digitalUploadForm');
    const initialSession = document.body?.dataset.authSessionReference || '';
    const initialTenant = document.body?.dataset.authTenantId || '';
    const activeRequests = new Set();
    const currentSession = () => /^[0-9A-F]{64}$/.test(initialSession) &&
        document.body?.dataset.authSessionReference === initialSession &&
        document.body?.dataset.authTenantId === initialTenant &&
        window.EduOSSessionTransitionPending?.() !== true;
    const sessionError = () => Error(words('Your session changed. Sign in again.',
        'আপনার সেশন পরিবর্তিত হয়েছে। আবার লগইন করুন।'));
    async function sessionFetch(url, options, consume) {
        if (!currentSession()) throw sessionError();
        const controller = new AbortController();
        activeRequests.add(controller);
        try {
            const response = await fetch(url, { ...options, signal: controller.signal });
            if (!currentSession()) throw sessionError();
            const data = await consume(response);
            if (!currentSession()) throw sessionError();
            return data;
        } finally { activeRequests.delete(controller); }
    }
    let page = 1, requestSequence = 0, uploadRequestId = null, editing = null;
    const pageSize = 20;
    const token = () => document.querySelector('meta[name="request-verification-token"]')?.content ||
        el('digitalUploadForm')?.querySelector('input[name="__RequestVerificationToken"]')?.value;
    function notify(message, success = false) {
        if (!currentSession()) return;
        const alert = el('digitalAlert');
        alert.className = 'alert alert-' + (success ? 'success' : 'danger');
        alert.textContent = message;
        alert.focus();
    }
    function clearAlert() {
        const alert = el('digitalAlert');
        alert.className = 'd-none'; alert.textContent = '';
    }
    async function api(url, options = {}) {
        const method = options.method || 'GET';
        const headers = { Accept: 'application/json', ...(options.headers || {}) };
        if (method !== 'GET') {
            const csrf = token();
            if (!csrf) throw Error(words('Security token missing. Reload the page.', 'নিরাপত্তা টোকেন নেই। পৃষ্ঠা পুনরায় খুলুন।'));
            headers.RequestVerificationToken = csrf;
        }
        return sessionFetch(url, { ...options, method, headers, cache: 'no-store',
            credentials: 'same-origin' }, async response => {
            const result = await response.json().catch(() => null);
            if (!response.ok || !result?.success)
                throw Error(result?.message || words('Request failed.', 'অনুরোধ ব্যর্থ হয়েছে।'));
            return result.data;
        });
    }
    function td(value) {
        const cell = document.createElement('td');
        cell.textContent = value == null || value === '' ? '—' : String(value);
        return cell;
    }
    function empty(message) {
        rows.replaceChildren();
        const tr = document.createElement('tr'), cell = td(message);
        cell.colSpan = 6; cell.className = 'text-center text-muted py-4';
        tr.append(cell); rows.append(tr);
    }
    function params() {
        const q = new URLSearchParams({ page: String(page), pageSize: String(pageSize),
            search: el('digitalQuery').value.trim() });
        if (admin && el('digitalStatus').value === 'all') q.set('includeInactive', 'true');
        return q;
    }
    async function download(resource, button) {
        if (!currentSession()) return;
        button.disabled = true;
        try {
            const file = await sessionFetch('/api/library/digital/' +
                encodeURIComponent(resource.reference) + '/download',
                { credentials: 'same-origin', cache: 'no-store',
                    headers: { Accept: 'application/octet-stream' } }, async response => {
                    if (!response.ok) {
                        const error = await response.json().catch(() => null);
                        throw Error(error?.message || words('Download denied.', 'ডাউনলোডের অনুমতি নেই।'));
                    }
                    return response.blob();
                });
            if (!currentSession()) return;
            const url = URL.createObjectURL(file);
            try {
                if (!currentSession()) return;
                const anchor = document.createElement('a');
                anchor.href = url;
                anchor.download = (resource.fileName || 'library-resource').split(/[\\/]/).pop().replace(/[\x00-\x1f\x7f]/g, '') || 'library-resource';
                document.body.append(anchor); anchor.click(); anchor.remove();
            } finally { setTimeout(() => URL.revokeObjectURL(url), 1000); }
        } catch (error) {
            if (currentSession() && error.name !== 'AbortError') notify(error.message);
        } finally { button.disabled = false; }
    }
    async function revoke(resource, button) {
        if (!window.confirm(words('Revoke access to this resource?', 'এই রিসোর্সের অনুমতি বাতিল করবেন?'))) return;
        button.disabled = true;
        try {
            await api('/api/library/digital/' + encodeURIComponent(resource.reference) + '/revoke', {
                method: 'POST', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ rowVersion: resource.rowVersion })
            });
            await load();
            notify(words('Access revoked.', 'অনুমতি বাতিল হয়েছে।'), true);
        } catch (error) { notify(error.message); button.disabled = false; }
    }
    function edit(resource) {
        if (!currentSession()) return;
        editing = resource;
        el('digitalEditName').value = resource.title || '';
        el('digitalEditExpiry').value = resource.expiresOn || '';
        el('digitalEditDialog').showModal();
    }
    async function saveEdit(event) {
        event.preventDefault();
        if (!editing || !el('digitalEditForm').reportValidity()) return;
        const button = el('digitalEditSave');
        button.disabled = true;
        try {
            await api('/api/library/digital/' + encodeURIComponent(editing.reference), {
                method: 'PUT', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    title: el('digitalEditName').value.trim(),
                    expiresOn: el('digitalEditExpiry').value || null,
                    rowVersion: editing.rowVersion
                })
            });
            el('digitalEditDialog').close(); editing = null;
            await load();
            notify(words('Resource updated.', 'রিসোর্স পরিবর্তন হয়েছে।'), true);
        } catch (error) { notify(error.message); }
        finally { button.disabled = false; }
    }
    function render(resources) {
        rows.replaceChildren();
        if (!resources.length) return empty(words('No resources found.', 'কোনো রিসোর্স পাওয়া যায়নি।'));
        resources.forEach(resource => {
            const tr = document.createElement('tr');
            const expired = !!resource.expiresOn && resource.expiresOn < new Date().toISOString().slice(0, 10);
            [resource.bookTitle, resource.title, resource.fileName,
                !resource.isActive ? words('Revoked', 'বাতিল') :
                    expired ? words('Expired', 'মেয়াদ শেষ') : words('Active', 'সক্রিয়'),
                resource.expiresOn || '—'].forEach(value => tr.append(td(value)));
            const actions = document.createElement('td');
            if (resource.isActive && !expired) {
                const button = document.createElement('button');
                button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary';
                button.textContent = words('Download', 'ডাউনলোড');
                button.addEventListener('click', () => download(resource, button));
                actions.append(button);
            }
            if (admin && resource.isActive) {
                const editButton = document.createElement('button');
                editButton.type = 'button'; editButton.className = 'btn btn-sm btn-outline-secondary ms-2';
                editButton.textContent = words('Edit', 'সম্পাদনা');
                editButton.addEventListener('click', () => edit(resource));
                actions.append(editButton);
                const button = document.createElement('button');
                button.type = 'button'; button.className = 'btn btn-sm btn-outline-danger ms-2';
                button.textContent = words('Revoke', 'বাতিল');
                button.addEventListener('click', () => revoke(resource, button));
                actions.append(button);
            }
            tr.append(actions); rows.append(tr);
        });
    }
    async function load() {
        const sequence = ++requestSequence;
        rows.replaceChildren();
        el('digitalCount').textContent = '';
        el('digitalPrevious').disabled = true;
        el('digitalNext').disabled = true;
        try {
            const result = await api('/api/library/digital?' + params());
            if (sequence !== requestSequence || !currentSession()) return;
            render(Array.isArray(result?.items) ? result.items : []);
            el('digitalCount').textContent = String(result?.totalCount ?? 0);
            el('digitalPage').textContent = words('Page ', 'পৃষ্ঠা ') + page +
                ' / ' + Math.max(1, result?.totalPages ?? 1);
            el('digitalPrevious').disabled = !result?.hasPreviousPage;
            el('digitalNext').disabled = !result?.hasNextPage;
        } catch (error) {
            if (sequence !== requestSequence || !currentSession()) return;
            empty(words('Unable to load resources.', 'রিসোর্স লোড করা যায়নি।'));
            notify(error.message);
        }
    }
    async function findBooks() {
        const select = el('digitalBook');
        select.replaceChildren(new Option(words('Select a book', 'বই নির্বাচন করুন'), ''));
        try {
            const query = el('digitalBookSearch').value.trim();
            const data = await api('/api/library/catalog?' + new URLSearchParams({
                search: query, page: '1', pageSize: '30'
            }));
            (data?.items || []).forEach(book => select.add(new Option(book.title, book.reference)));
        } catch (error) { notify(error.message); }
    }
    async function publish(event) {
        event.preventDefault();
        const form = el('digitalUploadForm');
        if (!form.reportValidity()) return;
        const file = el('digitalFile').files?.[0];
        if (!file || file.size < 1 || file.size > 5242880)
            return notify(words('Select a file up to 5 MB.', 'সর্বোচ্চ ৫ এমবি ফাইল নির্বাচন করুন।'));
        if (!uploadRequestId) uploadRequestId = crypto.randomUUID();
        const data = new FormData();
        data.append('RequestId', uploadRequestId);
        data.append('BookReference', el('digitalBook').value);
        data.append('Title', el('digitalTitle').value.trim());
        if (el('digitalExpiresOn').value) data.append('ExpiresOn', el('digitalExpiresOn').value);
        data.append('file', file, file.name);
        const button = el('digitalPublish');
        button.disabled = true;
        try {
            await api('/api/library/digital', { method: 'POST', body: data });
            form.reset(); uploadRequestId = null; clearAlert();
            await load();
            notify(words('Resource published.', 'রিসোর্স প্রকাশিত হয়েছে।'), true);
        } catch (error) { notify(error.message); }
        finally { button.disabled = false; }
    }
    async function exportPage() {
        const button = el('digitalExport');
        if (!currentSession()) return;
        button.disabled = true;
        try {
            const query = params(); query.set('language', bn ? 'bn-BD' : 'en-BD');
            const file = await sessionFetch('/api/library/digital/export?' + query,
                { credentials: 'same-origin', cache: 'no-store' }, async response => {
                    if (!response.ok) {
                        const error = await response.json().catch(() => null);
                        throw Error(error?.message || words('Export denied.', 'এক্সপোর্টের অনুমতি নেই।'));
                    }
                    return response.blob();
                });
            if (!currentSession()) return;
            const url = URL.createObjectURL(file);
            try {
                if (!currentSession()) return;
                const a = document.createElement('a');
                a.href = url; a.download = 'library-digital-page.csv';
                document.body.append(a); a.click(); a.remove();
            } finally { setTimeout(() => URL.revokeObjectURL(url), 1000); }
        } catch (error) {
            if (currentSession() && error.name !== 'AbortError') notify(error.message);
        } finally { button.disabled = false; }
    }
    el('digitalSearchForm').addEventListener('submit', event => {
        event.preventDefault(); page = 1; clearAlert(); load();
    });
    el('digitalPrevious').addEventListener('click', () => { if (page > 1) { page--; load(); } });
    el('digitalNext').addEventListener('click', () => { page++; load(); });
    if (admin) {
        el('digitalBookFind').addEventListener('click', findBooks);
        el('digitalUploadForm').addEventListener('submit', publish);
        ['digitalFile', 'digitalTitle', 'digitalBook', 'digitalExpiresOn'].forEach(id =>
            el(id).addEventListener('change', () => { uploadRequestId = null; }));
        el('digitalExport').addEventListener('click', exportPage);
        el('digitalEditForm').addEventListener('submit', saveEdit);
        el('digitalEditCancel').addEventListener('click', () => el('digitalEditDialog').close());
        el('digitalEditDialog').addEventListener('close', () => { editing = null; });
        findBooks();
    }
    function clearSensitive() {
        requestSequence++;
        for (const request of activeRequests) request.abort();
        activeRequests.clear();
        uploadRequestId = null; editing = null;
        rows.replaceChildren();
        el('digitalCount').textContent = '';
        el('digitalPage').textContent = '';
        el('digitalPrevious').disabled = true;
        el('digitalNext').disabled = true;
        el('digitalQuery').value = '';
        clearAlert();
        if (admin) {
            el('digitalUploadForm').reset();
            el('digitalBook').replaceChildren();
            el('digitalBookSearch').value = '';
            el('digitalEditForm').reset();
            if (el('digitalEditDialog').open) el('digitalEditDialog').close();
        }
    }
    const sessionObserver = new MutationObserver(() => {
        if (!currentSession()) {
            clearSensitive();
            sessionObserver.disconnect();
        }
    });
    sessionObserver.observe(document.body, { attributes: true,
        attributeFilter: ['data-auth-session-reference', 'data-auth-tenant-id'] });
    window.addEventListener('pagehide', clearSensitive);
    window.addEventListener('focus', () => { if (!currentSession()) clearSensitive(); });
    document.addEventListener('visibilitychange', () => {
        if (!currentSession()) clearSensitive();
    });
    window.addEventListener('pageshow', event => {
        if (!currentSession()) clearSensitive();
        else if (event.persisted) {
            if (admin) findBooks();
            load();
        }
    });
    if (currentSession()) load();
    else clearSensitive();
})();
