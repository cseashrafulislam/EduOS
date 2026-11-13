(() => {
    'use strict';
    const bn = (document.documentElement.lang || '').toLowerCase().startsWith('bn');
    document.querySelectorAll('[data-confirm-webhook]').forEach(form => {
        form.addEventListener('submit', event => {
            const retry = form.hasAttribute('data-confirm-retry');
            const message = retry
                ? (bn ? 'এই ব্যর্থ ওয়েবহুক টেস্ট আবার পাঠাবেন?' : 'Retry this signed webhook test delivery?')
                : (bn ? 'কনফিগার করা endpoint-এ signed test পাঠাবেন?' : 'Send a signed webhook test to the configured endpoint?');
            if (!confirm(message)) event.preventDefault();
        });
    });
})();
