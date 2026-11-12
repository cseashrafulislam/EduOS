(() => {
    'use strict';
    const raw = new URLSearchParams(location.search).get('smartSetup');
    if (!raw || !/^[A-Z]{2,20}$/i.test(raw)) return;
    const module = raw.toUpperCase();
    // Only the canonical destination for a reviewed module may receive a handoff.
    // Query parameters are navigation hints, never data, tenant or authorization.
    const destinations = {
        ACADEMIC: ['/academicmanagement/'],
        STUDENT: ['/students/'],
        ADMISSION: ['/admissions/'],
        ATTENDANCE: ['/academicroutines/'],
        ASSESSMENT: ['/assessmentadministration/'],
        FEES: ['/feeoperations/'],
        FINANCE: ['/accountingsetup/'],
        HR: ['/hrsetup/'],
        PAYROLL: ['/payrollsalarycomponents/'],
        INVENTORY: ['/assetlifecycle/', '/inventorycatalog/'],
        LIBRARY: ['/library/'],
        HOSTEL: ['/hosteloperations/'],
        TRANSPORT: ['/transportoperations/'],
        LMS: ['/learning/'],
        COMMUNICATION: ['/communicationadministration/', '/communicationworkspace/'],
        DOCUMENTS: ['/documentadministration/'],
        FACILITIES: ['/facilityspaces/'],
        REPORTS: ['/centralworkcenter/']
    };
    const path = location.pathname.toLowerCase().replace(/\/$/, '') + '/';
    if (!destinations[module]?.some(prefix => path.startsWith(prefix))) return;
    const body = document.body, main = document.getElementById('main-content');
    const token = document.querySelector('meta[name="request-verification-token"]')?.content;
    const tenant = body?.dataset.authTenantId, session = body?.dataset.authSessionReference;
    if (!main || !token || !tenant || !session) return;
    const currentSession = () => tenant === body.dataset.authTenantId &&
        session === body.dataset.authSessionReference &&
        window.EduOSSessionTransitionPending?.() !== true;
    const bn = document.documentElement.lang.toLowerCase().startsWith('bn');
    const t = (en, bd) => bn ? bd : en;
    const elem = (tag, cls, value) => {
        const x = document.createElement(tag);
        if (cls) x.className = cls;
        if (value !== undefined) x.textContent = value;
        return x;
    };
    const adapters = {
        HR: {
            OrganizationUnit: ['hrSetupDepartmentForm', 'hrSetupDepartmentId', 'hrSetupDepartmentCode', 'hrSetupDepartmentName'],
            Designation: ['hrSetupDesignationForm', 'hrSetupDesignationId', 'hrSetupDesignationCode', 'hrSetupDesignationName']
        },
        INVENTORY: { AssetCategory: ['assetCategoryForm', 'assetCategoryId', 'assetCategoryCode', 'assetCategoryName'] },
        LIBRARY: { BookCategory: ['libraryCategoryForm', 'categoryId', 'categoryCode', 'categoryName'] },
        COMMUNICATION: { NoticeCategory: ['categoryForm', 'categoryId', 'categoryCode', 'categoryName'] },
        DOCUMENTS: { DocumentType: ['typeForm', 'typeId', 'typeCode', 'typeName'] }
    };
    const formFields = entry => {
        const ids = adapters[module]?.[entry.kind];
        if (!ids) return null;
        const nodes = ids.map(id => document.getElementById(id));
        return nodes.every(Boolean) ? nodes : null;
    };
    const panel = elem('section', 'card border-primary mb-3', undefined);
    panel.setAttribute('aria-label', t('Smart setup suggestions', 'স্মার্ট সেটআপ প্রস্তাব'));
    const content = elem('div', 'card-body');
    const heading = elem('h2', 'h5 mb-2', t('Smart setup — reviewed suggestions', 'স্মার্ট সেটআপ — যাচাইকৃত প্রস্তাব'));
    const description = elem('p', 'small text-muted mb-2',
        t('Live institution-specific defaults; nothing is saved until you confirm or use the original form Save action.',
          'প্রতিষ্ঠানের ধরন অনুযায়ী তথ্য। নিশ্চিত না করলে বা মূল ফর্মে Save না করলে কিছু সংরক্ষণ হবে না।'));
    const notice = elem('div', 'small mb-2');
    notice.setAttribute('role', 'status');
    notice.setAttribute('aria-live', 'polite');
    const output = elem('div', '');
    content.append(heading, description, notice, output);
    panel.append(content);
    main.prepend(panel);
    const inform = (text, error = false) => {
        notice.className = 'alert ' + (error ? 'alert-danger' : 'alert-info') + ' py-2';
        notice.textContent = text;
    };
    const get = async url => {
        const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store',
            headers: { Accept: 'application/json' } });
        const result = await response.json().catch(() => null);
        if (!response.ok || result?.success !== true)
            throw new Error(result?.message || t('Setup data could not be loaded.', 'সেটআপ তথ্য লোড হয়নি।'));
        return result.data;
    };
    const uuid = () => {
        if (crypto.randomUUID) return crypto.randomUUID();
        const bytes = new Uint8Array(16);
        crypto.getRandomValues(bytes);
        bytes[6] = (bytes[6] & 15) | 64; bytes[8] = (bytes[8] & 63) | 128;
        const h = Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
        return [h.slice(0, 8), h.slice(8, 12), h.slice(12, 16), h.slice(16, 20), h.slice(20)].join('-');
    };
    const fill = (entry, automatic = false) => {
        const fields = formFields(entry);
        if (!fields || entry.status !== 'Missing') return;
        const [form, id, code, name] = fields;
        if (!id.value && code.value === entry.code && name.value === entry.name) {
            if (!automatic) {
                inform(t('This suggestion is already in the form. Review it and Save.',
                    'এই প্রস্তাব আগেই ফর্মে বসানো আছে। যাচাই করে Save করুন।'));
                form.scrollIntoView({ behavior: 'smooth', block: 'center' });
            }
            return;
        }
        if (id.value || code.value.trim() || name.value.trim()) {
            inform(t('Clear the current form before filling another suggestion. No changes were made.',
                'অন্য প্রস্তাব বসানোর আগে বর্তমান ফর্ম Clear করুন। কোনো তথ্য বদলানো হয়নি।'), true);
            return;
        }
        if (module === 'DOCUMENTS')
            document.querySelector('[data-doc-tab="types"]')?.click();
        code.value = entry.code;
        name.value = entry.name;
        for (const input of [code, name])
            input.dispatchEvent(new Event('input', { bubbles: true }));
        if (!automatic) {
            inform(t('Form filled. Review and click its normal Save button to create this record.',
                'ফর্মে তথ্য বসানো হয়েছে। যাচাই করে মূল Save বাটনে সংরক্ষণ করুন।'));
            form.scrollIntoView({ behavior: 'smooth', block: 'center' });
            code.focus({ preventScroll: true });
        }
    };
    let pendingRequestId = null;
    async function show() {
        if (!currentSession()) return;
        try {
            const [preview, readinessList] = await Promise.all([
                get('/api/smart-setup/preview'),
                get('/api/smart-setup/readiness?module=' + encodeURIComponent(module))
            ]);
            if (!currentSession()) return;
            const setup = preview.modules?.find(item => item.code === module);
            const readiness = readinessList.find(item => item.moduleCode === module);
            if (!setup || !setup.isAvailable || !readiness?.isAvailable) {
                inform(t('This module is not available to this institution.',
                    'এই প্রতিষ্ঠানের জন্য মডিউলটি উপলব্ধ নয়।'), true);
                return;
            }
            output.replaceChildren();
            output.append(elem('p', 'small fw-semibold mb-2',
                (bn ? setup.nameBangla : setup.name) + ' · ' + preview.institutionTypeName));
            const entries = setup.entries || [];
            const missing = entries.filter(x => x.status === 'Missing');
            const conflict = entries.some(x => x.status === 'Conflict');
            if (entries.length) {
                const list = elem('ul', 'list-unstyled mb-3');
                for (const entry of entries) {
                    const row = elem('li', 'd-flex flex-wrap align-items-center gap-2 border-bottom py-2');
                    row.append(elem('span', 'small flex-grow-1', entry.name + ' (' + entry.code + ')'));
                    const state = entry.status === 'Missing' ? t('Missing', 'যোগ করা বাকি') :
                        entry.status === 'Existing' ? t('Already exists', 'আগেই আছে') :
                        t('Review conflict', 'দ্বন্দ্ব যাচাই করুন');
                    row.append(elem('span', 'small text-muted', state));
                    if (entry.status === 'Missing' && formFields(entry)) {
                        const button = elem('button', 'btn btn-sm btn-outline-primary',
                            t('Fill form', 'ফর্মে বসান'));
                        button.type = 'button';
                        button.addEventListener('click', () => { if (currentSession()) fill(entry); });
                        row.append(button);
                    }
                    list.append(row);
                }
                output.append(list);
            }
            // An explicit Smart Setup deep-link may prefill one missing item per
            // empty create form. Further candidates remain selectable above.
            // Never overwrite a current draft/edit, and never invoke Save.
            const prefilled = new Set();
            for (const entry of missing) {
                const fields = formFields(entry);
                if (!fields || prefilled.has(fields[0].id)) continue;
                const [form, id, code, name] = fields;
                if (id.value || code.value.trim() || name.value.trim()) continue;
                fill(entry, true);
                prefilled.add(form.id);
            }
            if (prefilled.size)
                output.append(elem('p', 'small text-info',
                    t('One missing suggestion was placed in each empty matching form. Review before saving.',
                        'ফাঁকা সংশ্লিষ্ট ফর্মে একটি করে প্রস্তাব বসানো হয়েছে। সংরক্ষণের আগে যাচাই করুন।')));
            if (readiness.requirements?.length) {
                const list = elem('ul', 'small mb-3');
                for (const need of readiness.requirements.filter(x => !x.configured)) {
                    list.append(elem('li', '', bn ? need.labelBangla : need.label));
                }
                if (list.children.length) {
                    output.append(elem('p', 'fw-semibold small mb-1',
                        t('Still requires institution confirmation:', 'প্রতিষ্ঠানের অনুমোদিত তথ্য প্রয়োজন:')));
                    output.append(list);
                }
            }
            if (setup.supportsSafeApply && missing.length && !conflict && preview.institutionTypeId > 0) {
                const form = elem('div', 'd-flex flex-wrap align-items-center gap-2');
                const label = elem('label', 'small d-flex align-items-center gap-2');
                const check = elem('input', 'form-check-input mt-0');
                check.type = 'checkbox';
                label.append(check, elem('span', '',
                    t('I reviewed these missing safe defaults.', 'অনুপস্থিত নিরাপদ তথ্যগুলো যাচাই করেছি।')));
                const save = elem('button', 'btn btn-sm btn-primary',
                    t('Add ' + missing.length + ' missing record(s)', missing.length + 'টি অনুপস্থিত তথ্য যোগ করুন'));
                save.type = 'button';
                save.disabled = true;
                check.addEventListener('change', () => { save.disabled = !check.checked; });
                save.addEventListener('click', async () => {
                    if (!currentSession() || !check.checked || save.disabled) return;
                    pendingRequestId ||= uuid();
                    save.disabled = true; check.disabled = true;
                    try {
                        const response = await fetch('/api/smart-setup/apply', {
                            method: 'POST', credentials: 'same-origin', cache: 'no-store',
                            headers: { Accept: 'application/json', 'Content-Type': 'application/json',
                                RequestVerificationToken: token },
                            body: JSON.stringify({
                                clientRequestId: pendingRequestId,
                                institutionTypeId: preview.institutionTypeId,
                                templateVersion: preview.templateVersion,
                                modules: [module], confirmed: true
                            })
                        });
                        const result = await response.json().catch(() => null);
                        if (!response.ok || result?.success !== true)
                            throw new Error(result?.message || t('Setup was not saved.', 'সেটআপ সংরক্ষিত হয়নি।'));
                        if (!currentSession()) return;
                        pendingRequestId = null;
                        // The original module page reloads its authoritative lists from SQL.
                        location.reload();
                    } catch (error) {
                        if (currentSession()) inform(error.message, true);
                        check.disabled = false; save.disabled = !check.checked;
                    }
                });
                form.append(label, save);
                output.append(form);
            } else if (!missing.length && entries.length) {
                output.append(elem('p', 'small text-success',
                    t('Reviewed defaults already exist; no duplicate records required.',
                        'প্রস্তাবিত তথ্য আগে থেকেই আছে; ডুপ্লিকেট যোগ হবে না।')));
            } else if (!entries.length) {
                output.append(elem('p', 'small text-muted',
                    t('No generic records can safely be invented. Enter verified institution-specific data in this page.',
                        'এখানে অনুমানভিত্তিক তথ্য তৈরি করা যাবে না। প্রতিষ্ঠানের যাচাইকৃত তথ্য এই পেজে দিন।')));
            }
            const back = elem('a', 'btn btn-sm btn-outline-secondary mt-2',
                t('Back to Smart Setup', 'স্মার্ট সেটআপে ফিরুন'));
            back.href = '/SmartSetup/Index?module=' + encodeURIComponent(module);
            output.append(back);
        } catch (error) {
            if (currentSession()) inform(error.message, true);
        }
    }
    show();
})();
