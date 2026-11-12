/* Explicitly opt-in search filters only; no domain form/transaction writes. */
(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    if (root?.document) root.EduOSFilterDraft = api;
})(typeof window !== 'undefined' ? window : null, function () {
    'use strict';
    function parseCommand(input) {
        const text = String(input || '').trim();
        if (text.length < 8 || text.length > 512) return null;
        const en = text.match(/^(?:please\s+)?(?:filter|search)\s+(.+?)\s+(?:by|for|contains)\s+(.+)$/iu);
        const bn = text.match(/^(.+?)\s+খুঁজুন\s*[:：]\s*(.+)$/u);
        const label = (en?.[1] || bn?.[1] || '').trim();
        const value = (en?.[2] || bn?.[2] || '').trim();
        return label.length >= 2 && label.length <= 100 && value.length >= 1 &&
            value.length <= 80 && !/[\r\n]/u.test(value) ? { label, value } : null;
    }
    // Search is read-only, but a misleading opt-in must not expose protected
    // fields. Inspect accessible names, not just the HTML name.
    const sensitive = /(?:password|token|secret|salary|payroll|payment|amount|price|fee|grade|marks|attendance|stock|inventory|approval|posting|bank|account|nid|national.?id|birth|phone|mobile|email|address|student.?id|employee.?id|guardian.?id|পাসওয়ার্ড|পাসওয়ার্ড|গোপন|বেতন|টাকা|মূল্য|ফি|নম্বর|গ্রেড|উপস্থিতি|স্টক|অনুমোদন|ব্যাংক|ব্যাঙ্ক|হিসাব|জন্ম|ফোন|মোবাইল|ইমেইল|ঠিকানা|পরিচয়|পরিচয়)/iu;
    function isSafeTarget(field) {
        if (!field || field.dataset?.assistantFilter !== 'true') return false;
        const labels = Array.from(field.labels || []).map(x => x.textContent || '');
        const doc = field.ownerDocument;
        for (const id of String(field.getAttribute?.('aria-labelledby') || '').trim().split(/\\s+/u).slice(0, 8)) {
            const node = id && doc?.getElementById?.(id);
            if (node?.textContent) labels.push(node.textContent);
        }
        const metadata = [field.name, field.id, field.autocomplete, field.dataset?.assistantLabel,
            field.getAttribute?.('aria-label'), field.getAttribute?.('placeholder'),
            field.getAttribute?.('title'), ...labels].join(' ');
        if (sensitive.test(metadata) || field.closest?.('[data-assistant-sensitive="true"], [data-assistant-dependent="true"]') ||
            field.getAttribute?.('role') === 'combobox' || field.getAttribute?.('aria-controls') ||
            field.getAttribute?.('aria-owns') || field.getAttribute?.('data-select2-id')) return false;
        return field.tagName?.toLowerCase() === 'input' &&
            ['text', 'search'].includes(field.type) && !field.disabled && !field.readOnly &&
            field.isConnected !== false && !field.closest?.('[hidden], [inert], [aria-hidden="true"], .d-none') &&
            (!field.getClientRects || field.getClientRects().length > 0);
    }
    function applyDraft(field, value, hostWindow) {
        if (!isSafeTarget(field) || typeof value !== 'string' || value.length > 120 ||
            /[\r\n]/u.test(value) || typeof hostWindow?.Event !== 'function') return false;
        const max = Number.isInteger(field.maxLength) && field.maxLength >= 0 ? field.maxLength : 120;
        if (value.length > max) return false;
        const setter = Object.getOwnPropertyDescriptor(hostWindow?.HTMLInputElement?.prototype || {}, 'value')?.set;
        if (typeof setter === 'function') setter.call(field, value);
        else field.value = value;
        field.dispatchEvent(new hostWindow.Event('input', { bubbles: true }));
        return isSafeTarget(field) && field.value === value;
    }
    return { parseCommand, isSafeTarget, applyDraft };
});
