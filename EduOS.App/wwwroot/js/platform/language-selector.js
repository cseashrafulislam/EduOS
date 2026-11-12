/* Culture changes must work even when optional shell scripts fail to load.
   Explicit, CSRF-protected POST persists the user's preferred language. */
(() => {
    'use strict';
    const selector = document.querySelector('[data-language-selector]');
    const form = selector?.form;
    if (!form) return;
    selector.addEventListener('change', () => {
        if (form.dataset.languageSubmitting === 'true') return;
        form.dataset.languageSubmitting = 'true';
        selector.disabled = false;
        form.requestSubmit();
    });
})();
