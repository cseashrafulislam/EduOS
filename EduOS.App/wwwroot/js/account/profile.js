// ============================================================
// PROFILE.JS
// Account Profile
// ============================================================

document.addEventListener('DOMContentLoaded', function () {
    initializeProfileForm();
    initializePasswordForm();
    initializePasswordToggle();

    loadProfile();
    loadMfaStatus();
    initializeAuthSecurityControls();
    loadActiveSessions();
});

// ============================================================
// PROFILE LOAD
// ============================================================

async function loadProfile() {
    try {
        const response = await apiRequest('/api/auth/profile');

        const profile = response?.data;

        if (!profile) {
            throw new Error('Profile information not found.');
        }

        setValue('fullName', profile.fullName);
        setValue('email', profile.email);
        setValue('phoneNumber', profile.phoneNumber);
        setValue('address', profile.address);

        setText('profileDisplayName', profile.fullName || 'User');
        setText('profileDisplayEmail', profile.email || '');
        setText('profileLanguage', profile.preferredLanguage === 'en-BD' ? 'English (Bangladesh)' : 'বাংলা (বাংলাদেশ)');

        updateAvatar(profile.fullName);
    } catch (error) {
        console.error('Profile load error:', error);

        showAlert(
            'danger',
            error.message || 'Unable to load profile.'
        );
    }
}

// ============================================================
// OPTIONAL MFA STATUS (no sign-in enforcement or setup prompt)
// ============================================================

async function loadMfaStatus() {
    const status = document.getElementById('mfaStatus');
    const help = document.getElementById('mfaStatusHelp');
    const setupLink = document.getElementById('mfaSetupLink');
    if (!status || !help || !setupLink) return;
    setupLink.classList.add('d-none');
    try {
        const response = await apiRequest('/api/auth/mfa/status');
        if (response?.success !== true || typeof response.data?.enabled !== 'boolean')
            throw new Error('MFA status is unavailable.');
        if (response.data.enabled) {
            status.textContent = 'Enabled';
            status.className = 'badge bg-success';
            help.textContent = 'Your authenticator is enabled. MFA verification applies on subsequent sign-ins.';
            document.getElementById('mfaManagement')?.classList.remove('d-none');
        } else {
            status.textContent = 'Not enabled';
            status.className = 'badge bg-secondary';
            help.textContent = 'MFA is optional. Continue signing in with your password unless you enable it here.';
            setupLink.classList.remove('d-none');
        }
    } catch {
        status.textContent = 'Unavailable';
        status.className = 'badge bg-warning text-dark';
        help.textContent = 'Unable to load MFA status. Reload this page to retry.';
    }
}

// ============================================================
// PROFILE UPDATE
// ============================================================

function initializeProfileForm() {
    const form = document.getElementById('profileForm');

    if (!form) {
        return;
    }

    form.addEventListener('submit', async function (event) {
        event.preventDefault();

        await updateProfile();
    });
}

async function updateProfile() {
    clearValidation();

    const dto = {
        fullName: getValue('fullName'),
        phoneNumber: getValue('phoneNumber') || null,
        address: getValue('address') || null
    };

    if (!dto.fullName) {
        showInputError(
            'fullName',
            'Full name is required.'
        );

        return;
    }

    const button = document.getElementById('saveProfileBtn');

    setLoading(button, true, 'Saving...');

    try {
        const response = await apiRequest(
            '/api/auth/profile',
            {
                method: 'PUT',
                headers: {
                    'Content-Type': 'application/json',
                    'X-CSRF-TOKEN': getAntiForgeryToken()
                },
                body: JSON.stringify(dto)
            }
        );

        if (response?.success === false) {
            throw new Error(
                response.message || 'Profile update failed.'
            );
        }

        await loadProfile();

        showAlert(
            'success',
            response?.message || 'Profile updated successfully.'
        );
    } catch (error) {
        console.error('Profile update error:', error);

        showAlert(
            'danger',
            error.message || 'Profile update failed.'
        );
    } finally {
        setLoading(
            button,
            false,
            '<i class="bi bi-check-circle me-1"></i> Save Changes',
            true
        );
    }
}

// ============================================================
// PASSWORD
// ============================================================

function initializePasswordForm() {
    const form = document.getElementById('passwordForm');

    if (!form) {
        return;
    }

    form.addEventListener('submit', async function (event) {
        event.preventDefault();

        await changePassword();
    });
}

async function changePassword() {
    clearPasswordValidation();

    const dto = {
        currentPassword: getValue('currentPassword'),
        newPassword: getValue('newPassword'),
        confirmPassword: getValue('confirmPassword')
    };

    if (!dto.currentPassword) {
        showPasswordError(
            'currentPassword',
            'Current password is required.'
        );

        return;
    }

    if (!dto.newPassword) {
        setText(
            'newPasswordError',
            'New password is required.'
        );

        return;
    }

    if (dto.newPassword.length < 6) {
        setText(
            'newPasswordError',
            'Password must be at least 6 characters.'
        );

        return;
    }

    if (!dto.confirmPassword) {
        setText(
            'confirmPasswordError',
            'Confirm password is required.'
        );

        return;
    }

    if (dto.newPassword !== dto.confirmPassword) {
        setText(
            'confirmPasswordError',
            'Passwords do not match.'
        );

        return;
    }

    const button = document.getElementById('changePasswordBtn');

    setLoading(button, true, 'Changing...');

    try {
        const response = await apiRequest(
            '/api/auth/change-password',
            {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'X-CSRF-TOKEN': getAntiForgeryToken()
                },
                body: JSON.stringify(dto)
            }
        );

        if (response?.success === false) {
            throw new Error(
                response.message || 'Password change failed.'
            );
        }

        document.getElementById('passwordForm')?.reset();

        showAlert('success', response?.message || 'Password changed. Sign in again.');
        window.location.assign('/Account/Login?status=password-changed');
    } catch (error) {
        console.error('Password change error:', error);

        showAlert(
            'danger',
            error.message || 'Password change failed.'
        );
    } finally {
        setLoading(
            button,
            false,
            '<i class="bi bi-shield-check me-1"></i> Change Password',
            true
        );
    }
}

// ============================================================
// PASSWORD TOGGLE
// ============================================================

function initializePasswordToggle() {
    document
        .querySelectorAll('.password-toggle')
        .forEach(function (button) {

            button.addEventListener('click', function () {
                const targetId = button.dataset.target;
                const input = document.getElementById(targetId);

                if (!input) {
                    return;
                }

                const showPassword = input.type === 'password';

                input.type = showPassword
                    ? 'text'
                    : 'password';

                const icon = button.querySelector('i');

                if (icon) {
                    icon.className = showPassword
                        ? 'bi bi-eye-slash'
                        : 'bi bi-eye';
                }
            });
        });
}

// ============================================================
// FETCH HELPER
// ============================================================

async function apiRequest(url, options = {}) {
    const requestOptions = {
        credentials: 'include',
        ...options,
        headers: {
            Accept: 'application/json',
            ...(options.headers || {})
        }
    };

    const response = await fetch(url, requestOptions);

    let result = null;

    const contentType =
        response.headers.get('content-type') || '';

    if (contentType.includes('application/json')) {
        result = await response.json();
    } else {
        const text = await response.text();

        if (text) {
            result = {
                message: text
            };
        }
    }

    if (response.status === 401) {
        window.location.href = '/Account/Login';

        throw new Error(
            'Your session has expired.'
        );
    }

    if (response.status === 403) {
        throw new Error(
            result?.message ||
            'You do not have permission to perform this action.'
        );
    }

    if (!response.ok) {
        throw new Error(
            result?.message ||
            result?.title ||
            `Request failed with status ${response.status}.`
        );
    }

    return result;
}

// ============================================================
// ANTI FORGERY
// ============================================================

function getAntiForgeryToken() {
    return document.querySelector(
        'input[name="__RequestVerificationToken"]'
    )?.value ?? '';
}

// ============================================================
// AVATAR
// ============================================================

function updateAvatar(fullName) {
    const avatar = document.getElementById('profileAvatar');

    if (!avatar) {
        return;
    }

    const name = String(fullName || '').trim();

    if (!name) {
        avatar.textContent = 'U';
        return;
    }

    const parts = name
        .split(/\s+/)
        .filter(Boolean);

    const initials = parts.length === 1
        ? parts[0].substring(0, 2)
        : `${parts[0][0]}${parts[parts.length - 1][0]}`;

    avatar.textContent =
        initials.toUpperCase();
}

// ============================================================
// VALIDATION
// ============================================================

function showInputError(id, message) {
    const input = document.getElementById(id);

    if (!input) {
        return;
    }

    input.classList.add('is-invalid');

    const feedback =
        input.parentElement?.querySelector('.invalid-feedback');

    if (feedback) {
        feedback.textContent = message;
    }
}

function showPasswordError(id, message) {
    const input = document.getElementById(id);

    if (!input) {
        return;
    }

    input.classList.add('is-invalid');

    const group = input.closest('.input-group');

    const feedback =
        group?.querySelector('.invalid-feedback');

    if (feedback) {
        feedback.textContent = message;
    }
}

function clearValidation() {
    document
        .querySelectorAll('#profileForm .is-invalid')
        .forEach(function (input) {
            input.classList.remove('is-invalid');
        });
}

function clearPasswordValidation() {
    document
        .querySelectorAll('#passwordForm .is-invalid')
        .forEach(function (input) {
            input.classList.remove('is-invalid');
        });

    setText('newPasswordError', '');
    setText('confirmPasswordError', '');
}

// ============================================================
// COMMON HELPERS
// ============================================================

function getValue(id) {
    return document
        .getElementById(id)
        ?.value
        ?.trim() ?? '';
}

function setValue(id, value) {
    const element = document.getElementById(id);

    if (element) {
        element.value = value ?? '';
    }
}

function setText(id, value) {
    const element = document.getElementById(id);

    if (element) {
        element.textContent = value ?? '';
    }
}

// ============================================================
// ALERT
// ============================================================

function showAlert(type, message) {
    const container =
        document.getElementById('alertContainer');

    if (!container) {
        return;
    }

    container.innerHTML = `
        <div class="alert alert-${type} alert-dismissible fade show"
             role="alert">

            ${escapeHtml(message)}

            <button type="button"
                    class="btn-close"
                    data-bs-dismiss="alert">
            </button>

        </div>
    `;

    window.setTimeout(function () {
        container.innerHTML = '';
    }, 5000);
}

// ============================================================
// LOADING BUTTON
// ============================================================

function setLoading(button, loading, text, isHtml = false) {
    if (!button) {
        return;
    }

    button.disabled = loading;

    if (loading) {
        button.innerHTML = `
            <span class="spinner-border spinner-border-sm me-2"></span>
            ${escapeHtml(text)}
        `;

        return;
    }

    if (isHtml) {
        button.innerHTML = text;
    } else {
        button.textContent = text;
    }
}

// ============================================================
// HTML ESCAPE
// ============================================================

function escapeHtml(value) {
    return String(value ?? '').replace(
        /[&<>"']/g,
        function (character) {
            const entities = {
                '&': '&amp;',
                '<': '&lt;',
                '>': '&gt;',
                '"': '&quot;',
                "'": '&#39;'
            };

            return entities[character];
        }
    );
}

async function authSecurityPost(url, body) {
    return apiRequest(url, {
        method: 'POST', cache: 'no-store',
        headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': getAntiForgeryToken() },
        body: JSON.stringify(body)
    });
}

function initializeAuthSecurityControls() {
    for (const [id, url] of [
        ['mfaDisableBtn', '/api/auth/mfa/disable'],
        ['mfaRegenerateBtn', '/api/auth/mfa/recovery-codes/regenerate']
    ]) {
        const button = document.getElementById(id);
        if (!button) continue;
        button.addEventListener('click', async () => {
            const currentPassword = document.getElementById('mfaManagePassword')?.value || '';
            const code = document.getElementById('mfaManageCode')?.value.trim() || '';
            const useRecoveryCode = document.getElementById('mfaManageRecovery')?.checked ?? false;
            if (!currentPassword || !code) {
                showAlert('danger', 'Current password and verification code are required.');
                return;
            }
            button.disabled = true;
            try {
                const result = await authSecurityPost(url, { currentPassword, code, useRecoveryCode });
                document.getElementById('mfaManagePassword').value = '';
                document.getElementById('mfaManageCode').value = '';
                if (id === 'mfaDisableBtn') {
                    window.location.assign('/Account/Login');
                    return;
                }
                const codes = result?.data?.recoveryCodes;
                if (!Array.isArray(codes)) throw new Error('Recovery codes were not returned.');
                const list = document.getElementById('mfaNewCodes');
                list?.replaceChildren();
                for (const codeValue of codes) {
                    const item = document.createElement('li');
                    item.textContent = String(codeValue);
                    list?.appendChild(item);
                }
                document.getElementById('mfaNewCodesPanel')?.classList.remove('d-none');
                document.getElementById('mfaRegenerateBtn').disabled = true;
                document.getElementById('mfaDisableBtn').disabled = true;
                showAlert('success', 'Recovery codes regenerated. Save them before signing in again.');
            } catch (error) {
                showAlert('danger', error.message || 'Unable to update MFA settings.');
            } finally {
                if (document.getElementById('mfaNewCodesPanel')?.classList.contains('d-none') !== false)
                    button.disabled = false;
            }
        });
    }

    document.getElementById('sessionRevokeOthersBtn')?.addEventListener('click', async event => {
        const button = event.currentTarget;
        const input = document.getElementById('sessionRevokePassword');
        const currentPassword = input?.value || '';
        if (!currentPassword) {
            showAlert('danger', button.dataset.required || 'Current password is required.');
            input?.focus();
            return;
        }
        button.disabled = true;
        try {
            await authSecurityPost('/api/auth/sessions/revoke-others', { currentPassword });
            if (input) input.value = '';
            await loadActiveSessions();
            showAlert('success', button.dataset.success || 'Other devices signed out.');
        } catch (error) {
            showAlert('danger', error.message || button.dataset.failed || 'Unable to revoke other sessions.');
        } finally {
            button.disabled = false;
        }
    });

    document.getElementById('sessionRevokeAllBtn')?.addEventListener('click', async event => {
        const button = event.currentTarget;
        const currentPassword = document.getElementById('sessionRevokePassword')?.value || '';
        if (!currentPassword) {
            showAlert('danger', 'Enter your current password to sign out all devices.');
            return;
        }
        button.disabled = true;
        try {
            await authSecurityPost('/api/auth/sessions/revoke-all', { currentPassword });
            window.location.assign('/Account/Login');
        } catch (error) {
            showAlert('danger', error.message || 'Unable to revoke sessions.');
            button.disabled = false;
        }
    });
}

async function loadActiveSessions() {
    const container = document.getElementById('sessionList');
    if (!container) return;
    try {
        const response = await apiRequest('/api/auth/sessions', { cache: 'no-store' });
        if (!response?.success || !Array.isArray(response.data)) throw new Error('Session list unavailable.');
        container.replaceChildren();
        if (!response.data.length) {
            container.textContent = container.dataset.empty || 'No active sessions found.';
            return;
        }
        for (const session of response.data) {
            const row = document.createElement('div');
            row.setAttribute('role', 'listitem');
            row.className = 'd-flex flex-wrap justify-content-between align-items-center border-bottom py-2 gap-2';
            const description = document.createElement('div');
            const detail = document.createElement('strong');
            detail.textContent = session.isCurrent ? (container.dataset.current || 'This device') : (container.dataset.other || 'Other session');
            const meta = document.createElement('div');
            meta.className = 'text-muted small';
            meta.textContent = [session.ipAddress, session.userAgent, session.createdAtUtc]
                .filter(Boolean).join(' • ');
            description.append(detail, meta);
            const revoke = document.createElement('button');
            revoke.type = 'button';
            revoke.className = 'btn btn-sm btn-outline-danger';
            revoke.textContent = container.dataset.revoke || 'Revoke';
            revoke.addEventListener('click', async () => {
                const input = document.getElementById('sessionRevokePassword');
                const currentPassword = input?.value || '';
                if (!session.isCurrent && !currentPassword) {
                    showAlert('danger', container.dataset.passwordRequired || 'Current password is required.');
                    input?.focus();
                    return;
                }
                revoke.disabled = true;
                try {
                    await authSecurityPost('/api/auth/sessions/revoke', {
                        sessionId: session.sessionId,
                        currentPassword: session.isCurrent ? null : currentPassword
                    });
                    if (session.isCurrent) window.location.assign('/Account/Login');
                    else {
                        if (input) input.value = '';
                        await loadActiveSessions();
                    }
                } catch (error) {
                    showAlert('danger', error.message || container.dataset.revokeError || 'Unable to revoke session.');
                    revoke.disabled = false;
                }
            });
            row.append(description, revoke);
            container.appendChild(row);
        }
    } catch (error) {
        container.textContent = container.dataset.loadingError || 'Unable to load active sessions.';
        showAlert('danger', error.message || 'Unable to load sessions.');
    }
}
