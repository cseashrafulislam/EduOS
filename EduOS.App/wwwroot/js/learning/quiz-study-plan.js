/* Read-only learner study priorities from canonical, authorized quiz progress. Not an official grade or persisted plan. */
(function(root,factory){const api=factory();if(typeof module==='object'&&module.exports)module.exports=api;else root.EduQuizStudyPlan=api;})(typeof window!=='undefined'?window:globalThis,function(){
'use strict';
function build(progress,locale){
 const bn=locale==='bn-BD';
 if(!['bn-BD','en-BD'].includes(locale)||!progress||!['ready','no-evidence'].includes(progress.status)||
 !Array.isArray(progress.items)||!Number.isSafeInteger(progress.scanned)||!Number.isSafeInteger(progress.totalCount)||
 progress.scanned<0||progress.totalCount<progress.scanned||progress.items.length>200||progress.complete!==(progress.scanned===progress.totalCount))
 return {status:'invalid',items:[]};
 const seen=new Set(),items=[];
 for(const q of progress.items){
  if(!q||typeof q.quizReference!=='string'||!q.quizReference.trim()||q.quizReference.length>120||seen.has(q.quizReference)||
   typeof q.title!=='string'||!q.title.trim()||q.title.length>200||
   ![q.attempts,q.graded,q.passed,q.failed,q.latestAttemptNo].every(n=>Number.isSafeInteger(n)&&n>=0)||
   q.attempts<1||q.graded>q.attempts||q.passed+q.failed!==q.graded||
   (q.latestPassed!==null&&typeof q.latestPassed!=='boolean'))return {status:'invalid',items:[]};
  seen.add(q.quizReference);
  const priority=q.graded===0?'awaiting-evidence':q.latestPassed===false?'review':q.latestPassed===true?'extend':'pending';
  items.push({quizReference:q.quizReference,title:q.title,priority,
   guidance:priority==='review'?(bn?'নিজের মূল্যায়িত উত্তর পর্যালোচনা করে অনুমোদিত পুনঃচেষ্টা খুঁজুন।':'Review your graded answers, then check for an eligible retry.'):
   priority==='extend'?(bn?'উত্তীর্ণ ফল ধরে রাখুন; পরবর্তী অনুমোদিত অনুশীলন দেখুন।':'Maintain progress and explore the next eligible practice.'):
   priority==='pending'?(bn?'সর্বশেষ ফল প্রকাশের অপেক্ষা করুন; আগের ফল অপরিবর্তিত।':'Wait for the latest result; prior graded evidence remains unchanged.'):
   (bn?'এখনো মূল্যায়িত ফল নেই; অনুমোদিত অনুশীলন দেখুন।':'No graded evidence yet; explore eligible practice.')});
 }
 const order={'review':0,'awaiting-evidence':1,'pending':2,'extend':3};
 items.sort((a,b)=>order[a.priority]-order[b.priority]||a.title.localeCompare(b.title));
 return {status:items.length?'ready':'no-evidence',complete:progress.complete,items,
  message:bn?'কুইজভিত্তিক অস্থায়ী পড়ার পরিকল্পনা':'Provisional quiz-based study priorities',
  note:bn?'এটি শুধু নিজের অনুমোদিত ইতিহাস থেকে তৈরি; শিক্ষক নির্ধারিত লক্ষ্য, অধ্যায় দক্ষতা বা অফিসিয়াল ফল নয়।'+(progress.complete?'':' ইতিহাস আংশিক।'):
  'Derived only from your authorized history; not teacher goals, chapter mastery or official results.'+(progress.complete?'':' History is partial.')};
}
function exportCsv(result,locale){
 if(!['bn-BD','en-BD'].includes(locale)||!result||result.status!=='ready'||!Array.isArray(result.items)||
 !Number.isSafeInteger(result.totalCount)||!Number.isSafeInteger(result.scanned)||result.scanned<1||result.scanned>result.totalCount||result.complete!==(result.scanned===result.totalCount)||
 result.items.length<1||result.items.length>200||typeof result.complete!=='boolean')return null;
 const allowed=new Set(['review','awaiting-evidence','pending','extend']),seen=new Set();
 const rows=[[locale==='bn-BD'?'কুইজ':'Quiz',locale==='bn-BD'?'অগ্রাধিকার':'Priority',locale==='bn-BD'?'পরামর্শ':'Guidance',locale==='bn-BD'?'তথ্য':'Evidence']];
 for(const item of result.items){
  if(!item||typeof item.quizReference!=='string'||!item.quizReference.trim()||seen.has(item.quizReference)||
   typeof item.title!=='string'||!item.title.trim()||item.title.length>200||
   typeof item.guidance!=='string'||!item.guidance.trim()||item.guidance.length>500||!allowed.has(item.priority))return null;
  seen.add(item.quizReference);
  rows.push([item.title,item.priority,item.guidance,result.complete?'complete':'partial']);
 }
 const csvCell=value=>{
  let s=String(value).replace(/[\u0000-\u001f\u007f]/g,' ');
  if(/^[\s]*[=+@-]/.test(s))s="'"+s;
  return '"'+s.replace(/"/g,'""')+'"';
 };
 return '\uFEFF'+rows.map(row=>row.map(csvCell).join(',')).join('\r\n');
}
return Object.freeze({build,exportCsv});
});
