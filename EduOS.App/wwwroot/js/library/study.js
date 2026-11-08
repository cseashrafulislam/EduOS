(() => {
    'use strict';
    const el = id => document.getElementById(id);
    const rows = el('studyRows');
    if (!rows) return;
    const bn = document.documentElement.lang?.toLowerCase().startsWith('bn');
    const label = (en, bd) => bn ? bd : en;
    const originalSession = document.body?.dataset.authSessionReference || '';
    const originalTenant = document.body?.dataset.authTenantId || '';
    const active = new Set();
    let sequence = 0, page = 1;
    const validSession = () => /^[0-9A-F]{64}$/.test(originalSession) &&
        document.body?.dataset.authSessionReference === originalSession &&
        document.body?.dataset.authTenantId === originalTenant &&
        window.EduOSSessionTransitionPending?.() !== true;
    const token = () => document.querySelector('meta[name="request-verification-token"]')?.content ||
        el('studyFilters').querySelector('input[name="__RequestVerificationToken"]')?.value;
    function notice(value, success = false) {
        if (!validSession()) return;
        const alert = el('studyAlert');
        alert.className = 'alert alert-' + (success ? 'success' : 'danger') + ' mt-2';
        alert.textContent = value; alert.focus();
    }
    async function request(url, options = {}) {
        if (!validSession()) throw Error(label('Session changed. Sign in again.', 'সেশন বদলেছে। আবার লগইন করুন।'));
        const controller = new AbortController(); active.add(controller);
        try {
            const method = options.method || 'GET';
            const headers = { Accept: 'application/json', ...(options.headers || {}) };
            if (method !== 'GET') {
                const csrf = token();
                if (!csrf) throw Error(label('Security token missing.', 'নিরাপত্তা টোকেন পাওয়া যায়নি।'));
                headers.RequestVerificationToken = csrf;
            }
            const response = await fetch(url, { ...options, method, headers,
                credentials: 'same-origin', cache: 'no-store', signal: controller.signal });
            if (!validSession()) throw Error('Session changed');
            const payload = await response.json().catch(() => null);
            if (!validSession()) throw Error('Session changed');
            if (!response.ok || !payload?.success)
                throw Error(payload?.message || label('Request rejected.', 'অনুরোধ গ্রহণ হয়নি।'));
            return payload.data;
        } finally { active.delete(controller); }
    }
    function buildParams() {
        const params = new URLSearchParams({ page: String(page), pageSize: '20' });
        for (const [field, key] of [['studyExam', 'examCode'], ['studyBoard', 'boardCode'],
            ['studyYear', 'syllabusYear'], ['studyPaperYear', 'previousPaperYear'],
            ['studySearch', 'search'], ['studyChapter', 'chapter']]) {
            const value = el(field).value.trim();
            if (value) params.set(key, value);
        }
        return params;
    }
    function tag(name, value) {
        const span = document.createElement('span');
        span.className = 'badge bg-light text-dark border me-1 mb-1';
        span.textContent = name + ': ' + value;
        return span;
    }
    function safeHttps(value) {
        try { const url = new URL(value); return url.protocol === 'https:' && !url.username && !url.password; }
        catch { return false; }
    }
    function card(item, saved) {
        const wrapper = document.createElement('div'); wrapper.className = 'col-md-6 col-xl-4';
        const article = document.createElement('article');
        article.className = 'card h-100 shadow-sm';
        const body = document.createElement('div'); body.className = 'card-body';
        const heading = document.createElement('h4'); heading.className = 'h6';
        heading.textContent = item.title || item.bookTitle; body.append(heading);
        const book = document.createElement('p'); book.className = 'text-muted small mb-2';
        book.textContent = item.bookTitle; body.append(book);
        for (const [name, value] of [[label('Exam', 'পরীক্ষা'), item.examCode],
            [label('Board', 'বোর্ড'), item.boardCode],
            [label('Syllabus', 'সিলেবাস'), item.syllabusYear &&
                String(item.syllabusYear) + ' / ' + (item.syllabusVersion || '')],
            [label('Chapter', 'অধ্যায়'), item.chapter], [label('Topic', 'টপিক'), item.topic],
            [label('Past paper', 'পুরোনো প্রশ্ন'), item.previousPaperYear]])
            if (value != null && value !== '') body.append(tag(name, value));
        if (item.studyExplanation) {
            const explanation = document.createElement('p');
            explanation.className = 'small mt-2';
            explanation.textContent = item.studyExplanation; body.append(explanation);
        }
        const actions = document.createElement('div');
        actions.className = 'd-flex flex-wrap gap-2 mt-3';
        if (['application/pdf', 'image/jpeg', 'image/png', 'image/webp'].includes(item.contentType)) {
            const read = document.createElement('a');
            read.className = 'btn btn-sm btn-outline-primary';
            read.href = '/api/library/digital/' + encodeURIComponent(item.reference) + '/read';
            read.target = '_blank'; read.rel = 'noopener noreferrer';
            read.textContent = label('Read', 'পড়ুন'); actions.append(read);
        }
        if (safeHttps(item.syllabusSourceUrl)) {
            const source = document.createElement('a');
            source.className = 'btn btn-sm btn-outline-secondary';
            source.href = item.syllabusSourceUrl; source.target = '_blank';
            source.rel = 'noopener noreferrer';
            source.textContent = label('Syllabus source', 'সিলেবাসের উৎস'); actions.append(source);
        }
        const bookmark = document.createElement('button');
        bookmark.type = 'button'; bookmark.className = 'btn btn-sm btn-outline-success';
        const setLabel = () => {
            bookmark.textContent = saved ? label('Bookmarked', 'সংরক্ষিত') : label('Bookmark', 'সংরক্ষণ');
            bookmark.setAttribute('aria-pressed', String(saved));
        };
        setLabel();
        bookmark.addEventListener('click', async () => {
            if (!validSession()) return;
            bookmark.disabled = true;
            try {
                saved = await request('/api/library/digital/study/bookmarks/' +
                    encodeURIComponent(item.reference), {
                    method: 'PUT', headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ bookmarked: !saved })
                });
                if (validSession()) setLabel();
            } catch (error) { if (validSession() && error.name !== 'AbortError') notice(error.message); }
            finally { bookmark.disabled = false; }
        });
        actions.append(bookmark); body.append(actions); article.append(body); wrapper.append(article);
        return wrapper;
    }
    async function load() {
        const current = ++sequence;
        rows.replaceChildren(); el('studyCount').textContent = '';
        el('studyPrev').disabled = true; el('studyNext').disabled = true;
        try {
            const result = await request('/api/library/digital/study?' + buildParams());
            if (current !== sequence || !validSession()) return;
            const items = result?.items || [];
            const bookmarked = items.length ? await request('/api/library/digital/study/bookmarks?' +
                new URLSearchParams(items.map(item => ['references', item.reference]))) : [];
            if (current !== sequence || !validSession()) return;
            const saved = new Set(bookmarked);
            for (const item of items) rows.append(card(item, saved.has(item.reference)));
            if (!items.length) {
                const empty = document.createElement('p');
                empty.className = 'text-muted mb-0';
                empty.textContent = label('No approved study resources found.', 'অনুমোদিত উপকরণ পাওয়া যায়নি।');
                rows.append(empty);
            }
            el('studyCount').textContent = String(result?.totalCount ?? 0);
            el('studyPage').textContent = page + ' / ' + Math.max(1, result?.totalPages || 1);
            el('studyPrev').disabled = !result?.hasPreviousPage;
            el('studyNext').disabled = !result?.hasNextPage;
        } catch (error) { if (current === sequence && validSession() && error.name !== 'AbortError') notice(error.message); }
    }
    el('studyFilters').addEventListener('submit', event => { event.preventDefault(); page = 1; load(); });
    el('studyClear').addEventListener('click', () => {
        el('studyFilters').reset(); page = 1; load();
    });
    el('studyPrev').addEventListener('click', () => { if (page > 1) { page--; load(); } });
    el('studyNext').addEventListener('click', () => { page++; load(); });
    function clear() {
        sequence++;
        for (const controller of active) controller.abort();
        active.clear(); rows.replaceChildren();
        el('studyCount').textContent = ''; el('studyPage').textContent = '';
        el('studyPrev').disabled = true; el('studyNext').disabled = true;
    }
    const observer = new MutationObserver(() => {
        if (!validSession()) { clear(); observer.disconnect(); }
    });
    observer.observe(document.body, { attributes: true,
        attributeFilter: ['data-auth-session-reference', 'data-auth-tenant-id'] });
    window.addEventListener('pagehide', clear);
    document.addEventListener('visibilitychange', () => { if (!validSession()) clear(); });
    if (validSession()) load(); else clear();
})();
