(() => {
    'use strict';
    // Progressive enhancement only: existing Razor forms/APIs continue to work without this file.
    const root = document.querySelector('#academicManagement, #academicSectionsPage, #prerequisiteWorkspace');
    if (!root || root.dataset.academicWorkspaceReady === '1') return;
    root.dataset.academicWorkspaceReady = '1';
    root.classList.add('edu-academic-workspace');
    const bn = document.documentElement.lang.toLowerCase().startsWith('bn');
    const feature = root.dataset.activeFeature ||
        (root.id === 'academicSectionsPage' ? 'independentSections' : 'prerequisites');
    const config = {
        years: {
            en: 'Configure each academic year once. Search and update existing years below.',
            bn: 'শিক্ষাবর্ষ একবার সেট করুন। নিচে আগের শিক্ষাবর্ষ খুঁজুন ও প্রয়োজনমতো সংশোধন করুন.'
        },
        terms: {
            en: 'Keep terms linked to their academic year. Review the register before adding another.',
            bn: 'টার্মকে সঠিক শিক্ষাবর্ষের অধীনে রাখুন। নতুন টার্মের আগে তালিকা দেখুন।'
        },
        departments: {
            en: 'Maintain a single department directory for all programmes.',
            bn: 'সব প্রোগ্রামের জন্য একটি বিভাগ তালিকা পরিচালনা করুন।'
        },
        classes: {
            en: 'Manage programmes, classes and optional streams without duplicate setup.',
            bn: 'ডুপ্লিকেট এন্ট্রি ছাড়াই প্রোগ্রাম, শ্রেণি ও ঐচ্ছিক শাখা পরিচালনা করুন।'
        },
        sections: {
            en: 'Batches are optional intakes; classroom sections are maintained separately.',
            bn: 'ব্যাচ হলো ঐচ্ছিক ভর্তি গ্রুপ; ক্লাসের শাখা আলাদাভাবে পরিচালিত হয়।'
        },
        rooms: {
            en: 'Find campus rooms, verify capacity, and add new rooms only when needed.',
            bn: 'ক্যাম্পাসের কক্ষ ও ধারণক্ষমতা দেখুন; প্রয়োজন হলে নতুন কক্ষ যোগ করুন।'
        },
        subjects: {
            en: 'Keep a reusable subject catalog; curriculum versions use these subjects.',
            bn: 'বিষয়ের একটি পুনর্ব্যবহারযোগ্য তালিকা রাখুন; পাঠ্যক্রমে এগুলো ব্যবহার হবে।'
        },
        curricula: {
            en: 'Version curriculum changes and map subjects without rewriting previous history.',
            bn: 'পূর্বের ইতিহাস অক্ষত রেখে পাঠ্যক্রমের সংস্করণ ও বিষয় ম্যাপিং করুন।'
        },
        offerings: {
            en: 'Review section and seat availability before creating or editing offerings.',
            bn: 'বিষয় অফার তৈরি বা সংশোধনের আগে শাখা ও আসনসংখ্যা যাচাই করুন।'
        },
        independentSections: {
            en: 'Academic sections belong to a campus and class, independently of optional batches.',
            bn: 'শাখা ক্যাম্পাস ও শ্রেণির অধীনে থাকবে; ঐচ্ছিক ব্যাচের ওপর নির্ভর করবে না।'
        },
        prerequisites: {
            en: 'Set prerequisite subjects for a curriculum; avoid circular dependencies.',
            bn: 'পাঠ্যক্রমের বিষয়ের পূর্বশর্ত ঠিক করুন; চক্রাকার নির্ভরতা এড়িয়ে চলুন।'
        }
    };
    const headings = {
        departmentForm: ['Add department', 'নতুন বিভাগ'],
        yearForm: ['Add academic year', 'নতুন শিক্ষাবর্ষ'],
        termForm: ['Add term / semester', 'নতুন টার্ম / সেমিস্টার'],
        programForm: ['Add programme', 'নতুন প্রোগ্রাম'],
        levelForm: ['Add class / grade', 'নতুন শ্রেণি / গ্রেড'],
        trackCreateForm: ['Add academic stream', 'নতুন শিক্ষাধারা'],
        roomCreateForm: ['Add room', 'নতুন কক্ষ'],
        batchCreateForm: ['Add academic batch', 'নতুন একাডেমিক ব্যাচ'],
        subjectForm: ['Add subject', 'নতুন বিষয়'],
        curriculumForm: ['Create curriculum version', 'নতুন পাঠ্যক্রম সংস্করণ'],
        curriculumSubjectForm: ['Map a curriculum subject', 'পাঠ্যক্রমে বিষয় যোগ'],
        offeringForm: ['Add subject offering', 'নতুন বিষয় অফার'],
        sectionCreateForm: ['Add academic section', 'নতুন একাডেমিক শাখা'],
        prerequisiteForm: ['Add subject prerequisite', 'বিষয়ের পূর্বশর্ত যোগ']
    };
    const panels = new Map();
    const top = root.querySelector(':scope > div.d-flex, :scope > header, :scope > div[class*="justify-content-between"]');
    if (top) {
        top.classList.add('academic-page-top');
        const title = top.querySelector('h1, h2');
        const note = config[feature];
        if (title && note && root.id !== 'academicSectionsPage' && !top.querySelector('.academic-intro')) {
            const p = document.createElement('p');
            p.className = 'academic-intro';
            p.textContent = bn ? note.bn : note.en;
            title.insertAdjacentElement('afterend', p);
        }
    }
    function show(form, focus) {
        form.hidden = false;
        const panel = panels.get(form.id);
        if (panel) {
            panel.shell.classList.add('is-expanded');
            panel.button.setAttribute('aria-expanded', 'true');
            panel.button.textContent = bn ? 'বন্ধ করুন' : 'Close form';
        }
        if (focus) {
            const field = form.querySelector('input:not([type="hidden"]):not(:disabled), select:not(:disabled), textarea:not(:disabled)');
            if (field) requestAnimationFrame(() => field.focus({ preventScroll: true }));
        }
    }
    function hide(form) {
        form.hidden = true;
        const panel = panels.get(form.id);
        if (panel) {
            panel.shell.classList.remove('is-expanded');
            panel.button.setAttribute('aria-expanded', 'false');
            panel.button.textContent = bn ? 'ফর্ম খুলুন' : 'Open form';
        }
    }
    function enhance(id, pair) {
        const form = document.getElementById(id);
        if (!form || !root.contains(form) || panels.has(id)) return;
        const shell = document.createElement('div');
        shell.className = 'academic-editor-shell';
        const bar = document.createElement('div');
        bar.className = 'academic-editor-bar';
        const label = document.createElement('h3');
        label.className = 'academic-editor-label';
        label.textContent = bn ? pair[1] : pair[0];
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'btn btn-sm btn-outline-primary';
        button.setAttribute('aria-controls', id);
        button.setAttribute('aria-expanded', 'false');
        button.textContent = bn ? 'ফর্ম খুলুন' : 'Open form';
        bar.append(label, button);
        form.parentNode.insertBefore(shell, form);
        shell.append(bar, form);
        form.classList.add('academic-editor-form');
        panels.set(id, { shell, button });
        const shouldOpen = location.hash === '#' + id ||
            (id === 'sectionCreateForm' && new URLSearchParams(location.search).has('academicBatchId'));
        if (shouldOpen) show(form, false);
        else hide(form);
        button.addEventListener('click', () => {
            if (form.hidden) {
                show(form, true);
                shell.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
            } else hide(form); // Collapsing retains all entered values.
        });
    }
    Object.entries(headings).forEach(([id, title]) => enhance(id, title));
    // Existing list-edit handlers populate the same form. Reveal BEFORE their click
    // handlers call scrollIntoView; the original save/validation logic remains intact.
    const editorsByTable = {
        departmentRows: 'departmentForm',
        yearRows: 'yearForm',
        termRows: 'termForm',
        offeringRows: 'offeringForm',
        sectionRows: 'sectionCreateForm'
    };
    root.addEventListener('click', event => {
        const button = event.target.closest('button');
        const tableBody = button && button.closest('tbody');
        if (!tableBody || !editorsByTable[tableBody.id]) return;
        const label = (button.textContent || '').trim().toLowerCase();
        if (label === 'edit' || label === 'সম্পাদনা') {
            const form = document.getElementById(editorsByTable[tableBody.id]);
            if (form) show(form, false);
        }
    }, true);
    // Keep language changes scoped to existing static labels; never rewrite live
    // table rows, identifiers, user data, validation messages or form values.
    if (bn) {
        const textMap = {
            'Name *': 'নাম *', 'Code *': 'কোড *', 'Code': 'কোড',
            'Description': 'বিবরণ', 'Status': 'অবস্থা', 'Action': 'কার্যক্রম',
            'Actions': 'কার্যক্রম', 'Active': 'সক্রিয়', 'Inactive': 'নিষ্ক্রিয়',
            'Capacity': 'ধারণক্ষমতা', 'Capacity *': 'ধারণক্ষমতা *',
            'Academic year': 'শিক্ষাবর্ষ', 'Academic year *': 'শিক্ষাবর্ষ *',
            'Academic years': 'শিক্ষাবর্ষসমূহ', 'Academic Year': 'শিক্ষাবর্ষ',
            'Current year': 'বর্তমান শিক্ষাবর্ষ', 'Start date *': 'শুরুর তারিখ *',
            'End date *': 'শেষের তারিখ *', 'Campus': 'ক্যাম্পাস',
            'Campus *': 'ক্যাম্পাস *', 'Term name *': 'টার্মের নাম *',
            'Current term': 'বর্তমান টার্ম', 'Term': 'টার্ম',
            'Term (optional)': 'টার্ম (ঐচ্ছিক)', 'Programme': 'প্রোগ্রাম',
            'Programme *': 'প্রোগ্রাম *', 'Programme name *': 'প্রোগ্রামের নাম *',
            'Department': 'বিভাগ', 'Class': 'শ্রেণি', 'Class *': 'শ্রেণি *',
            'Class / Grade *': 'শ্রেণি / গ্রেড *',
            'Name': 'নাম', 'Type': 'ধরন', 'Default': 'ডিফল্ট',
            'Order': 'ক্রম', 'Display order': 'প্রদর্শন ক্রম',
            'Year': 'বছর', 'Dates': 'তারিখ', 'Current': 'বর্তমান',
            'Subject': 'বিষয়', 'Subject *': 'বিষয় *',
            'Subjects': 'বিষয়সমূহ', 'Section': 'শাখা',
            'Section *': 'শাখা *', 'Academic section *': 'একাডেমিক শাখা *',
            'Room (optional)': 'কক্ষ (ঐচ্ছিক)', 'Room': 'কক্ষ',
            'Default credits': 'ডিফল্ট ক্রেডিট', 'Delivery': 'পাঠদানের ধরন',
            'Mandatory': 'আবশ্যিক', 'Prerequisite': 'পূর্বশর্ত',
            'Required prior subject': 'পূর্ববর্তী আবশ্যিক বিষয়',
            'Effective on': 'কার্যকর তারিখ', 'Version *': 'সংস্করণ *',
            'Batch': 'ব্যাচ', 'Batch name *': 'ব্যাচের নাম *',
            'Batch start *': 'ব্যাচ শুরুর তারিখ *',
            'Batch end *': 'ব্যাচ শেষের তারিখ *',
            'Academic batch *': 'একাডেমিক ব্যাচ *',
            'Search': 'খুঁজুন', 'Refresh': 'রিফ্রেশ', 'Clear': 'পরিষ্কার',
            'Cancel': 'বাতিল', 'Previous': 'আগের পৃষ্ঠা',
            'Next': 'পরের পৃষ্ঠা', 'Save': 'সংরক্ষণ',
            'Add': 'যোগ করুন', 'Edit': 'সম্পাদনা',
            'Create Department': 'বিভাগ তৈরি করুন',
            'Save Year': 'শিক্ষাবর্ষ সংরক্ষণ', 'Save Term': 'টার্ম সংরক্ষণ',
            'Add Batch': 'ব্যাচ যোগ করুন', 'Add Subject': 'বিষয় যোগ করুন',
            'Add Section': 'শাখা যোগ করুন', 'Create Offering': 'বিষয় অফার তৈরি',
            'Create Curriculum': 'পাঠ্যক্রম তৈরি', 'Add Mapping': 'বিষয় ম্যাপ করুন'
        };
        root.querySelectorAll('label.form-label, .form-check-label, thead th, button, a.btn').forEach(node => {
            if (node.children.length) return;
            const raw = (node.textContent || '').trim();
            if (Object.prototype.hasOwnProperty.call(textMap, raw)) node.textContent = textMap[raw];
        });
    }
})();