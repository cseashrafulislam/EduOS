/* Progressive, bilingual native-constraint feedback for ordinary EduOS forms.
 * It never changes validation rules, submit handlers, permissions or transactions.
 * AJAX editors with novalidate/custom validation retain their own error workflow. */
(function (root, factory) {
    const api = factory();
    if (typeof module === 'object' && module.exports) module.exports = api;
    if (root && root.document) api.mount(root.document);
})(typeof window !== 'undefined' ? window : null, function () {
    'use strict';
    let sequence = 0;
    function isBangla(doc) { return (doc.documentElement?.lang || '').toLowerCase().startsWith('bn'); }
    function message(field, doc) {
        const invalid = field.validity || {};
        const bn = isBangla(doc);
        if (invalid.valueMissing) return bn ? 'এই ঘরটি পূরণ করুন।' : 'Complete this field.';
        if (invalid.typeMismatch) return bn ? 'সঠিক তথ্যের ধরন ব্যবহার করুন।' : 'Enter a valid value.';
        if (invalid.patternMismatch) return bn ? 'নির্ধারিত বিন্যাস অনুসরণ করুন।' : 'Use the required format.';
        if (invalid.tooShort) return bn ? 'তথ্যটি আরও বড় হতে হবে।' : 'Enter a longer value.';
        if (invalid.tooLong) return bn ? 'তথ্যটি আরও ছোট হতে হবে।' : 'Enter a shorter value.';
        if (invalid.rangeUnderflow) return bn ? 'অনুমোদিত সর্বনিম্ন মান ব্যবহার করুন।' : 'Enter a value at or above the minimum.';
        if (invalid.rangeOverflow) return bn ? 'অনুমোদিত সর্বোচ্চ মান ব্যবহার করুন।' : 'Enter a value at or below the maximum.';
        if (invalid.stepMismatch || invalid.badInput) return bn ? 'সঠিক সংখ্যার মান লিখুন।' : 'Enter a valid number.';
        return bn ? 'এই তথ্যটি যাচাই করুন।' : 'Check this value.';
    }
    function hasExistingError(field) {
        if (!field.form || !field.name || typeof field.form.querySelectorAll !== 'function') return false;
        return Array.from(field.form.querySelectorAll('[data-valmsg-for]')).some(element =>
            element.getAttribute('data-valmsg-for') === field.name && !!element.textContent?.trim());
    }
    function cleanup(field) {
        const id = field.dataset?.eduNativeError;
        if (!id) return;
        const node = field.ownerDocument?.getElementById(id);
        if (node) node.remove();
        const current = (field.getAttribute('aria-describedby') || '').split(/\s+/).filter(Boolean);
        const described = current.filter(value => value !== id).join(' ');
        if (described) field.setAttribute('aria-describedby', described);
        else field.removeAttribute('aria-describedby');
        if (field.dataset.eduNativeInvalid === 'absent') field.removeAttribute('aria-invalid');
        else if (field.dataset.eduNativeInvalid === 'false') field.setAttribute('aria-invalid', 'false');
        else if (field.dataset.eduNativeInvalid === 'true') field.setAttribute('aria-invalid', 'true');
        delete field.dataset.eduNativeInvalid;
        delete field.dataset.eduNativeError;
    }
    function onInvalid(event, doc) {
        const field = event.target;
        if (!field || !field.form || !field.validity || !field.dataset) return;
        if (field.form.noValidate || field.hasAttribute('data-edu-no-native-feedback') || hasExistingError(field)) {
            cleanup(field);
            return;
        }
        let id = field.dataset.eduNativeError;
        let error = id && doc.getElementById(id);
        if (!error) {
            error = doc.createElement('span');
            id = 'edu-native-validation-' + (++sequence);
            error.id = id;
            error.className = 'edu-native-validation';
            error.setAttribute('role', 'status');
            field.insertAdjacentElement('afterend', error);
            const described = (field.getAttribute('aria-describedby') || '').trim();
            field.setAttribute('aria-describedby', [described, id].filter(Boolean).join(' '));
            field.dataset.eduNativeError = id;
            field.dataset.eduNativeInvalid = field.hasAttribute('aria-invalid') ?
                field.getAttribute('aria-invalid') : 'absent';
            field.setAttribute('aria-invalid', 'true');
        }
        error.textContent = message(field, doc);
    }
    function onChange(event) {
        const field = event.target;
        if (!field?.dataset?.eduNativeError) return;
        if (field.validity?.valid) cleanup(field);
    }
    function mount(doc) {
        if (!doc || doc.__eduNativeValidationMounted) return;
        doc.__eduNativeValidationMounted = true;
        // The invalid event does not bubble; capture enables one listener for forms
        // added later without mutating or pre-empting existing MVC/JS handlers.
        doc.addEventListener('invalid', event => onInvalid(event, doc), true);
        doc.addEventListener('input', onChange, true);
        doc.addEventListener('change', onChange, true);
        doc.addEventListener('reset', event => {
            const fields = event.target?.elements || [];
            Array.from(fields).forEach(cleanup);
        }, true);
    }
    return { mount, message, onInvalid, onChange, cleanup, hasExistingError };
});
