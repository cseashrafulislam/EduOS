// ============================================================
// ACADEMIC-SETUP.JS
// Academic Setup - Full Fetch CRUD
// ============================================================

let academicYears = [];
let academicTerms = [];

document.addEventListener('DOMContentLoaded', function () {
    initializeAcademicYearForm();
    initializeTermForm();
    initializeYearListEvents();
    initializeAcademicYearModal();
    initializeTermModal();
    initializeContinueButton();

    loadAcademicYears();

    if (typeof loadOnboardingStatus === 'function') {
        loadOnboardingStatus();
    }
});

// ============================================================
// INITIALIZATION
// ============================================================

function initializeAcademicYearForm() {
    const form = document.getElementById('academicYearForm');

    if (!form) {
        return;
    }

    form.addEventListener('submit', async function (e) {
        e.preventDefault();
        await saveAcademicYear();
    });
}

function initializeTermForm() {
    const form = document.getElementById('termForm');

    if (!form) {
        return;
    }

    form.addEventListener('submit', async function (e) {
        e.preventDefault();
        await saveAcademicTerm();
    });
}

function initializeYearListEvents() {
    const yearList = document.getElementById('yearList');

    if (!yearList) {
        return;
    }

    yearList.addEventListener('click', async function (e) {
        const button = e.target.closest('[data-action]');

        if (!button) {
            return;
        }

        const action = button.dataset.action;
        const id = parseInt(button.dataset.id);

        if (!id) {
            return;
        }

        switch (action) {
            case 'add-term':
                openAddTerm(id);
                break;

            case 'edit-year':
                await editAcademicYear(id);
                break;

            case 'delete-year':
                await deleteAcademicYear(id);
                break;

            case 'edit-term':
                await editAcademicTerm(id);
                break;

            case 'delete-term':
                await deleteAcademicTerm(id);
                break;
        }
    });
}

function initializeAcademicYearModal() {
    const modalElement = document.getElementById('academicYearModal');

    if (!modalElement) {
        return;
    }

    document.querySelectorAll('[data-bs-target="#academicYearModal"]').forEach(function (button) {
        button.addEventListener('click', function () {
            resetAcademicYearForm();
        });
    });

    modalElement.addEventListener('hidden.bs.modal', function () {
        resetAcademicYearForm();
    });
}

function initializeTermModal() {
    const modalElement = document.getElementById('termModal');

    if (!modalElement) {
        return;
    }

    modalElement.addEventListener('hidden.bs.modal', function () {
        resetTermForm();
    });
}

function initializeContinueButton() {
    const button = document.getElementById('continueBtn');

    if (!button) {
        return;
    }

    button.addEventListener('click', async function () {
        await continueOnboarding();
    });
}

// ============================================================
// LOAD ACADEMIC YEARS + TERMS
// ============================================================

async function loadAcademicYears() {
    const container = document.getElementById('yearList');

    if (container) {
        container.innerHTML = `
            <div class="text-center py-4 text-muted">
                <div class="spinner-border spinner-border-sm me-2"></div>
                Loading academic years...
            </div>
        `;
    }

    try {
        const [yearResponse, termResponse] = await Promise.all([
            apiRequest('/api/institution-onboarding/academic-years'),
            apiRequest('/api/institution-onboarding/academic-terms')
        ]);

        academicYears = extractList(yearResponse);
        academicTerms = extractList(termResponse);

        renderAcademicYears();
        populateAcademicYearDropdown();

        const continueButton = document.getElementById('continueBtn');

        if (continueButton) {
            continueButton.disabled = academicYears.length === 0;
        }
    } catch (error) {
        console.error('Load academic setup error:', error);

        if (container) {
            container.innerHTML = `
                <div class="alert alert-danger mb-0">
                    ${escapeHtml(error.message || 'Unable to load academic setup.')}
                </div>
            `;
        }
    }
}

// ============================================================
// RENDER
// ============================================================

function renderAcademicYears() {
    const container = document.getElementById('yearList');

    if (!container) {
        return;
    }

    if (academicYears.length === 0) {
        container.innerHTML = `
            <div class="text-center py-4 text-muted">
                <i class="bi bi-calendar3" style="font-size:32px;"></i>
                <div class="mt-2">
                    No academic years yet. Add one to continue.
                </div>
            </div>
        `;
        return;
    }

    container.innerHTML = academicYears.map(function (year) {
        const terms = academicTerms.filter(function (term) {
            return Number(term.academicYearId) === Number(year.id);
        });

        return `
            <div class="year-card p-3 border rounded-3 mb-3">

                <div class="d-flex flex-wrap justify-content-between align-items-start gap-3">

                    <div>
                        <div class="fw-bold">
                            ${escapeHtml(year.name)}

                            ${year.isCurrent
                ? '<span class="badge bg-success ms-1">Current</span>'
                : ''}
                        </div>

                        <div class="small text-muted mt-1">
                            ${formatDate(year.startDate)}
                            →
                            ${formatDate(year.endDate)}
                        </div>
                    </div>

                    <div class="d-flex flex-wrap gap-1">

                        <button type="button"
                                class="btn btn-sm btn-outline-primary"
                                data-action="add-term"
                                data-id="${year.id}">
                            <i class="bi bi-plus-circle me-1"></i>
                            Term
                        </button>

                        <button type="button"
                                class="btn btn-sm btn-outline-secondary"
                                data-action="edit-year"
                                data-id="${year.id}"
                                title="Edit academic year">
                            <i class="bi bi-pencil"></i>
                        </button>

                        <button type="button"
                                class="btn btn-sm btn-outline-danger"
                                data-action="delete-year"
                                data-id="${year.id}"
                                title="Delete academic year">
                            <i class="bi bi-trash"></i>
                        </button>

                    </div>
                </div>

                ${renderAcademicTerms(terms)}

            </div>
        `;
    }).join('');
}

function renderAcademicTerms(terms) {
    if (terms.length === 0) {
        return `
            <div class="small text-muted mt-2 ps-2">
                No terms — terms are optional.
            </div>
        `;
    }

    return `
        <div class="mt-3">
            ${terms.map(function (term) {
        return `
                    <div class="d-flex justify-content-between align-items-center
                                border-start border-3 ps-3 py-2 mb-1">

                        <div>
                            <div class="small fw-semibold">
                                ${escapeHtml(term.name)}
                            </div>

                            <div class="small text-muted">
                                ${formatOptionalDateRange(term.startDate, term.endDate)}
                            </div>
                        </div>

                        <div class="d-flex gap-1">

                            <button type="button"
                                    class="btn btn-sm btn-outline-secondary"
                                    data-action="edit-term"
                                    data-id="${term.id}"
                                    title="Edit term">
                                <i class="bi bi-pencil"></i>
                            </button>

                            <button type="button"
                                    class="btn btn-sm btn-outline-danger"
                                    data-action="delete-term"
                                    data-id="${term.id}"
                                    title="Delete term">
                                <i class="bi bi-trash"></i>
                            </button>

                        </div>
                    </div>
                `;
    }).join('')}
        </div>
    `;
}

// ============================================================
// ACADEMIC YEAR - CREATE / UPDATE
// ============================================================

async function saveAcademicYear() {
    clearErrors();

    const dto = {
        id: getIntValue('yearId'),
        name: getValue('yearName'),
        startDate: getValue('yearStartDate'),
        endDate: getValue('yearEndDate'),
        isCurrent: document.getElementById('isCurrent')?.checked ?? false
    };

    if (!validateAcademicYear(dto)) {
        return;
    }

    const button = document.getElementById('saveYearBtn');

    setLoading(button, true, 'Saving...');

    try {
        const response = await apiRequest(
            '/api/institution-onboarding/academic-year',
            {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(dto)
            }
        );

        if (response?.success === false) {
            throw new Error(response.message || 'Academic year save failed.');
        }

        closeModal('academicYearModal');

        await loadAcademicYears();

        showAlert(
            'success',
            dto.id
                ? 'Academic year updated successfully.'
                : 'Academic year created successfully.'
        );
    } catch (error) {
        console.error('Academic year save error:', error);
        showAlert('danger', error.message || 'Academic year save failed.');
    } finally {
        setLoading(button, false, 'Save year');
    }
}

// ============================================================
// ACADEMIC YEAR - EDIT
// ============================================================

async function editAcademicYear(id) {
    if (!id) {
        return;
    }

    clearErrors();

    try {
        const response = await apiRequest(
            `/api/institution-onboarding/academic-year/${id}`
        );

        const year = extractItem(response);

        if (!year || !year.id) {
            throw new Error('Academic year not found.');
        }

        setValue('yearId', year.id);
        setValue('yearName', year.name);
        setValue('yearStartDate', toDateInputValue(year.startDate));
        setValue('yearEndDate', toDateInputValue(year.endDate));

        const isCurrent = document.getElementById('isCurrent');

        if (isCurrent) {
            isCurrent.checked = Boolean(year.isCurrent);
        }

        const title = document.getElementById('academicYearModalLabel');

        if (title) {
            title.textContent = 'Edit Academic Year';
        }

        const saveButton = document.getElementById('saveYearBtn');

        if (saveButton) {
            saveButton.textContent = 'Update year';
        }

        openModal('academicYearModal');
    } catch (error) {
        console.error('Academic year edit error:', error);
        showAlert('danger', error.message || 'Could not load academic year.');
    }
}

// ============================================================
// ACADEMIC YEAR - DELETE
// ============================================================

async function deleteAcademicYear(id) {
    if (!id) {
        return;
    }

    const confirmed = confirm(
        'Delete this academic year? Related academic terms may also be removed.'
    );

    if (!confirmed) {
        return;
    }

    try {
        const response = await apiRequest(
            `/api/institution-onboarding/academic-year/${id}`,
            {
                method: 'DELETE'
            }
        );

        if (response?.success === false) {
            throw new Error(response.message || 'Delete failed.');
        }

        await loadAcademicYears();

        showAlert('success', 'Academic year deleted successfully.');
    } catch (error) {
        console.error('Academic year delete error:', error);
        showAlert('danger', error.message || 'Academic year delete failed.');
    }
}

// ============================================================
// TERM - OPEN NEW
// ============================================================

function openAddTerm(academicYearId) {
    resetTermForm();

    const year = academicYears.find(function (item) {
        return Number(item.id) === Number(academicYearId);
    });

    setValue('termYearId', academicYearId);

    const label = document.getElementById('termYearLabel');

    if (label) {
        label.textContent = year
            ? `Academic Year: ${year.name}`
            : '';
    }

    const title = document.getElementById('termModalLabel');

    if (title) {
        title.textContent = 'Add Academic Term';
    }

    const button = document.getElementById('saveTermBtn');

    if (button) {
        button.textContent = 'Save term';
    }

    openModal('termModal');
}

// ============================================================
// TERM - CREATE / UPDATE
// ============================================================

async function saveAcademicTerm() {
    clearErrors();

    const dto = {
        id: getIntValue('termId'),
        academicYearId: getIntValue('termYearId'),
        name: getValue('termName'),
        startDate: nullableDateValue('termStartDate'),
        endDate: nullableDateValue('termEndDate')
    };

    if (!validateAcademicTerm(dto)) {
        return;
    }

    const button = document.getElementById('saveTermBtn');

    setLoading(button, true, 'Saving...');

    try {
        const response = await apiRequest(
            '/api/institution-onboarding/academic-term',
            {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(dto)
            }
        );

        if (response?.success === false) {
            throw new Error(response.message || 'Academic term save failed.');
        }

        closeModal('termModal');

        await loadAcademicYears();

        showAlert(
            'success',
            dto.id
                ? 'Academic term updated successfully.'
                : 'Academic term created successfully.'
        );
    } catch (error) {
        console.error('Academic term save error:', error);
        showAlert('danger', error.message || 'Academic term save failed.');
    } finally {
        setLoading(button, false, 'Save term');
    }
}

// ============================================================
// TERM - EDIT
// ============================================================

async function editAcademicTerm(id) {
    if (!id) {
        return;
    }

    clearErrors();

    try {
        const response = await apiRequest(
            `/api/institution-onboarding/academic-term/${id}`
        );

        const term = extractItem(response);

        if (!term || !term.id) {
            throw new Error('Academic term not found.');
        }

        setValue('termId', term.id);
        setValue('termYearId', term.academicYearId);
        setValue('termName', term.name);
        setValue('termStartDate', toDateInputValue(term.startDate));
        setValue('termEndDate', toDateInputValue(term.endDate));

        const year = academicYears.find(function (item) {
            return Number(item.id) === Number(term.academicYearId);
        });

        const label = document.getElementById('termYearLabel');

        if (label) {
            label.textContent = year
                ? `Academic Year: ${year.name}`
                : '';
        }

        const title = document.getElementById('termModalLabel');

        if (title) {
            title.textContent = 'Edit Academic Term';
        }

        const button = document.getElementById('saveTermBtn');

        if (button) {
            button.textContent = 'Update term';
        }

        openModal('termModal');
    } catch (error) {
        console.error('Academic term edit error:', error);
        showAlert('danger', error.message || 'Could not load academic term.');
    }
}

// ============================================================
// TERM - DELETE
// ============================================================

async function deleteAcademicTerm(id) {
    if (!id) {
        return;
    }

    const confirmed = confirm('Delete this academic term?');

    if (!confirmed) {
        return;
    }

    try {
        const response = await apiRequest(
            `/api/institution-onboarding/academic-term/${id}`,
            {
                method: 'DELETE'
            }
        );

        if (response?.success === false) {
            throw new Error(response.message || 'Delete failed.');
        }

        await loadAcademicYears();

        showAlert('success', 'Academic term deleted successfully.');
    } catch (error) {
        console.error('Academic term delete error:', error);
        showAlert('danger', error.message || 'Academic term delete failed.');
    }
}

// ============================================================
// YEAR DROPDOWN
// ============================================================

function populateAcademicYearDropdown() {
    const select = document.getElementById('termYearId');

    if (!select) {
        return;
    }

    select.innerHTML = `
        <option value="">Select academic year</option>
        ${academicYears.map(function (year) {
        return `
                <option value="${year.id}">
                    ${escapeHtml(year.name)}
                </option>
            `;
    }).join('')}
    `;
}

// ============================================================
// VALIDATION
// ============================================================

function validateAcademicYear(dto) {
    if (!dto.name) {
        showError('yearName', 'Year name is required.');
        return false;
    }

    if (!dto.startDate) {
        showError('yearStartDate', 'Start date is required.');
        return false;
    }

    if (!dto.endDate) {
        showError('yearEndDate', 'End date is required.');
        return false;
    }

    if (dto.startDate >= dto.endDate) {
        showError(
            'yearEndDate',
            'End date must be after start date.'
        );

        return false;
    }

    return true;
}

function validateAcademicTerm(dto) {
    if (!dto.academicYearId) {
        showError('termYearId', 'Academic year is required.');
        return false;
    }

    if (!dto.name) {
        showError('termName', 'Term name is required.');
        return false;
    }

    if (dto.startDate && dto.endDate && dto.startDate >= dto.endDate) {
        showError(
            'termEndDate',
            'End date must be after start date.'
        );

        return false;
    }

    return true;
}

// ============================================================
// CONTINUE
// ============================================================

async function continueOnboarding() {
    const button = document.getElementById('continueBtn');

    if (academicYears.length === 0) {
        showAlert(
            'danger',
            'Please add at least one academic year before continuing.'
        );

        return;
    }

    setLoading(button, true, 'Continuing...');

    try {
        await apiRequest('/api/onboarding/complete-step', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json'
            },
            body: JSON.stringify({
                step: 5,
                skipped: false
            })
        });

        window.location.href = '/Account/BrandingSetup';
    } catch (error) {
        console.error('Continue onboarding error:', error);

        showAlert(
            'danger',
            error.message || 'Unable to continue.'
        );

        setLoading(button, false, 'Continue');
    }
}

// ============================================================
// MODAL HELPERS
// ============================================================

function openModal(id) {
    const element = document.getElementById(id);

    if (!element || !window.bootstrap) {
        return;
    }

    const modal = bootstrap.Modal.getOrCreateInstance(element);
    modal.show();
}

function closeModal(id) {
    const element = document.getElementById(id);

    if (!element || !window.bootstrap) {
        return;
    }

    const modal = bootstrap.Modal.getInstance(element);

    if (modal) {
        modal.hide();
    }
}

function resetAcademicYearForm() {
    const form = document.getElementById('academicYearForm');

    if (form) {
        form.reset();
    }

    setValue('yearId', '');

    clearErrors();

    const title = document.getElementById('academicYearModalLabel');

    if (title) {
        title.textContent = 'Academic Year';
    }

    const button = document.getElementById('saveYearBtn');

    if (button) {
        button.textContent = 'Save year';
    }
}

function resetTermForm() {
    const form = document.getElementById('termForm');

    if (form) {
        form.reset();
    }

    setValue('termId', '');

    clearErrors();

    const title = document.getElementById('termModalLabel');

    if (title) {
        title.textContent = 'Academic Term';
    }

    const label = document.getElementById('termYearLabel');

    if (label) {
        label.textContent = '';
    }

    const button = document.getElementById('saveTermBtn');

    if (button) {
        button.textContent = 'Save term';
    }
}

// ============================================================
// FETCH API HELPER
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

    const contentType = response.headers.get('content-type') || '';

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
        throw new Error('Your session has expired.');
    }

    if (response.status === 403) {
        throw new Error(
            result?.message || 'You do not have permission to perform this action.'
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
// RESPONSE HELPERS
// ============================================================

function extractList(response) {
    if (Array.isArray(response)) {
        return response;
    }

    if (Array.isArray(response?.data)) {
        return response.data;
    }

    return [];
}

function extractItem(response) {
    if (!response) {
        return null;
    }

    return response.data || response;
}

// ============================================================
// INPUT HELPERS
// ============================================================

function getValue(id) {
    return document.getElementById(id)?.value?.trim() ?? '';
}

function getIntValue(id) {
    const value = parseInt(getValue(id));

    return Number.isNaN(value)
        ? null
        : value;
}

function nullableDateValue(id) {
    const value = getValue(id);

    return value || null;
}

function setValue(id, value) {
    const element = document.getElementById(id);

    if (element) {
        element.value = value ?? '';
    }
}

// ============================================================
// DATE HELPERS
// ============================================================

function toDateInputValue(value) {
    if (!value) {
        return '';
    }

    return String(value).substring(0, 10);
}

function formatDate(value) {
    if (!value) {
        return '--';
    }

    const date = new Date(value);

    if (Number.isNaN(date.getTime())) {
        return value;
    }

    return date.toLocaleDateString('en-GB');
}

function formatOptionalDateRange(startDate, endDate) {
    if (!startDate && !endDate) {
        return 'No date specified';
    }

    if (startDate && !endDate) {
        return `From ${formatDate(startDate)}`;
    }

    if (!startDate && endDate) {
        return `Until ${formatDate(endDate)}`;
    }

    return `${formatDate(startDate)} → ${formatDate(endDate)}`;
}

// ============================================================
// VALIDATION UI
// ============================================================

function showError(id, message) {
    const element = document.getElementById(id);

    if (!element) {
        return;
    }

    element.classList.add('is-invalid');

    const feedback = element.parentElement?.querySelector('.invalid-feedback');

    if (feedback) {
        feedback.textContent = message;
    }
}

function clearErrors() {
    document
        .querySelectorAll('.is-invalid')
        .forEach(function (element) {
            element.classList.remove('is-invalid');
        });

    document
        .querySelectorAll('.invalid-feedback')
        .forEach(function (element) {
            element.textContent = '';
        });
}

// ============================================================
// ALERT
// ============================================================

function showAlert(type, message) {
    const container = document.getElementById('alertContainer');

    if (!container) {
        return;
    }

    container.innerHTML = `
        <div class="alert alert-${type} alert-dismissible fade show" role="alert">
            ${escapeHtml(message)}

            <button type="button"
                    class="btn-close"
                    data-bs-dismiss="alert"
                    aria-label="Close">
            </button>
        </div>
    `;

    window.setTimeout(function () {
        container.innerHTML = '';
    }, 4000);
}

// ============================================================
// BUTTON LOADING
// ============================================================

function setLoading(button, loading, text) {
    if (!button) {
        return;
    }

    button.disabled = loading;

    if (loading) {
        button.innerHTML = `
            <span class="spinner-border spinner-border-sm me-2"
                  role="status"
                  aria-hidden="true">
            </span>
            ${escapeHtml(text)}
        `;

        return;
    }

    button.textContent = text;
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