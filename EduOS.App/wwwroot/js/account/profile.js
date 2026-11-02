// ============================================================
// PROFILE.JS
// Account Profile
// ============================================================


// The selected server culture is authoritative; no personal data is embedded in translations.
const profileBangla = document.documentElement.lang === "bn-BD";
const profileTranslations = {
    "Profile information not found.": "প্রোফাইলের তথ্য পাওয়া যায়নি।",
    "User": "ব্যবহারকারী",
    "Unable to load profile.": "প্রোফাইল লোড করা যায়নি।",
    "MFA status is unavailable.": "MFA-এর অবস্থা পাওয়া যায়নি।",
    "Enabled": "চালু",
    "Your authenticator is enabled. MFA verification applies on subsequent sign-ins.": "অথেনটিকেটর চালু আছে। পরবর্তী সাইন ইনে MFA যাচাই প্রযোজ্য হবে।",
    "Not enabled": "চালু নয়",
    "MFA is optional. Continue signing in with your password unless you enable it here.": "MFA ঐচ্ছিক। এখানে চালু না করলে পাসওয়ার্ড দিয়ে সাইন ইন করতে পারবেন।",
    "Unavailable": "পাওয়া যাচ্ছে না",
    "Unable to load MFA status. Reload this page to retry.": "MFA-এর অবস্থা লোড করা যায়নি। আবার চেষ্টা করতে পৃষ্ঠা রিফ্রেশ করুন।",
    "Full name is required.": "পূর্ণ নাম আবশ্যক।",
    "Saving...": "সংরক্ষণ হচ্ছে...",
    "Profile update failed.": "প্রোফাইল হালনাগাদ ব্যর্থ হয়েছে।",
    "Profile updated successfully.": "প্রোফাইল সফলভাবে হালনাগাদ হয়েছে।",
    "Current password is required.": "বর্তমান পাসওয়ার্ড আবশ্যক।",
    "New password is required.": "নতুন পাসওয়ার্ড আবশ্যক।",
    "Password must be at least 6 characters.": "পাসওয়ার্ড অন্তত ৬ অক্ষরের হতে হবে।",
    "Confirm password is required.": "পাসওয়ার্ড নিশ্চিত করা আবশ্যক।",
    "Passwords do not match.": "পাসওয়ার্ড দুটি মেলেনি।",
    "Changing...": "পরিবর্তন হচ্ছে...",
    "Password change failed.": "পাসওয়ার্ড পরিবর্তন ব্যর্থ হয়েছে।",
    "Password changed. Sign in again.": "পাসওয়ার্ড পরিবর্তিত হয়েছে। আবার সাইন ইন করুন।",
    "Your session has expired.": "আপনার সেশনের মেয়াদ শেষ হয়েছে।",
    "You do not have permission to perform this action.": "এই কাজ করার অনুমতি আপনার নেই।",
    "Account session changed. Reload to continue.": "অ্যাকাউন্ট সেশন পরিবর্তিত হয়েছে। চালিয়ে যেতে রিলোড করুন।",
    "Current password and verification code are required.": "বর্তমান পাসওয়ার্ড ও যাচাই কোড আবশ্যক।",
    "Recovery codes were not returned.": "রিকভারি কোড পাওয়া যায়নি।",
    "Recovery codes regenerated. Save them before signing in again.": "নতুন রিকভারি কোড তৈরি হয়েছে। আবার সাইন ইন করার আগে সংরক্ষণ করুন।",
    "Unable to update MFA settings.": "MFA সেটিংস হালনাগাদ করা যায়নি।",
    "Other devices signed out.": "অন্য ডিভাইসগুলো সাইন আউট হয়েছে।",
    "Unable to revoke other sessions.": "অন্য সেশনগুলো বাতিল করা যায়নি।",
    "Enter your current password to sign out all devices.": "সব ডিভাইস সাইন আউট করতে বর্তমান পাসওয়ার্ড লিখুন।",
    "Unable to revoke sessions.": "সেশনগুলো বাতিল করা যায়নি।",
    "Session list unavailable.": "সেশনের তালিকা পাওয়া যায়নি।",
    "No active sessions found.": "কোনো সক্রিয় সেশন পাওয়া যায়নি।",
    "This device": "এই ডিভাইস",
    "Other session": "অন্য সেশন",
    "Revoke": "বাতিল করুন",
    "Unable to revoke session.": "সেশন বাতিল করা যায়নি।",
    "Unable to load active sessions.": "সক্রিয় সেশন লোড করা যায়নি।",
    "Unable to load sessions.": "সেশনগুলো লোড করা যায়নি।",
    "Save Changes": "পরিবর্তন সংরক্ষণ করুন",
    "Change Password": "পাসওয়ার্ড পরিবর্তন",
    "Close notification": "বিজ্ঞপ্তি বন্ধ করুন",
    "Request failed. Please retry.": "অনুরোধ ব্যর্থ হয়েছে। আবার চেষ্টা করুন।"
};
function profileText(english) { return profileBangla ? (profileTranslations[english] || english) : english; }
const profileOriginalTenant = document.body?.dataset.authTenantId || '';
const profileOriginalSession = document.body?.dataset.authSessionReference || '';
let profileLocked = false;
let profileReady = false;
let mfaReady = false;
let mfaEnabled = false;
let sessionsReady = false;
function syncAccountActions() {
    const availability = {
        saveProfileBtn: profileReady, changePasswordBtn: profileReady,
        mfaDisableBtn: profileReady && mfaReady && mfaEnabled,
        mfaRegenerateBtn: profileReady && mfaReady && mfaEnabled,
        sessionRevokeOthersBtn: profileReady && sessionsReady,
        sessionRevokeAllBtn: profileReady && sessionsReady
    };
    for (const [id, allowed] of Object.entries(availability)) {
        const button = document.getElementById(id);
        if (button) button.disabled = profileLocked || !allowed;
    }
}

let profileReadGeneration = 0;
let mfaReadGeneration = 0;
let sessionsReadGeneration = 0;
function lockProfile() {
    profileLocked = true;
    profileReady = false;
    mfaReady = false; sessionsReady = false;
    ++profileReadGeneration; ++mfaReadGeneration; ++sessionsReadGeneration;
    for (const id of ["fullName","email","phoneNumber","address","currentPassword","newPassword",
        "confirmPassword","mfaManagePassword","mfaManageCode","sessionRevokePassword"]) {
        const input = document.getElementById(id);
        if (input) input.value = "";
    }
    for (const id of ["profileDisplayName","profileDisplayEmail","profileLanguage","mfaStatus","mfaStatusHelp"]) {
        const element = document.getElementById(id);
        if (element) element.textContent = "";
    }
    for (const id of ["sessionList","mfaNewCodes","alertContainer"]) document.getElementById(id)?.replaceChildren();
    const avatar = document.getElementById("profileAvatar");
    if (avatar) avatar.textContent = "U";
    document.getElementById("mfaNewCodesPanel")?.classList.add("d-none");
    document.getElementById("mfaManagement")?.classList.add("d-none");
    document.getElementById("mfaSetupLink")?.classList.add("d-none");
    for (const id of ["saveProfileBtn","changePasswordBtn","mfaDisableBtn","mfaRegenerateBtn",
        "sessionRevokeOthersBtn","sessionRevokeAllBtn"]) {
        const button = document.getElementById(id);
        if (button) button.disabled = true;
    }
}

document.addEventListener('DOMContentLoaded', function () {
    initializeProfileForm();
    initializePasswordForm();
    initializePasswordToggle();
    syncAccountActions();

    loadProfile();
    loadMfaStatus();
    initializeAuthSecurityControls();
    loadActiveSessions();
});

// ============================================================
// PROFILE LOAD
// ============================================================

async function loadProfile() {
    const generation = ++profileReadGeneration;
    try {
        const response = await apiRequest('/api/auth/profile', { cache: 'no-store', sessionBound: true });

        if (profileLocked || generation !== profileReadGeneration) return;
        const profile = response?.data;

        if (!profile) {
            throw new Error(profileText("Profile information not found."));
        }

        setValue('fullName', profile.fullName);
        setValue('email', profile.email);
        setValue('phoneNumber', profile.phoneNumber);
        setValue('address', profile.address);

        setText('profileDisplayName', profile.fullName || profileText("User"));
        setText('profileDisplayEmail', profile.email || '');
        setText('profileLanguage', profile.preferredLanguage === 'en-BD' ? 'English (Bangladesh)' : 'বাংলা (বাংলাদেশ)');

        updateAvatar(profile.fullName);
        profileReady = true;
        syncAccountActions();
    } catch (error) {
        if (profileLocked || generation !== profileReadGeneration) return;

        showAlert(
            'danger',
            error.message || profileText("Unable to load profile.")
        );
    }
}

// ============================================================
// OPTIONAL MFA STATUS (no sign-in enforcement or setup prompt)
// ============================================================

async function loadMfaStatus() {
    const generation = ++mfaReadGeneration;
    const status = document.getElementById('mfaStatus');
    const help = document.getElementById('mfaStatusHelp');
    const setupLink = document.getElementById('mfaSetupLink');
    if (!status || !help || !setupLink) return;
    setupLink.classList.add('d-none');
    try {
        const response = await apiRequest('/api/auth/mfa/status', { cache: 'no-store', sessionBound: true });
        if (profileLocked || generation !== mfaReadGeneration) return;
        if (response?.success !== true || typeof response.data?.enabled !== 'boolean')
            throw new Error(profileText("MFA status is unavailable."));
        mfaReady = true;
        mfaEnabled = response.data.enabled;
        syncAccountActions();
        if (response.data.enabled) {
            status.textContent = profileText("Enabled");
            status.className = 'badge bg-success';
            help.textContent = profileText("Your authenticator is enabled. MFA verification applies on subsequent sign-ins.");
            document.getElementById('mfaManagement')?.classList.remove('d-none');
        } else {
            status.textContent = profileText("Not enabled");
            status.className = 'badge bg-secondary';
            help.textContent = profileText("MFA is optional. Continue signing in with your password unless you enable it here.");
            setupLink.classList.remove('d-none');
        }
    } catch {
        if (profileLocked || generation !== mfaReadGeneration) return;
        status.textContent = profileText("Unavailable");
        status.className = 'badge bg-warning text-dark';
        help.textContent = profileText("Unable to load MFA status. Reload this page to retry.");
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
    if (profileLocked || !profileReady) return;
    clearValidation();

    const dto = {
        fullName: getValue('fullName'),
        phoneNumber: getValue('phoneNumber') || null,
        address: getValue('address') || null
    };

    if (!dto.fullName) {
        showInputError(
            'fullName',
            profileText("Full name is required.")
        );

        return;
    }

    const button = document.getElementById('saveProfileBtn');

    setLoading(button, true, profileText("Saving..."));

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
                profileText("Profile update failed.")
            );
        }

        await loadProfile();
        if (profileLocked || !profileReady) return;

        showAlert(
            'success',
            profileText("Profile updated successfully.")
        );
    } catch (error) {

        showAlert(
            'danger',
            error.message || profileText("Profile update failed.")
        );
    } finally {
        setLoading(
            button,
            false,
            `<i class="bi bi-check-circle me-1"></i> ${profileText('Save Changes')}`,
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
    if (profileLocked || !profileReady) return;
    clearPasswordValidation();

    const dto = {
        currentPassword: getValue('currentPassword'),
        newPassword: getValue('newPassword'),
        confirmPassword: getValue('confirmPassword')
    };

    if (!dto.currentPassword) {
        showPasswordError(
            'currentPassword',
            profileText("Current password is required.")
        );

        return;
    }

    if (!dto.newPassword) {
        setText(
            'newPasswordError',
            profileText("New password is required.")
        );

        return;
    }

    if (dto.newPassword.length < 6) {
        setText(
            'newPasswordError',
            profileText("Password must be at least 6 characters.")
        );

        return;
    }

    if (!dto.confirmPassword) {
        setText(
            'confirmPasswordError',
            profileText("Confirm password is required.")
        );

        return;
    }

    if (dto.newPassword !== dto.confirmPassword) {
        setText(
            'confirmPasswordError',
            profileText("Passwords do not match.")
        );

        return;
    }

    const button = document.getElementById('changePasswordBtn');

    setLoading(button, true, profileText("Changing..."));

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
                profileText("Password change failed.")
            );
        }

        document.getElementById('passwordForm')?.reset();

        showAlert('success', profileText("Password changed. Sign in again."));
        window.location.assign('/Account/Login?status=password-changed');
    } catch (error) {

        showAlert(
            'danger',
            error.message || profileText("Password change failed.")
        );
    } finally {
        setLoading(
            button,
            false,
            `<i class="bi bi-shield-check me-1"></i> ${profileText('Change Password')}`,
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
    const { sessionBound = false, ...fetchOptions } = options;
    const method = String(fetchOptions.method || 'GET').toUpperCase();
    const expectedSession = document.body?.dataset.authSessionReference || '';
    const expectedTenant = document.body?.dataset.authTenantId || '';
    if (profileLocked || expectedTenant !== profileOriginalTenant ||
        expectedSession !== profileOriginalSession) {
        lockProfile();
        throw new Error(profileText('Account session changed. Reload to continue.'));
    }
    if (!/^[0-9a-f]{64}$/i.test(expectedSession) ||
        (expectedTenant !== '' && !/^[1-9]\d*$/.test(expectedTenant)))
        throw new Error(profileText('Account session changed. Reload to continue.'));
    if (!['GET','HEAD'].includes(method)) {
        // A stale form must never submit against a different cookie/institution.
        if (typeof window.EduOSVerifySessionContext !== 'function' ||
            window.EduOSSessionTransitionPending?.() === true)
            throw new Error(profileText('Account session changed. Reload to continue.'));
        await window.EduOSVerifySessionContext();
        if (document.getElementById('eduosSessionShield')?.hidden !== true ||
            document.body?.dataset.authSessionReference !== expectedSession ||
            document.body?.dataset.authTenantId !== expectedTenant ||
            window.EduOSSessionTransitionPending?.() === true)
            throw new Error(profileText('Account session changed. Reload to continue.'));
    }
    const response = await fetch(url, {
        credentials: 'same-origin', cache: 'no-store', ...fetchOptions,
        headers: { Accept: 'application/json', ...(fetchOptions.headers || {}) }
    });
    // Recheck every completed request, including successful writes, against its original scope.
    // Never show stale account success or recovery information after a session transition.
    if (profileLocked || document.body?.dataset.authSessionReference !== expectedSession ||
        document.body?.dataset.authTenantId !== expectedTenant ||
        window.EduOSSessionTransitionPending?.() === true) {
        lockProfile();
        throw new Error(profileText('Account session changed. Reload to continue.'));
    }
    if (response.status === 401) {
        lockProfile();
        window.location.assign('/Account/Login');
        throw new Error(profileText('Your session has expired.'));
    }
    if (response.status === 403) {
        lockProfile();
        throw new Error(profileText('You do not have permission to perform this action.'));
    }
    if (sessionBound) {
        const received = response.headers.get('X-EduOS-Session-Reference');
        const receivedTenant = response.headers.get('X-EduOS-Tenant-Id');
        if (profileLocked || document.body?.dataset.authSessionReference !== expectedSession ||
            document.body?.dataset.authTenantId !== expectedTenant ||
            received !== expectedSession || receivedTenant !== expectedTenant ||
            window.EduOSSessionTransitionPending?.() === true) {
            lockProfile();
            void window.EduOSVerifySessionContext?.();
            throw new Error(profileText('Account session changed. Reload to continue.'));
        }
    }
    if (!response.ok) throw new Error(profileText('Request failed. Please retry.'));
    if (!(response.headers.get('content-type') || '').includes('application/json'))
        throw new Error(profileText('Request failed. Please retry.'));
    return response.json();
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
                    aria-label="${escapeHtml(profileText('Close notification'))}"
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

    button.disabled = loading || profileLocked || !profileReady;

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

async function authSecurityPost(url, body, sessionBound = false) {
    return apiRequest(url, {
        method: 'POST', cache: 'no-store', sessionBound,
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
                showAlert('danger', profileText("Current password and verification code are required."));
                return;
            }
            button.disabled = true;
            try {
                const result = await authSecurityPost(url, { currentPassword, code, useRecoveryCode }, true);
                document.getElementById('mfaManagePassword').value = '';
                document.getElementById('mfaManageCode').value = '';
                if (id === 'mfaDisableBtn') {
                    window.location.assign('/Account/Login');
                    return;
                }
                const codes = result?.data?.recoveryCodes;
                if (!Array.isArray(codes)) throw new Error(profileText("Recovery codes were not returned."));
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
                showAlert('success', profileText("Recovery codes regenerated. Save them before signing in again."));
            } catch (error) {
                showAlert('danger', error.message || profileText("Unable to update MFA settings."));
            } finally {
                if (document.getElementById('mfaNewCodesPanel')?.classList.contains('d-none') !== false)
                    syncAccountActions();
            }
        });
    }

    document.getElementById('sessionRevokeOthersBtn')?.addEventListener('click', async event => {
        const button = event.currentTarget;
        const input = document.getElementById('sessionRevokePassword');
        const currentPassword = input?.value || '';
        if (!currentPassword) {
            showAlert('danger', button.dataset.required || profileText("Current password is required."));
            input?.focus();
            return;
        }
        button.disabled = true;
        try {
            await authSecurityPost('/api/auth/sessions/revoke-others', { currentPassword });
            if (input) input.value = '';
            await loadActiveSessions();
            showAlert('success', button.dataset.success || profileText("Other devices signed out."));
        } catch (error) {
            showAlert('danger', error.message || button.dataset.failed || profileText("Unable to revoke other sessions."));
        } finally {
            syncAccountActions();
        }
    });

    document.getElementById('sessionRevokeAllBtn')?.addEventListener('click', async event => {
        const button = event.currentTarget;
        const currentPassword = document.getElementById('sessionRevokePassword')?.value || '';
        if (!currentPassword) {
            showAlert('danger', profileText("Enter your current password to sign out all devices."));
            return;
        }
        button.disabled = true;
        try {
            await authSecurityPost('/api/auth/sessions/revoke-all', { currentPassword });
            window.location.assign('/Account/Login');
        } catch (error) {
            showAlert('danger', error.message || profileText("Unable to revoke sessions."));
            syncAccountActions();
        }
    });
}

async function loadActiveSessions() {
    const generation = ++sessionsReadGeneration;
    const container = document.getElementById('sessionList');
    if (!container) return;
    try {
        const response = await apiRequest('/api/auth/sessions', { cache: 'no-store', sessionBound: true });
        if (profileLocked || generation !== sessionsReadGeneration) return;
        if (!response?.success || !Array.isArray(response.data)) throw new Error(profileText("Session list unavailable."));
        sessionsReady = true;
        syncAccountActions();
        container.replaceChildren();
        if (!response.data.length) {
            container.textContent = container.dataset.empty || profileText("No active sessions found.");
            return;
        }
        for (const session of response.data) {
            const row = document.createElement('div');
            row.setAttribute('role', 'listitem');
            row.className = 'd-flex flex-wrap justify-content-between align-items-center border-bottom py-2 gap-2';
            const description = document.createElement('div');
            const detail = document.createElement('strong');
            detail.textContent = session.isCurrent ? (container.dataset.current || profileText("This device")) : (container.dataset.other || profileText("Other session"));
            const meta = document.createElement('div');
            meta.className = 'text-muted small';
            meta.textContent = [session.ipAddress, session.userAgent, session.createdAtUtc]
                .filter(Boolean).join(' • ');
            description.append(detail, meta);
            const revoke = document.createElement('button');
            revoke.type = 'button';
            revoke.className = 'btn btn-sm btn-outline-danger';
            revoke.textContent = container.dataset.revoke || profileText("Revoke");
            revoke.addEventListener('click', async () => {
                const input = document.getElementById('sessionRevokePassword');
                const currentPassword = input?.value || '';
                if (!session.isCurrent && !currentPassword) {
                    showAlert('danger', container.dataset.passwordRequired || profileText("Current password is required."));
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
                    showAlert('danger', error.message || container.dataset.revokeError || profileText("Unable to revoke session."));
                    revoke.disabled = profileLocked || !sessionsReady;
                }
            });
            row.append(description, revoke);
            container.appendChild(row);
        }
    } catch (error) {
        if (profileLocked || generation !== sessionsReadGeneration) return;
        container.textContent = container.dataset.loadingError || profileText("Unable to load active sessions.");
        showAlert('danger', error.message || profileText("Unable to load sessions."));
    }
}
