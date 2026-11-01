/* Progressive enhancement for the server-authorized EduOS navigation.
 * All destinations come from Razor's role and module-entitlement checks.
 * This script only groups links already rendered for the current user. */
(() => {
    'use strict';

    const groups = {
        platform: [
            ['institutions', 'Institutions', 'প্রতিষ্ঠান', ['/PlatformTenants', '/PlatformCatalogAdministration']],
            ['features', 'Products & Features', 'প্রোডাক্ট ও ফিচার', ['/PlatformFeatureAdministration', '/PlatformProductModuleAdministration', '/PlatformInstitutionModulePresets', '/PlatformModuleFeatures']],
            ['subscriptions', 'Plans & Payments', 'প্ল্যান ও পেমেন্ট', ['/SubscriptionPlanAdministration', '/SubscriptionPaymentHistory', '/Account/PaymentVerification']],
            ['governance', 'Platform Governance', 'প্ল্যাটফর্ম পরিচালনা', ['/LegalDocuments']]
        ],
        setup: [
            ['access', 'Users & Access', 'ব্যবহারকারী ও অনুমতি', ['/TenantUsers', '/TenantPermissionOverrides']],
            ['institution', 'Institution & Settings', 'প্রতিষ্ঠান ও সেটিংস', ['/InstitutionManagement', '/TenantDomains', '/TenantTerminology', '/TenantSettings', '/NumberSeries', '/TenantModules', '/SubscriptionBilling', '/IntegrationAdministration']],
            ['enrollment', 'Student Administration', 'শিক্ষার্থী ব্যবস্থাপনা', ['/StudentLifecycle', '/StudentDocuments', '/StudentGuardians', '/StudentTransferRequests', '/EnrollmentManagement']],
            ['academic-setup', 'Academic Management', 'একাডেমিক ব্যবস্থাপনা', ['/AcademicManagement']]
        ],
        academics: [
            ['portal', 'My Learning Portal', 'আমার লার্নিং পোর্টাল', ['/StudentPortal', '/StudentSubjects', '/Alumni']],
            ['learners', 'Admissions & Students', 'ভর্তি ও শিক্ষার্থী', ['/Admissions', '/Students', '/LearnerIdentities']],
            ['learning', 'Learning & Courses', 'শিক্ষা ও কোর্স', ['/Learning']],
            ['assessments', 'Exams & Results', 'পরীক্ষা ও ফলাফল', ['/AssessmentAdministration']],
            ['operations', 'Classes & Attendance', 'ক্লাস ও উপস্থিতি', ['/AcademicRoutines', '/AcademicInstruction', '/AcademicCalendar', '/StudentLeaves', '/Attendance']]
        ],
        management: [
            ['people', 'HR & Payroll', 'এইচআর ও বেতন', ['/HR', '/EmployeeAttendance', '/LeaveEntitlements', '/PayrollOperations', '/EmployeePortal']],
            ['finance', 'Fees & Accounting', 'ফি ও হিসাব', ['/FeeOperations', '/FeeDiscountRules', '/FeeReceivablesAging', '/FeeHeadAccounting', '/AdmissionCashReceipts', '/FinanceAdjustments', '/AccountingSetup', '/AccountingJournals', '/AccountingReports']],
            ['inventory', 'Inventory & Assets', 'ইনভেন্টরি ও সম্পদ', ['/InventoryCatalog', '/InventoryMovements', '/AssetLifecycle']],
            ['campus', 'Campus Operations', 'ক্যাম্পাস পরিচালনা', ['/HostelOperations', '/hostel', '/TransportOperations', '/OperationsInsights']],
            ['library', 'Library', 'গ্রন্থাগার', ['/Library']],
            ['communication', 'Communication & Documents', 'যোগাযোগ ও ডকুমেন্ট', ['/CommunicationWorkspace', '/CommunicationAdministration', '/CustomFields', '/DocumentAdministration']],
            ['governance', 'Audit & Reporting', 'অডিট ও রিপোর্ট', ['/AuditLogs', '/ImportHistory']]
        ]
    };

    // Optional third level: Module > Settings/Operations/Reports > feature.
    // Only categorize links that Razor has already authorized and emitted.
    const categories = {
        'setup:academic-setup': [
            ['settings', 'Settings', 'সেটিংস', ['/AcademicManagement']],
            ['operations', 'Operations', 'কার্যক্রম', ['/AcademicManagement/Offerings']]
        ],
        'setup:institution': [
            ['settings', 'Settings', 'সেটিংস', ['/InstitutionManagement', '/TenantDomains', '/TenantTerminology', '/TenantSettings', '/NumberSeries', '/TenantModules']],
            ['operations', 'Operations', 'কার্যক্রম', ['/SubscriptionBilling', '/IntegrationAdministration']]
        ],
        'management:finance': [
            ['settings', 'Settings', 'সেটিংস', ['/FeeDiscountRules', '/FeeHeadAccounting', '/AccountingSetup']],
            ['operations', 'Operations', 'কার্যক্রম', ['/FeeOperations', '/AdmissionCashReceipts', '/FinanceAdjustments', '/AccountingJournals']],
            ['reports', 'Reports', 'রিপোর্ট', ['/FeeReceivablesAging', '/AccountingReports']]
        ]
    };

    function normalizedPath(path) {
        if (typeof path !== 'string') return '/';
        const clean = path.split(/[?#]/, 1)[0].replace(/\/+$/, '');
        return (clean || '/').toLowerCase();
    }

    function matchesPrefix(path, prefix) {
        const target = normalizedPath(path);
        const base = normalizedPath(prefix);
        return target === base || target.startsWith(base + '/');
    }

    function groupFor(section, href) {
        const path = normalizedPath(href);
        const definition = (groups[section] || []).find(([, , , prefixes]) =>
            prefixes.some(prefix => matchesPrefix(path, prefix)));
        return definition ? definition[0] : 'other';
    }

    function categoryFor(section, group, href) {
        const path = normalizedPath(href);
        // More-specific prefixes take priority (Offerings before AcademicManagement).
        const matches = (categories[`${section}:${group}`] || []).flatMap(([key, , , prefixes]) =>
            prefixes.filter(prefix => matchesPrefix(path, prefix)).map(prefix => ({ key, size: prefix.length })));
        return matches.sort((a, b) => b.size - a.size)[0]?.key || 'other';
    }

    function scoreActive(href, pathname, hash = '') {
        if (typeof href !== 'string' || !href.startsWith('/')) return -1;
        const [pathPart, linkHash = ''] = href.split('#', 2);
        const path = normalizedPath(pathPart);
        const current = normalizedPath(pathname);
        if (path === current) {
            // Prefer the exact anchor on an anchored page, otherwise its main link.
            return 10000 + path.length + (linkHash === hash.replace(/^#/, '') && linkHash ? 2000 : 0)
                - (linkHash && linkHash !== hash.replace(/^#/, '') ? 100 : 0);
        }
        const base = path.endsWith('/index') ? path.slice(0, -6) : path;
        return matchesPrefix(current, base) ? 100 + base.length : -1;
    }

    if (typeof module !== 'undefined' && module.exports) {
        module.exports = { groupFor, categoryFor, scoreActive, normalizedPath };
    }
    if (typeof document === 'undefined') return;

    const sidebar = document.getElementById('sidebarMenu');
    if (!sidebar) return;
    const navs = [...sidebar.querySelectorAll('nav[data-sidebar-section]')];
    if (!navs.length) return;

    const bangla = document.documentElement.lang?.toLowerCase().startsWith('bn');
    const storageKey = 'eduos.sidebar.navigation.v1';
    function readState() {
        try {
            const state = JSON.parse(sessionStorage.getItem(storageKey) || '{}');
            return {
                scroll: Number.isFinite(state.scroll) && state.scroll >= 0 ? state.scroll : 0,
                group: typeof state.group === 'string' ? state.group : '',
                category: typeof state.category === 'string' ? state.category : ''
            };
        } catch { return { scroll: 0, group: '', category: '' }; }
    }
    let state = readState();
    function saveState() {
        try { sessionStorage.setItem(storageKey, JSON.stringify(state)); } catch { /* Storage is optional. */ }
    }

    const allLinks = [];
    const allDetails = [];
    const allCategories = [];
    for (const nav of navs) {
        const section = nav.dataset.sidebarSection;
        const definitions = groups[section] || [];
        const links = [...nav.children].filter(node => node.matches?.('a.nav-link'));
        const categorized = new Map();
        const direct = [];
        for (const link of links) {
            allLinks.push(link);
            const href = link.getAttribute('href') || '';
            if (matchesPrefix(href, '/Dashboard')) {
                direct.push(link);
                continue;
            }
            const key = groupFor(section, href);
            if (!categorized.has(key)) categorized.set(key, []);
            categorized.get(key).push(link);
        }

        const container = document.createDocumentFragment();
        for (const link of direct) {
            link.classList.add('sidebar-dashboard-link');
            container.append(link);
        }
        for (const [key, english, bengali] of [...definitions, ['other', 'Other', 'অন্যান্য', []]]) {
            const children = categorized.get(key);
            if (!children?.length) continue;
            const details = document.createElement('details');
            details.className = 'sidebar-group';
            details.dataset.sidebarGroup = `${section}:${key}`;
            const summary = document.createElement('summary');
            summary.className = 'sidebar-group-toggle';
            const label = document.createElement('span');
            label.className = 'sidebar-group-label';
            label.textContent = bangla ? bengali : english;
            const count = document.createElement('span');
            count.className = 'sidebar-group-count';
            count.textContent = String(children.length);
            count.setAttribute('aria-hidden', 'true');
            summary.append(label, count);
            const submenu = document.createElement('div');
            submenu.className = 'sidebar-submenu';
            const definitionsForGroup = categories[`${section}:${key}`];
            if (definitionsForGroup) {
                const byCategory = new Map();
                for (const child of children) {
                    const category = categoryFor(section, key, child.getAttribute('href') || '');
                    if (!byCategory.has(category)) byCategory.set(category, []);
                    byCategory.get(category).push(child);
                }
                for (const [categoryKey, en, bn] of [...definitionsForGroup, ['other', 'Other', 'অন্যান্য', []]]) {
                    const sublinks = byCategory.get(categoryKey);
                    if (!sublinks?.length) continue;
                    const subDetails = document.createElement('details');
                    subDetails.className = 'sidebar-category';
                    subDetails.dataset.sidebarCategory = `${section}:${key}:${categoryKey}`;
                    const subToggle = document.createElement('summary');
                    subToggle.className = 'sidebar-category-toggle';
                    const subLabel = document.createElement('span');
                    subLabel.textContent = bangla ? bn : en;
                    const subCount = document.createElement('span');
                    subCount.className = 'sidebar-category-count';
                    subCount.textContent = String(sublinks.length);
                    subCount.setAttribute('aria-hidden', 'true');
                    subToggle.append(subLabel, subCount);
                    const categoryLinks = document.createElement('div');
                    categoryLinks.className = 'sidebar-category-links';
                    for (const link of sublinks) categoryLinks.append(link);
                    subDetails.append(subToggle, categoryLinks);
                    submenu.append(subDetails);
                    allCategories.push(subDetails);
                }
            } else {
                for (const child of children) submenu.append(child);
            }
            details.append(summary, submenu);
            container.append(details);
            allDetails.push(details);
        }
        nav.replaceChildren(container);
        nav.classList.add('sidebar-nav-grouped');
        if (!links.length) {
            nav.hidden = true;
            const heading = nav.previousElementSibling;
            if (heading?.classList.contains('menu-title')) {
                heading.hidden = true;
                if (heading.previousElementSibling?.classList.contains('sidebar-divider')) {
                    heading.previousElementSibling.hidden = true;
                }
            }
        }
    }

    // The most specific link wins. Server-rendered 'active' is a safe fallback.
    function highlightCurrent() {
        const active = allLinks.reduce((best, link) => {
            const score = scoreActive(link.getAttribute('href') || '', window.location.pathname, window.location.hash);
            return score > best.score ? { link, score } : best;
        }, { link: null, score: -1 });
        const selectedLink = active.link || allLinks.find(link => link.classList.contains('active'));
        for (const link of allLinks) {
            link.classList.toggle('active', link === selectedLink);
            if (link === selectedLink) link.setAttribute('aria-current', 'page');
            else link.removeAttribute('aria-current');
        }
        for (const details of allDetails) {
            details.classList.toggle('has-current', details.contains(selectedLink));
        }
        for (const details of allCategories) {
            details.classList.toggle('has-current', details.contains(selectedLink));
        }
        return selectedLink;
    }

    let selected = highlightCurrent();
    let activeGroup = selected?.closest('.sidebar-group') || null;
    const isTop = () => window.matchMedia('(min-width: 992px)').matches &&
        document.documentElement.dataset.workspaceNavigation === 'top';

    function restoreOpenGroup() {
        const toOpen = isTop() ? null : (activeGroup ||
            allDetails.find(details => details.dataset.sidebarGroup === state.group));
        for (const details of allDetails) details.open = details === toOpen;
        if (!isTop()) {
            const selectedCategory = selected?.closest('.sidebar-category');
            const toOpenCategory = selectedCategory ||
                allCategories.find(details => details.dataset.sidebarCategory === state.category && toOpen?.contains(details));
            for (const details of allCategories) details.open = details === toOpenCategory;
        }
    }
    restoreOpenGroup();

    for (const details of allDetails) {
        details.addEventListener('toggle', () => {
            if (!details.open) return;
            for (const other of allDetails) if (other !== details) other.open = false;
            state.group = details.dataset.sidebarGroup;
            saveState();
        });
    }
    for (const details of allCategories) {
        details.addEventListener('toggle', () => {
            if (!details.open) return;
            for (const other of allCategories) {
                if (other !== details && other.parentElement === details.parentElement) other.open = false;
            }
            state.category = details.dataset.sidebarCategory;
            saveState();
        });
    }

    let scheduled = false;
    sidebar.addEventListener('scroll', () => {
        if (scheduled || isTop()) return;
        scheduled = true;
        window.requestAnimationFrame(() => {
            scheduled = false;
            state.scroll = sidebar.scrollTop;
            saveState();
        });
    }, { passive: true });
    for (const link of allLinks) link.addEventListener('click', () => {
        state.scroll = sidebar.scrollTop;
        state.group = link.closest('.sidebar-group')?.dataset.sidebarGroup || state.group;
        state.category = link.closest('.sidebar-category')?.dataset.sidebarCategory || state.category;
        saveState();
    });
    window.addEventListener('pagehide', () => {
        if (!isTop()) state.scroll = sidebar.scrollTop;
        saveState();
    });

    function showActiveAndRestoreScroll() {
        if (isTop()) return;
        sidebar.scrollTop = state.scroll;
        if (!selected) return;
        const navRect = sidebar.getBoundingClientRect();
        const itemRect = selected.getBoundingClientRect();
        // Scroll only the navigation pane, never the page, if its selected link is hidden.
        if (itemRect.top < navRect.top + 12) sidebar.scrollTop -= navRect.top + 12 - itemRect.top;
        else if (itemRect.bottom > navRect.bottom - 12) sidebar.scrollTop += itemRect.bottom - navRect.bottom + 12;
        state.scroll = sidebar.scrollTop;
        saveState();
    }
    window.requestAnimationFrame(showActiveAndRestoreScroll);
    window.addEventListener('hashchange', () => {
        selected = highlightCurrent();
        activeGroup = selected?.closest('.sidebar-group') || null;
        restoreOpenGroup();
        window.requestAnimationFrame(showActiveAndRestoreScroll);
    });
    window.addEventListener('eduos:workspace-preferences', () => {
        if (isTop()) {
            allDetails.forEach(details => { details.open = false; });
            allCategories.forEach(details => { details.open = false; });
        } else {
            restoreOpenGroup();
            window.requestAnimationFrame(showActiveAndRestoreScroll);
        }
    });

    document.addEventListener('pointerdown', event => {
        if (!isTop() || sidebar.contains(event.target)) return;
        for (const details of allDetails) details.open = false;
        for (const details of allCategories) details.open = false;
    });
    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape' || !isTop()) return;
        const open = allDetails.find(details => details.open);
        if (!open) return;
        open.open = false;
        if (sidebar.contains(event.target)) open.querySelector('summary')?.focus();
    });
})();
