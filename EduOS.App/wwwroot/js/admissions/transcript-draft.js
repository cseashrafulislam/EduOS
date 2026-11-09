/* Admission transcript draft: local in-memory text only; never submits a record. */
(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    if (root) root.EduOSAdmissionDraft = api;
    if (root && root.document) api.mount(root.document);
})(typeof window !== 'undefined' ? window : null, function () {
    'use strict';
    const fields = ['applicantName', 'applicantNameBangla', 'primaryMobile', 'guardianName', 'guardianMobile', 'presentAddress'];
    const labels = {
        applicantName: ['Applicant name', 'আবেদনকারীর নাম'],
        applicantNameBangla: ['Bangla name', 'বাংলা নাম'],
        primaryMobile: ['Mobile', 'মোবাইল'],
        guardianName: ['Guardian name', 'অভিভাবকের নাম'],
        guardianMobile: ['Guardian mobile', 'অভিভাবকের মোবাইল'],
        presentAddress: ['Address', 'ঠিকানা']
    };
    // Bangladesh-first input: convert Bangla digits for telephone entry, never for names.
    function normalizePhone(value) {
        return String(value || '').replace(/[০-৯]/g, digit => String('০১২৩৪৫৬৭৮৯'.indexOf(digit))).trim();
    }
    function validateDraftField(key, value) {
        if (key === 'primaryMobile' || key === 'guardianMobile') {
            const phone = normalizePhone(value).replace(/^(?:\+?88)/, '');
            return /^(?:\+?88)?01[3-9][0-9]{8}$/.test(phone)
                ? { valid: true, value: phone }
                : { valid: false, value: phone };
        }
        return { valid: Boolean(String(value || '').trim()), value: String(value || '').trim() };
    }
    function parseDetailed(text) {
        const result = {};
        const errors = [];
        const keys = {
            name: 'applicantName', 'applicant name': 'applicantName',
            'নাম': 'applicantNameBangla', 'বাংলা নাম': 'applicantNameBangla',
            mobile: 'primaryMobile', phone: 'primaryMobile', 'মোবাইল': 'primaryMobile',
            guardian: 'guardianName', 'guardian name': 'guardianName', 'অভিভাবক': 'guardianName',
            'guardian mobile': 'guardianMobile', 'অভিভাবকের মোবাইল': 'guardianMobile',
            address: 'presentAddress', 'ঠিকানা': 'presentAddress'
        };
        // Reject oversized drafts without silently truncating pasted content.
        if (String(text || '').length > 3000) {
            return { fields: {}, errors: [{ tooLong: true, limit: 3000 }] };
        }
        String(text || '').split(/\r?\n/).forEach((line, index) => {
            const at = line.indexOf(':');
            if (at < 0) return;
            const key = keys[line.slice(0, at).trim().toLowerCase()];
            const value = line.slice(at + 1).trim();
            if (!key) return;
            // A recognized but blank label must fail closed. Silently skipping it
            // can make an applicant believe an intentionally supplied field was applied.
            if (!value) {
                errors.push({ key, line: index + 1, empty: true });
                return;
            }
            const limit = key === 'presentAddress' ? 1000 : 200;
            if (value.length > limit) {
                errors.push({ key, line: index + 1, limit });
                return;
            }
            // A repeated label is ambiguous; never let a later line silently overwrite
            // information the applicant already reviewed in the transcript.
            if (Object.prototype.hasOwnProperty.call(result, key)) {
                errors.push({ key, line: index + 1, duplicate: true });
                return;
            }
            result[key] = value;
        });
        return { fields: errors.length ? {} : result, errors };
    }
    function parse(text) {
        return parseDetailed(text).fields;
    }
    function mount(doc) {
        const form = doc.getElementById('admissionForm');
        const host = doc.getElementById('admissionDraftHost');
        if (!form || !host || host.dataset.ready === 'true') return;
        host.dataset.ready = 'true';
        const bn = (doc.documentElement.lang || '').toLowerCase().startsWith('bn');
        const t = (en, bd) => bn ? bd : en;
        const el = (tag, attrs, content) => {
            const node = doc.createElement(tag);
            Object.entries(attrs || {}).forEach(([k,v]) => node.setAttribute(k,v));
            if (content !== undefined) node.textContent = content;
            return node;
        };
        const draftDisclosure = el('details', {class:'border rounded p-3 mb-3',id:'admissionDraftDisclosure'});
        const heading = el('summary', {class:'fw-semibold'}, t('Optional: draft form fields from text', 'ঐচ্ছিক: লিখে ফরমের তথ্য তৈরি করুন'));
        const note = el('p', {class:'small text-muted'}, t('Microphone unavailable until an approved speech provider is configured. Type instead. Draft stays in this page only.', 'অনুমোদিত স্পিচ সেবা চালু না হওয়া পর্যন্ত মাইক্রোফোন বন্ধ। লিখে দিন। খসড়া শুধু এই পৃষ্ঠায় থাকবে।'));
        const input = el('textarea', {id:'admissionDraftText',class:'form-control',rows:'3','aria-label':t('Admission transcript draft','ভর্তি আবেদনের লিখিত খসড়া'),placeholder:t('Name: ...\nMobile: ...\nGuardian: ...','নাম: ...\nমোবাইল: ...\nঅভিভাবক: ...')});
        const previewButton = el('button',{type:'button',class:'btn btn-outline-primary mt-2'},t('Preview fields','তথ্য যাচাই'));
        const preview = el('div',{class:'mt-2','aria-live':'polite',role:'group','aria-label':t('Editable admission field preview','ভর্তির তথ্য সংশোধনের তালিকা')});
        const confirm = el('button',{type:'button',class:'btn btn-outline-success mt-2',disabled:'disabled'},t('Apply reviewed fields to form','যাচাইকৃত তথ্য ফরমে বসান'));
        const status = el('p',{class:'small mt-2',role:'status'},'');
        const privacy = el('p',{class:'small text-muted',role:'note'},t(
            'Speech recognition is not enabled. Browser microphone services may send audio to their provider. No audio is captured or transcript stored by this draft.',
            'স্পিচ রিকগনিশন চালু নেই। ব্রাউজারের মাইক্রোফোন সেবা অডিও সরবরাহকারীর কাছে পাঠাতে পারে। এই খসড়ায় অডিও নেওয়া বা লেখা সংরক্ষণ করা হয় না।'));
        const clear = el('button',{type:'button',class:'btn btn-outline-secondary mt-2 ms-2'},t('Clear draft','খসড়া মুছুন'));
        const undo = el('button',{type:'button',class:'btn btn-outline-secondary mt-2 ms-2',disabled:'disabled'},t('Undo applied fields','বসানো তথ্য আগের অবস্থায় নিন'));
        const reviewSummary = el('p',{class:'small mt-2','aria-live':'polite'},'');
        let lastApply = null;
        function resetUndo() { lastApply = null; undo.disabled = true; }
        undo.addEventListener('click', () => {
            if (!lastApply) return;
            // Never overwrite user edits made after the draft was applied.
            const changed = lastApply.some(({target,after}) => !target.isConnected || target.value !== after);
            if (changed) {
                status.textContent = t('The form changed after applying the draft. Undo cancelled to protect newer edits.',
                    'খসড়া বসানোর পরে ফরম বদলেছে। নতুন পরিবর্তন রক্ষায় আগের অবস্থায় নেওয়া হয়নি।');
                resetUndo();
                return;
            }
            lastApply.forEach(({target,before}) => {
                target.value = before;
                target.dispatchEvent(new Event('input',{bubbles:true}));
                target.dispatchEvent(new Event('change',{bubbles:true}));
            });
            resetUndo();
            status.textContent = t('Applied draft fields restored. No application was submitted.',
                'খসড়া থেকে বসানো তথ্য আগের অবস্থায় ফিরেছে। আবেদন জমা হয়নি।');
        });
        const connectivity = el('p',{class:'small',role:'status'},'');
        function updateConnectivity() {
            connectivity.textContent = rootOnline()
                ? t('Type to continue; microphone provider not configured.','লিখে চালিয়ে যান; মাইক্রোফোন সেবা কনফিগার করা নেই।')
                : t('Offline: typed draft still works. Do not submit until connected.','অফলাইন: লিখিত খসড়া ব্যবহার করা যাবে। সংযোগ না আসা পর্যন্ত জমা দেবেন না।');
        }
        function rootOnline() {
            return typeof navigator === 'undefined' || navigator.onLine !== false;
        }
        updateConnectivity();
        if (typeof window !== 'undefined') {
            window.addEventListener('online', updateConnectivity);
            window.addEventListener('offline', updateConnectivity);
        }
        clear.addEventListener('click', () => {
            input.value = '';
            invalidate();
            status.textContent = t('Draft cleared. No form fields changed.','খসড়া মুছে ফেলা হয়েছে। ফরমের তথ্য বদলায়নি।');
            input.focus();
        });
        let snapshot = null;
        function invalidate() { snapshot = null; confirm.disabled = true; preview.replaceChildren(); reviewSummary.textContent = ''; }
        input.addEventListener('input',invalidate);
        previewButton.addEventListener('click',() => {
            invalidate();
            const detailed = parseDetailed(input.value);
            if (detailed.errors.length) {
                const first = detailed.errors[0];
                status.textContent = first.tooLong
                    ? t('Draft exceeds 3000 characters. Shorten it and preview again; no fields changed.',
                        'খসড়া ৩০০০ অক্ষরের বেশি। লেখা ছোট করে আবার যাচাই করুন; কোনো তথ্য বদলায়নি।')
                    : first.empty
                    ? t(`Line ${first.line} has an empty admission field. Enter a value; no fields changed.`,
                        `লাইন ${first.line}-এ তথ্যের ঘর খালি। তথ্য লিখুন; কোনো তথ্য বদলায়নি।`)
                    : first.duplicate
                    ? t(`Line ${first.line} repeats an admission field. Keep one value per label; no fields changed.`,
                        `লাইন ${first.line}-এ একই তথ্য আবার দেওয়া হয়েছে। প্রতি তথ্য একবার লিখুন; কোনো তথ্য বদলায়নি।`)
                    : t(`Line ${first.line} exceeds ${first.limit} characters. Shorten it and preview again; no fields changed.`,
                        `লাইন ${first.line}-এ ${first.limit} অক্ষরের সীমা ছাড়িয়েছে। ছোট করে আবার যাচাই করুন; কোনো তথ্য বদলায়নি।`);
                input.setAttribute('aria-invalid', 'true');
                input.focus();
                return;
            }
            input.removeAttribute('aria-invalid');
            const parsed = detailed.fields;
            const entries = Object.entries(parsed).filter(([key]) => fields.includes(key));
            if (!entries.length) { status.textContent = t('Use Label: value on separate lines. No fields changed.','প্রতি লাইনে নাম: তথ্য লিখুন। কোনো তথ্য পরিবর্তিত হয়নি।'); return; }
            const table = el('div',{class:'row g-2'});
            const editable = {};
            entries.forEach(([key,value]) => {
                const wrap = el('div',{class:'col-md-6'});
                const label = el('label',{class:'form-label',for:'admissionDraftPreview_'+key},labels[key][bn ? 1 : 0]);
                const field = el(key === 'presentAddress' ? 'textarea':'input',{id:'admissionDraftPreview_'+key,class:'form-control',maxlength:String(key === 'presentAddress' ? 1000 : 200)});
                field.value = value;
                field.addEventListener('input',()=>field.removeAttribute('aria-invalid'));
                wrap.append(label,field); table.append(wrap); editable[key] = field;
            });
            preview.append(table);
            snapshot = editable;
            confirm.disabled = false;
            reviewSummary.textContent = t(`Ready to review ${entries.length} field(s). No form data changed yet.`,
                `যাচাইয়ের জন্য ${entries.length}টি তথ্য প্রস্তুত। ফরমের তথ্য এখনো বদলায়নি।`);
            status.textContent = t('Review and correct every field before applying. Submitting still requires the normal form action.','ফরমে বসানোর আগে সব তথ্য সংশোধন করুন। জমা দিতে স্বাভাবিক ফরম বাটন চাপতে হবে।');
        });
        confirm.addEventListener('click',() => {
            if (!snapshot) return;
            const reviewed = Object.entries(snapshot).map(([key, field]) => [key, validateDraftField(key, field.value)]);
            if (reviewed.some(([, result]) => !result.valid)) {
                const invalid = reviewed.find(([, result]) => !result.valid);
                snapshot[invalid[0]].setAttribute('aria-invalid','true');
                snapshot[invalid[0]].focus();
                status.textContent = t('Check required values and Bangladesh mobile numbers (01XXXXXXXXX). No fields changed.',
                    'তথ্য এবং বাংলাদেশের মোবাইল নম্বর (01XXXXXXXXX) ঠিক করুন। কোনো তথ্য বদলায়নি।');
                return;
            }
            // Fail closed before mutating any field: a hidden/disabled or missing
            // destination must never cause a misleading partial application.
            const destinations = reviewed.map(([key, result]) => ({
                key, result, target: form.querySelector('#' + key)
            }));
            const unavailable = destinations.find(({result,target}) =>
                !target || target.disabled || target.readOnly || !target.isConnected ||
                (Number(target.maxLength) >= 0 && result.value.length > Number(target.maxLength)));
            if (unavailable) {
                status.textContent = t('A destination field is unavailable or too short. No fields applied; review the form.',
                    'ফরমের কোনো ঘর অনুপস্থিত বা তথ্যের জন্য ছোট। কোনো তথ্য বসানো হয়নি; ফরম যাচাই করুন।');
                invalidate();
                return;
            }
            // Preserve a reversible, in-memory snapshot; never persist applicant PII.
            const previous = destinations.map(({result,target}) => ({target,before:target.value,after:result.value}));
            destinations.forEach(({result,target}) => {
                target.value = result.value;
                target.dispatchEvent(new Event('input',{bubbles:true}));
                target.dispatchEvent(new Event('change',{bubbles:true}));
            });
            lastApply = previous;
            undo.disabled = false;
            invalidate();
            input.value = '';
            status.textContent = t('Fields applied. Check the form and submit manually when ready.','তথ্য ফরমে বসানো হয়েছে। যাচাই করে নিজে জমা দিন।');
        });
        // Applicant-side readiness check is advisory: the canonical admission submit
        // handler remains the only writer and retains server-side validation.
        const readiness = el('section', {class:'mt-3','aria-labelledby':'admissionReadinessTitle'});
        const readinessTitle = el('h5',{id:'admissionReadinessTitle',class:'h6'},
            t('Application readiness','আবেদন প্রস্তুতি'));
        const readinessSummary = el('p',{class:'small',role:'status','aria-live':'polite'},'');
        const readinessList = el('ul',{class:'list-unstyled mb-2'});
        const checkButton = el('button',{type:'button',class:'btn btn-outline-primary btn-sm'},
            t('Check required information','প্রয়োজনীয় তথ্য যাচাই'));
        const requiredFields = Array.from(form.querySelectorAll('[required]')).filter(field =>
            field.id && field.type !== 'hidden');
        const fieldLabel = field => {
            const label = Array.from(form.querySelectorAll('label')).find(item =>
                item.getAttribute('for') === field.id);
            return label ? label.textContent.replace(/\s*\*\s*$/, '').trim() : field.id;
        };
        function checkReadiness() {
            readinessList.replaceChildren();
            const missing = requiredFields.filter(field =>
                field.disabled || !String(field.value || '').trim() || !field.checkValidity());
            missing.forEach(field => {
                const item = el('li',{class:'mb-1'});
                const jump = el('button',{type:'button',class:'btn btn-link btn-sm text-start p-0'},
                    fieldLabel(field));
                jump.addEventListener('click',() => {
                    field.scrollIntoView({behavior:'smooth',block:'center'});
                    field.focus();
                });
                item.append(jump);
                readinessList.append(item);
            });
            readinessSummary.textContent = missing.length
                ? t(`Complete or correct ${missing.length} required field(s) before submitting.`,
                    `জমা দেওয়ার আগে ${missing.length}টি প্রয়োজনীয় তথ্য পূরণ বা সংশোধন করুন।`)
                : t('Required fields look complete. Review all details, then use the normal Submit button.',
                    'প্রয়োজনীয় তথ্য পূরণ হয়েছে। সব তথ্য যাচাই করে স্বাভাবিক জমা বাটন ব্যবহার করুন।');
            return missing.length === 0;
        }
        checkButton.addEventListener('click',checkReadiness);
        form.addEventListener('input',() => { readinessSummary.textContent = ''; readinessList.replaceChildren(); });
        form.addEventListener('change',() => { readinessSummary.textContent = ''; readinessList.replaceChildren(); });
        readiness.append(readinessTitle,readinessSummary,readinessList,checkButton);
        draftDisclosure.append(heading,note,privacy,connectivity,input,previewButton,clear,preview,reviewSummary,confirm,undo,status);
        const readinessDisclosure = el('details',{class:'border rounded p-3 mb-3',id:'admissionReadinessDisclosure'});
        const readinessHeading = el('summary',{class:'fw-semibold'},t('Check application readiness', 'আবেদন প্রস্তুতি যাচাই'));
        readinessDisclosure.append(readinessHeading,readiness);
        host.append(draftDisclosure,readinessDisclosure);
    }
    return {parse,parseDetailed,mount,normalizePhone,validateDraftField};
});
