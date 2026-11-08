(() => {
    'use strict';
    const NAME = 'eduos-offline-study-v1', STORE = 'resources';
    const MAX_ITEMS = 20, MAX_BYTES = 4194304, KEY_ITERATIONS = 210000;
    const BLOCK = 262144, VERIFIER = 'EduOS offline study v1';
    const el = id => document.getElementById(id);
    const page = el('studyOfflineDialog');
    const offline = el('offlineStudyReader');
    if (!page && !offline) return;
    const text = new TextEncoder();
    const bn = document.documentElement.lang?.startsWith('bn');
    const tr = (en, bd) => bn ? bd : en;
    const session = document.body?.dataset.authSessionReference || '';
    const tenant = document.body?.dataset.authTenantId || '';
    const user = document.body?.dataset.authUserId || '';
    const account = tenant + ':' + user;
    const current = () => page && /^[A-F0-9]{64}$/.test(session) && tenant && user &&
        document.body?.dataset.authSessionReference === session &&
        document.body?.dataset.authTenantId === tenant &&
        document.body?.dataset.authUserId === user &&
        window.EduOSSessionTransitionPending?.() !== true;
    let generation = 0, selected = null, controller = null, objectUrl = null;
    let openKey = null, openRecord = null;
    const channel = typeof BroadcastChannel === 'function'
        ? new BroadcastChannel('eduos-offline-study') : null;
    const random = n => crypto.getRandomValues(new Uint8Array(n));
    const hex = bytes => Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('').toUpperCase();
    const digest = async bytes => hex(new Uint8Array(await crypto.subtle.digest('SHA-256', bytes)));
    const assertCurrent = g => { if (g !== generation || !current()) throw Error('Session changed; download cancelled.'); };

    function dbOpen() {
        return new Promise((resolve, reject) => {
            const req = indexedDB.open(NAME, 1);
            req.onupgradeneeded = () => req.result.createObjectStore(STORE, { keyPath: 'id' });
            req.onerror = () => reject(req.error);
            req.onsuccess = () => {
                const db = req.result;
                db.onversionchange = () => db.close();
                resolve(db);
            };
        });
    }
    async function operation(mode, fn) {
        const db = await dbOpen();
        try {
            return await new Promise((resolve, reject) => {
                const tx = db.transaction(STORE, mode);
                const req = fn(tx.objectStore(STORE));
                let value;
                req.onsuccess = () => { value = req.result; };
                req.onerror = () => reject(req.error);
                tx.oncomplete = () => resolve(value);
                tx.onerror = () => reject(tx.error);
                tx.onabort = () => reject(tx.error);
            });
        } finally { db.close(); }
    }
    const all = () => operation('readonly', store => store.getAll());
    const get = id => operation('readonly', store => store.get(id));
    const put = value => operation('readwrite', store => store.put(value));
    const remove = id => operation('readwrite', store => store.delete(id));
    function clearLocal(broadcast = true) {
        ++generation;
        controller?.abort(); controller = null;
        if (objectUrl) URL.revokeObjectURL(objectUrl);
        objectUrl = null;
        openKey = null; openRecord = null;
        if (offline) el('offlineReaderContent').replaceChildren();
        if (broadcast) channel?.postMessage({ type: 'clear' });
        return new Promise(resolve => {
            const req = indexedDB.deleteDatabase(NAME);
            req.onsuccess = resolve; req.onerror = resolve; req.onblocked = resolve;
        });
    }
    channel?.addEventListener('message', event => {
        if (event.data?.type === 'clear') {
            clearLocal(false).then(() => { if (offline) list().catch(() => {}); });
        }
    });
    window.EduOSOfflineStudy = { clear: clearLocal };

    async function keyFrom(password, salt) {
        const source = await crypto.subtle.importKey('raw', text.encode(password), 'PBKDF2', false,
            ['deriveKey']);
        return crypto.subtle.deriveKey({ name: 'PBKDF2', salt, iterations: KEY_ITERATIONS,
            hash: 'SHA-256' }, source, { name: 'AES-GCM', length: 256 }, false,
        ['encrypt', 'decrypt']);
    }
    async function seal(key, bytes) {
        const iv = random(12);
        return { iv: Array.from(iv), body: await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, key, bytes) };
    }
    const unseal = (key, value) => crypto.subtle.decrypt(
        { name: 'AES-GCM', iv: new Uint8Array(value.iv) }, key, value.body);
    async function verify(key, record) {
        try {
            return new TextDecoder().decode(await unseal(key, record.check)) === VERIFIER;
        } catch { return false; }
    }
    async function cleanup() {
        for (const item of await all()) if (item.expiresAt <= Date.now()) await remove(item.id);
    }
    function progress(value) {
        const target = el('studyOfflineProgress');
        if (target && current()) target.textContent = value;
    }
    async function json(url, signal) {
        const response = await fetch(url, { cache: 'no-store', credentials: 'same-origin', signal,
            headers: { Accept: 'application/json' } });
        const payload = await response.json().catch(() => null);
        if (!response.ok || !payload?.success || !payload.data)
            throw Error(payload?.message || tr('Offline rights unavailable.', 'অফলাইন অধিকার নেই।'));
        return payload.data;
    }
    async function chunk(ref, offset, sha, size, signal) {
        const url = '/api/library/digital/' + encodeURIComponent(ref) +
            '/offline/chunk?' + new URLSearchParams({ offset: String(offset), sha256: sha });
        for (let attempt = 0; attempt < 3; attempt++) {
            try {
                const response = await fetch(url, { cache: 'no-store', credentials: 'same-origin', signal });
                if (response.status !== 206 || response.headers.get('ETag') !== '"' + sha + '"' ||
                    response.headers.get('Content-Range') !== 'bytes ' + offset + '-' +
                        (offset + Math.min(BLOCK, size - offset) - 1) + '/' + size)
                    throw Error('Content changed or download permission revoked.');
                const data = await response.arrayBuffer();
                if (data.byteLength !== Math.min(BLOCK, size - offset))
                    throw Error('Incomplete file chunk.');
                return data;
            } catch (error) {
                if (signal.aborted || /revoked|changed/i.test(error.message) || attempt === 2) throw error;
                await new Promise(resolve => setTimeout(resolve, (attempt + 1) * 400));
            }
        }
        throw Error('Download failed.');
    }
    async function download(item, password, g) {
        if (!crypto.subtle || !indexedDB) throw Error('Encrypted storage is unavailable.');
        if (password.length < 12 || password.length > 128) throw Error('Use a 12–128 character passphrase.');
        controller = new AbortController();
        const ref = item.reference;
        const permit = await json('/api/library/digital/' + encodeURIComponent(ref) +
            '/offline/permit', controller.signal);
        assertCurrent(g);
        if (permit.reference.toLowerCase() !== ref.toLowerCase() ||
            permit.sizeBytes < 1 || permit.sizeBytes > MAX_BYTES || permit.chunkBytes !== BLOCK ||
            !/^[A-Fa-f0-9]{64}$/.test(permit.sha256) ||
            !['application/pdf', 'image/jpeg', 'image/png', 'image/webp'].includes(permit.contentType) ||
            !Number.isFinite(Date.parse(permit.expiresAtUtc))) throw Error('Invalid offline permit.');
        await cleanup();
        assertCurrent(g);
        const id = account + ':' + ref.toLowerCase();
        let record = await get(id);
        if (record && (record.sha256 !== permit.sha256 || record.sizeBytes !== permit.sizeBytes ||
            record.expiresAt <= Date.now())) { await remove(id); record = null; }
        if (!record && (await all()).length >= MAX_ITEMS)
            throw Error('Device limit of 20 offline resources reached. Remove older files.');
        const salt = record ? new Uint8Array(record.salt) : random(16);
        const key = await keyFrom(password, salt);
        assertCurrent(g);
        if (record && !await verify(key, record)) throw Error('Incorrect offline passphrase.');
        if (!record) record = {
            id, reference: ref, account, sha256: permit.sha256, sizeBytes: permit.sizeBytes,
            contentType: permit.contentType, expiresAt: Date.parse(permit.expiresAtUtc),
            salt: Array.from(salt), check: await seal(key, text.encode(VERIFIER)),
            title: await seal(key, text.encode(String(permit.title).slice(0, 250))),
            parts: [], complete: false, progressVersion: null, progressPage: 0
        };
        if (!record.progress) {
            try {
                const snapshot = await json('/api/library/digital/study/progress/' +
                    encodeURIComponent(ref), controller.signal);
                assertCurrent(g);
                record.progressVersion = snapshot.rowVersion || null;
                record.progressPage = snapshot.page || 0;
            } catch (error) {
                if (controller.signal.aborted || !current()) throw error;
                // Reader position remains authoritative on the server; if this
                // snapshot fails, a later sync will require conflict resolution.
            }
        }
        const pieces = Math.ceil(record.sizeBytes / BLOCK);
        for (let i = 0; i < pieces; i++) {
            assertCurrent(g);
            if (record.parts[i]) continue;
            const raw = await chunk(ref, i * BLOCK, record.sha256, record.sizeBytes, controller.signal);
            assertCurrent(g);
            record.parts[i] = await seal(key, raw);
            assertCurrent(g);
            await put(record);
            progress(tr(`Downloaded ${i + 1}/${pieces} chunks.`, `${i + 1}/${pieces} অংশ ডাউনলোড।`));
        }
        const assembled = new Uint8Array(record.sizeBytes);
        for (let i = 0; i < pieces; i++) {
            assertCurrent(g);
            const bytes = new Uint8Array(await unseal(key, record.parts[i]));
            if (bytes.length !== Math.min(BLOCK, record.sizeBytes - i * BLOCK))
                throw Error('Invalid saved chunk.');
            assembled.set(bytes, i * BLOCK);
        }
        if (await digest(assembled) !== record.sha256) {
            await remove(id); throw Error('The downloaded file failed integrity verification.');
        }
        assertCurrent(g);
        record.complete = true;
        await put(record);
        progress(tr('Saved. Open Offline reader to view.', 'সংরক্ষিত। অফলাইন রিডারে খুলুন।'));
    }
    if (page) {
        window.EduOSOfflineStudy.prepare = item => {
            if (!current() || !item?.offlineAllowed) return;
            selected = item;
            el('studyOfflineDescription').textContent = item.title || item.bookTitle || '';
            el('studyOfflineProgress').textContent = '';
            el('studyOfflinePassword').value = '';
            page.showModal();
        };
        el('studyOfflineCancel').addEventListener('click', () => {
            controller?.abort(); page.close(); selected = null;
        });
        el('studyOfflineForm').addEventListener('submit', async event => {
            event.preventDefault();
            if (!selected || !current()) return;
            const g = generation;
            const input = el('studyOfflinePassword');
            const password = input.value; input.value = '';
            el('studyOfflineSave').disabled = true;
            try { await download(selected, password, g); }
            catch (error) { if (current()) progress(error.message); }
            finally { el('studyOfflineSave').disabled = false; controller = null; }
        });
        const observer = new MutationObserver(() => {
            if (!current()) { clearLocal(); page.close(); observer.disconnect(); }
        });
        observer.observe(document.body, { attributes: true,
            attributeFilter: ['data-auth-session-reference', 'data-auth-tenant-id', 'data-auth-user-id'] });
    }
    async function list() {
        if (!offline) return;
        await cleanup();
        const target = el('offlineReaderList'); target.replaceChildren();
        const resources = (await all()).slice(0, MAX_ITEMS);
        if (!resources.length) target.textContent = tr('No offline resources saved.', 'অফলাইন ফাইল নেই।');
        for (const record of resources) {
            const button = document.createElement('button');
            button.type = 'button'; button.className = 'list-group-item list-group-item-action';
            button.textContent = (record.complete ? '📖 ' : '⏳ ') +
                new Date(record.expiresAt).toLocaleString() +
                (record.complete ? '' : tr(' — resume from library', ' — লাইব্রেরি থেকে চালু করুন'));
            button.disabled = !record.complete;
            button.addEventListener('click', () => open(record));
            target.append(button);
        }
    }
    async function syncProgress(record, key) {
        if (!record.progress || !navigator.onLine) return;
        const pending = JSON.parse(new TextDecoder().decode(await unseal(key, record.progress)));
        const tokenResponse = await fetch('/api/library/digital/study/progress/token',
            { credentials: 'same-origin', cache: 'no-store' });
        if (!tokenResponse.ok || tokenResponse.redirected) throw Error('Sign in to sync reading progress.');
        const csrf = (await tokenResponse.json()).token;
        if (!csrf) throw Error('Security token missing.');
        async function save(command) {
            return fetch('/api/library/digital/study/progress/' + encodeURIComponent(record.reference), {
                method: 'PUT', credentials: 'same-origin', cache: 'no-store',
                headers: { 'Content-Type': 'application/json', RequestVerificationToken: csrf },
                body: JSON.stringify(command)
            });
        }
        let response = await save(pending);
        if (response.status === 409) {
            const currentResponse = await fetch('/api/library/digital/study/progress/' +
                encodeURIComponent(record.reference), { credentials: 'same-origin', cache: 'no-store' });
            if (!currentResponse.ok) throw Error('Cannot resolve a reader position conflict.');
            const current = (await currentResponse.json()).data;
            if (!current) throw Error('Cannot read current server position.');
            if (!window.confirm(tr(`Server page ${current.page}; offline page ${pending.page}. Use offline position?`,
                `সার্ভারে পৃষ্ঠা ${current.page}; অফলাইনে ${pending.page}। অফলাইনের অবস্থান রাখবেন?`))) {
                record.progress = null; record.progressVersion = current.rowVersion || null;
                record.progressPage = current.page; await put(record); return;
            }
            pending.expectedRowVersion = current.rowVersion || null;
            pending.commandId = crypto.randomUUID();
            record.progress = await seal(key, text.encode(JSON.stringify(pending)));
            await put(record);
            response = await save(pending);
        }
        if (!response.ok) throw Error('Sync rejected; offline position is still saved.');
        const snapshot = (await response.json()).data;
        if (!snapshot) throw Error('Invalid sync acknowledgement.');
        record.progress = null; record.progressVersion = snapshot.rowVersion || null;
        record.progressPage = snapshot.page; await put(record);
        el('offlineReaderStatus').textContent = tr('Reading position synchronized.', 'পড়ার অবস্থান সিঙ্ক হয়েছে।');
    }
    async function open(record) {
        const status = el('offlineReaderStatus');
        const container = el('offlineReaderContent');
        container.replaceChildren();
        if (objectUrl) URL.revokeObjectURL(objectUrl);
        objectUrl = null;
        if (record.expiresAt <= Date.now()) { await remove(record.id); return list(); }
        const password = el('offlineReaderPassphrase').value;
        el('offlineReaderPassphrase').value = '';
        if (password.length < 12 || password.length > 128)
            return void (status.textContent = tr('Enter the encryption passphrase.', 'এনক্রিপশন পাসফ্রেজ লিখুন।'));
        try {
            const key = await keyFrom(password, new Uint8Array(record.salt));
            if (!await verify(key, record)) throw Error('Incorrect passphrase.');
            const parts = Math.ceil(record.sizeBytes / BLOCK);
            if (!record.complete || record.parts.length !== parts) throw Error('Incomplete offline file.');
            const bytes = new Uint8Array(record.sizeBytes);
            for (let i = 0; i < parts; i++) {
                const chunk = new Uint8Array(await unseal(key, record.parts[i]));
                if (chunk.length !== Math.min(BLOCK, record.sizeBytes - i * BLOCK))
                    throw Error('Corrupt offline part.');
                bytes.set(chunk, i * BLOCK);
            }
            if (await digest(bytes) !== record.sha256 || record.expiresAt <= Date.now())
                throw Error('Offline file expired or failed integrity verification.');
            const title = new TextDecoder().decode(await unseal(key, record.title));
            const heading = document.createElement('h2'); heading.className = 'h5';
            heading.textContent = title; container.append(heading);
            objectUrl = URL.createObjectURL(new Blob([bytes], { type: record.contentType }));
            const reader = record.contentType.startsWith('image/') ?
                document.createElement('img') : document.createElement('iframe');
            reader.src = objectUrl;
            if (reader.tagName === 'IFRAME') reader.setAttribute('sandbox', 'allow-downloads');
            reader.className = 'w-100 border rounded offline-reader-frame';
            reader.setAttribute('title', title); container.append(reader);
            openKey = key; openRecord = record;
            const progressForm = document.createElement('form');
            progressForm.className = 'd-flex flex-wrap align-items-end gap-2 my-3';
            const label = document.createElement('label');
            label.className = 'form-label'; label.textContent = tr('Last read page', 'সর্বশেষ পড়া পৃষ্ঠা');
            const input = document.createElement('input');
            input.type = 'number'; input.min = '1'; input.max = '100000';
            input.required = true; input.value = String(record.progressPage || 1);
            input.className = 'form-control'; label.append(input);
            const save = document.createElement('button'); save.type = 'submit';
            save.className = 'btn btn-primary';
            save.textContent = tr('Save & sync position', 'পৃষ্ঠা সেভ ও সিঙ্ক');
            progressForm.append(label, save); container.append(progressForm);
            progressForm.addEventListener('submit', async event => {
                event.preventDefault();
                const page = Number(input.value);
                if (!Number.isInteger(page) || page < 1 || page > 100000) return;
                save.disabled = true;
                try {
                    record.progress = await seal(key, text.encode(JSON.stringify({
                        page, commandId: crypto.randomUUID(),
                        expectedRowVersion: record.progressVersion || null
                    })));
                    await put(record);
                    status.textContent = tr('Position saved to encrypted device storage.',
                        'পড়ার অবস্থান এনক্রিপ্টেড স্টোরেজে রাখা হয়েছে।');
                    if (navigator.onLine) await syncProgress(record, key);
                } catch (error) { status.textContent = error.message; }
                finally { save.disabled = false; }
            });
            status.textContent = tr('Offline copy loaded. Check rights when online.',
                'অফলাইন ফাইল খোলা হয়েছে। অনলাইনে অনুমতি পুনরায় যাচাই হবে।');
        } catch (error) { status.textContent = error.message; }
    }
    if (offline) {
        el('offlineReaderClear').addEventListener('click', async () => {
            await clearLocal(); await list();
        });
        list().catch(() => { el('offlineReaderStatus').textContent = 'Device storage unavailable.'; });
        window.addEventListener('visibilitychange', () => {
            if (document.visibilityState === 'visible') list().catch(() => {});
        });
        window.addEventListener('pagehide', () => {
            if (objectUrl) URL.revokeObjectURL(objectUrl);
        });
        window.addEventListener('online', async () => {
            for (const record of await all()) {
                try {
                    const response = await fetch('/api/library/digital/' +
                        encodeURIComponent(record.reference) + '/offline/permit',
                    { cache: 'no-store', credentials: 'same-origin' });
                    if ([401, 403, 404].includes(response.status) || response.redirected)
                        await remove(record.id);
                    else if (response.ok) {
                        const permit = (await response.json()).data;
                        if (permit?.sha256 !== record.sha256 ||
                            Date.parse(permit?.expiresAtUtc) < Date.now()) await remove(record.id);
                    }
                } catch { /* No reliable connection; preserve short-lived encrypted copy. */ }
            }
            if (openRecord && openKey && await get(openRecord.id)) {
                try { await syncProgress(openRecord, openKey); }
                catch (error) { el('offlineReaderStatus').textContent = error.message; }
            }
            await list();
        });
    }
})();
