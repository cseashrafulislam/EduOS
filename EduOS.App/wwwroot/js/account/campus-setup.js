// ============================================================
// CAMPUS-SETUP.JS - Onboarding Step 4
// ============================================================

document.addEventListener('DOMContentLoaded', async function () {

    await loadCampusList();

    // ── Add / Edit campus ─────────────────────────────────────
    const campusForm = document.getElementById('campusForm');
    if (campusForm) {
        campusForm.addEventListener('submit', async function (e) {
            e.preventDefault();
            clearErrors();

            const dto = {
                id: val('campusId') ? parseInt(val('campusId')) : null,
                name: val('campusName'),
                code: val('campusCode'),
                address: val('campusAddress'),
                phone: val('campusPhone'),
                email: val('campusEmail'),
                headName: val('campusHeadName'),
                isHeadOffice: document.getElementById('isHeadOffice')?.checked ?? false
            };

            if (!dto.name) { showError('campusName', 'Campus name is required'); return; }

            const btn = document.getElementById('saveCampusBtn');
            setLoading(btn, true, 'Saving...');

            try {
                const res = await fetch('/api/institution-onboarding/campus', {
                    method: 'POST',
                    credentials: 'include',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(dto)
                });
                const json = await res.json();
                if (json.success) {
                    closeCampusModal();
                    await loadCampusList();
                    showAlert('success', 'Campus saved successfully');
                } else {
                    showAlert('danger', json.message || 'Save failed');
                }
            } catch {
                showAlert('danger', 'Network error');
            } finally {
                setLoading(btn, false, 'Save campus');
            }
        });
    }

    // ── Continue button ───────────────────────────────────────
    document.getElementById('continueBtn')?.addEventListener('click', async function () {
        setLoading(this, true, 'Continuing...');
        try {
            await fetch('/api/onboarding/complete-step', {
                method: 'POST', credentials: 'include',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ step: 4, skipped: false })
            });
            window.location.href = '/Account/AcademicSetup';
        } catch {
            showAlert('danger', 'Error. Please try again.');
            setLoading(this, false, 'Continue');
        }
    });
});

// ── Load campus list ──────────────────────────────────────────
async function loadCampusList() {
    try {
        const res = await fetch('/api/institution-onboarding/campus-list', { credentials: 'include' });
        const json = await res.json();
        renderCampusList(Array.isArray(json) ? json : json.data || []);
    } catch { /* ignore */ }
}

function renderCampusList(campuses) {
    const list = document.getElementById('campusList');
    if (!list) return;
    const btn = document.getElementById('continueBtn');

    if (!campuses || campuses.length === 0) {
        list.innerHTML = `<div class="text-center py-4 text-muted">
            <i class="bi bi-geo-alt" style="font-size:32px"></i>
            <div class="mt-2">No campuses added yet. Add at least one to continue.</div></div>`;
        if (btn) btn.disabled = true;
        return;
    }

    if (btn) btn.disabled = false;

    list.innerHTML = campuses.map(c => `
        <div class="campus-item d-flex align-items-center justify-content-between p-3 border rounded mb-2">
            <div>
                <div class="fw-bold">${escHtml(c.name)}
                    ${c.isHeadOffice ? '<span class="badge bg-primary ms-1">Head office</span>' : ''}
                </div>
                <div class="small text-muted">${escHtml(c.code || '')} ${c.address ? '· ' + escHtml(c.address) : ''}</div>
            </div>
            <div class="d-flex gap-2">
                <button class="btn btn-sm btn-outline-secondary"
                        onclick="editCampus(${c.id})" title="Edit">
                    <i class="bi bi-pencil"></i>
                </button>
                <button class="btn btn-sm btn-outline-danger"
                        onclick="deleteCampus(${c.id}, '${escHtml(c.name)}')" title="Delete">
                    <i class="bi bi-trash"></i>
                </button>
            </div>
        </div>`).join('');
}

async function editCampus(id) {
    try {
        const res = await fetch(`/api/institution-onboarding/campus/${id}`, { credentials: 'include' });
        const json = await res.json();
        const d = json.data || json;
        setVal('campusId', id);
        setVal('campusName', d.name);
        setVal('campusCode', d.code);
        setVal('campusAddress', d.address);
        setVal('campusPhone', d.phone);
        setVal('campusEmail', d.email);
        setVal('campusHeadName', d.headName);
        const hq = document.getElementById('isHeadOffice');
        if (hq) hq.checked = d.isHeadOffice;
        openCampusModal();
    } catch { showAlert('danger', 'Could not load campus.'); }
}

async function deleteCampus(id, name) {
    if (!confirm(`Delete campus "${name}"? This cannot be undone.`)) return;
    try {
        const res = await fetch(`/api/institution-onboarding/campus/${id}`, {
            method: 'DELETE', credentials: 'include'
        });
        const json = await res.json();
        if (json.success) {
            await loadCampusList();
            showAlert('success', 'Campus deleted.');
        } else {
            showAlert('danger', json.message || 'Delete failed.');
        }
    } catch { showAlert('danger', 'Network error.'); }
}

function openCampusModal() {
    const modal = document.getElementById('campusModal');
    if (modal && window.bootstrap) {
        new bootstrap.Modal(modal).show();
    }
}

function closeCampusModal() {
    const modal = document.getElementById('campusModal');
    if (modal) {
        const instance = bootstrap.Modal.getInstance(modal);
        if (instance) instance.hide();
        document.getElementById('campusForm')?.reset();
        setVal('campusId', '');
    }
}

// ── Helpers ───────────────────────────────────────────────────
function val(id) { return document.getElementById(id)?.value?.trim() ?? ''; }
function setVal(id, v) { const el = document.getElementById(id); if (el) el.value = v ?? ''; }
function showError(id, msg) {
    const el = document.getElementById(id);
    if (!el) return;
    el.classList.add('is-invalid');
    const fb = el.nextElementSibling;
    if (fb?.classList.contains('invalid-feedback')) fb.textContent = msg;
}
function clearErrors() { document.querySelectorAll('.is-invalid').forEach(el => el.classList.remove('is-invalid')); }
function escHtml(s) {
    return (s || '').replace(/[&<>"']/g, c =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}
function showAlert(type, msg) {
    const c = document.getElementById('alertContainer');
    if (c) c.innerHTML = `<div class="alert alert-${type} alert-dismissible">
        ${msg}<button type="button" class="btn-close" data-bs-dismiss="alert"></button></div>`;
    setTimeout(() => { if (c) c.innerHTML = ''; }, 4000);
}
function setLoading(btn, loading, label) {
    if (!btn) return;
    btn.disabled = loading;
    btn.innerHTML = loading ? `<span class="spinner-border spinner-border-sm me-2"></span>${label}` : label;
}

document.addEventListener('DOMContentLoaded', () => loadOnboardingStatus?.());
