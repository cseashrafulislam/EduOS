(() => {
    'use strict';
    const form = document.getElementById('acceptInvitationForm');
    if (!form) return;
    const token = new URLSearchParams(window.location.hash.slice(1)).get('token');
    if (token) window.history.replaceState(null, '', window.location.pathname);
    const alert = document.getElementById('inviteAlert');
    const button = document.getElementById('acceptInviteBtn');
    function notify(message, good) {
        alert.textContent = message;
        alert.className = 'alert ' + (good ? 'alert-success' : 'alert-danger');
    }
    if (!token || !/^[A-F0-9]{64}$/.test(token)) {
        notify('Invalid invitation link. Ask your institution administrator to resend it.', false);
        button.disabled = true;
        return;
    }
    form.addEventListener('submit', async event => {
        event.preventDefault();
        const password = document.getElementById('invitePassword').value;
        const confirmPassword = document.getElementById('inviteConfirmPassword').value;
        if (!password || password !== confirmPassword) {
            notify('Password confirmation does not match.', false);
            return;
        }
        button.disabled = true;
        try {
            const response = await fetch('/api/tenant-users/invitations/accept', {
                method: 'POST', credentials: 'same-origin', cache: 'no-store',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    token, password, confirmPassword,
                    fullName: document.getElementById('inviteFullName').value.trim() || null,
                    mfaCode: document.getElementById('inviteMfaCode').value.trim() || null,
                    useRecoveryCode: document.getElementById('inviteRecovery').checked
                })
            });
            const payload = await response.json().catch(() => null);
            if (!response.ok || payload?.success !== true)
                throw new Error(payload?.message || 'Invitation could not be accepted.');
            form.reset();
            form.hidden = true;
            notify('Invitation accepted. You can now sign in to your institution.', true);
        } catch (error) {
            notify(error.message || 'Unable to accept the invitation.', false);
            button.disabled = false;
        }
    });
})();
