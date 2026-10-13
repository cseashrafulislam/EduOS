(() => {
'use strict';
const root=document.getElementById('learningApp');if(!root)return;
const $=id=>document.getElementById(id),author=root.dataset.author==='true',student=root.dataset.student==='true',manager=root.dataset.manager==='true';
const state={courses:[],detail:null,programs:[],subjects:[],page:1,pages:1,editingCourse:null,editingLesson:null,editingAssignment:null,loadId:0,reviewPage:1,reviewPages:1,reviewLoadId:0};
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
 try{const data=await api('/api/lms/courses?page='+state.page+'&pageSize=20');if(request!==state.loadId)return;
  state.courses=data?.items||[];state.pages=Math.max(1,data?.totalPages||1);
  select('learningCourse',state.courses.map(x=>({id:x.reference,name:x.title+' — '+(x.subjectName||x.code)})),'Select course');
  if(state.courses.some(x=>x.reference===old))$('learningCourse').value=old;
  $('learningCoursePage').textContent='Page '+state.page+' / '+state.pages;
  $('learningCoursePrev').disabled=state.page<=1;$('learningCourseNext').disabled=state.page>=state.pages;
  if($('learningCourse').value)await load();
  else{state.detail=null;render();}
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
 const x=chosen();if(!x){state.detail=null;render();return;}
 const request=++state.loadId;
 try{const d=await api('/api/lms/courses/'+encodeURIComponent(x.reference));if(request!==state.loadId)return;
  state.detail=d;$('learningProgress').textContent=(d.lessons||[]).length+' lessons';render();
 }catch(e){if(request!==state.loadId)return;state.detail=null;render();notice('danger',e.message);}
}
function render(){
 const lessons=$('learningLessons'),assignments=$('learningAssignments');lessons.replaceChildren();assignments.replaceChildren();
 for(const x of state.detail?.lessons||[]){
  const tr=document.createElement('tr'),content=cell((x.content||'').slice(0,180)),action=document.createElement('td');
  if(x.contentUrl){try{const url=new URL(x.contentUrl);if(['http:','https:'].includes(url.protocol)){
   const a=document.createElement('a');a.href=url.href;a.target='_blank';a.rel='noopener noreferrer';a.textContent=' Resource';content.append(a);
  }}catch{}}
  if(student)action.append(button('Mark completed',()=>complete(x)));
  if(author)action.append(button('Edit',()=>editLesson(x)));
  tr.append(cell(x.displayOrder),cell(x.title),content,action);lessons.append(tr);
 }
 for(const x of state.detail?.assignments||[]){
  const tr=document.createElement('tr'),action=document.createElement('td');
  if(author)action.append(button('Edit',()=>editAssignment(x)));
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
 const x=state.editingLesson,body={courseReference:chosen().reference,title:$('learningLessonTitle').value.trim(),
  content:$('learningContent').value.trim()||null,contentUrl:$('learningVideo').value.trim()||null,
  displayOrder:Number($('learningOrder').value),isPublished:$('learningLessonPublished').checked,rowVersion:x?.rowVersion||null};
 $('learningSaveLesson').disabled=true;
 try{await api('/api/lms/lessons'+(x?'/'+encodeURIComponent(x.reference):''),x?'PUT':'POST',body);
  resetLesson();await load();notice('success','Lesson saved.');
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
 const x=state.editingAssignment,raw=$('learningDue').value,dt=raw?new Date(raw):null;
 if(dt&&!Number.isFinite(dt.getTime()))return notice('danger','Invalid due date.');
 const body={courseReference:chosen().reference,title:$('learningAssignmentTitle').value.trim(),
  instructions:$('learningInstructions').value.trim()||null,maxMarks:Number($('learningTotal').value),
  dueAt:dt?.toISOString()||null,isPublished:$('learningAssignmentPublished').checked,
  type:1,rowVersion:x?.rowVersion||null};
 $('learningSaveAssignment').disabled=true;
 try{await api('/api/lms/assignments'+(x?'/'+encodeURIComponent(x.reference):''),x?'PUT':'POST',body);
  resetAssignment();await load();notice('success','Assignment saved.');
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
$('learningRefresh').addEventListener('click',()=>courses());
$('learningCourse').addEventListener('change',()=>load());
$('learningCoursePrev').addEventListener('click',()=>{if(state.page>1){state.page--;courses();}});
$('learningCourseNext').addEventListener('click',()=>{if(state.page<state.pages){state.page++;courses();}});
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
