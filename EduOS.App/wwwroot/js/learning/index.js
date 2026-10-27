(() => {
'use strict';
const root=document.getElementById('learningApp');if(!root)return;
const $=id=>document.getElementById(id),author=root.dataset.author==='true',student=root.dataset.student==='true',manager=root.dataset.manager==='true';
const state={courses:[],detail:null,programs:[],subjects:[],page:1,pages:1,editingCourse:null,editingLesson:null,editingAssignment:null,loadId:0,lessonPage:1,lessonPages:1,assignmentPage:1,assignmentPages:1,reviewPage:1,reviewPages:1,reviewLoadId:0,authoringLocked:true,includeArchived:false};
const opt=(value,label)=>{const x=document.createElement('option');x.value=String(value);x.textContent=label;return x;};
const cell=value=>{const x=document.createElement('td');x.textContent=value==null?'':String(value);return x;};
const notice=(type,message)=>{const x=$('learningAlert');x.className='alert alert-'+type;x.textContent=message;x.focus();};
const chosen=()=>state.courses.find(x=>x.reference===$('learningCourse').value);
async function api(url,method='GET',body){
 const headers={Accept:'application/json'};if(body!==undefined)headers['Content-Type']='application/json';
 if(method!=='GET'){const token=document.querySelector('meta[name="request-verification-token"]')?.content;
  if(!token)throw Error('Security token missing. Reload the page.');headers.RequestVerificationToken=token;}
 const response=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers,...(body===undefined?{}:{body:JSON.stringify(body)})});
 const result=await response.json().catch(()=>null);
 if(!response.ok||!result?.success)throw Error(result?.message||'Request failed.');
 return result.data;
}
function select(id,items,placeholder){
 const node=$(id),previous=node.value;node.replaceChildren(opt('',placeholder));
 items.forEach(x=>node.append(opt(x.id,x.name)));
 if(items.some(x=>String(x.id)===previous))node.value=previous;
}
function button(label,fn){const b=document.createElement('button');b.type='button';b.className='btn btn-sm btn-outline-primary';b.textContent=label;b.onclick=fn;return b;}
async function courses(){
 const old=$('learningCourse').value,request=++state.loadId;
 try{const data=await api('/api/lms/courses?page='+state.page+'&pageSize=20'+(student&&state.includeArchived?'&includeArchived=true':''));if(request!==state.loadId)return;
  state.courses=data?.items||[];state.pages=Math.max(1,data?.totalPages||1);
  select('learningCourse',state.courses.map(x=>({id:x.reference,name:(x.isActive?'':'[Archived] ')+x.title+' — '+(x.subjectName||x.code)})),'Select course');
  if(state.courses.some(x=>x.reference===old))$('learningCourse').value=old;
   else{state.lessonPage=1;state.assignmentPage=1;state.lessonPages=1;state.assignmentPages=1;}
  $('learningCoursePage').textContent='Page '+state.page+' / '+state.pages;
  $('learningCoursePrev').disabled=state.page<=1;$('learningCourseNext').disabled=state.page>=state.pages;
  if($('learningCourse').value)await load();
  else{state.detail=null;state.authoringLocked=true;render();}
 }catch(e){notice('danger',e.message);}
}
async function catalog(){
 if(!author)return;
 try{const d=await api('/api/academic-setup/catalog');
  state.programs=(d.programs||[]).filter(x=>x.isActive);state.subjects=(d.subjects||[]).filter(x=>x.isActive);
  select('learningProgram',state.programs.map(x=>({id:x.id,name:x.name})),'Optional academic program');
  select('learningSubject',state.subjects.map(x=>({id:x.id,name:x.name+' ('+x.code+')'})),'Optional subject');
 }catch(e){notice('warning','Academic catalog unavailable: '+e.message);}
}
async function instructors(){
 if(!manager)return;
 try{const rows=await api('/api/lms/instructors?take=100');
  select('learningInstructor',(rows||[]).map(x=>({id:x.employeeReference,name:x.name+' ('+x.employeeCode+')'})),'Optional instructor');
 }catch(e){notice('warning','Instructor lookup unavailable: '+e.message);}
}
function resetCourse(){
 state.editingCourse=null;$('learningCourseForm').reset();$('learningProgram').value='';$('learningSubject').value='';
 if(manager)$('learningInstructor').value='';
}
function editCourse(){
 const x=chosen();if(!x)return notice('warning','Choose a course.');
 state.editingCourse=x;$('learningProgram').value=x.academicProgramId||'';$('learningSubject').value=x.subjectId||'';
 if(manager)$('learningInstructor').value=x.primaryInstructorReference||'';
 $('learningCode').value=x.code;$('learningTitle').value=x.title;$('learningDescription').value=x.description||'';
 $('learningSelfPaced').checked=!!x.isSelfPaced;$('learningCourseActive').checked=!!x.isActive;$('learningTitle').focus();
}
async function saveCourse(e){
 e.preventDefault();if(!e.currentTarget.reportValidity())return;
 const x=state.editingCourse;
 if(x&&state.authoringLocked)return notice('warning','Completed enrollments lock course changes.');
 const body={academicProgramId:Number($('learningProgram').value)||null,subjectId:Number($('learningSubject').value)||null,
  primaryInstructorReference:manager?$('learningInstructor').value||null:null,code:$('learningCode').value.trim(),
  title:$('learningTitle').value.trim(),description:$('learningDescription').value.trim()||null,
  isSelfPaced:$('learningSelfPaced').checked,isActive:$('learningCourseActive').checked,rowVersion:x?.rowVersion||null};
 $('learningSaveCourse').disabled=true;
 try{const result=await api('/api/lms/courses'+(x?'/'+encodeURIComponent(x.reference):''),x?'PUT':'POST',body);
  resetCourse();state.page=1;await courses();
  if(state.courses.some(c=>c.reference===result.reference)){$('learningCourse').value=result.reference;await load();}
  notice('success','Course saved.');
 }catch(err){notice('danger',err.message);}finally{$('learningSaveCourse').disabled=false;}
}
async function load(){
 const x=chosen();if(!x){state.detail=null;state.authoringLocked=true;render();return;}
 const request=++state.loadId;
 try{
  if(author){
   state.authoringLocked=true;
   const status=await api('/api/lms/courses/'+encodeURIComponent(x.reference)+'/authoring-lock');
   if(request!==state.loadId)return;
   state.authoringLocked=!!status.isLocked;
  }
  const d=await api('/api/lms/courses/'+encodeURIComponent(x.reference)+'/content-page?lessonPage='+state.lessonPage+'&assignmentPage='+state.assignmentPage+'&pageSize=20');
  if(request!==state.loadId)return;
  state.lessonPages=Math.max(1,d.lessons?.totalPages||1);
  state.assignmentPages=Math.max(1,d.assignments?.totalPages||1);
  state.detail={course:d.course,lessons:d.lessons?.items||[],assignments:d.assignments?.items||[]};
  $('learningProgress').textContent=(d.lessons?.totalCount||0)+' lessons';render();
 }catch(e){if(request!==state.loadId)return;state.detail=null;render();notice('danger',e.message);}
}
function render(){
 if(student){
  const archived=!!state.detail?.course&&!state.detail.course.isActive;
  $('learningHistoricalNotice').classList.toggle('d-none',!archived);
 }
 const locked=author&&state.authoringLocked;
 if(author){
  const banner=$('learningAuthoringLock');
  banner.classList.toggle('d-none',!locked||!chosen());
  banner.textContent=locked&&chosen()?'Completed enrollments lock this course. Create a new course version for changes.':'';
  $('learningEditCourse').disabled=locked||!chosen();
  $('learningSaveLesson').disabled=locked;
  $('learningSaveAssignment').disabled=locked;
 }
 const lessons=$('learningLessons'),assignments=$('learningAssignments');lessons.replaceChildren();assignments.replaceChildren();
  $('learningLessonPage').textContent='Page '+state.lessonPage+' / '+state.lessonPages;
  $('learningLessonPrev').disabled=state.lessonPage<=1;
  $('learningLessonNext').disabled=state.lessonPage>=state.lessonPages;
  $('learningAssignmentPage').textContent='Page '+state.assignmentPage+' / '+state.assignmentPages;
  $('learningAssignmentPrev').disabled=state.assignmentPage<=1;
  $('learningAssignmentNext').disabled=state.assignmentPage>=state.assignmentPages;
 for(const x of state.detail?.lessons||[]){
  const tr=document.createElement('tr'),content=cell((x.content||'').slice(0,180)),action=document.createElement('td');
  if(x.contentUrl){try{const url=new URL(x.contentUrl);if(['http:','https:'].includes(url.protocol)){
   const a=document.createElement('a');a.href=url.href;a.target='_blank';a.rel='noopener noreferrer';a.textContent=' Resource';content.append(a);
  }}catch{}}
  if(student&&state.detail?.course?.isActive)action.append(button('Mark completed',()=>complete(x)));
  if(author&&!locked)action.append(button('Edit',()=>editLesson(x)));
  tr.append(cell(x.displayOrder),cell(x.title),content,action);lessons.append(tr);
 }
 for(const x of state.detail?.assignments||[]){
  const tr=document.createElement('tr'),action=document.createElement('td');
  if(author&&!locked)action.append(button('Edit',()=>editAssignment(x)));
  tr.append(cell(x.title),cell(x.dueAt?String(x.dueAt).slice(0,16):'—'),cell(x.maxMarks),cell(x.isPublished?'Published':'Draft'),action);
  assignments.append(tr);
 }
 if(!lessons.childNodes.length){const row=document.createElement('tr'),td=cell('No lessons.');td.colSpan=4;row.append(td);lessons.append(row);}
 if(!assignments.childNodes.length){const row=document.createElement('tr'),td=cell('No assignments.');td.colSpan=5;row.append(td);assignments.append(row);}
 if(author)reviewChoices();
}
function resetLesson(){state.editingLesson=null;$('learningLessonForm')?.reset();}
function editLesson(x){
 state.editingLesson=x;$('learningOrder').value=x.displayOrder;$('learningLessonTitle').value=x.title;
 $('learningVideo').value=x.contentUrl||'';$('learningContent').value=x.content||'';
 $('learningLessonPublished').checked=!!x.isPublished;$('learningLessonTitle').focus();
}
async function saveLesson(e){
 e.preventDefault();if(!e.currentTarget.reportValidity()||!chosen())return;
 if(state.authoringLocked)return notice('warning','Completed enrollments lock lesson changes.');
 const x=state.editingLesson,body={courseReference:chosen().reference,title:$('learningLessonTitle').value.trim(),
  content:$('learningContent').value.trim()||null,contentUrl:$('learningVideo').value.trim()||null,
  displayOrder:Number($('learningOrder').value),isPublished:$('learningLessonPublished').checked,rowVersion:x?.rowVersion||null};
 $('learningSaveLesson').disabled=true;
 try{await api('/api/lms/lessons'+(x?'/'+encodeURIComponent(x.reference):''),x?'PUT':'POST',body);
  resetLesson();state.lessonPage=1;await load();notice('success','Lesson saved.');
 }catch(err){notice('danger',err.message);}finally{$('learningSaveLesson').disabled=false;}
}
function resetAssignment(){state.editingAssignment=null;$('learningAssignmentForm')?.reset();}
function editAssignment(x){
 state.editingAssignment=x;$('learningAssignmentTitle').value=x.title;$('learningInstructions').value=x.instructions||'';
 $('learningTotal').value=x.maxMarks;$('learningAssignmentPublished').checked=!!x.isPublished;
 if(x.dueAt){const d=new Date(x.dueAt);if(Number.isFinite(d.getTime()))$('learningDue').value=new Date(d.getTime()-d.getTimezoneOffset()*60000).toISOString().slice(0,16);}
}
async function saveAssignment(e){
 e.preventDefault();if(!e.currentTarget.reportValidity()||!chosen())return;
 if(state.authoringLocked)return notice('warning','Completed enrollments lock assignment changes.');
 const x=state.editingAssignment,raw=$('learningDue').value,dt=raw?new Date(raw):null;
 if(dt&&!Number.isFinite(dt.getTime()))return notice('danger','Invalid due date.');
 const body={courseReference:chosen().reference,title:$('learningAssignmentTitle').value.trim(),
  instructions:$('learningInstructions').value.trim()||null,maxMarks:Number($('learningTotal').value),
  dueAt:dt?.toISOString()||null,isPublished:$('learningAssignmentPublished').checked,
  type:1,rowVersion:x?.rowVersion||null};
 $('learningSaveAssignment').disabled=true;
 try{await api('/api/lms/assignments'+(x?'/'+encodeURIComponent(x.reference):''),x?'PUT':'POST',body);
  resetAssignment();state.assignmentPage=1;await load();notice('success','Assignment saved.');
 }catch(err){notice('danger',err.message);}finally{$('learningSaveAssignment').disabled=false;}
}
function reviewChoices(){
 const node=$('learningReviewAssignment'),previous=node.value;
 const assignments=state.detail?.assignments||[];
 node.replaceChildren(opt('','Choose an assignment'));
 assignments.forEach(x=>node.append(opt(x.reference,x.title)));
 node.value=assignments.some(x=>x.reference===previous)?previous:(assignments[0]?.reference||'');
 state.reviewPage=1;loadReviews();
}
function reviewEmpty(message){
 const body=$('learningReviewRows'),tr=document.createElement('tr'),td=cell(message);
 td.colSpan=6;tr.append(td);body.replaceChildren(tr);
}
async function loadReviews(){
 if(!author)return;
 const reference=$('learningReviewAssignment').value,seq=++state.reviewLoadId;
 if(!reference){reviewEmpty('Choose an assignment.');return;}
 try{
  const data=await api('/api/lms/assignments/'+encodeURIComponent(reference)+'/submissions?page='+state.reviewPage+'&pageSize=25');
  if(seq!==state.reviewLoadId||reference!==$('learningReviewAssignment').value)return;
  state.reviewPages=Math.max(1,data?.totalPages||1);
  $('learningReviewPage').textContent='Page '+state.reviewPage+' / '+state.reviewPages+' ('+(data?.totalCount||0)+' submissions)';
  $('learningReviewPrev').disabled=state.reviewPage<=1;
  $('learningReviewNext').disabled=state.reviewPage>=state.reviewPages;
  const body=$('learningReviewRows');body.replaceChildren();
  const assignment=(state.detail?.assignments||[]).find(x=>x.reference===reference);
  for(const row of data?.items||[]){
   const tr=document.createElement('tr'),marks=document.createElement('td'),action=document.createElement('td');
   const editable=Number(row.state)===2||Number(row.state)===4;
   if(editable){
    const input=document.createElement('input');input.type='number';input.min='0';input.step='0.01';
    input.max=String(assignment?.maxMarks??100000);input.className='form-control form-control-sm mb-1';
    input.value=row.marks==null?'':String(row.marks);input.placeholder='Marks';input.setAttribute('aria-label','Awarded marks');
    const feedback=document.createElement('textarea');feedback.className='form-control form-control-sm';
    feedback.maxLength=2000;feedback.rows=2;feedback.value=row.feedback||'';feedback.setAttribute('aria-label','Feedback');
    marks.append(input,feedback);
    const grade=button('Grade',async()=>{
     const value=Number(input.value),limit=Number(assignment?.maxMarks??0);
     if(!input.value.trim()||!Number.isFinite(value)||value<0||value>limit||!row.rowVersion)
      return notice('warning','Enter valid marks within the assignment maximum.');
     grade.disabled=true;
     try{await api('/api/lms/assignments/submissions/'+encodeURIComponent(row.id)+'/grade','POST',
       {marks:value,feedback:feedback.value.trim()||null,rowVersion:row.rowVersion});
      notice('success','Submission graded.');await loadReviews();
     }catch(e){notice('danger',e.message);}finally{grade.disabled=false;}
    });
    action.append(grade);
   }else marks.textContent=(row.marks==null?'—':String(row.marks))+(row.feedback?' / '+row.feedback:'');
   tr.append(cell(row.studentName||'Enrollment #'+row.courseEnrollmentId),
    cell(row.submittedAt?String(row.submittedAt).slice(0,16):'—'),
    cell((row.submissionText||'').slice(0,400)),cell(({2:'Submitted',3:'Graded',4:'Returned'})[row.state]||String(row.state)),marks,action);
   body.append(tr);
  }
  if(!body.childNodes.length)reviewEmpty('No submitted work on this page.');
 }catch(e){if(seq!==state.reviewLoadId)return;reviewEmpty('Unable to load submissions.');notice('danger',e.message);}
}
async function complete(lesson){
 try{await api('/api/lms/lessons/progress','POST',{lessonReference:lesson.reference,progressPercent:100,isCompleted:true});
  notice('success','Lesson progress saved.');
 }catch(e){notice('danger',e.message);}
}
if(student){
 $('learningIncludeArchived').addEventListener('change',e=>{
  state.includeArchived=!!e.target.checked;state.page=1;courses();
 });
 $('learningHistoricalExport').addEventListener('click',()=>{
  const rows=[['Code','Title','Program','Subject','Status'],...state.courses.map(x=>[
   x.code,x.title,x.academicProgramName||'',x.subjectName||'',x.isActive?'Active':'Archived'])];
  const csv=rows.map(row=>row.map(v=>'"'+String(v??'').replace(/"/g,'""')+'"').join(',')).join('\r\n');
  const url=URL.createObjectURL(new Blob(['\uFEFF'+csv],{type:'text/csv;charset=utf-8'}));
  const a=document.createElement('a');a.href=url;a.download='my-lms-courses-page-'+state.page+'.csv';
  a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
 });
}
$('learningRefresh').addEventListener('click',()=>courses());
$('learningCourse').addEventListener('change',()=>{state.lessonPage=1;state.assignmentPage=1;load();});
$('learningCoursePrev').addEventListener('click',()=>{if(state.page>1){state.page--;courses();}});
$('learningCourseNext').addEventListener('click',()=>{if(state.page<state.pages){state.page++;courses();}});
$('learningLessonPrev').addEventListener('click',()=>{if(state.lessonPage>1){state.lessonPage--;load();}});
$('learningLessonNext').addEventListener('click',()=>{if(state.lessonPage<state.lessonPages){state.lessonPage++;load();}});
$('learningAssignmentPrev').addEventListener('click',()=>{if(state.assignmentPage>1){state.assignmentPage--;load();}});
$('learningAssignmentNext').addEventListener('click',()=>{if(state.assignmentPage<state.assignmentPages){state.assignmentPage++;load();}});
function exportContent(kind){
 const rows=kind==='lessons'
  ?[['Order','Title','Content','Resource','Published'],...(state.detail?.lessons||[]).map(x=>[x.displayOrder,x.title,x.content||'',x.contentUrl||'',x.isPublished?'Yes':'No'])]
  :[['Title','Due date','Maximum marks','Published'],...(state.detail?.assignments||[]).map(x=>[x.title,x.dueAt||'',x.maxMarks,x.isPublished?'Yes':'No'])];
 const safe=value=>{let s=String(value??'');if(/^[=+@\\-\\t\\r]/.test(s))s="'"+s;return '"'+s.replaceAll('"','""')+'"';};
 const csv=rows.map(row=>row.map(safe).join(',')).join('\\r\\n');
 const url=URL.createObjectURL(new Blob(['\\uFEFF'+csv],{type:'text/csv;charset=utf-8'}));
 const a=document.createElement('a');a.href=url;a.download='lms-'+kind+'-visible-page-'+(kind==='lessons'?state.lessonPage:state.assignmentPage)+'.csv';
 a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
}
$('learningLessonExport').addEventListener('click',()=>exportContent('lessons'));
$('learningAssignmentExport').addEventListener('click',()=>exportContent('assignments'));
if(author){
 $('learningCourseForm').addEventListener('submit',saveCourse);$('learningEditCourse').addEventListener('click',editCourse);
 $('learningResetCourse').addEventListener('click',resetCourse);
 $('learningLessonForm').addEventListener('submit',saveLesson);$('learningResetLesson').addEventListener('click',resetLesson);
 $('learningAssignmentForm').addEventListener('submit',saveAssignment);$('learningResetAssignment').addEventListener('click',resetAssignment);
 $('learningReviewAssignment').addEventListener('change',()=>{state.reviewPage=1;loadReviews();});
 $('learningReviewRefresh').addEventListener('click',()=>loadReviews());
 $('learningReviewPrev').addEventListener('click',()=>{if(state.reviewPage>1){state.reviewPage--;loadReviews();}});
 $('learningReviewNext').addEventListener('click',()=>{if(state.reviewPage<state.reviewPages){state.reviewPage++;loadReviews();}});
}
catalog();instructors();courses();
})();
