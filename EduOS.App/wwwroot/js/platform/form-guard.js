/* Shared opt-in protection for transactional forms. The server remains authoritative for
   authorization, validation, idempotency, money, concurrency and posting. */
(function (root) {
    'use strict';
    const active = new Set();
    let navigationGuardRegistered = false;
    function fingerprint(form) {
        const fields = Array.from(form.elements || []).filter(field =>
            ['INPUT', 'SELECT', 'TEXTAREA'].includes(field.tagName) &&
            !['button', 'submit', 'reset', 'image'].includes((field.type || '').toLowerCase()));
        return JSON.stringify(fields.map((field, index) => [
            field.name || field.id || String(index), field.type || '',
            field.type === 'checkbox' || field.type === 'radio' ? !!field.checked :
                field.multiple ? Array.from(field.selectedOptions || []).map(option => option.value) :
                field.value ?? ''
        ]));
    }
    function create(form) {
        if (!form || !form.elements) throw new Error('A form is required.');
        let savedFingerprint = fingerprint(form);
        const guard = Object.freeze({
            isDirty: () => fingerprint(form) !== savedFingerprint,
            markClean: () => { savedFingerprint = fingerprint(form); },
            confirmDiscard: () => fingerprint(form) === savedFingerprint ||
                (typeof root.confirm === 'function' && root.confirm(
                    (root.document?.documentElement?.lang || '').toLowerCase().startsWith('bn')
                        ? 'সংরক্ষণ না করা পরিবর্তন বাদ দেবেন?' : 'Discard unsaved changes?')),
            dispose: () => active.delete(guard)
        });
        active.add(guard);
        if (!navigationGuardRegistered && typeof root.addEventListener === 'function') {
            root.addEventListener('beforeunload', event => {
                if (![...active].some(item => item.isDirty())) return;
                event.preventDefault();
                event.returnValue = '';
            });
            navigationGuardRegistered = true;
        }
        return guard;
    }
    root.EduOSFormGuard = Object.freeze({ create, fingerprint });
})(typeof window !== 'undefined' ? window : globalThis);
