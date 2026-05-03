// ============================================================
// ACADEMIC-SETUP.JS - Onboarding Step 5
// ============================================================

document.addEventListener('DOMContentLoaded', async function () {

    await loadAcademicYears();
    loadOnboardingStatus?.();

    // ── Save Academic Year ────────────────────────────────────
    document.getElementById('academicYearForm')?.addEventListener('submit', async function (e) {
        e.preventDefault();
        clearErrors();

        const dto = {
            id: intVal('yearId'),
            name: val('yearName'),
            startDate: val('yearStartDate'),
            endDate: val('yearEndDate'),
            isCurrent: document.getElementById('isCurrent')?.checked ?? false
        };

        if (!dto.name) { showError('yearName', 'Year name is required'); return; }
        if (!dto.startDate) { showError('yearStartDate', 'Start date is required'); return; }
        if (!dto.endDate) { showError('yearEndDate', 'End date is required'); return; }
        if (dto.startDate >= dto.endDate) { showError('yearEndDate', 'End date must be after start date'); return; }

        const btn = document.getElementById('saveYearBtn');
        setLoading(btn, true, 'Saving...');
        try {
            const res = await fetch('/api/institution-onboarding/academic-year', {
                method: 'POST', credentials: 'include',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(dto)
            });
            const json = await res.json();
            if (json.success) {
                closeModal('academicYearModal');
                await loadAcademicYears();
                showAlert('success', 'Academic year saved');
            } else {
                showAlert('danger', json.message || 'Save failed');
            }
        } catch { showAlert('danger', 'Network error'); }
        finally { setLoading(btn, false, 'Save year'); }
    });

    // ── Save Academic Term ────────────────────────────────────
    document.getElementById('termForm')?.addEventListener('submit', async function (e) {
        e.preventDefault();
        clearErrors();

        const dto = {
            id: intVal('termId'),
            academicYearId: intVal('termYearId'),
            name: val('termName'),
            startDate: val('termStartDate'),
            endDate: val('termEndDate')
        };

        if (!dto.academicYearId) { showAlert('danger', 'Select an academic year first'); return; }
        if (!dto.name) { showError('termName', 'Term name is required'); return; }

        const btn = document.getElementById('saveTermBtn');
        setLoading(btn, true, 'Saving...');
        try {
            const res = await fetch('/api/institution-onboarding/academic-term', {
                method: 'POST', credentials: 'include',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(dto)
            });
            const json = await res.json();
            if (json.success) {
                closeModal('termModal');
                await loadAcademicYears();
                showAlert('success', 'Term saved');
            } else {
                showAlert('danger', json.message || 'Save failed');
            }
        } catch { showAlert('danger', 'Network error'); }
        finally { setLoading(btn, false, 'Save term'); }
    });

    // ── Continue ──────────────────────────────────────────────
    document.getElementById('continueBtn')?.addEventListener('click', async function () {
        setLoading(this, true, 'Continuing...');
        try {
            await fetch('/api/onboarding/complete-step', {
                method: 'POST', credentials: 'include',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ step: 5, skipped: false })
            });
            window.location.href = '/Account/BrandingSetup';
        } catch {
            showAlert('danger', 'Error. Please try again.');
            setLoading(this, false, 'Continue');
        }
    });
});

// ── Load academic years + terms ───────────────────────────────
async function loadAcademicYears() {
    try {
        const [yearsRes, termsRes] = await Promise.all([
            fetch('/api/institution-onboarding/academic-years', { credentials: 'include' }),
            fetch('/api/institution-onboarding/academic-terms', { credentials: 'include' })
        ]);
        const years = await yearsRes.json();
        const terms = await termsRes.json();

        const yearsData = Array.isArray(years) ? years : (years.data || []);
        const termsData = Array.isArray(terms) ? terms : (terms.data || []);

        renderYearList(yearsData, termsData);
        populateYearDropdown(yearsData);

        const continueBtn = document.getElementById('continueBtn');
        if (continueBtn) continueBtn.disabled = yearsData.length === 0;
    } catch { /* ignore */ }
}

function renderYearList(years, terms) {
    const el = document.getElementById('yearList');
    if (!el) return;

    if (!years.length) {
        el.innerHTML = `<div class="text-center py-4 text-muted">
            <i class="bi bi-calendar3" style="font-size:32px"></i>
            <div class="mt-2">No academic years yet. Add one to continue.</div></div>`;
        return;
    }

    el.innerHTML = years.map(y => {
        const yTerms = terms.filter(t => t.academicYearId === y.id);
        return `
            <div class="year-card p-3 border rounded mb-3">
                <div class="d-flex justify-content-between align-items-start">
                    <div>
                        <div class="fw-bold">${escHtml(y.name)}
                            ${y.isCurrent ? '<span class="badge bg-success ms-1">Current</span>' : ''}
                        </div>
                        <div class="small text-muted">${fmtDate(y.startDate)} → ${fmtDate(y.endDate)}</div>
                    </div>
                    <div class="d-flex gap-1">
                        <button class="btn btn-sm btn-outline-primary"
                                onclick="openAddTerm(${y.id}, '${escHtml(y.name)}')" title="Add term">
                            <i class="bi bi-plus"></i> Term
                        </button>
                        <button class="btn btn-sm btn-outline-secondary"
                                onclick="editYear(${y.id})" title="Edit">
                            <i class="bi bi-pencil"></i>
                        </button>
                        <button class="btn btn-sm btn-outline-danger"
                                onclick="deleteYear(${y.id})" title="Delete">
                            <i class="bi bi-trash"></i>
                        </button>
                    </div>
                </div>
                ${yTerms.length ? `
                    <div class="mt-2 ps-2">
                        ${yTerms.map(t => `
                            <div class="d-flex align-items-center justify-content-between py-1 border-start ps-2">
                                <span class="small">${escHtml(t.name)} (${fmtDate(t.startDate)} → ${fmtDate(t.endDate)})</span>
                                <button class="btn btn-sm text-danger p-0 ms-2"
                                        onclick="deleteTerm(${t.id})" title="Remove term">
                                    <i class="bi bi-x"></i>
                                </button>
                            </div>`).join('')}
                    </div>` : '<div class="small text-muted mt-1 ps-2">No terms — terms are optional</div>'}
            </div>`;
    }).join('');
}

function populateYearDropdown(years) {
    const select = document.getElementById('termYearId');
    if (!select) return;
    select.innerHTML = '<option value="">Select academic year</option>' +
        years.map(y => `<option value="${y.id}">${escHtml(y.name)}</option>`).join('');
}

async function editYear(id) {
    try {
        const res = await fetch(`/api/institution-onboarding/academic-year/${id}`, { credentials: 'include' });
        const json = await res.json();
        const d = json.data || json;
        setVal('yearId', id); setVal('yearName', d.name);
        setVal('yearStartDate', d.startDate?.substring(0, 10));
        setVal('yearEndDate', d.endDate?.substring(0, 10));
        const ic = document.getElementById('isCurrent');
        if (ic) ic.checked = d.isCurrent;
        openModal('academicYearModal');
    } catch { showAlert('danger', 'Could not load year.'); }
}

async function deleteYear(id) {
    if (!confirm('Delete this academic year? All related terms will also be removed.')) return;
    try {
        const res = await fetch(`/api/institution-onboarding/academic-year/${id}`,
            { method: 'DELETE', credentials: 'include' });
        const json = await res.json();
        if (json.success) { await loadAcademicYears(); showAlert('success', 'Deleted.'); }
        else showAlert('danger', json.message || 'Delete failed.');
    } catch { showAlert('danger', 'Network error.'); }
}

async function deleteTerm(id) {
    if (!confirm('Remove this term?')) return;
    try {
        const res = await fetch(`/api/institution-onboarding/academic-term/${id}`,
            { method: 'DELETE', credentials: 'include' });
        const json = await res.json();
        if (json.success) { await loadAcademicYears(); }
        else showAlert('danger', json.message || 'Delete failed.');
    } catch { showAlert('danger', 'Network error.'); }
}

function openAddTerm(yearId, yearName) {
    setVal('termId', '');
    setVal('termYearId', yearId);
    setVal('termName', ''); setVal('termStartDate', ''); setVal('termEndDate', '');
    const label = document.getElementById('termYearLabel');
    if (label) label.textContent = `for: ${yearName}`;
    openModal('termModal');
}

function openModal(id) {
    const el = document.getElementById(id);
    if (el && window.bootstrap) new bootstrap.Modal(el).show();
}
function closeModal(id) {
    const el = document.getElementById(id);
    if (el) { const m = bootstrap.Modal.getInstance(el); if (m) m.hide(); }
    document.getElementById(id.replace('Modal', 'Form') + 'Form')?.reset();
}

// ── Shared helpers ────────────────────────────────────────────
function val(id) { return document.getElementById(id)?.value?.trim() ?? ''; }
function intVal(id) { const v = parseInt(val(id)); return isNaN(v) ? null : v; }
function setVal(id, v) { const el = document.getElementById(id); if (el) el.value = v ?? ''; }
function fmtDate(d) { return d ? new Date(d).toLocaleDateString('en-GB') : ''; }
function escHtml(s) {
    return (s || '').replace(/[&<>"']/g, c =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}
function showError(id, msg) {
    const el = document.getElementById(id);
    if (!el) return;
    el.classList.add('is-invalid');
    const fb = el.nextElementSibling;
    if (fb?.classList.contains('invalid-feedback')) fb.textContent = msg;
}
function clearErrors() { document.querySelectorAll('.is-invalid').forEach(el => el.classList.remove('is-invalid')); }
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
