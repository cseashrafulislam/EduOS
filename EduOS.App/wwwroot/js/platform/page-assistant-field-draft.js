/* Reviewed C3 field draft contract. No submit, fetch, persistence or business write.
 * Only explicitly opted-in visible plain-text fields may receive a proposed value. */
(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    if (root?.document) root.EduOSFieldDraft = api;
})(typeof window !== 'undefined' ? window : null, function () {
    'use strict';
    const unsafe = /(?:password|token|secret|salary|payroll|payment|amount|price|fee|grade|marks|attendance|stock|inventory|approval|posting|bank|account|nid|national.?id|birth|phone|mobile|email|address|student.?id|employee.?id|guardian.?id)/iu;
    function parseCommand(input) {
        const value = String(input || '').trim();
        if (value.length < 8 || value.length > 512 || /[\r\n]/u.test(value)) return null;
        const en = value.match(/^(?:please\s+)?(?:draft|prefill|fill)\s+(.+?)\s+(?:with|as)\s+(.+)$/iu);
        const bn = value.match(/^(.+?)\s+(?:লিখুন|বসান|দিন)\s*[:：]\s*(.+)$/u);
        const label = (en?.[1] || bn?.[1] || '').trim();
        const text = (en?.[2] || bn?.[2] || '').trim();
        return label.length >= 2 && label.length <= 100 && text.length >= 1 &&
            text.length <= 120 && !/[\r\n]/u.test(text) ? { label, value: text } : null;
    }
    // Form authors must opt in. Labels are security-relevant: a harmless HTML
    // name can still represent a protected field in Bengali or English.
    const unsafeBn = /(?:পাসওয়ার্ড|পাসওয়ার্ড|গোপন|টোকেন|বেতন|পেরোল|বেতনভাতা|টাকা|মূল্য|ফি|মার্কস|নম্বর|গ্রেড|উপস্থিতি|স্টক|ইনভেন্টরি|অনুমোদন|পোস্টিং|ব্যাংক|ব্যাঙ্ক|হিসাব|জাতীয়.?পরিচয়|জাতীয়.?পরিচয়|জন্ম|ফোন|মোবাইল|ইমেইল|ই-মেইল|ঠিকানা|শিক্ষার্থী.?আইডি|অভিভাবক.?আইডি|কর্মচারী.?আইডি)/u;
    function labelText(field) {
        const labels = Array.from(field.labels || []).map(label => label.textContent || '');
        const aria = field.getAttribute?.('aria-label');
        const title = field.getAttribute?.('title');
        if (aria) labels.push(aria);
        if (title) labels.push(title);
        // aria-labelledby is a security boundary as well as an accessibility
        // label: a generic HTML name must not disguise a protected control.
        const labelledBy = String(field.getAttribute?.('aria-labelledby') || '').trim();
        if (labelledBy) {
            const ids = labelledBy.split(/\s+/u);
            const doc = field.ownerDocument;
            if (ids.length > 32 || new Set(ids).size !== ids.length || !doc?.getElementById) return null;
            for (const id of ids) {
                const node = doc.getElementById(id);
                if (!node || !String(node.textContent || '').trim()) return null;
                labels.push(node.textContent);
            }
        }
        return labels.join(' ');
    }
    function safeTarget(field) {
        if (!field || field.dataset?.assistantDraft !== 'true' || field.disabled || field.readOnly ||
            field.isConnected === false || field.closest?.('[hidden], [inert], [aria-hidden="true"], .d-none') ||
            (field.getClientRects && field.getClientRects().length === 0)) return false;
        const tag = field.tagName?.toLowerCase();
        if (tag !== 'input' && tag !== 'textarea') return false;
        if (tag === 'input' && !['text', 'search'].includes(field.type)) return false;
        if (field.form?.dataset?.assistantDraft !== 'true') return false;
        const metadata = [field.name, field.id, field.autocomplete, labelText(field),
            field.dataset?.assistantLabel, field.getAttribute?.('placeholder')].join(' ');
        if (labelText(field) === null || unsafe.test(metadata) || unsafeBn.test(metadata)) return false;
        if (field.closest?.('[data-assistant-sensitive="true"]')) return false;
        // Do not draft into rich editors, linked widgets or custom components.
        // These have their own canonical validation/dependency workflows.
        if (field.isContentEditable || field.getAttribute?.('contenteditable') === 'true' ||
            field.getAttribute?.('role') === 'combobox' ||
            field.getAttribute?.('aria-controls') ||
            field.getAttribute?.('aria-owns') ||
            field.getAttribute?.('data-select2-id') ||
            field.closest?.('.select2-container, [data-assistant-dependent="true"]')) return false;
        return true;
    }
    function apply(field, value, hostWindow, options) {
        if (!safeTarget(field) || typeof value !== 'string' ||
            value.length > 120 || /[\r\n]/u.test(value) || typeof hostWindow?.Event !== 'function') return false;
        // Undo can restore an originally empty optional field; never bypass
        // the original field's required/pattern validation for new drafts.
        const restoringEmpty = options?.restoreEmpty === true && value === '' &&
            field.required !== true && field.getAttribute?.('required') == null;
        if (Number.isInteger(field.maxLength) && field.maxLength >= 0 && value.length > field.maxLength) return false;
        // Preflight constraints before writing: do not depend on browser-specific
        // checkValidity availability or let an empty new draft evade minlength.
        if (value === '' && !restoringEmpty) return false;
        if (field.required && value.trim() === '') return false;
        const pattern = field.getAttribute?.('pattern');
        if (pattern && !restoringEmpty) {
            try {
                if (!(new RegExp('^(?:' + pattern + ')$', 'u')).test(value)) return false;
            } catch { return false; }
        }
        if (!restoringEmpty && field.minLength > 0 && value.length < field.minLength) return false;
        if (String(field.value ?? '') === value) return true;
        // A native setter notifies React-like controlled input trackers without
        // directly invoking form submit/change or any domain write endpoint.
        const proto = field.tagName?.toLowerCase() === 'textarea'
            ? hostWindow?.HTMLTextAreaElement?.prototype : hostWindow?.HTMLInputElement?.prototype;
        const setter = proto && Object.getOwnPropertyDescriptor(proto, 'value')?.set;
        if (typeof setter === 'function') setter.call(field, value);
        else field.value = value;
        field.dispatchEvent(new hostWindow.Event('input', { bubbles: true }));
        return safeTarget(field) && field.value === value;
    }
    return { parseCommand, safeTarget, apply };
});
