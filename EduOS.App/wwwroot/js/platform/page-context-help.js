(() => {
    'use strict';

    // Only reviewed, fixed help text is displayed. Route names are never inserted as HTML.
    const guides = [
        { prefix: '/dashboard', en: 'Review institution activity, plan usage and important account notices. Use available actions to open the relevant workflow.', bn: 'প্রতিষ্ঠানের কার্যক্রম, প্ল্যান ব্যবহার ও জরুরি নোটিশ দেখুন। প্রয়োজনীয় কাজের জন্য সংশ্লিষ্ট অপশন খুলুন।' },
        { prefix: '/workspace', en: 'Choose your appearance and save it for this institution. Appearance cannot grant additional access.', bn: 'এই প্রতিষ্ঠানের জন্য ডিজাইন নির্বাচন করে সংরক্ষণ করুন। ডিজাইন পরিবর্তনে নতুন অনুমতি পাওয়া যায় না।' },
        { prefix: '/account/profile', en: 'Review your account details and available security options. Confirm changes before saving.', bn: 'অ্যাকাউন্ট তথ্য ও নিরাপত্তা অপশন দেখুন। পরিবর্তন সংরক্ষণের আগে যাচাই করুন।' },
        { prefix: '/account/campussetup', en: 'Manage campuses that belong to this institution. Check existing records before editing or archiving.', bn: 'এই প্রতিষ্ঠানের ক্যাম্পাস পরিচালনা করুন। সম্পাদনা বা আর্কাইভের আগে বিদ্যমান তথ্য যাচাই করুন।' },
        { prefix: '/account/academicsetup', en: 'Review the academic year and term dates before saving. Existing academic records may restrict changes.', bn: 'সংরক্ষণের আগে শিক্ষাবর্ষ ও টার্মের তারিখ যাচাই করুন। বিদ্যমান রেকর্ড পরিবর্তন সীমিত করতে পারে।' },
        { prefix: '/account/generalsettings', en: 'Choose approved regional and language settings for the institution, then save.', bn: 'প্রতিষ্ঠানের অনুমোদিত অঞ্চল ও ভাষার সেটিং নির্বাচন করে সংরক্ষণ করুন।' },
        { prefix: '/account/modulesetup', en: 'Select only entitled modules. Unavailable modules require an authorized plan change.', bn: 'শুধু অনুমোদিত মডিউল নির্বাচন করুন। অন্য মডিউলের জন্য অনুমোদিত প্ল্যান পরিবর্তন প্রয়োজন।' },
        { prefix: '/tenantsettings', en: 'Edit institution settings with their current version. Protected values remain hidden; blank keeps an existing protected value.', bn: 'বর্তমান সংস্করণ যাচাই করে সেটিং সম্পাদনা করুন। সুরক্ষিত মান আড়াল থাকে; ফাঁকা রাখলে আগের মান বজায় থাকবে।' },
        { prefix: '/tenantpermissionoverrides', en: 'Choose an active member, then review explicit allow or deny and expiry. Inherit removes the exception; changes require your institution authorization.', bn: 'সক্রিয় সদস্য বেছে বিশেষ অনুমতি, নিষেধ ও মেয়াদ যাচাই করুন। ভূমিকা অনুযায়ী নির্বাচন করলে ব্যতিক্রম সরে যাবে।' }
    ];
    const fallback = {
        en: 'Use the actions permitted for your role. Check required fields and validation messages before saving.',
        bn: 'আপনার ভূমিকার জন্য অনুমোদিত অপশন ব্যবহার করুন। সংরক্ষণের আগে আবশ্যিক ঘর ও যাচাই বার্তা দেখুন।'
    };
    function guideFor(path, bangla) {
        const route = typeof path === 'string' ? path.toLowerCase() : '';
        const entry = guides.find(item => route === item.prefix || route.startsWith(item.prefix + '/'));
        return (entry || fallback)[bangla ? 'bn' : 'en'];
    }
    if (typeof module !== 'undefined' && module.exports) module.exports = { guideFor };
    if (typeof document === 'undefined') return;

    const help = document.getElementById('pageContextHelp');
    if (!help) return;
    const summary = help.querySelector('summary');
    const text = help.querySelector('[data-page-context-help-text]');
    if (!summary || !text) return;
    const bangla = document.documentElement.lang.toLowerCase().startsWith('bn');
    text.textContent = guideFor(window.location.pathname, bangla);

    // Avoid duplicating an explicit contextual help control supplied by a page.
    if (document.querySelector('[data-page-help-primary]')) {
        help.hidden = true;
        return;
    }

    let hoverTimer = null;
    help.addEventListener('mouseenter', () => {
        if (!window.matchMedia('(hover: hover) and (pointer: fine)').matches) return;
        hoverTimer = window.setTimeout(() => { help.open = true; }, 450);
    });
    help.addEventListener('mouseleave', () => {
        if (hoverTimer !== null) window.clearTimeout(hoverTimer);
        hoverTimer = null;
        if (!help.contains(document.activeElement)) help.open = false;
    });
    document.addEventListener('pointerdown', event => {
        if (help.open && !help.contains(event.target)) help.open = false;
    });
    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape' || !help.open) return;
        help.open = false;
        summary.focus();
    });
})();
