/* One shared, provider-free server-reviewed command adapter for the existing
 * Page Assistant. Reuses its on-device mic, field/filter draft adapters and dialog.
 * The server receives only opt-in visible control labels, never field values.
 * No submit, financial, attendance or approval endpoint is reachable here. */
(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    if (root?.document) api.mount(root.document, root);
})(typeof window !== 'undefined' ? window : null, function () {
    'use strict';
    const unsafe = /password|token|secret|salary|payroll|payment|amount|price|fee|grade|marks|attendance|stock|inventory|approval|posting|bank|account|nid|national.?id|birth|phone|mobile|email|address|student.?id|employee.?id|guardian.?id|পাসওয়ার্ড|পাসওয়ার্ড|গোপন|বেতন|পেরোল|টাকা|মূল্য|ফি|মার্কস|নম্বর|গ্রেড|উপস্থিতি|স্টক|ইনভেন্টরি|অনুমোদন|পোস্টিং|ব্যাংক|ব্যাঙ্ক|হিসাব|জাতীয়.?পরিচয়|জাতীয়.?পরিচয়|জন্ম|ফোন|মোবাইল|ইমেইল|ই-মেইল|ঠিকানা|শিক্ষার্থী.?আইডি|অভিভাবক.?আইডি|কর্মচারী.?আইডি/iu;
    const allowedKey = /^[a-z][a-z0-9_.-]{0,79}$/i;
    function visible(field) {
        return !!field && field.isConnected !== false && !field.disabled &&
            field.type !== 'hidden' && !field.readOnly &&
            !field.closest?.('[hidden], [inert], [aria-hidden="true"], .d-none') &&
            (!field.getClientRects || field.getClientRects().length > 0);
    }
    function labels(field, doc) {
        const values = [...(field.labels || [])].map(x => String(x.textContent || ''));
        for (const value of [field.getAttribute?.('aria-label'), field.dataset?.assistantLabel]) {
            if (value) values.push(value);
        }
        const ids = String(field.getAttribute?.('aria-labelledby') || '').trim().split(/\s+/u).filter(Boolean);
        if (ids.length > 8 || new Set(ids).size !== ids.length) return [];
        for (const id of ids) {
            const label = doc.getElementById(id)?.textContent;
            if (!label) return [];
            values.push(label);
        }
        return [...new Set(values.map(x => x.normalize('NFKC').trim())
            .filter(x => x.length >= 2 && x.length <= 100 && !/[\r\n<>]/u.test(x)))].slice(0, 8);
    }
    function collectTargets(doc, host) {
        const main = doc.getElementById('main-content');
        if (!main) return [];
        const candidates = [...main.querySelectorAll('input,textarea,select')];
        const targets = [];
        for (const field of candidates) {
            if (targets.length >= 40) break;
            if (!visible(field)) continue;
            const shown = labels(field, doc);
            if (shown.length === 0 || unsafe.test([field.id, field.name, ...shown].join(' '))) continue;
            const tag = String(field.tagName || '').toUpperCase();
            let kind = 'readonly';
            if (host.EduOSFieldDraft?.safeTarget?.(field)) kind = 'text';
            else if (host.EduOSFilterDraft?.isSafeTarget?.(field)) kind = 'filter';
            else if (tag === 'SELECT' && field.dataset?.assistantVoiceSelect === 'true') kind = 'select';
            const options = [];
            if (kind === 'select') {
                for (const option of [...(field.options || [])].slice(0, 60)) {
                    const text = String(option.textContent || '').trim();
                    if (!text || text.length > 100 || unsafe.test(text) ||
                        !option.value || option.disabled) continue;
                    options.push({ key: 'o' + (options.length + 1), labels: [text] });
                }
            }
            const key = 't' + (targets.length + 1);
            if (!allowedKey.test(key)) continue;
            targets.push({ field, key, kind, labels: shown, options });
        }
        return targets;
    }
    function bodyOf(utterance, path, targets) {
        return { utterance, pagePath: path,
            targets: targets.map(({key,kind,labels,options}) => ({key,kind,labels,options})) };
    }
    async function preview(fetcher, utterance, path, targets, csrf, signal) {
        if (typeof fetcher !== 'function' || !csrf || utterance.length < 2 ||
            utterance.length > 512 || !/^\/(?:[a-z\d_-]+(?:\/[a-z\d_-]+)*)\/?$/i.test(path) ||
            !Array.isArray(targets) || targets.length < 1 || targets.length > 40)
            throw Error('Invalid assistant command context.');
        const response = await fetcher('/api/ai/voice/page/preview', {
            method:'POST', credentials:'same-origin', cache:'no-store',
            headers:{Accept:'application/json','Content-Type':'application/json',
                RequestVerificationToken:csrf},
            body:JSON.stringify(bodyOf(utterance,path,targets)), signal
        });
        const data = await response.json().catch(() => null);
        if (!response.ok || !data || typeof data.decision !== 'string')
            throw Error(response.status === 401 || response.status === 403
                ? 'Assistant access denied.' : 'Assistant review unavailable.');
        return data;
    }
    function mount(doc, host) {
        const dialog=doc.getElementById('pageAssistantDialog');
        const button=doc.getElementById('pageAssistantServerReview');
        const query=doc.getElementById('pageAssistantQuery');
        const result=doc.getElementById('pageAssistantServerResult');
        if (!dialog || !button || !query || !result) return;
        const bn=(doc.documentElement.lang || '').startsWith('bn');
        const t=(en,bd)=>bn?bd:en;
        const tenant=doc.body?.dataset.authTenantId || '';
        const session=doc.body?.dataset.authSessionReference || '';
        const context=()=>dialog.open && (doc.body?.dataset.authTenantId || '') === tenant &&
            (doc.body?.dataset.authSessionReference || '') === session &&
            host.EduOSSessionTransitionPending?.() !== true;
        const csrf=()=>doc.querySelector('meta[name="request-verification-token"]')?.content || '';
        const path=()=>host.location?.pathname || '';
        let seq=0, active=null, pending=null;
        function invalidate() {
            seq++; active?.abort(); active=null; pending=null;
            button.disabled=false; result.replaceChildren();
        }
        function message(text) {
            const p=doc.createElement('p'); p.textContent=text; result.append(p);
        }
        function action(text, handler) {
            const b=doc.createElement('button');
            b.type='button'; b.className='btn btn-outline-primary btn-sm';
            b.textContent=text; b.addEventListener('click', handler); result.append(b);
        }
        button.addEventListener('click', async () => {
            invalidate();
            if (!context()) return;
            const utterance=query.value.trim();
            const pathname=path();
            const targets=collectTargets(doc,host);
            if (!utterance || targets.length===0) {
                message(t('Enter a command on a page with eligible visible controls.',
                    'অনুমোদিত দৃশ্যমান ঘরসহ পেজে নির্দেশ লিখুন।')); return;
            }
            const snapshot=JSON.stringify(bodyOf(utterance,pathname,targets));
            const request=++seq;
            const controller=new AbortController(); active=controller; button.disabled=true;
            message(t('Reviewing command. Nothing has changed.',
                'নির্দেশ যাচাই হচ্ছে। কোনো তথ্য পরিবর্তিত হয়নি।'));
            try {
                const data=await preview(host.fetch?.bind(host),utterance,pathname,targets,csrf(),controller.signal);
                if (request!==seq || !context() || query.value.trim()!==utterance ||
                    path()!==pathname ||
                    JSON.stringify(bodyOf(utterance,pathname,collectTargets(doc,host)))!==snapshot) return;
                result.replaceChildren();
                if (data.decision!=='Ready' || data.businessWriteExecuted!==false ||
                    data.requiresConfirmation!==true || data.pagePath!==pathname) {
                    message(String(data.message || t('Command unavailable.', 'নির্দেশটি অনুমোদিত নয়।'))); return;
                }
                const hit=targets.find(x=>x.key===data.targetKey);
                if (!hit || !visible(hit.field) || !['FocusField','DraftText','SetFilter','SelectOption'].includes(data.action)) {
                    message(t('The reviewed field is no longer available.', 'যাচাইকৃত ঘরটি পাওয়া যাচ্ছে না।')); return;
                }
                pending={hit,utterance,snapshot,pathname};
                const field=hit.field, prior=String(field.value || '');
                const approvedValue=String(data.proposedValue || '');
                const proposedOption=hit.options.find(x=>x.key===data.optionKey);
                message(String(data.message || 'Preview ready.') +
                    (data.action==='SelectOption' && proposedOption ? ' ' + proposedOption.labels[0] : ''));
                action(data.action==='FocusField' || data.action==='SelectOption'
                    ? t('Focus field (select manually)','ঘরে যান (নিজে নির্বাচন করুন)')
                    : t('Confirm safe draft','নিরাপদ খসড়া নিশ্চিত করুন'), () => {
                    if (!context() || !pending || pending.hit!==hit || pending.utterance!==query.value.trim() ||
                        path()!==pending.pathname || !visible(field) ||
                        JSON.stringify(bodyOf(utterance,pathname,collectTargets(doc,host)))!==pending.snapshot) {
                        invalidate(); message(t('Page or session changed. Review again.',
                            'পেজ বা সেশন বদলেছে। আবার যাচাই করুন।')); return;
                    }
                    if (data.action==='FocusField' || data.action==='SelectOption') {
                        invalidate(); dialog.close();
                        field.scrollIntoView?.({behavior:'smooth',block:'center'});
                        field.focus?.(); return;
                    }
                    if (String(field.value || '')!==prior) {
                        invalidate(); message(t('Field changed. Review again.','ঘর বদলেছে। আবার যাচাই করুন।')); return;
                    }
                    const ok=data.action==='DraftText'
                        ? host.EduOSFieldDraft?.safeTarget?.(field)===true &&
                          host.EduOSFieldDraft.apply(field,approvedValue,host)
                        : data.action==='SetFilter' && host.EduOSFilterDraft?.isSafeTarget?.(field)===true &&
                          host.EduOSFilterDraft.applyDraft(field,approvedValue,host);
                    invalidate();
                    message(ok ? t('Draft prepared; review and save manually.', 'খসড়া বসেছে; নিজে যাচাই করে সংরক্ষণ করুন।')
                        : t('Draft rejected by field validation.', 'ঘরের যাচাইয়ে খসড়া বাতিল হয়েছে।'));
                });
            } catch(err) {
                if (request===seq && context() && err?.name!=='AbortError') {
                    result.replaceChildren(); message(err?.message || 'Command review failed.');
                }
            } finally { if(request===seq){active=null;button.disabled=false;} }
        });
        query.addEventListener('input',invalidate);
        dialog.addEventListener('close',invalidate);
        host.addEventListener?.('pagehide',invalidate);
    }
    return {collectTargets,bodyOf,preview,mount};
});
