**EduOS**

**Complete Institution User Manual**

**Signup থেকে Academic Year-End পর্যন্ত পূর্ণ Entry Sequence**

বাংলাদেশের একটি সমন্বিত স্কুল ও কলেজের জন্য

**Class 1–10 (SSC) + Class 11–12 (HSC)**

ভাষা: বাংলা (প্রয়োজনীয় English Menu/Field নামসহ)

সংস্করণ 1.0 • ১৩ নভেম্বর ২০২৬ • কোড রেফারেন্স: EduOS / Ashraful branch

গুরুত্বপূর্ণ \| এটি একটি কাজভিত্তিক Operational Manual। কোডে নিশ্চিত করা স্ক্রিনগুলোর Route উল্লেখ করা হয়েছে; অন্যান্য মডিউলে কার্যক্রমের নাম দিয়ে নির্দেশনা দেওয়া হয়েছে। কোনো Workflow/Permission শুধু ডকুমেন্টে লেখা আছে বলে Production-এ সম্পূর্ণ পরীক্ষিত ধরে নেবেন না।

# ১. এই Manual কীভাবে ব্যবহার করবেন

একটি প্রতিষ্ঠানের নতুন Tenant Admin / Principal / Admission Officer / Accountant / Teacher যেন একই নির্দেশনা অনুসরণ করে কাজ শুরু করতে পারেন—এটাই লক্ষ্য। পুরো বই শুরু থেকে শেষ পর্যন্ত অনুসরণ করলে Setup → Student Entry → Teaching → Fees → Exam → Result → Year-End সম্পন্ন করার সঠিক ক্রম পাওয়া যাবে।

**প্রথম কাজ** পৃষ্ঠা ২-এর “মাস্টার সিকোয়েন্স” মেনে প্রথমবারের Setup শেষ করুন।

**প্রতিদিনের কাজ** “Daily/Monthly Work” অংশ অনুসরণ করুন।

**ভর্তি মৌসুমে** Admission → Student → Enrollment → Section/Roll → Fee Creation ক্রমে যান।

**বার্ষিক সমাপ্তিতে** Result Finalization → Certificates → Next Year Setup → Promotion / New Admission ক্রমে যান।

গুরুত্বপূর্ণ \| একই Editable Fact দুই জায়গায় লিখবেন না। Smart Setup শুধু প্রথমবারের Safe Master Data প্রস্তুত করে; পরবর্তীতে মূল Settings/Operations পেজই সম্পাদনার জায়গা।

## কার জন্য কোন অংশ

| **ব্যবহারকারী**                       | **Manual-এ প্রাসঙ্গিক অংশ** | **মূল দায়িত্ব**                                           |
|--------------------------------------|---------------------------|---------------------------------------------------------|
| Tenant Admin / প্রতিষ্ঠানের অ্যাডমিন    | অধ্যায় ২–৮, ১৫–১৯          | সাবস্ক্রিপশন, ক্যাম্পাস, একাডেমিক, Role, Config             |
| Principal / Academic Admin           | অধ্যায় ৬–৭, ১০–১৪, ১৬      | শিক্ষা, রুটিন, পরীক্ষা, অনুমোদন                             |
| Admission Officer                    | অধ্যায় ৮–৯                 | ফরম, আবেদন, যাচাই, ভর্তি ও Student Entry                 |
| Accountant / Cashier                 | অধ্যায় ১১–১২, ১৭           | Fee Structure, Bill, Collection, Ledger, Reconciliation |
| Teacher / Exam Controller            | অধ্যায় ১০, ১৩–১৪           | Attendance, Marks Entry, Review, Result                 |
| Library/Hostel/Transport/HR Operator | অধ্যায় ১৫                  | শুধু চালু করা মডিউলের অপারেশন                              |

# ২. এক নজরে: ১৭ ধাপের Master Entry Sequence

| **ক্রম** | **কাজ**                              | **কী সংরক্ষণ হবে**                                       | **পরের ধাপ** |
|---------|--------------------------------------|---------------------------------------------------------|--------------|
| 01      | Signup / Email Verification          | অ্যাকাউন্ট ও প্রতিষ্ঠানের Email যাচাই                       | 02           |
| 02      | Institution Profile + Plan + Payment | প্রতিষ্ঠান, প্ল্যান, পেমেন্ট ও Activation                    | 03           |
| 03      | Campus + Onboarding Essentials       | শাখা/ক্যাম্পাস, প্রাথমিক Academic Year, Module             | 04           |
| 04      | Smart Institution Setup              | Safe Reference Defaults Review + Apply                  | 05           |
| 05      | Academic Masters                     | Year → Terms → Programmes → Classes → Sections          | 06           |
| 06      | Academic Content                     | Subjects → Curricula → Group/Offering → Routine         | 07           |
| 07      | Access & Staff                       | Roles → Employees/Teachers → Class & Subject Assignment | 08           |
| 08      | Finance Setup                        | COA → Period → Cash/Bank → Fee Heads/Structure          | 09           |
| 09      | Admission Setup                      | Admission Form → Dates/Eligibility → Publish            | 10           |
| 10      | Student Intake                       | Application → Review → Offer → Student/Guardian         | 11           |
| 11      | Enrollment                           | Campus/Class/Year/Section/Group/Roll Confirmation       | 12           |
| 12      | Student Billing                      | Eligible Bills → Receipts → Due & GL Reconciliation     | 13           |
| 13      | Daily Academic                       | Attendance → Lessons/LMS → Notice & Records             | 14           |
| 14      | Examination                          | Grade/Exam Setup → Marks → Review → Result Publish      | 15           |
| 15      | Reports & Certificates               | Attendance/Fee/Academic/Finance/SSC-HSC Records         | 16           |
| 16      | Year-End                             | Close/Review → Next Year → Promotion/Exit/Transfer      | 17           |
| 17      | Renewal & Operations                 | Renewal, backup/audit, yearly cycle restart             | পরের বছর     |

গুরুত্বপূর্ণ \| সব প্রতিষ্ঠানকে সব Module চালু করতে হবে না। যেগুলো ব্যবহার করবেন না, সেগুলো Setup করবেন না। Admission/Student/Finance/Exam-এর Source Record অনুমোদন ছাড়া পুনরায় তৈরি করবেন না।

# ৩. নতুন প্রতিষ্ঠান Signup থেকে Dashboard

## ৩.১ Account ও Subscription (আসল স্ক্রিনের ক্রম)

**Step 01 — Signup \| /Account/Signup** প্রতিষ্ঠানের প্রতিনিধির নাম, বৈধ Email, Password এবং প্রয়োজনীয় Registration তথ্য দিন। বিদ্যমান প্রতিষ্ঠান থাকলে নতুন Duplicate Tenant খুলবেন না।

**Step 02 — Verify Email \| /Account/VerifyEmail** Email-এ প্রাপ্ত Verification Link/Code দিয়ে Email Verify করুন। Code ভুল/মেয়াদোত্তীর্ণ হলে Resend ও সংশ্লিষ্ট Message দেখুন।

**Step 03 — Institution Profile \| /Account/InstitutionProfile** Institution Name, Type, Contact, Address, Country/Timezone, Registration/EIIN (প্রযোজ্য হলে) এবং প্রাথমিক তথ্য নিশ্চিত করুন। School + College চালালে উপযুক্ত Combined/Hybrid Type ও পরিসর নির্বাচন করুন; শুধু Type দেখে Actual Class/Subject auto-create হয়েছে ধরে নেবেন না।

**Step 04 — Select Plan \| /Account/PlanSelection** School + Higher Secondary-এর প্রয়োজনীয় Module/Capacity অন্তর্ভুক্ত আছে কি না দেখুন। Plan Price, Limit ও Billing Period স্ক্রিনে যেটি কার্যকর দেখাবে সেটিই গ্রহণ করুন।

**Step 05 — Payment \| /Account/Payment** Invoice/Payment Reference মিলিয়ে যথাযথ Channel-এ Payment Submit করুন। একই Transaction বারবার নতুন করে Save করবেন না।

**Step 06 — Payment Verification** পেমেন্টের Status Pending থাকলে Verification/Approval পর্যন্ত Transaction ID সংরক্ষণ করুন। Verification সাধারণত SuperAdmin-এ হয় (/Account/PaymentVerification); Tenant Admin নিজে Approve করবেন না।

**Step 07 — Campus Setup \| /Account/CampusSetup** প্রধান ক্যাম্পাস/শাখা তৈরি করুন। একই ক্যাম্পাসে Class 1–12 চললে এক Campus-ই যথেষ্ট; আলাদা বাস্তব লোকেশন/অপারেশন হলে পৃথক Campus।

**Step 08 — Academic Essentials \| /Account/AcademicSetup** প্রাথমিক শিক্ষাবর্ষ/শিক্ষাবর্ষের তারিখ দিন। পরে মূল Academic Management-এ পূর্ণ Setup হবে।

**Step 09 — Modules \| /Account/ModuleSetup** শুধু চুক্তিভুক্ত/প্রয়োজনীয় মডিউল Enable করুন।

**Step 10 — Branding → General → Gateway** /Account/BrandingSetup → /Account/GeneralSettings → /Account/GatewaySetup: Logo/Theme, সাধারণ/যোগাযোগ সেটিং ও অনুমোদিত Payment/SMS Gateway Config করুন।

**Step 11 — Finish \| /Account/OnboardingComplete** Activation/Access নিশ্চিত করে Dashboard ও Smart Setup খুলুন। গুরুত্বপূর্ণ Credentials প্রতিষ্ঠাননির্ধারিত নিরাপদ ব্যবস্থায় সংরক্ষণ করুন।

গুরুত্বপূর্ণ \| Payment Approved হওয়া আর সব Optional Setup শেষ হওয়া এক নয়। Activation হয়ে গেলে অনুমোদিত Menu-তে প্রবেশের কথা; কোনো Version-এ অনাবশ্যক ধাপ আটকে দিলে এটিকে Onboarding Bug হিসেবে যাচাই করুন, পেমেন্ট আবার করবেন না।

## ৩.২ Login-এর পর প্রথম Security Check

1.  Tenant/Admin account-এর Institution Name, Campus Scope ও Plan Status সঠিক আছে কি না পরীক্ষা করুন।

2.  Role Management-এ Principal, Admission Officer, Accountant, Teacher আলাদা Role/Permission দিন; সবার জন্য SuperAdmin ব্যবহার করবেন না।

3.  BN/EN Language, Date/Time Zone (Asia/Dhaka), Academic Year ও Main Campus নির্ধারণ করুন।

4.  অন্য প্রতিষ্ঠানের Student/Data দেখা যাচ্ছে কি না যাচাই করুন; এমন কিছু হলে কাজ বন্ধ করে Admin-কে জানান।

# ৪. Smart Institution Setup: কী Auto হবে, কী Manual হবে

Verified setup hub: /SmartSetup/Index. এটি প্রথমবারের Reference Data তৈরি করতে সাহায্য করে; Academic/Finance/Library-এর আলাদা মূল Settings পেজগুলোই Authoritative।

**Step 12 — Preview** Smart Institution Setup খুলে Institution Type অনুযায়ী প্রস্তুত Reference Record দেখুন।

**Step 13 — Select** যে Seed Missing এবং আপনার প্রতিষ্ঠানের উপযোগী শুধু সেগুলো Tick করুন; Active Existing Data-এর Duplicate করবেন না।

**Step 14 — Confirm & Save** Save Checked Records করার আগে Module, Code/Name ও Version পরীক্ষা করুন।

**Step 15 — Readiness** যে জায়গায় “Add here” আছে সেখানে সংশ্লিষ্ট আসল Settings Form খুলে বাস্তব Data Save করুন।

**Step 16 — Review** Academic Year, Exam Grade, Subject, Fee, HR, Finance, Library ও Hostel/Transport Readiness দেখে Missing সেটিং পূরণ করুন।

| **Smart Setup থেকে Safe Defaults**      | **মূল পেজে User-কে নিজে Entry দিতে হবে**                |
|-----------------------------------------|--------------------------------------------------------|
| Medium, Subject Name (Reference)        | Year, Class, Class Group, Curriculum, Subject Offering |
| Fee Head Name                           | Actual Amount, Discount Policy, Invoice, Receipt       |
| Grade Scheme Template (GPA 5.00 ইত্যাদি) | কোন Class/Exam-এ কোন Scheme কার্যকর, Marks & Result     |
| Basic COA Labels                        | Bank Account, Opening Balance, Journal Post            |
| HR Designation, Payroll Component Name  | Real Employee, Salary, Leave Balance, Payroll          |
| Book/Asset/Document Category            | Actual Book, Copy/Stock, Document, Room/Bed/Vehicle    |

গুরুত্বপূর্ণ \| Smart Setup “10–12 টি করে সব Table-এ Fake Data” তৈরি করবে না। Safe Defaults কতটি হবে তা বাস্তব Catalogue ও Institution Type নির্ধারণ করে। অর্থ, শিক্ষার্থী, শিক্ষক, ফলাফল, Seat, Room/Bed—এসব কখনো অনুমান করে Save করবেন না।

# ৫. School + College-এর Academic Structure ঠিক করা

## ৫.১ Recommended hierarchy (একই Tenant, এক বা একাধিক Campus)

Institution/Tenant → Campus → Academic Year → Programme/Level → Class → Section; Curriculum ও Subject Offering সেই Class/Year/Group-এর সাথে প্রাসঙ্গিকভাবে যুক্ত থাকবে। Batch থাকলে সেটি Intake/Cohort/Session প্রয়োজন অনুযায়ী আলাদা; Section-এর Parent নয়।

| **স্তর**             | **Demo Name**                           | **ব্যবহার**                                    |
|---------------------|-----------------------------------------|-----------------------------------------------|
| Institution         | Demo name institute                     | এক প্রতিষ্ঠান; হিসাব ও Data Ownership এক Tenant |
| Campus              | Main Campus                             | এক বাস্তব Site হলে এক Campus                   |
| Programme 1         | School Programme                        | Class One থেকে Class Ten                      |
| Programme 2         | Higher Secondary Programme              | Class Eleven ও Class Twelve                   |
| Academic Year       | 2027                                    | ক্যালেন্ডার/টার্ম/ফি/পরীক্ষার Year Scope          |
| Class               | Class One … Class Twelve                | শ্রেণি/গ্রেড; School ও College ভাগে যুক্ত         |
| Section             | A, B, C (Class One), A/B (Class Eleven) | Class + Campus ভিত্তিক Parallel Teaching Group |
| Batch (Optional)    | HSC Intake 2027 / Cohort 2027–29        | যখন সত্যিকারের Intake/Cohort tracking দরকার    |
| Group/Track         | Science, Business Studies, Humanities   | Class IX–X এবং XI–XII-এ প্রযোজ্য হলে            |
| Curriculum/Offering | Board/Curriculum + Subjects             | সংশ্লিষ্ট Year, Class ও Group অনুযায়ী            |

## ৫.২ Academic Settings-এ কোন ক্রমে Entry

**Step 17 — Year \| /AcademicManagement/Years** 2027-এর মতো Academic Year Name, Start Date, End Date, Campus Scope ও Status দিন। একই Year বারবার খুলবেন না।

**Step 18 — Term \| /AcademicManagement/Terms** প্রযোজ্য Semester/Term/Examination Period নির্ধারণ করুন। School-এর জন্য Term 1, Half-yearly, Annual—প্রতিষ্ঠানের বাস্তব নিয়ম অনুযায়ী।

**Step 19 — Programme/Department** School এবং Higher Secondary-এর academic track/programme প্রয়োজন অনুযায়ী সেট করুন। Department/Faculty শুধু বাস্তব ব্যবহার থাকলে ব্যবহার করুন।

**Step 20 — Classes \| /AcademicManagement/Classes** School: One–Ten (১০টি), College: Eleven–Twelve (২টি)। Class Code/Order/Programme সঠিকভাবে নির্ধারণ করুন।

**Step 21 — Sections \| /AcademicManagement/Sections** প্রতিটি Campus ও Class-এর জন্য A/B/C শাখা তৈরি করুন; Code, Capacity ও Status দিন।

**Step 22 — Batches \| /AcademicManagement/Batches** শুধু Cohort/Session ভিত্তিক Tracking দরকার হলে Batch তৈরি করুন; Class One-এর জন্য Batch না থাকলেও Class One → A/B/C Section তৈরি হতে পারবে।

**Step 23 — Rooms \| /AcademicManagement/Rooms** বাস্তব কক্ষ/Capacity ও Campus Mapping দিন; শুধু নাম লিখে বড় Facility Inventory অনুমান করবেন না।

**Step 24 — Subjects \| /AcademicManagement/Subjects** Safe Template-এ থাকা Subject যাচাই/সংশোধন করুন; প্রকৃত পাঠ্যক্রম অনুযায়ী Subject Code, Paper, Compulsory/Optional ঠিক করুন।

**Step 25 — Curricula \| /AcademicManagement/Curricula** কোন Year/Programme/Class-এ কোন কারিকুলাম কার্যকর তা নির্ধারণ করুন।

**Step 26 — Offerings \| /AcademicManagement/Offerings** Class+Year+Group/Track অনুযায়ী Subject Offering/Teacher Allocation নিশ্চিত করুন।

**Step 27 — Routine \| /AcademicRoutines/Index** Class Section, Room, Day, Period ও Teacher অনুযায়ী ক্লাস রুটিন তৈরি করুন; একই সময়ে Teacher/Room conflict হবে না।

**Step 28 — Holidays/Calendar** সরকারি Holiday Template ব্যবহার করলে নিজ প্রতিষ্ঠানের Calendar-এ প্রযোজ্য দিন Review করে Save করুন; সরকার ঘোষিত সংশোধিত ছুটি যাচাই করে নিন।

## ৫.৩ Academic Section এবং Academic Batch—একে অপরের বিকল্প নয়

| **Academic Section**                                              | **Academic Batch**                                         |
|-------------------------------------------------------------------|------------------------------------------------------------|
| Class One → Section A/B/C; Class Eleven → A/B                     | HSC 2027 Intake / নির্দিষ্ট Cohort                           |
| Campus + Class ভিত্তিক; Batch ছাড়াই কাজ করবে                       | Year/Programme/Class-সংশ্লিষ্ট অতিরিক্ত Cohort grouping       |
| উদ্দেশ্য: Section-wise Roll, Teacher, Attendance, Routine           | উদ্দেশ্য: Intake/Cohort-wise Tracking, Capacity বা Reporting |
| প্রতি বছর নতুন Section লাগবে কি না নির্ভর করে Structure Reuse-এর ওপর | সব ক্লাসে বাধ্যতামূলক নয়                                      |

গুরুত্বপূর্ণ \| Code-এ দুইটি আলাদা Screen আছে। আগে Batches View-এর JavaScript-এ “sections” নাম থাকার ফলে বিভ্রান্তি ছিল; User Manual-এর নিয়ম অনুসারে Section কখনো Batch-এর Child নয়। প্রকৃত ব্যবহারে Class-এর অধীনে Section Select হওয়া উচিত।

# ৬. Class 1–12: পূর্ণ Academic Example

উদাহরণ: “Demo name institute”, Main Campus, Academic Year 2027। নিচের Section সংখ্যা শুধুই উদাহরণ—আসল অনুমোদিত Capacity/Student Count অনুযায়ী দিন।

| **Class**   | **Programme**    | **Section (উদাহরণ)** | **Group/Track**                         |
|-------------|------------------|----------------------|-----------------------------------------|
| 1 — One     | School           | A, B, C              | General                                 |
| 2 — Two     | School           | A, B, C              | General                                 |
| 3 — Three   | School           | A, B, C              | General                                 |
| 4 — Four    | School           | A, B, C              | General                                 |
| 5 — Five    | School           | A, B, C              | General                                 |
| 6 — Six     | School           | A, B                 | General                                 |
| 7 — Seven   | School           | A, B                 | General                                 |
| 8 — Eight   | School           | A, B                 | General                                 |
| 9 — Nine    | School           | A, B                 | Science / Business Studies / Humanities |
| 10 — Ten    | School           | A, B                 | Science / Business Studies / Humanities |
| 11 — Eleven | Higher Secondary | A, B                 | Science / Business Studies / Humanities |
| 12 — Twelve | Higher Secondary | A, B                 | Science / Business Studies / Humanities |

## ৬.১ Class 1–8: সাধারণ নিয়ম

প্রতি Class-এর Section আলাদা হবে; একটি Section-এ N জন শিক্ষার্থী থাকতে পারে। Student-এর এক Academic Year-এ একটি Primary Active Enrollment, একটি অনুমোদিত Class এবং একটি Section থাকবে। Subject Offering প্রযোজ্য Curriculum থেকে নির্ধারিত হবে।

## ৬.২ Class 9–10: SSC Stream

Class IX/X-এ Science, Business Studies ও Humanities (যেখানে প্রতিষ্ঠান/Board অনুমোদন দেয়) একটি বাস্তব Group/Track হিসেবে থাকবে। Class Nine পুনরায় “Nine Science”, “Nine Business” নামে ৩টি আলাদা Master Class বানানো বাধ্যতামূলক নয়। Group-aware Subject Offering, Optional Subject এবং Section Timetable আলাদাভাবে নির্ধারণ করুন। SSC Registration/Elective Subject/Board Number প্রতিষ্ঠান ও বোর্ডের প্রকৃত নিয়ম অনুযায়ী রেকর্ড করুন; EduOS-এর Student ID বোর্ডের Registration Number নয়।

## ৬.৩ Class 11–12: College / HSC

Higher Secondary Programme-এর Class Eleven ও Class Twelve সেট করুন। HSC 1st/2nd Year, Group/Track, 1st/2nd Paper, Optional/Fourth Subject এবং উপযুক্ত Admission Session বাস্তব Curriculum অনুযায়ী Set করুন। XI ভর্তি শুধুমাত্র SSC পাশ করে বা অনুমোদিত যোগ্যতায় নতুন Admission/Enrollment-এর মাধ্যমে হবে। Class Ten থেকে Eleven-এ Automatic Promotion করবেন না।

## ৬.৪ তিনটি মূল Example

| **Scenario**                | **সঠিক Data Entry**                                                                                                                  |
|-----------------------------|--------------------------------------------------------------------------------------------------------------------------------------|
| Class One: 90 জন            | Class One = 1; Section A/B/C = 3; প্রত্যেক Section-এর বাস্তব Capacity (যেমন 30); Batch = Optional/None                                  |
| Class Nine: 100 জন, ৩ Group | Class Nine একবার; Section A/B/... Capacity; Science/Business/Humanities Group ও Subject Offering আলাদা                               |
| Class Eleven: 75 জন         | Higher Secondary Programme; Class Eleven; A/B Section; নতুন XI Admission; Year/Group/Optional Subject; HSC Intake Batch শুধু প্রয়োজন হলে |

# ৭. Staff, Role, Teacher ও Timetable Setup

**Step 29 — User/Role Setup** Principal, Vice Principal, Admission Officer, Class Teacher, Exam Controller, Accountant, Librarian ইত্যাদি Role/Permission দিন। Employee আর Login User সবসময় একই Entity নয়—প্রয়োজন অনুযায়ী Link করুন।

**Step 30 — HR Master** Designation, Department, Shift, Employment Type, Holiday/Leave Type আসল সেটিং অনুযায়ী ঠিক করুন।

**Step 31 — Employee/Teacher Entry** Name, Employee Code, Joining Date, Campus, Designation, Contact ও অনুমোদিত পরিচয়-নথি দিন।

**Step 32 — Subject/Class Assignment** Class+Section/Subject ভিত্তিক Teacher Assignment দিন; একই Teacher-এর Routine Conflict যাচাই করুন।

**Step 33 — Calendar/Routine Publish** Holiday/Examination Schedule মেনে Class Routine যাচাই করে Publish করুন।

**Step 34 — Login Check** Teacher শুধু নিজের Assigned Section/Subject/Marks দেখতে ও সম্পাদনা করতে পারছেন কি না পরীক্ষা করুন।

# ৮. Admission Season শুরু করার আগে

**Step 35 — Publishable Admission Form \| /Admissions/Forms** Academic Year, Applicable Campus, Programme/Class (যেমন One / Six / Nine / Eleven), Application Period, Seat, Eligibility, Required Docs এবং প্রযোজ্য Form Fee নিশ্চিত করুন।

**Step 36 — Admission Rules** যে Class-এ Entrance/Assessment প্রয়োজন নেই সেটি জোর করে তৈরি করবেন না; থাকলে Assessment Setup, Marks Rule ও Merit Rule নির্ধারণ করুন।

**Step 37 — Fee/Bank Readiness** Admission Fee Collection চালু করলে প্রথমে Finance Setup ও Financial Account Mapping Complete করুন।

**Step 38 — Review & Publish** Form Preview করে শুধু প্রয়োজনীয় তথ্য নিন। Child Identity/Guardian, School/Class, Document & Consent স্পষ্ট রাখুন। তারিখ ঠিক হলে Publish করুন।

গুরুত্বপূর্ণ \| XI Admission-এর জন্য বোর্ড/সরকারি Admission Policy ভিন্ন হতে পারে। বাহ্যিক কেন্দ্রীয় Admission Portal-এ আবেদন ও ফলাফল হলে EduOS-এ কেবল যাচাইকৃত Approved Intake Import/Admission Record তৈরি করুন; EduOS-কে স্বয়ংক্রিয়ভাবে সরকারি Portal ধরা যাবে না।

# ৯. Application → Student → Enrollment: সঠিক ক্রম

**Step 39 — New Application \| /Admissions/NewApplication** New Admission Application খুলে Applicant Name, DOB, Target Class/Group, Guardian, Contact, Previous School, প্রয়োজনীয় নথি ও Consent দিন।

**Step 40 — Application List \| /Admissions/Applications** Submitted আবেদন খুঁজুন; Missing Document, Eligibility, Duplicate Applicant ও Existing Student/Guardian যাচাই করুন।

**Step 41 — Assessment \| /Admissions/AssessmentSetup** Entrance/Interview লাগলে পরীক্ষার Scheme ও Marks Setup করুন; অন্যথায় “Not applicable” রাখুন।

**Step 42 — Merit / Decision \| /Admissions/Merit** যেখানে প্রযোজ্য, Score/Capacity/Quota/Approval অনুযায়ী Merit/Selection করুন।

**Step 43 — Offer \| /Admissions/Offers** Authorized Officer Decision/Offer নিশ্চিত করুন; Reject/Waitlist ইতিহাস মুছবেন না।

**Step 44 — Student Profile \| /Students/Create** Approved Application থেকে Student তৈরি/Link করুন। আবেদন ছাড়াই পুরোনো Data Migration বা Direct Admission হলে Duplicate Check-সহ Create করুন।

**Step 45 — Guardian Link** আগে Parent/Guardian থাকলে Reuse করুন; নতুন Guardian হলে একবার Profile তৈরি করে Student-এর সাথে Relationship দিন।

**Step 46 — Year Enrollment** Academic Year + Campus + Programme + Class + Group/Track + Section + Roll/Student Number (যা প্রযোজ্য) নিশ্চিত করুন।

**Step 47 — Student Verification** Student Details/Student 360°-এ Profile, Guardian, Enrollment History, Fees ও Documents মিলিয়ে নিন।

**Step 48 — Fee Generation** Fee Policy কার্যকর থাকলে অনুমোদিত Structure অনুযায়ী Invoice/Bill তৈরি করুন; একই Admission-এর Duplicate Invoice যেন না হয়।

## ৯.১ Student Entry: Recommended Field Order

| **ফিল্ড গ্রুপ**       | **কী দিবেন**                                           | **Source/Rule**                                |
|--------------------|--------------------------------------------------------|------------------------------------------------|
| Identity           | Name (বাংলা/English), DOB, Gender, Photo, Contact      | Birth Certificate/Admission Record থেকে যাচাই  |
| Guardian           | Parent/Guardian Name, Relation, Contact, Address       | আগে আছে কিনা খুঁজে Link; Duplicate নয়            |
| Academic           | Year, Campus, Class, Section, Group, Roll              | Active Class+Year; Section ঐ Class-এর          |
| Previous Education | Previous Institution, SSC Result (XI-এর জন্য), Transfer | Verification ছাড়া Auto Fill নয়                 |
| Documents          | Birth Cert, Transfer, Exam Doc, Photo (প্রযোজ্য)         | Restricted Access, Retention & Consent         |
| Status             | Applied/Approved/Enrolled/Transferred etc.             | Server-reviewed legal transition; Free Text নয় |

গুরুত্বপূর্ণ \| Admission Application, Student Profile এবং Yearly Enrollment তিনটি আলাদা কাজ। Apply হলেই Student Record/Enrollment তৈরি হয়েছে ধরে নেবেন না। Approved Application থেকে একবারই Create/Link করবেন।

# ১০. প্রতিদিনের Academic Operations

**Step 49 — Student Attendance** Year → Class → Section → Date → Student Roster নির্বাচন করে Present/Absent/Late/Leave দিন। একই দিনে একই Scope-এ Duplicate Save করবেন না।

**Step 50 — Attendance Correction** ভুল হলে অনুমোদিত Correction/Edit Action ব্যবহার করুন; আগের Audit History মুছবেন না।

**Step 51 — Teacher Routine** Assigned Class/Section/Subject অনুযায়ী Period Coverage ও Class Diary/Lesson Plan (যদি Module থাকে) দিন।

**Step 52 — Notice & Communication** Holiday, Exam Schedule, Fee Due ও অন্যান্য Notice Target Audience বেছে Publish করুন। Permission/Consent যাচাই করুন।

**Step 53 — Learning/LMS** Course/Section/Subject Mapping নিশ্চিত করে Lessons, Materials, Quiz/Assignment যোগ করুন; Library-এর একই Official Book-এর Duplicate Soft Copy Owner বানাবেন না।

## Daily Closing Check

- সব চালু Section-এর Attendance Saved? Exceptions List-এ Absent/Late ও Missing Section আছে?

- Today-এর Admission, Fee Collection, Approved Receipt ও Reconciliation Pending আছে?

- Teacher Conflict/Exam Schedule Change/Notice Delivery Fail আছে?

# ১১. Finance Setup: Student Fee-এর আগে যা লাগবে

**Step 54 — Chart of Accounts \| /AccountingSetup/Index** Asset, Liability, Equity, Income, Expense এবং Posting Accounts যাচাই করুন। Safe Seed COA Labels থেকে actual Fee Income/Cash/Bank mapping না দিলে posting চলবে না।

**Step 55 — Accounting Period** Finance Year, Period Start/End, Open Status নির্ধারণ করুন; Closed Period-এ Backdated Posting করবেন না।

**Step 56 — Cash / Bank** প্রতিষ্ঠানের বাস্তব Cash Ledger/Bank Account Authorize করুন; Account Holder/Bank/Branch/Number সংবেদনশীলভাবে যাচাই করুন।

**Step 57 — Opening Balance \| /AccountingJournals/Index** বাস্তব Opening Asset/Liability/Equity Balance থেকে Balanced Journal Draft → Approval → Post করুন; শুধু Save করলে Balance Report Update হয় না।

**Step 58 — Fee Head \| /FeeOperations/Structures** Admission, Tuition, Examination, Session, Transport, Hostel ইত্যাদি আসল Fee Head নির্ধারণ করুন। একই অর্থের Fee Head Duplicate করবেন না।

**Step 59 — Fee Structure** Year/Programme/Class/Group (যা প্রযোজ্য), Effective Date, Amount, Frequency, Scholarship/Discount Rule ও Ledger Mapping নিশ্চিত করুন।

**Step 60 — Fee Billing \| /FeeOperations/Billing** Eligible Enrollment অনুযায়ী Bill Generate/Review করুন; User-selected Period/Class Scope ও Duplicate Check বাধ্যতামূলক।

**Step 61 — Collection \| /FeeOperations/Ledger** Student Outstanding Invoice Select → Payment Method Cash/Bank → Amount/Date/Reference → Save/Confirm → Receipt Check করুন।

**Step 62 — Posting & Reconciliation** Posted Receipt/Invoice vs Student Due vs General Ledger vs Bank/Cash মিলিয়ে দেখুন। ভুল Posted Transaction Edit/Delete না করে অনুমোদিত Reversal/Adjustment ব্যবহার করুন।

## ১১.১ একটি Student Fee Example

Class Six, Academic Year 2027, Tuition 1,500 BDT/Month (শুধুই উদাহরণ): Fee Head=Tuition → Approved Fee Structure=1,500 → Student-এর Jan Bill=1,500 → Cash Collection=1,000 → Outstanding=500। এক Receipt-এ 1,000 Posted হলে Save পুনরায় চাপ দিয়ে আরেকটি Receipt তৈরি করবেন না।

গুরুত্বপূর্ণ \| সকল Amount বাস্তব Institution Rule অনুযায়ী হবে; এখানে উদাহরণ Price কোনো Default নয়। Student Due = Original Approved Bill − Allocated Authorized Payments ± Approved Adjustments; GL Posting ও Student Ledger এক Source Transaction থেকে মিলতে হবে।

# ১২. Examination & Result: Setup থেকে Publication

**Step 63 — Grade Scheme \| /AssessmentAdministration/GradeSchemes** Bangladesh Board GPA 5.00 Template থাকলে applicability যাচাই করে Class/Exam Policy-তে যথাযথভাবে Attach করুন। University CGPA 4.00 Template School/College SSC/HSC-তে ভুলভাবে Attach করবেন না।

**Step 64 — Assessment Setup \| /AssessmentAdministration/Setup** Exam Type, Term, Subject/Section, Components (Written/MCQ/Practical/etc.), Weight, Pass Rule, Schedule ও Approval Owner দিন।

**Step 65 — Schedule/Admit Card** /AssessmentAdministration/AssessmentSchedules → /AssessmentAdministration/AdmitCards: Date, Time, Room, Roll, Conflict ও Eligibility যাচাই করে Publish করুন।

**Step 66 — Marks Entry \| /AssessmentAdministration/Marks** Authorized Teacher/Exam Controller নির্ধারিত Subject/Section/Component-এর Marks দেবেন; Max Mark, Absent, Late Entry Validations পরীক্ষা করুন।

**Step 67 — Marks Review** /AssessmentAdministration/MarkReviews এবং ComponentMarks/AssessmentComponents প্রয়োজনে যাচাই করুন; একবারই Corrected Marks approve করুন।

**Step 68 — Result Review \| /AssessmentAdministration/Results** Class/Subject/Exam Total, Failed/Absent, Grade Mapping, Optional Subject, GPA এবং Exceptions Preview করুন।

**Step 69 — Publication \| /AssessmentAdministration/Publications** Competent Approval/Release-এর পর Publish করুন; ভুল প্রকাশিত ফল Silent Edit নয়—Approved Correction/Rescrutiny ব্যবহার করুন।

**Step 70 — Transcripts/Certificates** /AssessmentAdministration/Transcripts → /AssessmentAdministration/Certificates: অনুমোদিত Template/Issued Records অনুযায়ী Print/Download করুন।

## ১২.১ SSC ও HSC Results—বিশেষ সতর্কতা

EduOS Internal Exam Result ও Education Board-এর Official SSC/HSC Result এক নয়। Internal Class 10 Final Result থেকে Board Pass/Registration/Certificate Number অনুমান করবেন না। Official Verified Board Data এলে পৃথক Evidence/Result/Completion Record সংযুক্ত করুন। একই নীতিতে Class Twelve-এর HSC Completion করুন।

# ১৩. Class 10 SSC → Class 11 HSC: End-to-End Transition

| **ক্রম** | **অপারেশন**                          | **যা নিশ্চিত করবেন**                                                   |
|---------|--------------------------------------|-----------------------------------------------------------------------|
| 1       | Class 10 পরীক্ষা/Attendance/Fee Audit | Pending Marks, Published Result, Dues ও Discipline State যাচাই        |
| 2       | SSC Candidate Record                 | Correct Student Identity, Group/Subjects/Board Evidence যাচাই         |
| 3       | SSC Official Status                  | SSC result/registration কেবল যাচাইকৃত উৎসে Update                      |
| 4       | School Completion / Exit             | Class 10 Alumni/Completed/Exited Status নিয়ন্ত্রিত Workflow-তে          |
| 5       | New XI Admission                     | নতুন Applicant/Admission Decision (যদি একই Student হয়, Identity Reuse) |
| 6       | Class Eleven Enrollment              | Higher Secondary Programme, Group, Subjects, Section, Roll, Year      |
| 7       | New Fee Setup                        | Class XI-এর আলাদা Approved Fee Structure/Discount/Invoice             |
| 8       | College Attendance/Exam              | XI Section Roster, Teacher Assignment, Term/Subjects                  |

গুরুত্বপূর্ণ \| Class 10 → 11 স্বয়ংক্রিয় “Promote” নয়। Education Stage/Admission Rules/Programme বদলাতে পারে। একই Learner/Student Identity Reuse করুন, কিন্তু Historical School Enrollment কখনো মুছে নতুন কলেজ Enrollment বানাবেন না।

# ১৪. Year-End, Promotion, Transfer ও Graduation

**Step 71 — Year-End Review** Last Attendance/Exam/Due/GL Reports export করুন; Pending Marks, Disputed Fees, Exception & Migration Report পর্যালোচনা করুন।

**Step 72 — Next Academic Year** আগের Year-এর Master Data দেখিয়ে Next Year/Terms/Calendar/Fees/Routine Reuse করুন—যেখানে Safe, সেখানে Preview ও Explicit Confirm; Date/Price/Capacity Auto-Guess নয়।

**Step 73 — Promotion Preview** Class One→Two, Two→Three … Nine→Ten; eligible Student, Result, Active Status, Target Class/Section Capacity দেখুন।

**Step 74 — Promotion Approval** Failed/Withheld/Transfer/Dropout Student-কে শর্ত ছাড়া Promote করবেন না। Approve করে New Year Enrollment তৈরি করুন; Old Year History Read-only রাখুন।

**Step 75 — XI→XII** HSC 1st Year→2nd Year Promotion প্রতিষ্ঠানের Academic Policy অনুযায়ী; Group/Subject & Outstanding Review করুন।

**Step 76 — Class X/XII Exit** Class X-এ SSC Completion ও Class XII-এ HSC Graduation/Alumni / Leaving Document প্রয়োজনীয় Official Evidence অনুযায়ী।

**Step 77 — Transfer & Dropout** Official reason/effective date/approval দিয়ে Record করুন; Attendance/Fees/Result Historical Data মুছবেন না।

**Step 78 — Close Financial Period** Posted Receipts, Reversals, Bank Reconciliation ও GL Balance যাচাই করে সঠিক কর্তৃপক্ষ Period Close করবেন।

# ১৫. প্রয়োজন অনুযায়ী বাকি Module কীভাবে চালু করবেন

| **Module**          | **সঠিক Entry Order**                                                                              | **কখন দরকার**                |
|---------------------|---------------------------------------------------------------------------------------------------|------------------------------|
| HR / Payroll        | Designation → Employee → Salary Structure → Leave/Attendance → Payroll Run → Approval/Payment     | Employee Payroll থাকলে       |
| Library             | Categories → Book Master/Edition → Physical/Digital Copy/License → Membership → Issue/Return/Fine | প্রতিষ্ঠানে Library থাকলে      |
| LMS / Learning      | Course/Subject Offering → Module → Lesson/Material → Assignment/Quiz → Grade                      | Online Learning থাকলে        |
| Hostel              | Actual Building/Room/Bed → Rent/Rules → Student Allocation → Charges/Checkout                     | বাস্তব Hostel থাকলে           |
| Transport           | Vehicle/Driver/Route/Stops → Capacity/Fare → Student Allocation → Charge                          | বাস/Transport থাকলে          |
| Inventory/Assets    | Category → Store/Item/Asset → Opening Stock via Approved Movement → Issue/Receive                 | Stock ও Fixed Assets থাকলে   |
| Communication       | Template → Approved Recipient List/Consent → Provider → Send → Delivery/Fails                     | SMS/Email/Notice থাকলে       |
| Document Management | Document Type → Permission → File Upload → Student/Employee Link → Retention                      | Private Official Files থাকলে |

গুরুত্বপূর্ণ \| Official Books-এর এক Authoritative Library Book Record রাখুন। LMS একই বইয়ের File/Reference পুনর্ব্যবহার করবে—/Library/OfficialBooks এবং /Learning/OfficialBooks নামে দুটি Editable Source বানানো সঠিক নয়। Copyright/License অনুযায়ী Digital File ব্যবহার করুন।

# ১৬. Office Operation Calendar: কে কখন কী করবেন

| **সময়**        | **Admission/Academic**                                     | **Accounts/Administration**                                    |
|----------------|------------------------------------------------------------|----------------------------------------------------------------|
| প্রতিদিন        | Attendance, Notice, Teacher Class Log, New Applications    | Receipts, Cash/Bank, Due Review, Pending Approvals             |
| প্রতি সপ্তাহে    | Missing Attendance, Timetable Conflicts, Applicant Backlog | Unpaid Bills, Posting Exceptions, Bank Deposits, Access Review |
| প্রতি মাসে      | Assessment/Result Progress, Parent Communication           | Monthly Billing, Payroll (যদি থাকে), GL & Reconciliation       |
| Term/Exam শেষে | Marks Lock/Review, Publish, Reports, Corrected Cases       | Exam Fee/Arrears, Revenue vs Student Ledger                    |
| শিক্ষাবর্ষ শেষে  | Promotions, Transfers, SSC/HSC Verified Exit               | Year-End Closing, Next-Year Fee Setup, Audit/Backup            |

# ১৭. Reports: কোন রিপোর্টে কোন Data মিলবে

| **রিপোর্ট**                    | **আগে কোন Entry থাকা চাই**                           | **যা Cross-check করবেন**                             |
|-------------------------------|------------------------------------------------------|------------------------------------------------------|
| Student Register / Class-wise | Approved Student + Active Enrollment + Section       | Unique Student, Class, Year, Campus/Section          |
| Attendance Summary            | Class+Section Roster ও Daily Attendance              | Present+Absent+Leave/etc. = Eligible Roster Scope    |
| Admission Funnel              | Published Form → Application → Decision/Offer        | Duplicate/Rejected/Waiting বনাম Actual Admission     |
| Fee Due & Collection          | Fee Structure → Bills → Receipts/Allocations         | Student Due = Bill − Payments ± Adjustments          |
| Income/GL/Trial Balance       | Approved Posted Journal/Receipt/Accounting Period    | Debits=Credits; Cash/Bank & Fee Control reconcile    |
| Exam Results & Grade          | Exam Setup → Marks → Review → Publish                | Subjects, Components, GPA, Absent, Publish Date      |
| SSC/HSC Exits                 | Actual Board Evidence + Completion/Enrollment Status | Internal vs Official Result পৃথক                      |
| Teacher/Payroll               | Employee + Role/Assignment + Authorized Payroll      | Payroll vs Salary Journal, No Double Posting         |
| Inventory/Hostel/Transport    | Actual canonical operational transactions            | Booking/Issue/Charge vs corresponding Finance source |

# ১৮. সাধারণ ভুল ও দ্রুত সমাধান

| **সমস্যা**                                  | **কারণ হতে পারে**                              | **যা করবেন**                                                                        |
|--------------------------------------------|------------------------------------------------|-------------------------------------------------------------------------------------|
| Payment Approved কিন্তু Next/Sidebar খুলছে না | Activation/Onboarding Guard/Session stale      | Transaction Reference দেখে Status Refresh/Login; Duplicate Payment নয়; Admin ticket |
| Section দেখা যায় না                        | Campus/Class ভুল; Section Active নয়             | AcademicManagement/Sections-এ Class+Campus+Status যাচাই                             |
| Batch চাইছে, Class One-এর Batch নেই        | Section/Batch ভুল Coupling অথবা UI Bug          | Batch বানিয়ে workaround নয়; Class-based Section ঠিক করুন                             |
| Student Duplicate                          | Application/Direct Create একাধিকবার            | Existing Learner/Student খুঁজে Link; soft-delete/reconcile policy                     |
| Student Class List-এ নেই                   | Enrollment/Year/Campus/Section ভুল              | Student Profile ও Active Enrollment Cross-check                                     |
| Fee Bill Double                            | একই Period-এ Generate পুনরায়                    | Invoice Search ও Idempotency/Exception Check; duplicate delete নয়                   |
| Receipt Save হয়েছে কিন্তু Due কমেনি          | Receipt Draft/Unposted/Unallocated             | Approved Allocation/Post Status যাচাই                                               |
| Marks Save হয়েছে, Result-এ নেই             | Missing Component/Review/Publication           | Marks → Review → Result Preview → Publish                                           |
| Grade ভুল                                   | Exam/Class-এর জন্য ভুল Scheme/Threshold          | Scheme Effective Policy সংশোধন; published history controlled                        |
| Year-End Promotion-এ Student Missing       | Failed/Withheld/Transfer/Inactive বা Scope     | Promotion Eligibility/Exception Preview দেখুন                                        |
| Report Total Match করছে না                 | Tenant/Year/Campus/Date/Posted Status mismatch | Filter ও Source Transactions reconcile                                              |

# ১৯. একটি পূর্ণ Worked Example (নতুন প্রতিষ্ঠানে প্রথম দিন)

**Demo A — School Class One** Signup/Payment Verified → Campus Main → Year 2027 → School Programme → Class One → Section A/B/C → Subject/Curriculum → Teacher/Routine → Form for Class One → 90 Applications → 90 Approved Student/Guardian Links → 30 করে Section Assignment (কেবল Capacity অনুমোদিত হলে) → Fee Bill → Attendance/Exam।

**Demo B — SSC Class Nine** School Programme → Class Nine → Science/Business/Humanities (প্রযোজ্য) → Group-specific Subject Offering → Section/Routine → 9ম শ্রেণির Student Enrollment → Class IX Attendance → Annual Result → Class X Promotion।

**Demo C — College Class Eleven** Higher Secondary Programme → Class Eleven → Academic Year/Admission Session → Group and Paper-based Offering → XI Application/Official Intake Verification → Student Identity Reuse if Existing → New XI Enrollment/Section → College Fee → XI Attendance → XI Assessment → Class XII Promotion → HSC completion।

# ২০. Final Go-Live Checklist (টিক দিয়ে যাচাই করুন)

☐ Admin Signup + Email Verified + Plan/Payment Activation সঠিক।

☐ Institution Name, Type, Campus, Logo/Contact, Time Zone যাচাই।

☐ Academic Year, Term, School & College Programme এবং Class One–Twelve প্রস্তুত।

☐ Class One-এর Section A/B/C Batch ছাড়াই Create ও Select করা যায়।

☐ Class 9/10 & 11/12 Group/Track/Subject Offering সঠিক।

☐ Smart Setup Safe Defaults Review ও Domain-wise Real Settings Save Complete।

☐ Role/Permission পৃথক; অন্য Tenant/Campus-এর তথ্য দেখা যায় না।

☐ Teacher Assignment/Routine/Holiday ও Classroom Capacity যাচাই।

☐ Admission Form Publish → Application → Review → Approved Student workflow পরীক্ষিত।

☐ Existing Guardian Reuse; Duplicate Student/Enrollment Block/Review পরীক্ষিত।

☐ One Student: correct Academic Year/Class/Section/Group/Roll দেখা যায়।

☐ Fee Head/Structure/Real Amount/Bank/Cash/GL/Period যাচাই।

☐ Bill→Partial/Full Payment→Receipt→Outstanding→GL Reconciliation মিলছে।

☐ Attendance Enter/Edit (Authorized) এবং Class-wise Report মিলছে।

☐ Marks Entry→Review→Approve→Publish→Certificate/Transcript workflow যাচাই।

☐ Class X→XI নতুন Admission, IX→X এবং XI→XII Promotion পৃথক।

☐ Year-End Backup/Restore, Report Scope, Closing, Audit Trail যাচাই।

☐ Mobile/BN-EN/Menu/Read-only Permission এবং Error Recovery যাচাই।

# ২১. Route Reference (যেগুলো কোডে যাচাই করা হয়েছে)

| **Feature**                              | **Current verified route**                                                    |
|------------------------------------------|-------------------------------------------------------------------------------|
| Signup / Login                           | /Account/Signup \| /Account/Login                                             |
| Email / Profile / Plan                   | /Account/VerifyEmail \| /Account/InstitutionProfile \| /Account/PlanSelection |
| Payment / Complete                       | /Account/Payment \| /Account/OnboardingComplete                               |
| Campus / Academic / Modules              | /Account/CampusSetup \| /Account/AcademicSetup \| /Account/ModuleSetup        |
| Branding / General / Gateway             | /Account/BrandingSetup \| /Account/GeneralSettings \| /Account/GatewaySetup   |
| Smart Setup                              | /SmartSetup/Index                                                             |
| Academic Year / Terms / Classes          | /AcademicManagement/Years \| /Terms \| /Classes                               |
| Academic Sections / Batches              | /AcademicManagement/Sections \| /AcademicManagement/Batches                   |
| Rooms / Subjects / Curricula / Offerings | /AcademicManagement/Rooms \| /Subjects \| /Curricula \| /Offerings            |
| Admission                                | /Admissions/Forms \| /NewApplication \| /Applications \| /Merit \| /Offers    |
| Student                                  | /Students/Create \| /Students/Index                                           |
| Fees                                     | /FeeOperations/Structures \| /Billing \| /Ledger                              |
| Academic Routine                         | /AcademicRoutines/Index                                                       |
| Assessment                               | /AssessmentAdministration/Setup \| /Marks \| /Results \| /Publications        |
| Grade / Transcript / Certificate         | /AssessmentAdministration/GradeSchemes \| /Transcripts \| /Certificates       |
| Finance                                  | /AccountingSetup/Index \| /AccountingJournals/Index                           |

Route table-এ “/Terms” বা “/Marks” সংক্ষিপ্ত রূপ পূর্বের Controller Prefix-সহ পড়তে হবে। Menu Names, Enabled Modules, Authorized Roles ও UI Layout Deployment অনুযায়ী পরিবর্তিত হতে পারে।

# ২২. Source, Scope ও Manual Maintenance

এই Manual ১৩ নভেম্বর ২০২৬ তারিখে EduOS GitHub-এর Ashraful Branch-এ যাচাই করা AccountController, AdmissionsController, AcademicManagementController, StudentsController, FeeOperationsController, AssessmentAdministrationController, SectionRegister, Smart Setup implementation এবং UI/UX ও Project Plan-এর তথ্যের ওপর ভিত্তি করে প্রস্তুত।

পূর্বের Project Plan, Smart Setup technical notes এবং per-page Help Guide ছিল; এই ডকুমেন্টে সেগুলোকে প্রথমবারের জন্য একটি end-to-end Institution Operator sequence-এ একত্র করা হয়েছে। পূর্বের Repo Files replace/push করা হয়নি; এটি standalone revised manual।

Source documents:

https://github.com/cseashrafulislam/EduOS/blob/Ashraful/docs/SMART_SETUP_IMPLEMENTATION.md

https://github.com/cseashrafulislam/EduOS/blob/Ashraful/docs/PROJECT_MASTER_PLAN.md

https://github.com/cseashrafulislam/EduOS/blob/Ashraful/docs/UI_PERSONALIZATION.md

https://github.com/cseashrafulislam/EduOS/blob/Ashraful/EduOS.App/Views/AcademicManagement/SectionRegister.cshtml

## ডকুমেন্ট আপডেট করার নিয়ম

5.  Feature/Menu/Route/Role বা Academic Model বদলালে এই Manual-এর সংশ্লিষ্ট Step ও Route Reference একই Release-এ Update করুন।

6.  যে Feature এখনও QA/UAT Pass করেনি, তাকে “Available/Complete” বলা যাবে না; বাস্তব Deploy-এ Operator Check করুন।

7.  প্রতিটি কৃত্রিম Example Price/Capacity ও Demo Institution Name-কে Live Master Data হিসেবে Import করবেন না।

8.  SSC/HSC Subject/Board/Admission/Registration বিধি প্রতি Academic Year-এ Official Circular অনুযায়ী Recheck করুন।