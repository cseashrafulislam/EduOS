/* Safe global page locator. Opt-in speech is on-device only when the browser
 * confirms an already installed local pack. No provider/AI network requests,
 * ERP field writes, transaction calls, persistence or implicit submissions. */
(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    if (root && root.document) api.mount(root.document, root);
})(typeof window !== 'undefined' ? window : null, function () {
    'use strict';

    function normalizeLabel(label) {
        return String(label || '').normalize('NFKC').toLocaleLowerCase()
            .replace(/[\u200b-\u200d\ufeff]/g, '').replace(/[\s*ঃ:।.]+$/g, '')
            .replace(/\s+/g, ' ').trim();
    }

    function matchLabel(entries, requested) {
        const query = normalizeLabel(requested);
        if (query.length < 2 || query.length > 120) return { kind: 'invalid' };
        const exact = entries.filter(entry => entry.labels.some(label => normalizeLabel(label) === query));
        if (exact.length === 1) return { kind: 'match', entry: exact[0] };
        if (exact.length > 1) return { kind: 'ambiguous', count: exact.length };
        if (query.length < 3) return { kind: 'missing' };
        const partial = entries.filter(entry => entry.labels.some(label => normalizeLabel(label).includes(query)));
        if (partial.length === 1) return { kind: 'match', entry: partial[0] };
        return partial.length ? { kind: 'ambiguous', count: partial.length } : { kind: 'missing' };
    }

    // Only remove reviewed navigation/focus phrasing. Commands are interpreted
    // as a label lookup and NEVER as permission to edit, submit or post data.
    function spokenLabel(text) {
        const phrase = normalizeLabel(text);
        const en = phrase.match(/^(?:please\s+)?(?:open|go to|show|find|search for)\s+(.+)$/u);
        const bn = phrase.match(/^(.+?)\s+(?:খুলুন|খোলো|দেখাও|দেখান|খুঁজুন|খুঁজে দাও|যাও)$/u);
        const label = (en?.[1] || bn?.[1] || '').trim();
        if (!label) return null;
        return label.replace(/\s+(?:page|field|menu|পেজ|পাতা|ঘর)$/u, '').trim() || null;
    }

    function matchRequestedLabel(entries, requested) {
        const direct = matchLabel(entries, requested);
        if (direct.kind !== 'missing') return direct;
        const label = spokenLabel(requested);
        return label && label !== normalizeLabel(requested) ? matchLabel(entries, label) : direct;
    }

    function isAvailableField(field) {
        if (!field || field.disabled || field.type === 'hidden' || field.readOnly ||
            field.closest('[hidden], [inert], [aria-hidden="true"], .d-none')) return false;
        if (typeof field.getClientRects === 'function' && field.getClientRects().length === 0) return false;
        return true;
    }

    function fieldsOnPage(doc) {
        const main = doc.getElementById('main-content');
        if (!main) return [];
        return [...main.querySelectorAll('input,select,textarea')]
            .filter(isAvailableField).map(field => {
                const labels = [...(field.labels || [])].map(item => item.textContent || '');
                const aria = field.getAttribute('aria-label');
                if (aria) labels.push(aria);
                return { node: field, labels: labels.filter(Boolean) };
            }).filter(entry => entry.labels.length > 0);
    }

    // A section folded by sidebar-navigation.js is presentation state, not a
    // loss of server authorization. Hidden leaves/other hidden ancestors are
    // still excluded; only the known section nav may be collapsed.
    function isAvailableNavigationLink(link) {
        const hidden = link?.closest?.('[hidden], [inert]');
        if (!hidden) return true;
        return hidden.matches?.('nav[data-sidebar-section]') === true &&
            ['academics', 'management'].includes(hidden.dataset.sidebarSection) &&
            !hidden.parentElement?.closest?.('[hidden], [inert]');
    }

    function linksInSidebar(doc) {
        const sidebar = doc.getElementById('sidebarMenu');
        if (!sidebar) return [];
        return [...sidebar.querySelectorAll('nav a.nav-link[href]')].filter(link => {
            const href = link.getAttribute('href') || '';
            return href.startsWith('/') && !href.startsWith('//') && isAvailableNavigationLink(link);
        }).map(link => ({ node: link, labels: [link.textContent || ''] }));
    }

    // Never fall back to browser/cloud speech recognition. Some browsers offer
    // the old remote Web Speech path; only the on-device API with an installed
    // language pack is eligible, and no pack downloads are triggered here.
    async function createOnDeviceRecognizer(hostWindow, language) {
        const Speech = hostWindow?.SpeechRecognition || hostWindow?.webkitSpeechRecognition;
        if (typeof Speech !== 'function' || typeof Speech.available !== 'function') return null;
        try {
            const status = await Speech.available({ langs: [language], processLocally: true });
            if (status !== 'available') return null;
            const recognition = new Speech();
            if (!('processLocally' in recognition)) return null;
            recognition.processLocally = true;
            if (recognition.processLocally !== true) return null;
            recognition.lang = language;
            recognition.continuous = false;
            recognition.interimResults = false;
            recognition.maxAlternatives = 1;
            return recognition;
        } catch { return null; }
    }

    function mount(doc, hostWindow) {
        const dialog = doc.getElementById('pageAssistantDialog');
        const open = doc.getElementById('pageAssistantOpen');
        const close = doc.getElementById('pageAssistantClose');
        const query = doc.getElementById('pageAssistantQuery');
        const find = doc.getElementById('pageAssistantFind');
        const result = doc.getElementById('pageAssistantResult');
        const voice = doc.getElementById('pageAssistantVoice');
        const voiceStatus = doc.getElementById('pageAssistantVoiceStatus');
        if (!dialog || !open || !close || !query || !find || !result) return;
        const bn = (doc.documentElement.lang || '').toLowerCase().startsWith('bn');
        const t = (en, bd) => bn ? bd : en;
        const tenant = doc.body?.dataset.authTenantId || '';
        const session = doc.body?.dataset.authSessionReference || '';
        const currentContext = () => (doc.body?.dataset.authTenantId || '') === tenant &&
            (doc.body?.dataset.authSessionReference || '') === session;
        let pending = null;
        let recognizer = null;
        let speechRequest = 0;
        function voiceMessage(value) { if (voiceStatus) voiceStatus.textContent = value; }
        function stopSpeech() {
            speechRequest++;
            if (recognizer) {
                const old = recognizer;
                recognizer = null;
                old.onresult = null;
                old.onerror = null;
                old.onend = null;
                try { old.abort(); } catch { /* A closed mic is already stopped. */ }
            }
            if (voice) { voice.disabled = false; voice.setAttribute('aria-pressed', 'false'); }
        }
        function reset() { pending = null; result.replaceChildren(); }
        function message(value) { const p = doc.createElement('p'); p.textContent = value; result.append(p); }
        function action(label, handler) {
            const button = doc.createElement('button');
            button.type = 'button';
            button.className = 'btn btn-outline-primary btn-sm';
            button.textContent = label;
            button.addEventListener('click', handler);
            result.append(button);
        }
        function lookup() {
            stopSpeech();
            reset();
            if (!currentContext()) { message(t('Your session changed. Reload the page.', 'সেশন পরিবর্তিত হয়েছে। পেজটি রিফ্রেশ করুন।')); return; }
            const requested = query.value.trim();
            const formDraft = hostWindow.EduOSFieldDraft;
            const proposed = formDraft?.parseCommand(requested);
            if (proposed) {
                const visible = fieldsOnPage(doc);
                const hit = matchLabel(visible, proposed.label);
                if (hit.kind !== 'match') {
                    message(t('Enter one exact visible field label for a draft.', 'খসড়ার জন্য একটি নির্দিষ্ট দৃশ্যমান ঘরের নাম দিন।'));
                    return;
                }
                const target = hit.entry.node;
                const previous = String(target.value || '');
                pending = target;
                const requestedSnapshot = requested;
                message(t(`Preview only: ${hit.entry.labels[0]} = "${proposed.value}". No field has changed.`,
                    `শুধু প্রিভিউ: ${hit.entry.labels[0]} = "${proposed.value}"। কোনো ঘর বদলায়নি।`));
                if (!formDraft.safeTarget(target)) {
                    message(t('This field does not permit assistant prefill. Copy the text manually after review.',
                        'এই ঘরে সহকারী দিয়ে লেখা বসানো অনুমোদিত নয়। যাচাই করে নিজে লিখুন।'));
                    action(t('Focus field', 'ঘরে যান'), () => {
                        if (!currentContext() || pending !== target || !target.isConnected || !isAvailableField(target)) {
                            reset(); message(t('Field/session changed.', 'ঘর বা সেশন বদলেছে।')); return;
                        }
                        dialog.close(); target.scrollIntoView({ behavior: 'smooth', block: 'center' }); target.focus();
                    });
                    return;
                }
                action(t('Confirm field draft', 'খসড়া নিশ্চিত করুন'), () => {
                    if (!currentContext() || pending !== target || query.value.trim() !== requestedSnapshot ||
                        !target.isConnected || !isAvailableField(target) || !formDraft.safeTarget(target) ||
                        String(target.value || '') !== previous) {
                        reset(); message(t('Field/session changed. Start again.', 'ঘর বা সেশন বদলেছে। আবার শুরু করুন।')); return;
                    }
                    if (!formDraft.apply(target, proposed.value, hostWindow)) {
                        reset(); message(t('Field validation rejected the draft.', 'ঘরের যাচাইয়ে খসড়া বাতিল হয়েছে।')); return;
                    }
                    reset();
                    message(t('Draft entered, not submitted. Review the form before saving.',
                        'খসড়া বসেছে, জমা হয়নি। সংরক্ষণের আগে ফর্ম যাচাই করুন।'));
                    action(t('Undo field draft', 'খসড়া ফেরত নিন'), () => {
                        if (!currentContext() || !target.isConnected || !isAvailableField(target) ||
                            !formDraft.safeTarget(target) || String(target.value || '') !== proposed.value) {
                            reset(); message(t('Undo unavailable after a field/session change.', 'ঘর বা সেশন বদলেছে; ফেরানো যাচ্ছে না।')); return;
                        }
                        // Empty previous values are valid on undo; no business submit occurs.
                        const restored = formDraft.apply(target, previous, hostWindow, { restoreEmpty: previous === '' });
                        reset(); message(restored ? t('Previous text restored.', 'আগের লেখা ফিরেছে।')
                            : t('Could not restore. Review the field manually.', 'ফেরানো যায়নি। নিজে ঘর যাচাই করুন।'));
                    });
                });
                return;
            }
            const filters = hostWindow.EduOSFilterDraft;
            const draft = filters?.parseCommand(requested);
            if (draft) {
                const matches = fieldsOnPage(doc).filter(x => filters.isSafeTarget(x.node));
                const hit = matchLabel(matches, draft.label);
                if (hit.kind !== 'match') {
                    message(t('Enter a unique visible search-field label.', 'সঠিক ও নির্দিষ্ট খোঁজার ঘরের নাম লিখুন।'));
                    return;
                }
                const target = hit.entry.node;
                const previous = String(target.value || '');
                pending = target;
                message(t(`Preview: ${hit.entry.labels[0]} = "${draft.value}". Nothing has changed.`,
                    `আগে দেখুন: ${hit.entry.labels[0]} = "${draft.value}"। এখনো কিছু বদলায়নি।`));
                action(t('Apply search draft', 'খোঁজার লেখা বসান'), () => {
                    if (!currentContext() || pending !== target || !target.isConnected ||
                        !isAvailableField(target) || !filters.isSafeTarget(target) ||
                        String(target.value || '') !== previous) {
                        reset(); message(t('Session or field changed. Search again.', 'সেশন বা ঘর বদলেছে। আবার খুঁজুন।'));
                        return;
                    }
                    if (!filters.applyDraft(target, draft.value, hostWindow)) {
                        reset(); message(t('Search draft was rejected.', 'খোঁজার লেখা গ্রহণ করা হয়নি।'));
                        return;
                    }
                    reset();
                    message(t('Search draft applied. Review or undo, then press Search on the page.',
                        'লেখা বসেছে। যাচাই বা ফেরত নিয়ে পেজের খুঁজুন চাপুন।'));
                    action(t('Undo draft', 'লেখা ফেরত নিন'), () => {
                        if (!currentContext() || !isAvailableField(target) ||
                            !filters.isSafeTarget(target) || String(target.value || '') !== draft.value) {
                            reset(); message(t('Undo unavailable after field/session changes.', 'ঘর বা সেশন বদলেছে, ফেরানো যাচ্ছে না।'));
                            return;
                        }
                        const undone = filters.applyDraft(target, previous, hostWindow);
                        reset(); message(undone ? t('Previous filter restored.', 'আগের লেখা ফিরে এসেছে।')
                            : t('Could not restore the old filter.', 'আগের লেখা ফেরানো যায়নি।'));
                    });
                    action(t('Focus search field', 'খোঁজার ঘরে যান'), () => {
                        if (!currentContext() || !isAvailableField(target) || !filters.isSafeTarget(target)) return;
                        dialog.close(); target.scrollIntoView({ behavior: 'smooth', block: 'center' }); target.focus();
                    });
                });
                return;
            }
            const field = matchRequestedLabel(fieldsOnPage(doc), requested);
            const nav = matchRequestedLabel(linksInSidebar(doc), requested);
            if (field.kind === 'ambiguous' || nav.kind === 'ambiguous' ||
                (field.kind === 'match' && nav.kind === 'match')) {
                message(t('More than one item matches. Enter its full visible label.', 'একাধিক মিল পাওয়া গেছে। সম্পূর্ণ লেবেল লিখুন।'));
                return;
            }
            if (field.kind === 'match') {
                const target = field.entry.node;
                pending = target;
                message(t(`Field found: ${field.entry.labels[0]}. Review before focusing.`,
                    `ঘর পাওয়া গেছে: ${field.entry.labels[0]}। যাচাই করে যান।`));
                action(t('Focus field', 'ঘরে যান'), () => {
                    if (!currentContext() || pending !== target || !isAvailableField(target) || !target.isConnected) {
                        reset(); message(t('Field or session changed. Search again.', 'ঘর বা সেশন বদলেছে। আবার খুঁজুন।')); return;
                    }
                    dialog.close();
                    target.scrollIntoView({ behavior: 'smooth', block: 'center' });
                    target.focus();
                });
                return;
            }
            if (nav.kind === 'match') {
                const link = nav.entry.node;
                const href = link.getAttribute('href');
                pending = link;
                message(t(`Menu found: ${nav.entry.labels[0]}. Review before opening.`,
                    `মেনু পাওয়া গেছে: ${nav.entry.labels[0]}। যাচাই করে খুলুন।`));
                action(t('Open page', 'পেজ খুলুন'), () => {
                    if (!currentContext() || pending !== link || !link.isConnected ||
                        link.getAttribute('href') !== href || !href.startsWith('/') || href.startsWith('//')) {
                        reset(); message(t('Menu or session changed. Search again.', 'মেনু বা সেশন বদলেছে। আবার খুঁজুন।')); return;
                    }
                    hostWindow.location.assign(href);
                });
                return;
            }
            message(t('No unique visible field or authorized menu matched. Try its full label.',
                'একটি নির্দিষ্ট ঘর বা অনুমোদিত মেনু পাওয়া যায়নি। সম্পূর্ণ নাম লিখুন।'));
        }
        if (voice && voiceStatus) voice.addEventListener('click', async () => {
            if (recognizer) { stopSpeech(); voiceMessage(t('Listening stopped.', 'শোনা বন্ধ হয়েছে।')); return; }
            if (!currentContext() || !dialog.open) return;
            const request = ++speechRequest;
            voice.disabled = true;
            voiceMessage(t('Checking installed on-device speech support…', 'ডিভাইসে থাকা ভয়েস সুবিধা যাচাই হচ্ছে…'));
            const language = bn ? 'bn-BD' : 'en-BD';
            const next = await createOnDeviceRecognizer(hostWindow, language);
            if (request !== speechRequest || !currentContext() || !dialog.open) {
                try { next?.abort(); } catch { /* Nothing was started. */ }
                voice.disabled = false;
                return;
            }
            voice.disabled = false;
            if (!next) {
                voiceMessage(t('Local speech is unavailable or its language pack is not installed. Type instead.',
                    'ডিভাইসে এই ভাষার ভয়েস সুবিধা নেই। লিখে ব্যবহার করুন।'));
                return;
            }
            recognizer = next;
            voice.setAttribute('aria-pressed', 'true');
            next.onresult = event => {
                if (recognizer !== next || !currentContext() || !dialog.open) { stopSpeech(); return; }
                const transcript = String(event?.results?.[event.resultIndex || 0]?.[0]?.transcript || '').trim();
                if (transcript.length < 2 || transcript.length > 512) {
                    voiceMessage(t('Speech was not recognized clearly. Try typing.', 'কথা স্পষ্ট বোঝা যায়নি। লিখে চেষ্টা করুন।'));
                    return;
                }
                query.value = transcript;
                reset();
                voiceMessage(t('Review the transcript, then choose Find. No action was performed.',
                    'লেখাটি যাচাই করে খুঁজুন চাপুন। কোনো কাজ স্বয়ংক্রিয়ভাবে হয়নি।'));
            };
            next.onerror = () => {
                stopSpeech();
                voiceMessage(t('On-device recognition stopped. Type instead.', 'ডিভাইসের ভয়েস বন্ধ হয়েছে। লিখে ব্যবহার করুন।'));
            };
            next.onend = () => {
                if (recognizer !== next) return;
                recognizer = null;
                voice.setAttribute('aria-pressed', 'false');
            };
            try {
                next.start();
                voiceMessage(t('Listening on this device… select Stop to cancel.',
                    'ডিভাইসে শোনা হচ্ছে… বন্ধ করতে Stop চাপুন।'));
            } catch {
                stopSpeech();
                voiceMessage(t('Microphone is unavailable. Type instead.', 'মাইক্রোফোন চালু হয়নি। লিখে ব্যবহার করুন।'));
            }
        });
        open.addEventListener('click', () => {
            if (!currentContext()) return;
            stopSpeech(); reset(); query.value = '';
            voiceMessage('');
            dialog.showModal(); query.focus();
        });
        close.addEventListener('click', () => { stopSpeech(); dialog.close(); });
        dialog.addEventListener('close', () => { stopSpeech(); reset(); open.focus(); });
        find.addEventListener('click', lookup);
        query.addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); lookup(); } });
        query.addEventListener('input', reset);
        hostWindow.addEventListener('pageshow', () => {
            if (!currentContext() && dialog.open) { stopSpeech(); dialog.close(); }
        });
        hostWindow.addEventListener('pagehide', stopSpeech);
    }

    return { normalizeLabel, matchLabel, spokenLabel, matchRequestedLabel,
        isAvailableField, isAvailableNavigationLink, createOnDeviceRecognizer, mount };
});
