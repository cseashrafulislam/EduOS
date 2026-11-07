/* Bounded, learner-owned quiz progress; no chapter inference or grade writes. */
(function(root,factory){const api=factory();if(typeof module==='object'&&module.exports)module.exports=api;else root.EduQuizProgress=api;})(typeof window!=='undefined'?window:globalThis,function(){
'use strict';
function build(pages,total,locale){
 if(locale!=='bn-BD'&&locale!=='en-BD')return {status:'invalid',items:[]};
 if(!Array.isArray(pages)||pages.length<1||pages.length>10||!Number.isSafeInteger(total)||total<0)return {status:'invalid',items:[]};
 const rows=pages.flat();if(pages.some(p=>!Array.isArray(p)||p.length>20)||rows.length>total||rows.length>200)return {status:'invalid',items:[]};
 const ids=new Set(), groups=new Map();
 for(const r of rows){
  if(!r||!Number.isSafeInteger(r.attemptId)||r.attemptId<=0||ids.has(r.attemptId)||
    typeof r.quizReference!=='string'||!r.quizReference.trim()||r.quizReference.length>120||
    typeof r.quizTitle!=='string'||!r.quizTitle.trim()||r.quizTitle.length>200||
    !Number.isSafeInteger(r.attemptNo)||r.attemptNo<1||
    (r.passed!==true&&r.passed!==false&&r.passed!==null)||
    (r.passed!==null&&(!Number.isFinite(r.obtainedMarks)||r.obtainedMarks<0)))return {status:'invalid',items:[]};
  ids.add(r.attemptId);
  let g=groups.get(r.quizReference);
  if(!g){g={quizReference:r.quizReference,title:r.quizTitle,attempts:0,graded:0,passed:0,failed:0,latestAttemptNo:0,latestPassed:null,latestAttemptId:0};groups.set(r.quizReference,g);}
  if(g.title!==r.quizTitle)return {status:'invalid',items:[]};
  g.attempts++;
  if(r.passed!==null){g.graded++;if(r.passed)g.passed++;else g.failed++;}
  if(r.attemptNo>g.latestAttemptNo){g.latestAttemptNo=r.attemptNo;g.latestPassed=r.passed;g.latestAttemptId=r.attemptId;}
 }
 const bn=locale==='bn-BD',complete=rows.length===total;
 return {status:rows.length?'ready':'no-evidence',complete,scanned:rows.length,totalCount:total,
  note:bn?'শুধু অনুমোদিত কুইজের ইতিহাস; অধ্যায়ভিত্তিক দক্ষতা বা চূড়ান্ত গ্রেড নয়।':'Authorized quiz history only; not chapter proficiency or an official grade.',
  items:[...groups.values()].sort((a,b)=>b.failed-a.failed||a.title.localeCompare(b.title)).map(g=>({
   ...g,advice:g.graded===0?(bn?'মূল্যায়িত ফল নেই':'No graded evidence'):
    g.latestPassed===false?(bn?'নিজের উত্তর পর্যালোচনা করুন':'Review your answers'):
    g.latestPassed===true?(bn?'সর্বশেষ মূল্যায়নে উত্তীর্ণ':'Latest graded attempt passed'):
    (bn?'সর্বশেষ প্রচেষ্টা মূল্যায়নের অপেক্ষায়':'Latest attempt pending')
  }))};
}
return Object.freeze({build});
});
