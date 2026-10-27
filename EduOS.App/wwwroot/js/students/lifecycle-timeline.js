(() => {
    'use strict';
    const target = document.getElementById('studentLifecycleTimeline');
    const previous = document.getElementById('studentLifecyclePrevious');
    const next = document.getElementById('studentLifecycleNext');
    const pageLabel = document.getElementById('studentLifecyclePage');
    if (!target || !previous || !next || !pageLabel) return;
    const pageSize = 20;
    let reference = null;
    let page = 1;
    let hasMore = false;
    let pending = null;
    let sequence = 0;
    const box = message => {
        const p = document.createElement('p');
        p.className = 'text-muted small mb-2';
        p.textContent = message;
        return p;
    };
    const controls = (loading = false) => {
        previous.disabled = loading || !reference || page <= 1;
        next.disabled = loading || !reference || !hasMore;
        pageLabel.textContent = reference ? 'Page ' + page : '';
    };
    const load = async () => {
        if (!reference) return;
        if (pending) pending.abort();
        pending = new AbortController();
        const request = pending;
        const current = ++sequence;
        controls(true);
        target.replaceChildren(box('Loading lifecycle history…'));
        try {
            const query = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
            const response = await fetch('/api/students/' + encodeURIComponent(reference) +
                '/lifecycle-timeline?' + query.toString(), {
                method: 'GET', credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'application/json' }, signal: request.signal
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload?.success || !Array.isArray(payload.data?.events))
                throw new Error('Unable to load lifecycle history.');
            if (current !== sequence || request !== pending) return;
            const events = payload.data.events;
            hasMore = payload.data.hasMore === true;
            if (!events.length) {
                target.replaceChildren(box('No lifecycle events on this page.'));
            } else {
                const list = document.createElement('ol');
                list.className = 'list-group list-group-numbered';
                for (const item of events) {
                    const li = document.createElement('li');
                    li.className = 'list-group-item';
                    const title = document.createElement('strong');
                    title.textContent = String(item.eventType || 'Lifecycle event');
                    const when = document.createElement('span');
                    when.className = 'text-muted ms-2';
                    const date = new Date(item.occurredAt);
                    when.textContent = Number.isNaN(date.getTime()) ? '' : date.toLocaleString();
                    li.append(title, when);
                    const summary = [item.fromStatus && item.toStatus ?
                        item.fromStatus + ' → ' + item.toStatus : item.detail, item.reason]
                        .filter(Boolean).join(' · ');
                    if (summary) li.append(box(summary));
                    list.append(li);
                }
                target.replaceChildren(list);
            }
        } catch (error) {
            if (current === sequence && error.name !== 'AbortError') {
                hasMore = false;
                target.replaceChildren(box('Lifecycle history could not be loaded. Please retry.'));
            }
        } finally {
            if (current === sequence) controls();
        }
    };
    document.addEventListener('student:opened', event => {
        reference = event.detail?.reference || null;
        page = 1;
        hasMore = false;
        if (reference) void load();
        else { sequence++; pending?.abort(); target.replaceChildren(); controls(); }
    });
    previous.addEventListener('click', () => {
        if (reference && page > 1) { page--; void load(); }
    });
    next.addEventListener('click', () => {
        if (reference && hasMore) { page++; void load(); }
    });
    document.getElementById('studentModal')?.addEventListener('hidden.bs.modal', () => {
        sequence++;
        pending?.abort();
        pending = null;
        reference = null;
        page = 1;
        hasMore = false;
        target.replaceChildren();
        controls();
    });
    controls();
})();
