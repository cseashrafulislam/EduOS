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
    function isSafeTarget(field) {
        return field?.dataset?.assistantFilter === 'true' && field.tagName?.toLowerCase() === 'input' &&
            ['text', 'search'].includes(field.type) && !field.disabled && !field.readOnly &&
            field.isConnected !== false && !field.closest?.('[hidden], [inert], [aria-hidden="true"], .d-none') &&
            (!field.getClientRects || field.getClientRects().length > 0);
    }
    function applyDraft(field, value, hostWindow) {
        if (!isSafeTarget(field) || typeof value !== 'string' || value.length > 120 ||
            /[\r\n]/u.test(value) || typeof hostWindow?.Event !== 'function') return false;
        const max = Number.isInteger(field.maxLength) && field.maxLength >= 0 ? field.maxLength : 120;
        if (value.length > max) return false;
        field.value = value;
        field.dispatchEvent(new hostWindow.Event('input', { bubbles: true }));
        return true;
    }
    return { parseCommand, isSafeTarget, applyDraft };
});
