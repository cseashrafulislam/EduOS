(() => {
    'use strict';
    const el = id => document.getElementById(id);
    if (!el('assistantForm')) return;
    const bn = document.documentElement.lang?.toLowerCase().startsWith('bn');
    const say = (en, bd) => bn ? bd : en;
    const session = document.body?.dataset.authSessionReference || '';
    const tenant = document.body?.dataset.authTenantId || '';
    const active = new Set();
    let sequence = 0, reviewSequence = 0, sourcePage = 1, reviewSourcePage = 1,
        reviewing = 1, reviewTotal = 0;
    const validSession = () => /^[0-9A-F]{64}$/.test(session) &&
        document.body?.dataset.authSessionReference === session &&
        document.body?.dataset.authTenantId === tenant &&
        window.EduOSSessionTransitionPending?.() !== true;
    const token = () => document.querySelector('meta[name="request-verification-token"]')?.content ||
        el('assistantForm').querySelector('input[name="__RequestVerificationToken"]')?.value;
    function notice(message, success = false) {
        if (!validSession()) return;
        const alert = el('assistantAlert');
        alert.className = 'alert alert-' + (success ? 'success' : 'danger') + ' mt-3';
        alert.textContent = message;
        alert.focus();
    }
    async function api(url, body) {
        if (!validSession()) throw Error(say('Session changed. Sign in again.', 'সেশন বদলেছে। আবার লগইন করুন।'));
        const controller = new AbortController(); active.add(controller);
        try {
            const headers = { Accept: 'application/json' };
            if (body !== undefined) {
                const csrf = token();
                if (!csrf) throw Error(say('Security token missing.', 'নিরাপত্তা টোকেন নেই।'));
                headers.RequestVerificationToken = csrf;
                headers['Content-Type'] = 'application/json';
            }
            const response = await fetch(url, {
                method: body === undefined ? 'GET' : 'POST', headers,
                body: body === undefined ? undefined : JSON.stringify(body),
                credentials: 'same-origin', cache: 'no-store', signal: controller.signal
            });
            const payload = await response.json().catch(() => null);
            if (!validSession()) throw Error('Session changed');
            if (!response.ok || !payload?.success)
                throw Error(payload?.message || say('Request rejected.', 'অনুরোধ গ্রহণ হয়নি।'));
            return payload.data;
        } finally { active.delete(controller); }
    }
    const param = (search, page = 1) => new URLSearchParams({
        page: String(page), pageSize: '20', ...(search.trim() ? { search: search.trim() } : {})
    });
    function fill(select, items) {
        const previous = select.value;
        select.replaceChildren(new Option(say('Select a chapter', 'অধ্যায় নির্বাচন করুন'), ''));
        for (const item of items) {
            const label = [item.bookTitle, item.edition, item.chapter,
                item.approvedPassages ? `(${item.approvedPassages})` : ''].filter(Boolean).join(' · ');
            const option = new Option(label, item.reference);
            option.dataset.language = item.language;
            select.add(option);
        }
        if ([...select.options].some(o => o.value === previous)) select.value = previous;
    }
    async function loadSources(page = 1) {
        const current = ++sequence;
        try {
            const data = await api('/api/ai/study/sources?' + param(el('assistantSearch').value, page));
            if (current !== sequence) return;
            sourcePage = data.page;
            fill(el('assistantSource'), data.items);
            el('assistantCount').textContent = say(`${data.totalCount} reviewed chapters`, `${data.totalCount}টি অনুমোদিত অধ্যায়`);
            el('assistantSourcePage').textContent = `${data.page} / ${data.totalPages || 1}`;
            el('assistantSourcePrev').disabled = !data.hasPreviousPage;
            el('assistantSourceNext').disabled = !data.hasNextPage;
        } catch (error) { if (error.name !== 'AbortError') notice(error.message); }
    }
    el('assistantFind').addEventListener('click', () => loadSources(1));
    el('assistantSourcePrev').addEventListener('click', () => loadSources(Math.max(1, sourcePage - 1)));
    el('assistantSourceNext').addEventListener('click', () => loadSources(sourcePage + 1));
    el('assistantSearch').addEventListener('keydown', event => {
        if (event.key === 'Enter') { event.preventDefault(); loadSources(1); }
    });
    el('assistantForm').addEventListener('submit', async event => {
        event.preventDefault();
        if (!el('assistantForm').reportValidity()) return;
        const sourceReference = el('assistantSource').value;
        if (!sourceReference) return notice(say('Choose a chapter.', 'অধ্যায় নির্বাচন করুন।'));
        const button = el('assistantAsk'); button.disabled = true;
        el('assistantResult').textContent = say('Searching reviewed evidence…', 'অনুমোদিত উদ্ধৃতি খোঁজা হচ্ছে…');
        el('assistantCitation').textContent = '';
        try {
            const response = await api('/api/ai/study/ask', { sourceReference, question: el('assistantQuestion').value });
            el('assistantResult').textContent = response.message;
            el('assistantCitation').textContent = response.citation ?
                say('Source: ', 'উৎস: ') + response.citation : (response.studySuggestion || '');
        } catch (error) { if (error.name !== 'AbortError') notice(error.message); }
        finally { button.disabled = false; }
    });

    if (!el('assistantReviewPanel')) { loadSources(); return; }
    const reviewSource = el('assistantReviewSource');
    async function loadReviewSources(page = 1) {
        const current = ++reviewSequence;
        try {
            const data = await api('/api/ai/study/author-sources?' + param(el('assistantReviewSearch').value, page));
            if (current !== reviewSequence) return;
            reviewSourcePage = data.page;
            fill(reviewSource, data.items);
            el('assistantReviewSourcePage').textContent = `${data.page} / ${data.totalPages || 1}`;
            el('assistantReviewSourcePrev').disabled = !data.hasPreviousPage;
            el('assistantReviewSourceNext').disabled = !data.hasNextPage;
            if (reviewSource.value) await loadReviews(1);
            else el('assistantReviewRows').replaceChildren();
        } catch (error) { if (error.name !== 'AbortError') notice(error.message); }
    }
    async function loadReviews(page) {
        if (!reviewSource.value) { el('assistantReviewRows').replaceChildren(); return; }
        try {
            const data = await api('/api/ai/study/reviews/' + encodeURIComponent(reviewSource.value) +
                '?' + param('', page));
            reviewing = data.page; reviewTotal = data.totalCount;
            const list = el('assistantReviewRows'); list.replaceChildren();
            for (const row of data.items) {
                const box = document.createElement('article');
                box.className = 'border rounded p-3';
                const head = document.createElement('div');
                head.className = 'small fw-semibold';
                head.textContent = row.sourceLocator + ' · ' + row.state;
                const passage = document.createElement('p');
                passage.className = 'mb-2 mt-2'; passage.textContent = row.excerptText;
                box.append(head, passage);
                if (row.reviewReason) {
                    const reason = document.createElement('p');
                    reason.className = 'small text-muted'; reason.textContent = row.reviewReason;
                    box.append(reason);
                }
                if (row.state === 'Pending' || row.state === 'Approved') {
                    const actions = document.createElement('div'); actions.className = 'd-flex flex-wrap gap-2';
                    const decisions = row.state === 'Pending' ? ['Approve', 'Reject'] : ['Revoke'];
                    for (const decision of decisions) {
                        const button = document.createElement('button');
                        button.type = 'button'; button.className = 'btn btn-sm btn-outline-primary';
                        button.textContent = decision;
                        button.addEventListener('click', async () => {
                            const reason = decision === 'Approve' ? null : window.prompt(
                                say('Review reason (optional)', 'রিভিউয়ের কারণ (ঐচ্ছিক)'), '');
                            if (decision !== 'Approve' && reason === null) return;
                            button.disabled = true;
                            try {
                                await api('/api/ai/study/excerpts/' + encodeURIComponent(row.reference) + '/review',
                                    { decision, rowVersion: row.rowVersion, reason: reason || null });
                                await loadReviews(reviewing);
                                notice(say('Review saved.', 'রিভিউ সংরক্ষিত হয়েছে।'), true);
                            } catch (error) { if (error.name !== 'AbortError') notice(error.message); }
                            finally { button.disabled = false; }
                        });
                        actions.append(button);
                    }
                    box.append(actions);
                }
                list.append(box);
            }
            el('assistantReviewPage').textContent = `${data.page} / ${data.totalPages || 1}`;
            el('assistantReviewPrev').disabled = !data.hasPreviousPage;
            el('assistantReviewNext').disabled = !data.hasNextPage;
        } catch (error) { if (error.name !== 'AbortError') notice(error.message); }
    }
    el('assistantReviewFind').addEventListener('click', () => loadReviewSources(1));
    el('assistantReviewSourcePrev').addEventListener('click', () => loadReviewSources(Math.max(1, reviewSourcePage - 1)));
    el('assistantReviewSourceNext').addEventListener('click', () => loadReviewSources(reviewSourcePage + 1));
    reviewSource.addEventListener('change', () => {
        const option = reviewSource.selectedOptions[0];
        if (option?.dataset.language) el('assistantLanguage').value = option.dataset.language;
        loadReviews(1);
    });
    el('assistantExcerptForm').addEventListener('submit', async event => {
        event.preventDefault();
        if (!el('assistantExcerptForm').reportValidity() || !reviewSource.value)
            return notice(say('Choose a licensed source.', 'লাইসেন্সধারী অধ্যায় নির্বাচন করুন।'));
        try {
            await api('/api/ai/study/excerpts', {
                sourceReference: reviewSource.value,
                sourceLocator: el('assistantLocator').value,
                excerptText: el('assistantExcerptText').value,
                languageCode: el('assistantLanguage').value
            });
            el('assistantLocator').value = ''; el('assistantExcerptText').value = '';
            await loadReviews(1);
            notice(say('Submitted for independent review.', 'স্বতন্ত্র অনুমোদনের জন্য জমা হয়েছে।'), true);
        } catch (error) { if (error.name !== 'AbortError') notice(error.message); }
    });
    el('assistantReviewPrev').addEventListener('click', () => loadReviews(Math.max(1, reviewing - 1)));
    el('assistantReviewNext').addEventListener('click', () => {
        if (reviewing * 20 < reviewTotal) loadReviews(reviewing + 1);
    });
    loadSources(1); loadReviewSources(1);
})();
