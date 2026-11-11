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
    function safeTarget(field) {
        if (!field || field.dataset?.assistantDraft !== 'true' || field.disabled || field.readOnly ||
            field.isConnected === false || field.closest?.('[hidden], [inert], [aria-hidden="true"], .d-none') ||
            (field.getClientRects && field.getClientRects().length === 0)) return false;
        const tag = field.tagName?.toLowerCase();
        if (tag !== 'input' && tag !== 'textarea') return false;
        if (tag === 'input' && !['text', 'search'].includes(field.type)) return false;
        if (field.form?.dataset?.assistantDraft !== 'true') return false;
        if (unsafe.test([field.name, field.id, field.autocomplete].join(' '))) return false;
        if (field.closest?.('[data-assistant-sensitive="true"]')) return false;
        return true;
    }
    function apply(field, value, hostWindow) {
        if (!safeTarget(field) || typeof value !== 'string' ||
            value.length > 120 || /[\r\n]/u.test(value) || typeof hostWindow?.Event !== 'function') return false;
        if (Number.isInteger(field.maxLength) && field.maxLength >= 0 && value.length > field.maxLength) return false;
        if (typeof field.checkValidity === 'function') {
            const original = field.value;
            // No speculative writes: reject known pattern/required constraints using
            // attribute validation where supported, otherwise defer to the canonical form.
            const pattern = field.getAttribute?.('pattern');
            if (pattern) {
                try { if (!(new RegExp('^(?:' + pattern + ')$', 'u')).test(value)) return false; }
                catch { return false; }
            }
            if (field.minLength > 0 && value.length < field.minLength) return false;
            if (original === value) return true;
        }
        field.value = value;
        field.dispatchEvent(new hostWindow.Event('input', { bubbles: true }));
        return true;
    }
    return { parseCommand, safeTarget, apply };
});
