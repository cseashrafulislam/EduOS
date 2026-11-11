(() => {
  "use strict";

  const bn = {
    navFeatures:"ফিচার",navInstitutions:"প্রতিষ্ঠান",navScreenshots:"স্ক্রিনশট",navHow:"যেভাবে কাজ করে",navContact:"যোগাযোগ",demoLogin:"ডেমো লগইন",
    eyebrow:"স্মার্ট ও AI-সহায়ক শিক্ষা ব্যবস্থাপনা",heroTitle:'EduOS — শিক্ষাপ্রতিষ্ঠানের<span class="accent-text"> ইন্টেলিজেন্ট অপারেটিং সিস্টেম</span>',
    heroDesc:"স্কুল, কলেজ, বিশ্ববিদ্যালয় ও প্রশিক্ষণ প্রতিষ্ঠানের জন্য একটি সমন্বিত প্ল্যাটফর্ম। ভর্তি, একাডেমিক, হাজিরা, পরীক্ষা, ফাইন্যান্স, HR ও লার্নিং পরিচালনা করুন স্মার্ট অটোমেশন ও AI-সহায়ক টুলের মাধ্যমে।",
    explorePlatform:"প্ল্যাটফর্ম দেখুন",tryDemo:"ডেমো দেখুন",heroCheck1:"বিভিন্ন প্রতিষ্ঠানের জন্য কনফিগারযোগ্য",heroCheck2:"English ও বাংলা ইন্টারফেস",
    floatReady:"বাস্তব কাজের ধারা মাথায় রেখে ডিজাইন",floatUnified:"একটি সংযুক্ত ওয়ার্কস্পেস",illustrationDisclaimer:"EduOS ওয়ার্কস্পেস",
    benefit1:"সমন্বিত প্ল্যাটফর্ম",benefit2:"নমনীয় কার্যপ্রবাহ",benefit3:"রোল অনুযায়ী অ্যাক্সেস",benefit4:"সহজ রিপোর্ট ও রেকর্ড",
    featuresEyebrow:"পুরো প্রতিষ্ঠানের জন্য",featuresTitle:"আপনার সব কার্যক্রম এক জায়গায়।",featuresDesc:"প্রথম ভর্তি আবেদন থেকে নিয়মিত শিক্ষা ও প্রশাসন—EduOS গুরুত্বপূর্ণ কাজগুলোকে একে অপরের সঙ্গে যুক্ত করে।",
    admissionTitle:"ভর্তি ও শিক্ষার্থী",admissionText:"ভর্তি প্রক্রিয়া, শিক্ষার্থীর প্রোফাইল, এনরোলমেন্ট এবং প্রয়োজনীয় রেকর্ড পরিচালনা করুন।",feature01:"০১ / শিক্ষার্থী ব্যবস্থাপনা",
    academicTitle:"একাডেমিক ও পরীক্ষা",academicText:"একাডেমিক কাঠামো, কোর্স, পরীক্ষা, ফলাফল ও সংশ্লিষ্ট তথ্য সাজিয়ে রাখুন।",feature02:"০২ / একাডেমিক",
    financeTitle:"ফি ও হিসাব",financeText:"ফি স্ট্রাকচার, শিক্ষার্থীর বিল, রসিদ এবং প্রতিষ্ঠানের হিসাব পরিচালনা করুন।",feature03:"০৩ / ফাইন্যান্স",
    hrTitle:"এইচআর ও পে-রোল",hrText:"কর্মীদের তথ্য, বেতন প্রক্রিয়া এবং প্রশাসনিক রেকর্ড সুসংগঠিত রাখুন।",feature04:"০৪ / মানবসম্পদ",
    lmsTitle:"লার্নিং ও লাইব্রেরি",lmsText:"কোর্স, শেখার উপকরণ এবং লাইব্রেরির কার্যক্রম একাডেমিক কাজের সঙ্গে পরিচালনা করুন।",feature05:"০৫ / লার্নিং",
    campusTitle:"হোস্টেল, পরিবহন ও ইনভেন্টরি",campusText:"ক্যাম্পাসের দৈনন্দিন সেবা ও প্রয়োজনীয় সম্পদ পরিচালনায় সহায়তা পান।",feature06:"০৬ / ক্যাম্পাস সেবা",
    availabilityNote:"আপনার প্রতিষ্ঠানের প্রয়োজন অনুযায়ী মডিউল ও সেটিংস বেছে নিন।",
    institutionsEyebrow:"এক প্ল্যাটফর্ম, নানা ধরনের প্রতিষ্ঠান",institutionsTitle:"আপনার শিক্ষা-পদ্ধতির সঙ্গে মানিয়ে নেবে।",institutionsDesc:"প্রতিষ্ঠানভেদে শ্রেণি, কোর্স, পরিভাষা ও কাজের ধরন আলাদা। EduOS নির্দিষ্ট কাঠামোয় সীমাবদ্ধ না রেখে কনফিগারযোগ্য ব্যবস্থা তৈরি করছে।",institutionsLink:"আপনার প্রতিষ্ঠান নিয়ে কথা বলুন",
    school:"স্কুল",college:"কলেজ",university:"বিশ্ববিদ্যালয়",madrasa:"মাদ্রাসা",training:"ট্রেনিং সেন্টার",coaching:"কোচিং ও অনলাইন শিক্ষা",
    showcaseEyebrow:"যে সফটওয়্যারটি ব্যবহার করবেন, সেটি দেখুন",showcaseTitle:"EduOS-এর ভেতরের অভিজ্ঞতা।",showcaseDesc:"EduOS ড্যাশবোর্ড, শিক্ষার্থী, ফি, একাডেমিক এবং রিপোর্টিং স্ক্রিনগুলো দেখুন।",
    shotDashboard:"ড্যাশবোর্ড",shotStudents:"শিক্ষার্থী",shotFees:"ফি ও বিলিং",shotAcademics:"একাডেমিক",shotReports:"রিপোর্ট",
    screenshotPendingTitle:"EduOS স্ক্রিন গ্যালারি",screenshotPendingText:"ড্যাশবোর্ড, শিক্ষার্থী, ফি, একাডেমিক ও রিপোর্টিং স্ক্রিন।",
    screenshotCaption:"স্ক্রিন প্রিভিউতে উদাহরণ তথ্য ব্যবহার করা হয়েছে।",
    workflowEyebrow:"ধাপে ধাপে সহজ কাজ",workflowTitle:"কম ঝামেলা। বেশি স্বচ্ছতা।",workflowDesc:"প্রতিষ্ঠানের গুরুত্বপূর্ণ কাজগুলো সংযুক্তভাবে পরিচালনার একটি ধারাবাহিক উপায়।",
    step1Title:"প্রতিষ্ঠান সেটআপ",step1Text:"একাডেমিক কাঠামো, ক্যাম্পাস, রোল এবং প্রয়োজনীয় মডিউল কনফিগার করুন।",
    step2Title:"শিক্ষার্থী ও শিক্ষা পরিচালনা",step2Text:"শিক্ষার্থী, কর্মী, কোর্স ও একাডেমিক কার্যক্রম সাজিয়ে রাখুন।",
    step3Title:"দৈনন্দিন কার্যক্রম",step3Text:"ফি, পেমেন্ট এবং প্রতিষ্ঠানের প্রাসঙ্গিক সেবা পরিচালনা করুন।",
    step4Title:"পুরো চিত্র বুঝুন",step4Text:"সংযুক্ত রেকর্ড ও রিপোর্ট দেখে সিদ্ধান্ত নিতে সহায়তা পান।",
    faqEyebrow:"সাধারণ প্রশ্নের সহজ উত্তর",faqTitle:"শুরু করার আগে।",faqIntro:"প্রতিটি প্রতিষ্ঠানের চাহিদা আলাদা। ডেমো দেখার আগে কয়েকটি প্রয়োজনীয় বিষয় জেনে নিন।",
    faq1q:"EduOS কি শুধু স্কুলের জন্য?",faq1a:"না। স্কুল, কলেজ, বিশ্ববিদ্যালয়, মাদ্রাসা, কোচিং ও প্রশিক্ষণ প্রতিষ্ঠানের কথা মাথায় রেখে ডিজাইন করা হয়েছে। প্রতিষ্ঠানের ধরন অনুযায়ী কনফিগারেশন ভিন্ন হতে পারে।",
    faq2q:"বাংলায় সফটওয়্যার ব্যবহার করা যাবে?",faq2a:"EduOS-এ বাংলা ও ইংরেজি অভিজ্ঞতা এবং প্রতিষ্ঠানভিত্তিক মডিউল রয়েছে।",
    faq3q:"সফটওয়্যার নেওয়ার আগে দেখা যাবে?",faq3a:"হ্যাঁ। Demo Gallery দেখুন অথবা ইমেইল ও WhatsApp-এ যোগাযোগ করে সরাসরি ডেমো রিকোয়েস্ট করুন।",
    faq4q:"সব প্রতিষ্ঠানের সেটিং কি একই থাকবে?",faq4a:"না। EduOS-এ একাডেমিক পরিভাষা, কাঠামো, মডিউল ও কাজের ধারা কনফিগার করার পরিকল্পনা রয়েছে; কোনো নির্দিষ্ট প্রতিষ্ঠানের আকার ধরে ডিজাইন করা হয়নি।",
    contactEyebrow:"পরবর্তী ধাপ শুরু হোক এখানেই",contactTitle:"আপনার প্রতিষ্ঠান পরিচালনা হোক আরও সহজ।",contactDesc:"EduOS-এর ফিচার দেখুন, তারপর আপনার প্রতিষ্ঠানের প্রয়োজন ও কাজের ধারা নিয়ে টিমের সঙ্গে আলোচনা করুন।",
    contactDemo:"লাইভ ডেমো খুলুন",contactExplore:"ফিচার দেখুন",contactPending:"ডেমো ও প্রতিষ্ঠানের প্রয়োজন নিয়ে যোগাযোগ করুন।",
    footerDesc:"EduOS — শিক্ষাপ্রতিষ্ঠানের ইন্টেলিজেন্ট অপারেটিং সিস্টেম।",allRights:"সর্বস্বত্ব সংরক্ষিত।",madeForEducation:"শিক্ষার জন্য তৈরি, পরিবর্তনের জন্য প্রস্তুত।",
    dialogTitle:"EduOS ডেমো",dialogText:"ডেমো গ্যালারি দেখুন অথবা ইমেইল ও WhatsApp-এ যোগাযোগ করুন।",dialogOk:"বুঝেছি"
  };

  const en = {
    navFeatures:"Features",navInstitutions:"Institutions",navScreenshots:"Screenshots",navHow:"How it works",navContact:"Contact",demoLogin:"Demo Login",
    eyebrow:"AI-ASSISTED EDUCATION MANAGEMENT",heroTitle:'EduOS — The Intelligent<span class="accent-text"> Education Operating System</span>',
    heroDesc:"One unified platform for Schools, Colleges, Universities & Training Institutes. Manage Admissions, Academics, Attendance, Exams, Finance, HR, and Learning with smart automation and AI-assisted tools.",
    explorePlatform:"Explore all modules",tryDemo:"View demo previews",heroCheck1:"Flexible for different institutions",heroCheck2:"English & বাংলা interface",
    floatReady:"Designed around real workflows",floatUnified:"One connected workspace",illustrationDisclaimer:"EduOS Workspace Overview",
    benefit1:"One connected platform",benefit2:"Flexible workflows",benefit3:"Role-based access",benefit4:"Clear reports & records",
    featuresEyebrow:"MADE FOR THE WHOLE INSTITUTION",featuresTitle:"Your operations, in one place.",featuresDesc:"From a student's first inquiry to everyday learning and administration, EduOS brings essential workflows closer together.",
    admissionTitle:"Admissions & Students",admissionText:"Manage admission journeys, student profiles, enrollment details and ongoing records.",feature01:"01 / STUDENT EXPERIENCE",
    academicTitle:"Academics & Examinations",academicText:"Organize academic structures, courses, examinations, results and related records.",feature02:"02 / ACADEMICS",
    financeTitle:"Fees & Finance",financeText:"Coordinate fee structures, student billing, receipts and institutional accounting.",feature03:"03 / FINANCE",
    hrTitle:"HR & Payroll",hrText:"Keep employee information, payroll processes and administrative records organized.",feature04:"04 / PEOPLE OPERATIONS",
    lmsTitle:"Learning & Library",lmsText:"Bring learning resources, course management and library operations closer to academic life.",feature05:"05 / LEARNING",
    campusTitle:"Hostel, Transport & Inventory",campusText:"Support the practical services and resources that keep a campus running.",feature06:"06 / CAMPUS SERVICES",
    availabilityNote:"Choose the modules and settings that fit your institution.",
    institutionsEyebrow:"ONE PLATFORM. MANY LEARNING ENVIRONMENTS.",institutionsTitle:"Built to adapt to how you teach.",institutionsDesc:"Different institutions have different terms, stages, structures and daily needs. EduOS is designed with configurable workflows rather than a one-size-fits-all institution template.",institutionsLink:"Talk about your institution",
    school:"Schools",college:"Colleges",university:"Universities",madrasa:"Madrasas",training:"Training Centres",coaching:"Coaching & Online Learning",
    showcaseEyebrow:"SEE WHAT YOUR TEAM WILL USE",showcaseTitle:"A closer look at EduOS.",showcaseDesc:"Explore EduOS dashboard, students, fees, academics and reporting workspaces.",
    shotDashboard:"Dashboard",shotStudents:"Students",shotFees:"Fees & Billing",shotAcademics:"Academics",shotReports:"Reports",
    screenshotPendingTitle:"EduOS Screen Gallery",screenshotPendingText:"Explore dashboard, students, billing, academics and reporting screens.",
    screenshotCaption:"Screen previews use example data.",
    workflowEyebrow:"A SIMPLER DAY, STEP BY STEP",workflowTitle:"Less switching. More clarity.",workflowDesc:"A connected way to organize the key moments of institutional work.",
    step1Title:"Set up your institution",step1Text:"Configure the academic structure, campuses, roles and relevant modules.",
    step2Title:"Manage people & learning",step2Text:"Keep students, staff, courses and academic activity organized.",
    step3Title:"Run everyday operations",step3Text:"Work with fees, payments and the services your institution uses.",
    step4Title:"Understand the full picture",step4Text:"Review connected records and reports to guide decisions.",
    faqEyebrow:"GOOD QUESTIONS, CLEAR ANSWERS",faqTitle:"Before you get started.",faqIntro:"Your institution is unique. Here are a few useful things to know before requesting a walkthrough.",
    faq1q:"Is EduOS only for schools?",faq1a:"No. EduOS is designed for multiple education environments, including colleges, universities, madrasas, coaching and training centres. Configuration varies by institution.",
    faq2q:"Can the interface be used in Bangla?",faq2a:"EduOS provides English and Bangla experiences with institution-focused modules.",
    faq3q:"Can I see the software before choosing it?",faq3a:"Yes, a live demonstration link can be provided when the demo environment is published. The button on this site will link directly to that environment once configured.",
    faq4q:"Will every institution use the same settings?",faq4a:"No. EduOS is designed for configurable academic terminology, structure, modules and workflows rather than a fixed institution size or setup.",
    contactEyebrow:"YOUR NEXT CHAPTER STARTS HERE",contactTitle:"Let's make your institution easier to run.",contactDesc:"Explore EduOS at your own pace, then connect with the team to discuss your institution's workflows and goals.",
    contactDemo:"Explore Demo & Contact",contactExplore:"Explore Features",contactPending:"Contact us for an EduOS walkthrough.",
    footerDesc:"The Intelligent Education Operating System for every learning institution.",allRights:"All rights reserved.",madeForEducation:"Made for education, built for flexibility.",
    dialogTitle:"EduOS Demo",dialogText:"Explore EduOS screens and contact the team by email or WhatsApp.",dialogOk:"Got it"
  };

  const translations = { en, bn };
  const languageToggle = document.getElementById("language-toggle");
  let language = "en"; // Homepage is static English; /bn/ is its indexed Bengali counterpart.

  function renderLanguage() {
    document.documentElement.lang = language;
    document.querySelectorAll("[data-i18n]").forEach((el) => {
      const translated = translations[language][el.dataset.i18n];
      if (translated !== undefined) el.textContent = translated;
    });
    document.querySelectorAll("[data-i18n-html]").forEach((el) => {
      const translated = translations[language][el.dataset.i18nHtml];
      if (translated !== undefined) el.innerHTML = translated; // Fixed authored strings only.
    });
    document.getElementById("language-label").textContent = language === "en" ? "বাংলা" : "English";
    document.title = language === "bn"
      ? "EduOS — শিক্ষাপ্রতিষ্ঠানের ইন্টেলিজেন্ট অপারেটিং সিস্টেম"
      : "EduOS | The Intelligent Education Operating System";
    document.querySelectorAll("[data-shot]").forEach((tab) => {
      if (tab.getAttribute("aria-selected") === "true") document.getElementById("screenshot-heading").textContent = tab.textContent;
    });
  }
  languageToggle.addEventListener("click", () => window.location.assign("/bn/"));
  renderLanguage();

  const menuButton = document.getElementById("menu-toggle");
  const navigation = document.getElementById("primary-nav");
  menuButton.addEventListener("click", () => {
    const open = navigation.classList.toggle("open");
    menuButton.setAttribute("aria-expanded", String(open));
    menuButton.setAttribute("aria-label", open ? "Close navigation" : "Open navigation");
  });
  navigation.querySelectorAll("a").forEach((link) => link.addEventListener("click", () => {
    navigation.classList.remove("open");
    menuButton.setAttribute("aria-expanded", "false");
  }));

  const tabs = Array.from(document.querySelectorAll("[data-shot]"));
  const screenshotImage = document.getElementById("screenshot-image");
  const placeholder = document.getElementById("screenshot-placeholder");
  let currentRequest = 0;
  function chooseScreenshot(tab) {
    tabs.forEach((candidate) => {
      const selected = candidate === tab;
      candidate.setAttribute("aria-selected", String(selected));
      candidate.tabIndex = selected ? 0 : -1;
    });
    document.getElementById("screenshot-heading").textContent = tab.textContent;
    const request = ++currentRequest;
    const source = `/assets/screenshots/${tab.dataset.shot}.webp`;
    screenshotImage.hidden = true;
    placeholder.hidden = false;
    const candidate = new Image();
    candidate.onload = () => {
      if (request !== currentRequest) return;
      screenshotImage.src = candidate.src;
      screenshotImage.alt = candidate.src.endsWith(".webp") ? `${tab.textContent} — verified application screenshot` : `${tab.textContent} — illustrative demo design, not a real screenshot`;
      screenshotImage.hidden = false;
      placeholder.hidden = true;
    };
    candidate.onerror = () => { if (request !== currentRequest) return; if (candidate.src.endsWith(".webp")) candidate.src = `/assets/screenshots/${tab.dataset.shot}.svg`; else { screenshotImage.hidden = true; placeholder.hidden = false; } };
    candidate.src = source;
  }
  tabs.forEach((tab, index) => {
    tab.addEventListener("click", () => chooseScreenshot(tab));
    tab.addEventListener("keydown", (event) => {
      if (!["ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
      event.preventDefault();
      const next = event.key === "Home" ? 0 : event.key === "End" ? tabs.length - 1 : (index + (event.key === "ArrowRight" ? 1 : tabs.length - 1)) % tabs.length;
      tabs[next].focus();
      chooseScreenshot(tabs[next]);
    });
  });
  chooseScreenshot(tabs[0]);

  const config = window.EDUOS_MARKETING || {};
  document.querySelectorAll("[data-demo]").forEach(button => button.addEventListener("click", () => window.location.assign("/demo/")));

  const channels = document.getElementById("contact-channels");
  function contactLink(text, target, label) {
    const a = document.createElement("a");
    a.href = target;
    a.textContent = text;
    a.setAttribute("aria-label", label);
    if (target.startsWith("https:")) { a.target = "_blank"; a.rel = "noopener noreferrer"; }
    channels.append(a);
  }
  const email = String(config.contactEmail || "").trim();
  if (/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) contactLink(`✉ ${email}`, `mailto:${email}`, "Email EduOS sales");
  const phone = String(config.contactPhone || "").trim();
  if (/^\+?[0-9\s()-]{8,22}$/.test(phone)) contactLink(`☎ ${phone}`, `tel:${phone.replace(/[^0-9+]/g, "")}`, "Call EduOS sales");
  const whatsapp = String(config.whatsappNumber || "").replace(/\D/g, "");
  if (whatsapp.length >= 10 && whatsapp.length <= 15) contactLink("◉ WhatsApp", `https://wa.me/${whatsapp}`, "Chat with EduOS sales on WhatsApp");
  if (channels.children.length) document.getElementById("contact-unavailable").hidden = true;

  document.getElementById("year").textContent = String(new Date().getFullYear());
})();
