(() => {
    'use strict';
    // Progressive enhancement only: existing Razor forms/APIs continue to work without this file.
    const root = document.querySelector('#academicManagement, #academicSectionsPage, #prerequisiteWorkspace');
    if (!root || root.dataset.academicWorkspaceReady === '1') return;
    root.dataset.academicWorkspaceReady = '1';
    root.classList.add('edu-academic-workspace');
    const bn = document.documentElement.lang.toLowerCase().startsWith('bn');
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
    function show(form, focus) {
        form.hidden = false;
        const panel = panels.get(form.id);
        if (panel) {
            panel.shell.classList.add('is-expanded');
            panel.button.setAttribute('aria-expanded', 'true');
            panel.button.classList.remove('btn-primary');
            panel.button.classList.add('btn-outline-secondary');
            panel.button.textContent = bn ? 'বন্ধ করুন' : 'Close';
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
            panel.button.classList.remove('btn-outline-secondary');
            panel.button.classList.add('btn-primary');
            panel.button.textContent = panel.title;
        }
    }
    function enhance(id, pair) {
        const form = document.getElementById(id);
        if (!form || !root.contains(form) || panels.has(id)) return;
        const shell = document.createElement('div');
        shell.className = 'academic-editor-shell';
        const bar = document.createElement('div');
        bar.className = 'academic-editor-bar';
        const title = bn ? pair[1] : pair[0];
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'btn btn-sm btn-primary';
        button.setAttribute('aria-controls', id);
        button.setAttribute('aria-expanded', 'false');
        button.textContent = title;
        bar.append(button);
        form.parentNode.insertBefore(shell, form);
        shell.append(bar, form);
        form.classList.add('academic-editor-form');
        panels.set(id, { shell, button, title });
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
    // Attach related navigation to the editor toggle. Avoid a standalone toolbar
    // that adds an entire empty row above the Sections / Batches directory.
    const relatedId = root.id === 'academicSectionsPage' ? 'sectionCreateForm' :
        root.dataset.activeFeature === 'sections' ? 'batchCreateForm' : null;
    const related = root.querySelector('.academic-related-link');
    const relatedBar = relatedId && panels.get(relatedId)?.shell.querySelector('.academic-editor-bar');
    if (related && relatedBar) {
        relatedBar.prepend(related);
        relatedBar.classList.add('academic-editor-bar--split');
    }
    // Keep curriculum navigation beside the subject mapping action rather than
    // occupying a separate full-width row. The link remains usable without JS.
    const curriculumRelated = root.querySelector('.academic-curriculum-related');
    const mappingBar = document.getElementById('curriculumSubjectForm')
        ?.closest('.academic-editor-shell')?.querySelector('.academic-editor-bar');
    if (curriculumRelated && mappingBar) {
        mappingBar.prepend(curriculumRelated);
        mappingBar.classList.add('academic-editor-bar--split');
    }
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
        Object.assign(textMap, {
            'Academic departments': 'একাডেমিক বিভাগ', 'Terms / Semesters': 'টার্ম / সেমিস্টার',
            'Class programmes and grades': 'প্রোগ্রাম, শ্রেণি ও গ্রেড',
            'Available programmes': 'উপলব্ধ প্রোগ্রাম',
            'Programme metadata & department reassignment': 'প্রোগ্রাম তথ্য ও বিভাগ পরিবর্তন',
            'Class / Grade administration': 'শ্রেণি / গ্রেড ব্যবস্থাপনা',
            'Academic tracks / streams': 'একাডেমিক শিক্ষাধারা',
            'Academic rooms': 'একাডেমিক কক্ষ', 'Academic Batches': 'একাডেমিক ব্যাচ',
            'Academic Batch Register': 'একাডেমিক ব্যাচ তালিকা',
            'Curriculum versions & subject mapping': 'পাঠ্যক্রম সংস্করণ ও বিষয় ম্যাপিং',
            'Add subject to an existing curriculum': 'বিদ্যমান পাঠ্যক্রমে বিষয় যোগ',
            'Batch subject offerings': 'ব্যাচের বিষয় অফার',
            'Search name / code prefix': 'নাম / কোড দিয়ে খুঁজুন',
            'Search subject name / code prefix': 'বিষয়ের নাম / কোড দিয়ে খুঁজুন',
            'Search offering code / subject': 'অফার কোড / বিষয় খুঁজুন',
            'Name / code prefix': 'নাম / কোড দিয়ে খুঁজুন',
            'Name / Code': 'নাম / কোড',
            'Room name/code prefix': 'কক্ষের নাম / কোড খুঁজুন',
            'Programme name / code prefix': 'প্রোগ্রামের নাম / কোড খুঁজুন',
            'Level name / code prefix': 'শ্রেণির নাম / কোড খুঁজুন',
            'Find department by prefix': 'বিভাগ খুঁজুন',
            'Find programmes by prefix (for large catalogs)': 'প্রোগ্রাম খুঁজুন',
            'Find curriculum (prefix)': 'পাঠ্যক্রম খুঁজুন',
            'Search subject': 'বিষয় খুঁজুন',
            'Search versions': 'সংস্করণ খুঁজুন',
            'Curriculum version': 'পাঠ্যক্রম সংস্করণ',
            'Load curriculum catalog': 'পাঠ্যক্রমের তালিকা দেখুন',
            'Add prerequisite': 'পূর্বশর্ত যোগ করুন',
            'Add link': 'সম্পর্ক যোগ করুন',
            'CSV (page)': 'CSV (এই পৃষ্ঠা)',
            'Print': 'প্রিন্ট',
            'Export CSV': 'CSV এক্সপোর্ট',
            'Export visible CSV': 'দৃশ্যমান তালিকা CSV',
            'Cycle': 'শিক্ষাকাল', 'Months': 'মেয়াদ (মাস)',
            'Offered at campuses (Ctrl/Cmd + click)': 'যেসব ক্যাম্পাসে চালু (একাধিক নির্বাচন)',
            'Programme / Class': 'প্রোগ্রাম / শ্রেণি',
            'Campus availability': 'ক্যাম্পাসে উপলব্ধতা',
            'Allowed campuses (Ctrl/Cmd + click)': 'অনুমোদিত ক্যাম্পাস (একাধিক নির্বাচন)',
            'Primary campus': 'প্রধান ক্যাম্পাস',
            'Unrestricted — allow all tenant campuses': 'প্রতিষ্ঠানের সব ক্যাম্পাসে অনুমোদিত',
            'Code (immutable)': 'কোড (পরিবর্তনযোগ্য নয়)',
            'Short name': 'সংক্ষিপ্ত নাম',
            'Duration (months)': 'মেয়াদ (মাস)',
            'Award title': 'সনদের নাম',
            'Save metadata': 'তথ্য সংরক্ষণ',
            'Save allocation': 'ক্যাম্পাস বরাদ্দ সংরক্ষণ',
            'Programme name *': 'প্রোগ্রামের নাম *',
            'Class / Grade name *': 'শ্রেণি / গ্রেডের নাম *',
            'Level number *': 'শ্রেণি নম্বর *',
            'Level number': 'শ্রেণি নম্বর',
            'Level': 'শ্রেণি', 'Progression': 'উন্নীতকরণ',
            'Track': 'শিক্ষাধারা', 'Track name *': 'শিক্ষাধারার নাম *',
            'Add track': 'শিক্ষাধারা যোগ করুন', 'Save track': 'শিক্ষাধারা সংরক্ষণ',
            'Find programmes': 'প্রোগ্রাম খুঁজুন',
            'Add Programme': 'প্রোগ্রাম যোগ করুন',
            'Add Class': 'শ্রেণি যোগ করুন',
            'Campus *': 'ক্যাম্পাস *',
            'Type': 'ধরন', 'Subject name *': 'বিষয়ের নাম *',
            'Save subject': 'বিষয় সংরক্ষণ',
            'Curriculum name *': 'পাঠ্যক্রমের নাম *',
            'Curriculum *': 'পাঠ্যক্রম *',
            'Curriculum subject *': 'পাঠ্যক্রমের বিষয় *',
            'Offering code *': 'অফারের কোড *',
            'Effective from *': 'কার্যকর শুরুর তারিখ *',
            'Effective dates': 'কার্যকর তারিখসমূহ',
            'Version': 'সংস্করণ',
            'Marks': 'মোট নম্বর', 'Pass': 'পাস নম্বর',
            'Offerings': 'বিষয় অফার', 'Enrollments': 'ভর্তি',
            'Pending': 'অপেক্ষমাণ', 'Allocated': 'বরাদ্দকৃত',
            'Reserved': 'সংরক্ষিত', 'Available': 'উপলব্ধ',
            'Mode': 'পদ্ধতি',
            'Organize programmes by department. Active programmes prevent department deactivation. Codes are immutable.':
                'বিভাগ অনুযায়ী প্রোগ্রাম সাজান। সক্রিয় প্রোগ্রাম থাকলে বিভাগ নিষ্ক্রিয় করা যাবে না; কোড অপরিবর্তনীয়।',
            'Programme = academic stream (e.g. Secondary); Level = Class/Grade within that programme. Do not duplicate records per section.':
                'প্রোগ্রাম হলো শিক্ষাধারা (যেমন মাধ্যমিক); শ্রেণি হলো সেই প্রোগ্রামের গ্রেড। প্রতিটি শাখার জন্য আলাদা শ্রেণি তৈরি করবেন না।',
            'Create a cohort/intake here only when required. Academic Sections are maintained independently under Campus + Class; creating a Section never requires a Batch.':
                'প্রয়োজন হলেই ব্যাচ/ভর্তি গ্রুপ তৈরি করুন। ক্যাম্পাস ও শ্রেণির অধীনে শাখা আলাদাভাবে থাকে; শাখার জন্য ব্যাচ লাগে না।',
            'Manage tenant-wide subjects. Codes are immutable; linked curriculum history prevents deactivation. Existing curriculum credits and marks are not changed by editing defaults.':
                'প্রতিষ্ঠানের বিষয় তালিকা পরিচালনা করুন। বিষয় কোড অপরিবর্তনীয়; বিদ্যমান পাঠ্যক্রমের নম্বর বা ক্রেডিট ডিফল্ট পরিবর্তনে বদলাবে না।',
            'Create a new curriculum version for historical changes. Existing curriculum assignments are not silently overwritten.':
                'ইতিহাস সংরক্ষণের জন্য নতুন পাঠ্যক্রম সংস্করণ তৈরি করুন। আগের বরাদ্দ স্বয়ংক্রিয়ভাবে বদলানো হবে না।',
            'Both subjects must be mapped to this curriculum. Circular dependencies are rejected.':
                'দুটি বিষয়ই নির্বাচিত পাঠ্যক্রমে থাকতে হবে। চক্রাকার পূর্বশর্ত গ্রহণযোগ্য নয়।'
        });
        root.querySelectorAll('label.form-label, .form-check-label, thead th, h2, h3, p.text-muted, button, a.btn').forEach(node => {
            if (node.children.length) return;
            const raw = (node.textContent || '').trim();
            if (Object.prototype.hasOwnProperty.call(textMap, raw)) node.textContent = textMap[raw];
        });
    }
})();