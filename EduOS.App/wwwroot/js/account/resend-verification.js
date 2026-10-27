(() => {
    'use strict';
    document.querySelectorAll('form[data-verification-recovery]').forEach(form => {
        form.addEventListener('submit', async event => {
            event.preventDefault();
            const email = form.querySelector('input[name="email"]');
            const button = form.querySelector('button[type="submit"]');
            const status = form.querySelector('[data-recovery-status]');
            if (!email || !button || !status) return;
            if (!email.validity.valid) {
                status.textContent = 'Enter a valid registration email address.';
                return;
            }
            button.disabled = true;
            status.textContent = 'Submitting your request…';
            try {
                const response = await fetch('/api/institution-onboarding/resend-verification', {
                    method: 'POST',
                    credentials: 'same-origin',
                    cache: 'no-store',
                    headers: {
                        'Content-Type': 'application/json',
                        'Accept': 'application/json',
                        'RequestVerificationToken': form.querySelector('input[name="__RequestVerificationToken"]')?.value || ''
                    },
                    body: JSON.stringify({ email: email.value.trim() })
                });
                const result = await response.json().catch(() => null);
                status.textContent = response.ok && result?.success
                    ? 'If your registration is eligible, a verification email will be sent. Please check your inbox and spam folder.'
                    : 'Unable to submit right now. Please try again later.';
            } catch {
                status.textContent = 'Unable to connect. Please try again later.';
            } finally {
                button.disabled = false;
            }
        });
    });
})();
