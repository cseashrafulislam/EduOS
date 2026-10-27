(function (root) {
    'use strict';
    const types = Object.freeze({ Text: 1, Number: 2, Decimal: 3, Date: 4, DateTime: 5,
        Boolean: 6, Select: 7, MultiSelect: 8, Json: 9 });
    function parseOptions(json) {
        try {
            const values = JSON.parse(json || '[]');
            return Array.isArray(values) && values.every(x => typeof x === 'string') ? values : [];
        } catch { return []; }
    }
    function normalizeField(field) {
        if (!field || typeof field.fieldKey !== 'string' ||
            !/^[A-Za-z][A-Za-z0-9_]*$/.test(field.fieldKey)) return null;
        const type = Number(field.dataType);
        if (!Number.isInteger(type) || type < 1 || type > 9) return null;
        return { key: field.fieldKey, label: String(field.label || field.fieldKey),
            type, required: Boolean(field.isRequired), order: Number(field.displayOrder) || 0,
            options: type === types.Select || type === types.MultiSelect
                ? parseOptions(field.optionsJson) : [] };
    }
    function readValue(control) {
        const type = Number(control.dataset.fieldType);
        if (type === types.MultiSelect) {
            const selected = Array.from(control.selectedOptions || [], x => x.value).filter(Boolean);
            return selected.length ? JSON.stringify(selected) : null;
        }
        const raw = String(control.value ?? '').trim();
        if (!raw) return null;
        if (type === types.DateTime) {
            const date = new Date(raw);
            return Number.isNaN(date.getTime()) ? raw : date.toISOString();
        }
        return raw;
    }
    const api = { types, parseOptions, normalizeField, readValue };
    if (typeof module !== 'undefined' && module.exports) module.exports = api;
    if (root) root.EduOSAdmissionFields = api;
})(typeof globalThis !== 'undefined' ? globalThis : null);
