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
    function parse(text) {
        const result = {};
        const keys = {
            name: 'applicantName', 'applicant name': 'applicantName',
            'নাম': 'applicantNameBangla', 'বাংলা নাম': 'applicantNameBangla',
            mobile: 'primaryMobile', phone: 'primaryMobile', 'মোবাইল': 'primaryMobile',
            guardian: 'guardianName', 'guardian name': 'guardianName', 'অভিভাবক': 'guardianName',
            'guardian mobile': 'guardianMobile', 'অভিভাবকের মোবাইল': 'guardianMobile',
            address: 'presentAddress', 'ঠিকানা': 'presentAddress'
        };
        String(text || '').split(/\r?\n/).forEach(line => {
            const at = line.indexOf(':');
            if (at < 0) return;
            const key = keys[line.slice(0, at).trim().toLowerCase()];
            const value = line.slice(at + 1).trim();
            if (key && value) result[key] = value.slice(0, key === 'presentAddress' ? 1000 : 200);
        });
        return result;
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
        const heading = el('h4', {class:'h6'}, t('Draft from text (no automatic submission)', 'লিখে খসড়া তৈরি (স্বয়ংক্রিয় জমা নয়)'));
        const note = el('p', {class:'small text-muted'}, t('Microphone unavailable until an approved speech provider is configured. Type instead. Draft stays in this page only.', 'অনুমোদিত স্পিচ সেবা চালু না হওয়া পর্যন্ত মাইক্রোফোন বন্ধ। লিখে দিন। খসড়া শুধু এই পৃষ্ঠায় থাকবে।'));
        const input = el('textarea', {id:'admissionDraftText',class:'form-control',rows:'3',maxlength:'3000','aria-label':t('Admission transcript draft','ভর্তি আবেদনের লিখিত খসড়া'),placeholder:t('Name: ...\nMobile: ...\nGuardian: ...','নাম: ...\nমোবাইল: ...\nঅভিভাবক: ...')});
        const previewButton = el('button',{type:'button',class:'btn btn-outline-primary mt-2'},t('Preview fields','তথ্য যাচাই'));
        const preview = el('div',{class:'mt-2','aria-live':'polite'});
        const confirm = el('button',{type:'button',class:'btn btn-outline-success mt-2',disabled:'disabled'},t('Apply reviewed fields to form','যাচাইকৃত তথ্য ফরমে বসান'));
        const status = el('p',{class:'small mt-2',role:'status'},'');
        let snapshot = null;
        function invalidate() { snapshot = null; confirm.disabled = true; preview.replaceChildren(); }
        input.addEventListener('input',invalidate);
        previewButton.addEventListener('click',() => {
            invalidate();
            const parsed = parse(input.value);
            const entries = Object.entries(parsed).filter(([key]) => fields.includes(key));
            if (!entries.length) { status.textContent = t('Use Label: value on separate lines. No fields changed.','প্রতি লাইনে নাম: তথ্য লিখুন। কোনো তথ্য পরিবর্তিত হয়নি।'); return; }
            const table = el('div',{class:'row g-2'});
            const editable = {};
            entries.forEach(([key,value]) => {
                const wrap = el('div',{class:'col-md-6'});
                const label = el('label',{class:'form-label'},labels[key][bn ? 1 : 0]);
                const field = el(key === 'presentAddress' ? 'textarea':'input',{class:'form-control',maxlength:String(key === 'presentAddress' ? 1000 : 200)});
                field.value = value;
                label.append(field);
                wrap.append(label); table.append(wrap); editable[key] = field;
            });
            preview.append(table);
            snapshot = editable;
            confirm.disabled = false;
            status.textContent = t('Review and correct every field before applying. Submitting still requires the normal form action.','ফরমে বসানোর আগে সব তথ্য সংশোধন করুন। জমা দিতে স্বাভাবিক ফরম বাটন চাপতে হবে।');
        });
        confirm.addEventListener('click',() => {
            if (!snapshot) return;
            Object.entries(snapshot).forEach(([key,field]) => {
                const target = form.querySelector('#' + key);
                if (!target || target.disabled || target.readOnly) return;
                target.value = field.value.slice(0, Number(target.maxLength) > 0 ? target.maxLength : 1000);
                target.dispatchEvent(new Event('input',{bubbles:true}));
                target.dispatchEvent(new Event('change',{bubbles:true}));
            });
            invalidate();
            input.value = '';
            status.textContent = t('Fields applied. Check the form and submit manually when ready.','তথ্য ফরমে বসানো হয়েছে। যাচাই করে নিজে জমা দিন।');
        });
        host.append(heading,note,input,previewButton,preview,confirm,status);
    }
    return {parse,mount};
});
