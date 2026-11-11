(() => {
    'use strict';
    const root = document.getElementById('officialEducationBooks');
    if (!root) return;
    const el = id => document.getElementById(id);
    const bn = root.dataset.language === 'bn-BD';
    const say = (en, bd) => bn ? bd : en;
    const allowedHosts = new Set(['nctb.gov.bd', 'nctb.portal.gov.bd', 'ntrca.gov.bd']);
    const state = { entries: [], page: 1, pageSize: 12, loading: true };
    const filters = {
        search: el('officialBookSearch'),
        publisher: el('officialBookPublisher'),
        track: el('officialBookTrack')
    };
    const session = [document.body?.dataset.authTenantId, document.body?.dataset.authSessionReference];
    const sessionValid = () => session[0] === document.body?.dataset.authTenantId &&
        session[1] === document.body?.dataset.authSessionReference &&
        window.EduOSSessionTransitionPending?.() !== true;
    const text = (tag, value, className) => {
        const node = document.createElement(tag);
        if (className) node.className = className;
        node.textContent = value;
        return node;
    };
    const verifiedSource = source => {
        try {
            const url = new URL(source);
            return url.protocol === 'https:' && allowedHosts.has(url.hostname) &&
                !url.username && !url.password && !url.port ? url.href : null;
        } catch { return null; }
    };
    const dataValid = entry => entry && typeof entry.id === 'string' &&
        (entry.authority === 'NCTB' || entry.authority === 'NTRCA') &&
        entry.sourceType === 'official-publisher-page' && entry.localCopy === false &&
        typeof entry.titleBn === 'string' && typeof entry.titleEn === 'string' &&
        typeof entry.track === 'string' && verifiedSource(entry.sourceUrl);

    function filtered() {
        const query = filters.search.value.trim().toLocaleLowerCase();
        return state.entries.filter(x => {
            if (filters.publisher.value && x.authority !== filters.publisher.value) return false;
            if (filters.track.value && x.track !== filters.track.value) return false;
            if (!query) return true;
            return [x.titleBn, x.titleEn, x.track, x.year, x.authority].some(v =>
                String(v || '').toLocaleLowerCase().includes(query));
        });
    }
    function render() {
        if (!sessionValid()) return;
        const list = el('officialBookItems');
        list.replaceChildren();
        const items = filtered();
        const pages = Math.max(1, Math.ceil(items.length / state.pageSize));
        state.page = Math.min(state.page, pages);
        const visible = items.slice((state.page - 1) * state.pageSize, state.page * state.pageSize);
        el('officialBookCount').textContent = say(items.length + ' official source page(s)',
            items.length + 'টি সরকারি উৎস পেজ');
        el('officialBookPage').textContent = say('Page ', 'পৃষ্ঠা ') + state.page + ' / ' + pages;
        el('officialBookPrev').disabled = state.page <= 1;
        el('officialBookNext').disabled = state.page >= pages;
        if (!visible.length) {
            list.append(text('p', say('No matching publisher page. Try a different level or keyword.',
                'কোনো তথ্য পাওয়া যায়নি। অন্য শ্রেণি বা শব্দ দিয়ে খুঁজুন।'), 'p-3 text-muted mb-0'));
            return;
        }
        for (const item of visible) {
            const row = document.createElement('article');
            row.className = 'list-group-item d-flex flex-wrap align-items-center justify-content-between gap-3';
            const desc = document.createElement('div');
            desc.className = 'flex-grow-1';
            desc.append(text('h3', bn ? item.titleBn : item.titleEn, 'h6 fw-semibold mb-1'));
            desc.append(text('p', item.authority + ' · ' + item.track + ' · ' + item.year +
                ' · ' + say('Publisher page (PDF links there)', 'প্রকাশকের পেজে PDF ডাউনলোড লিংক'), 'small text-muted mb-0'));
            const link = text('a', say('Official download page', 'সরকারি ডাউনলোড পেজ'), 'btn btn-sm btn-outline-primary');
            link.href = verifiedSource(item.sourceUrl);
            link.target = '_blank';
            link.rel = 'noopener noreferrer';
            link.referrerPolicy = 'no-referrer';
            row.append(desc, link);
            list.append(row);
        }
    }
    function populateTracks() {
        const distinct = [...new Set(state.entries.filter(x =>
            !filters.publisher.value || x.authority === filters.publisher.value).map(x => x.track))].sort();
        const old = filters.track.value;
        filters.track.replaceChildren(new Option(say('All levels', 'সব স্তর'), ''));
        for (const track of distinct) filters.track.add(new Option(track, track));
        filters.track.value = distinct.includes(old) ? old : '';
    }
    function change() { state.page = 1; render(); }
    filters.publisher.addEventListener('change', () => { populateTracks(); change(); });
    filters.track.addEventListener('change', change);
    filters.search.addEventListener('input', change);
    el('officialBookSearchForm').addEventListener('submit', event => event.preventDefault());
    el('officialBookReset').addEventListener('click', () => {
        filters.search.value = ''; filters.publisher.value = '';
        populateTracks(); filters.track.value = ''; change(); filters.search.focus();
    });
    el('officialBookPrev').addEventListener('click', () => {
        if (state.page > 1) { state.page--; render(); }
    });
    el('officialBookNext').addEventListener('click', () => {
        if (state.page * state.pageSize < filtered().length) { state.page++; render(); }
    });
    fetch(root.dataset.catalogUrl, { credentials: 'same-origin', cache: 'force-cache', headers: { Accept: 'application/json' } })
        .then(response => { if (!response.ok) throw new Error('Catalog unavailable'); return response.json(); })
        .then(data => {
            if (!sessionValid() || data.schemaVersion !== 1 ||
                data.distribution !== 'official-source-links-only' || !Array.isArray(data.entries))
                throw new Error('Invalid source catalog');
            const seen = new Set();
            state.entries = data.entries.filter(item => {
                if (!dataValid(item) || seen.has(item.id)) return false;
                seen.add(item.id); return true;
            });
            state.loading = false;
            populateTracks();
            render();
        })
        .catch(() => {
            if (!sessionValid()) return;
            const host = el('officialBookItems');
            host.replaceChildren(text('p', say('Could not load the official directory. Reload the page to retry.',
                'সরকারি উৎস তালিকা পাওয়া যায়নি। আবার পেজ লোড করুন।'), 'p-3 text-danger mb-0'));
            el('officialBookCount').textContent = '';
        });
})();
