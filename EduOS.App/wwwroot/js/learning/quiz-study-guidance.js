/* Read-only, model-off learner guidance from the currently authorized quiz-history page.
   Never infer a chapter, skill score, official grade, or unseen attempts. */
(function(root, factory) {
  const guidance = factory();
  if (typeof module === 'object' && module.exports) module.exports = guidance;
  else root.EduQuizStudyGuidance = guidance;
})(typeof window !== 'undefined' ? window : globalThis, function() {
  'use strict';
  function build(rows, locale) {
    const bn = locale === 'bn-BD';
    if (!bn && locale !== 'en-BD') return { status: 'invalid', items: [] };
    if (!Array.isArray(rows) || rows.length > 20 ||
        rows.some(r => !r || typeof r.quizTitle !== 'string' ||
          r.quizTitle.length > 200 || !Number.isSafeInteger(r.attemptId) ||
          r.attemptId <= 0 || (r.passed !== true && r.passed !== false && r.passed !== null) ||
          (r.passed !== null && (!Number.isFinite(r.obtainedMarks) || r.obtainedMarks < 0))))
      return { status: 'invalid', items: [] };
    const graded = rows.filter(r => r.passed !== null);
    const failed = graded.filter(r => r.passed === false);
    return {
      status: graded.length ? 'ready' : 'no-evidence',
      note: bn
        ? 'শুধু এই পাতায় দেখা মূল্যায়নের ভিত্তিতে পরামর্শ; এটি অধ্যায়ভিত্তিক দক্ষতা বা চূড়ান্ত ফল নয়।'
        : 'Guidance from this history page only; not a chapter-level skill score or official grade.',
      items: failed.slice(0, 5).map(r => ({
        title: r.quizTitle,
        attemptId: r.attemptId,
        advice: bn
          ? 'এই কুইজের নিজের উত্তর পর্যালোচনা করুন এবং শিক্ষক নির্ধারিত পাঠ অনুশীলন করুন।'
          : 'Review your own quiz answers and practise the teacher-assigned material.'
      })),
      message: failed.length
        ? (bn ? 'পুনরায় অনুশীলনের পরামর্শ' : 'Suggested review')
        : graded.length
          ? (bn ? 'এই পাতায় পুনরায় অনুশীলনের প্রয়োজনীয় ফল নেই।' : 'No failed graded attempts on this page.')
          : (bn ? 'এই পাতায় প্রকাশিত মূল্যায়নের তথ্য নেই।' : 'No graded evidence on this page.')
    };
  }
  return Object.freeze({ build });
});
