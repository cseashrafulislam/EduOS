(() => {
    'use strict';
    const target = document.getElementById('studentLifecycleTimeline');
    if (!target) return;
    let pending = null;
    let sequence = 0;
    const box = text => {
        const p = document.createElement('p');
        p.className = 'text-muted small mb-2';
        p.textContent = text;
        return p;
    };
    document.addEventListener('student:opened', async event => {
        const reference = event.detail && event.detail.reference;
        if (!reference) return;
        if (pending) pending.abort();
        pending = new AbortController();
        const current = ++sequence;
        target.replaceChildren(box('Loading lifecycle history…'));
        try {
            const response = await fetch('/api/students/' + encodeURIComponent(reference) + '/lifecycle-timeline', {
                method: 'GET', credentials: 'same-origin', cache: 'no-store',
                headers: { Accept: 'application/json' }, signal: pending.signal
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || !payload || !payload.success || !Array.isArray(payload.data?.events))
                throw new Error('Unable to load lifecycle history.');
            if (current !== sequence) return;
            const events = payload.data.events;
            if (!events.length) { target.replaceChildren(box('No recorded lifecycle transitions.')); return; }
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
                    item.fromStatus + ' → ' + item.toStatus : item.detail, item.reason].filter(Boolean).join(' · ');
                if (summary) li.append(box(summary));
                list.append(li);
            }
            const nodes = [list];
            if (payload.data.hasMore) nodes.push(box('Showing the latest 100 events.'));
            target.replaceChildren(...nodes);
        } catch (error) {
            if (current === sequence && error.name !== 'AbortError')
                target.replaceChildren(box('Lifecycle history could not be loaded. Please reopen this student.'));
        }
    });
    document.getElementById('studentModal')?.addEventListener('hidden.bs.modal', () => {
        sequence++;
        if (pending) pending.abort();
        target.replaceChildren();
    });
})();
