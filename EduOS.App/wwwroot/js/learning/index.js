(() => {
'use strict';
const root=document.getElementById('learningApp');if(!root)return;
const $=id=>document.getElementById(id),author=root.dataset.author==='true',student=root.dataset.student==='true',manager=root.dataset.manager==='true';
const state={courses:[],detail:null,programs:[],subjects:[],page:1,pages:1,editingCourse:null,editingLesson:null,editingAssignment:null,loadId:0,lessonPage:1,lessonPages:1,assignmentPage:1,assignmentPages:1,reviewPage:1,reviewPages:1,reviewRows:[],reviewLoadId:0,reviewFileLoadId:0,reviewRubric:[],rubricLoadId:0,rubricVersion:'',rubricCriteria:[],studentRubricLoadId:0,authoringLocked:true,includeArchived:false,myProgressPage:1,myProgressPages:1,myProgressRows:[],myProgressLoadId:0,lessonProgress:new Map(),progressLoaded:false,instructorLoadId:0,instructorTimer:null,studentLoadId:0,studentTimer:null,enrollRequestId:null};
const initialTenant=document.body?.dataset.authTenantId||'';
const initialSession=document.body?.dataset.authSessionReference||'';
const privateDownloads=new Set();
const sessionValid=()=>document.body?.dataset.authTenantId===initialTenant&&
 document.body?.dataset.authSessionReference===initialSession&&
 window.EduOSSessionTransitionPending?.()!==true;
function closeReviewFiles(){
 state.reviewFileLoadId++;
 for(const controller of privateDownloads)controller.abort();privateDownloads.clear();
 if(author){$('learningReviewFilesPanel').classList.add('d-none');$('learningReviewFilesRows').replaceChildren();
  $('learningReviewFilesTitle').textContent='Submission details';$('learningReviewFullAnswer').textContent='';}
}
function clearSensitive(){
 state.loadId++;state.reviewLoadId++;state.rubricLoadId++;state.studentRubricLoadId++;state.myProgressLoadId++;state.instructorLoadId++;state.studentLoadId++;
 clearTimeout(state.instructorTimer);clearTimeout(state.studentTimer);state.enrollRequestId=null;closeReviewFiles();
 state.courses=[];state.detail=null;state.reviewRows=[];state.reviewRubric=[];state.rubricCriteria=[];state.rubricVersion='';state.myProgressRows=[];state.lessonProgress.clear();
 state.editingCourse=null;state.editingLesson=null;state.editingAssignment=null;
 root.querySelectorAll('tbody').forEach(x=>x.replaceChildren());
 root.querySelectorAll('form').forEach(x=>x.reset());
 root.querySelectorAll('select').forEach(x=>x.replaceChildren());
 if(author)$('learningRubricRows').replaceChildren();
 if(student){$('learningStudentRubricPanel').classList.add('d-none');$('learningStudentRubricRows').replaceChildren();}
}
const opt=(value,label)=>{const x=document.createElement('option');x.value=String(value);x.textContent=label;return x;};
const cell=value=>{const x=document.createElement('td');x.textContent=value==null?'':String(value);return x;};
const notice=(type,message)=>{const x=$('learningAlert');x.className='alert alert-'+type;x.textContent=message;x.focus();};
const chosen=()=>state.courses.find(x=>x.reference===$('learningCourse').value);
async function api(url,method='GET',body){
 if(!sessionValid())throw Error('Session changed. Reload the page.');
 const headers={Accept:'application/json'};if(body!==undefined)headers['Content-Type']='application/json';
 if(method!=='GET'){const token=document.querySelector('meta[name="request-verification-token"]')?.content;
  if(!token)throw Error('Security token missing. Reload the page.');headers.RequestVerificationToken=token;}
 const response=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers,...(body===undefined?{}:{body:JSON.stringify(body)})});
 const result=await response.json().catch(()=>null);
 if(!sessionValid()){clearSensitive();throw Error('Session changed. Reload the page.');}
 if(!response.ok||!result?.success)throw Error(result?.message||'Request failed.');
 return result.data;
}
function select(id,items,placeholder){
 const node=$(id),previous=node.value;node.replaceChildren(opt('',placeholder));
 items.forEach(x=>node.append(opt(x.id,x.name)));
 if(items.some(x=>String(x.id)===previous))node.value=previous;
}
function button(label,fn){const b=document.createElement('button');b.type='button';b.className='btn btn-sm btn-outline-primary me-1';b.textContent=label;b.onclick=fn;return b;}
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
async function instructors(search=''){
 if(!manager)return;
 const seq=++state.instructorLoadId;
 try{const rows=await api('/api/lms/instructors?search='+encodeURIComponent(search.trim())+'&take=25');
  if(seq!==state.instructorLoadId)return;
  const node=$('learningInstructor'),selected=node.value,selectedLabel=node.selectedOptions[0]?.textContent;
  const choices=(rows||[]).map(x=>({id:x.employeeReference,name:x.name+' ('+x.employeeCode+')'}));
  if(selected&&!choices.some(x=>x.id===selected))choices.push({id:selected,name:selectedLabel||'Current instructor'});
  const edited=state.editingCourse;
  if(edited?.primaryInstructorReference&&!choices.some(x=>x.id===edited.primaryInstructorReference))
   choices.push({id:edited.primaryInstructorReference,name:edited.primaryInstructorName||'Assigned instructor'});
  select('learningInstructor',choices,'Optional instructor');
 }catch(e){if(seq===state.instructorLoadId)notice('warning','Instructor lookup unavailable: '+e.message);}
}
async function searchStudents(search){
 if(!manager)return;
 const seq=++state.studentLoadId,node=$('learningStudentChoice'),term=search.trim();
 node.replaceChildren(opt('',term.length<2?'Enter at least 2 characters':'Searching…'));
 if(term.length<2)return;
 try{
  const rows=await api('/api/lms/students/search?search='+encodeURIComponent(term)+'&take=25');
  if(seq!==state.studentLoadId)return;
  node.replaceChildren(opt('',rows.length?'Select student':'No matching students'));
  for(const x of rows)node.append(opt(x.studentReference,x.studentName+' ('+x.studentCode+')'));
 }catch(e){if(seq===state.studentLoadId)notice('danger',e.message);}
}
async function enrollStudent(e){
 e.preventDefault();if(!sessionValid())return clearSensitive();
 const course=chosen(),student=$('learningStudentChoice').value;
 if(!course||!student)return notice('warning','Select a course and an eligible student.');
 if(!state.enrollRequestId)state.enrollRequestId=crypto.randomUUID();
 const button=$('learningEnrollSave');button.disabled=true;
 try{
  await api('/api/lms/courses/enrollments','POST',
   {clientRequestId:state.enrollRequestId,courseReference:course.reference,studentReference:student});
  state.enrollRequestId=null;$('learningStudentChoice').value='';
  notice('success','Student enrolled.');
 }catch(error){notice('danger',error.message);}finally{button.disabled=false;}
}
function resetCourse(){
 state.editingCourse=null;$('learningCourseForm').reset();$('learningProgram').value='';$('learningSubject').value='';
 if(manager)$('learningInstructor').value='';
}
function editCourse(){
 const x=chosen();if(!x)return notice('warning','Choose a course.');
 state.editingCourse=x;$('learningProgram').value=x.academicProgramId||'';$('learningSubject').value=x.subjectId||'';
 if(manager){
  const node=$('learningInstructor');
  if(x.primaryInstructorReference&&!Array.from(node.options).some(o=>o.value===x.primaryInstructorReference))
   node.append(opt(x.primaryInstructorReference,x.primaryInstructorName||'Assigned instructor'));
  node.value=x.primaryInstructorReference||'';
 }
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
 state.lessonProgress=new Map();state.progressLoaded=false;
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
  if(student){
   const progress=await api('/api/lms/courses/'+encodeURIComponent(x.reference)+'/lesson-progress?page='+state.lessonPage+'&pageSize=20');
   if(request!==state.loadId)return;
   state.lessonProgress=new Map((progress?.items||[]).map(p=>[p.lessonReference,p]));
   state.progressLoaded=true;
  }
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
  if(student){
   const progress=state.lessonProgress.get(x.reference);
   if(progress?.isCompleted)action.textContent='Completed';
   else if(!state.progressLoaded)action.textContent='Progress unavailable';
   else if(state.detail?.course?.isActive)action.append(button('Mark completed',()=>complete(x)));
   else action.textContent='Not completed';
  }
  if(author&&!locked)action.append(button('Edit',()=>editLesson(x)));
  tr.append(cell(x.displayOrder),cell(x.title),content,action);lessons.append(tr);
 }
 for(const x of state.detail?.assignments||[]){
  const tr=document.createElement('tr'),action=document.createElement('td');
  if(author&&!locked)action.append(button('Edit',()=>editAssignment(x)));
  if(student)action.append(button('View rubric',()=>showStudentRubric(x)));
  tr.append(cell(x.title),cell(x.dueAt?String(x.dueAt).slice(0,16):'—'),cell(x.maxMarks),cell(x.isPublished?'Published':'Draft'),action);
  assignments.append(tr);
 }
 if(!lessons.childNodes.length){const row=document.createElement('tr'),td=cell('No lessons.');td.colSpan=4;row.append(td);lessons.append(row);}
 if(!assignments.childNodes.length){const row=document.createElement('tr'),td=cell('No assignments.');td.colSpan=5;row.append(td);assignments.append(row);}
 if(author){reviewChoices();rubricChoices();}
}

function rubricChoices(){
 const node=$('learningRubricAssignment'),previous=node.value;
 const assignments=state.detail?.assignments||[];
 node.replaceChildren(opt('','Choose an assignment'));
 assignments.forEach(x=>node.append(opt(x.reference,x.title)));
 node.value=assignments.some(x=>x.reference===previous)?previous:'';
 loadRubric();
}
function rubricTotal(){
 const sum=state.rubricCriteria.reduce((total,x)=>total+(Number(x.maxMarks)||0),0);
 const assignment=(state.detail?.assignments||[]).find(x=>x.reference===$('learningRubricAssignment').value);
 const valid=assignment&&Math.abs(sum-Number(assignment.maxMarks))<0.005;
 $('learningRubricTotal').textContent=assignment
  ?'Rubric '+sum.toFixed(2)+' / assignment '+Number(assignment.maxMarks).toFixed(2)+' marks'+(valid?' ✓':' — totals must match')
  :'Choose an assignment.';
 $('learningRubricSave').disabled=!assignment||state.authoringLocked;
 $('learningRubricAdd').disabled=!assignment||state.authoringLocked||state.rubricCriteria.length>=20;
 return valid;
}
function drawRubric(){
 const tbody=$('learningRubricRows');tbody.replaceChildren();
 state.rubricCriteria.forEach((criterion,index)=>{
  const tr=document.createElement('tr'),name=document.createElement('td'),marks=document.createElement('td'),action=document.createElement('td');
  const title=document.createElement('input');title.className='form-control form-control-sm';
  title.maxLength=200;title.value=criterion.title;title.setAttribute('aria-label','Criterion '+(index+1));
  title.disabled=state.authoringLocked;title.oninput=()=>{criterion.title=title.value;};name.append(title);
  const number=document.createElement('input');number.className='form-control form-control-sm';
  number.type='number';number.step='.01';number.min='.01';number.max='100000';
  number.value=String(criterion.maxMarks);number.disabled=state.authoringLocked;
  number.setAttribute('aria-label','Criterion maximum');number.oninput=()=>{criterion.maxMarks=number.value;rubricTotal();};marks.append(number);
  if(!state.authoringLocked){action.append(button('Remove',()=>{state.rubricCriteria.splice(index,1);drawRubric();}));}
  tr.append(name,marks,action);tbody.append(tr);
 });
 if(!state.rubricCriteria.length){const tr=document.createElement('tr'),td=cell('No rubric — instructors may use overall grading.');td.colSpan=3;tr.append(td);tbody.append(tr);}
 rubricTotal();
}
async function loadRubric(){
 const reference=$('learningRubricAssignment').value,seq=++state.rubricLoadId;
 state.rubricCriteria=[];state.rubricVersion='';drawRubric();if(!reference)return;
 try{const rubric=await api('/api/lms/assignments/'+encodeURIComponent(reference)+'/rubric');
  if(seq!==state.rubricLoadId||reference!==$('learningRubricAssignment').value)return;
  state.rubricCriteria=(rubric.criteria||[]).map(x=>({title:x.title,maxMarks:x.maxMarks}));
  state.rubricVersion=rubric.rowVersion;drawRubric();
 }catch(e){if(seq===state.rubricLoadId)notice('danger','Unable to load rubric: '+e.message);}
}
async function saveRubric(){
 const reference=$('learningRubricAssignment').value;
 if(!reference||state.authoringLocked||!state.rubricVersion)return notice('warning','Select an editable assignment and load its rubric.');
 if(state.rubricCriteria.some(x=>!String(x.title).trim()||!Number.isFinite(Number(x.maxMarks))||Number(x.maxMarks)<=0)||
    (state.rubricCriteria.length&& !rubricTotal()))return notice('warning','Enter criterion titles and marks matching the assignment maximum.');
 const button=$('learningRubricSave');button.disabled=true;
 try{const rubric=await api('/api/lms/assignments/'+encodeURIComponent(reference)+'/rubric','PUT',
  {rowVersion:state.rubricVersion,criteria:state.rubricCriteria.map(x=>({title:x.title.trim(),maxMarks:Number(x.maxMarks)}))});
  state.rubricVersion=rubric.rowVersion;state.rubricCriteria=rubric.criteria.map(x=>({title:x.title,maxMarks:x.maxMarks}));
  const assignment=(state.detail?.assignments||[]).find(x=>x.reference===reference);
  if(assignment)assignment.rowVersion=rubric.rowVersion;
  drawRubric();notice('success','Assignment rubric saved.');
 }catch(e){notice('danger',e.message);}finally{button.disabled=false;rubricTotal();}
}
async function showStudentRubric(assignment){
 const seq=++state.studentRubricLoadId,panel=$('learningStudentRubricPanel');
 panel.classList.remove('d-none');$('learningStudentRubricTitle').textContent='Marking rubric — '+assignment.title;
 const body=$('learningStudentRubricRows');body.replaceChildren();
 try{const data=await api('/api/lms/assignments/'+encodeURIComponent(assignment.reference)+'/rubric');
  if(seq!==state.studentRubricLoadId)return;
  for(const x of data.criteria||[]){const tr=document.createElement('tr');tr.append(cell(x.title),cell(x.maxMarks));body.append(tr);}
  if(!body.childNodes.length){const tr=document.createElement('tr'),td=cell('Overall marks: '+assignment.maxMarks+' (no criterion breakdown).');td.colSpan=2;tr.append(td);body.append(tr);}
 }catch(e){if(seq===state.studentRubricLoadId){panel.classList.add('d-none');notice('danger',e.message);}}
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
async function downloadSubmissionFile(submissionId,file,button){
 if(!sessionValid()){clearSensitive();return;}
 const controller=new AbortController();privateDownloads.add(controller);button.disabled=true;
 try{
  const url='/api/lms/assignment-work/submissions/'+submissionId+'/files/'+
   encodeURIComponent(file.fileAssetReference)+'/download';
  const response=await fetch(url,{credentials:'same-origin',cache:'no-store',signal:controller.signal});
  if(!response.ok){const result=await response.json().catch(()=>({}));throw Error(result.message||'Download unavailable.');}
  const blob=await response.blob();
  if(controller.signal.aborted||!sessionValid()){clearSensitive();return;}
  const name=String(file.fileName||'assignment').replace(/[\\/\u0000-\u001f]/g,'_').slice(0,255);
  const objectUrl=URL.createObjectURL(blob),link=document.createElement('a');
  link.href=objectUrl;link.download=name;document.body.append(link);link.click();link.remove();
  setTimeout(()=>URL.revokeObjectURL(objectUrl),1000);
 }catch(e){if(e.name!=='AbortError')notice('danger',e.message);}
 finally{privateDownloads.delete(controller);button.disabled=false;}
}
async function showReviewFiles(submission){
 closeReviewFiles();
 const seq=state.reviewFileLoadId,panel=$('learningReviewFilesPanel'),body=$('learningReviewFilesRows');
 panel.classList.remove('d-none');$('learningReviewFilesTitle').textContent='Submission — '+(submission.studentName||'Student');
 $('learningReviewFullAnswer').textContent=submission.submissionText||'No written answer (file submission).';
 const waiting=document.createElement('tr'),cell=document.createElement('td');
 cell.colSpan=2;cell.textContent='Loading files…';waiting.append(cell);body.append(waiting);
 try{
  const rows=await api('/api/lms/assignment-work/submissions/'+submission.id+'/files');
  if(seq!==state.reviewFileLoadId)return;
  body.replaceChildren();
  for(const file of rows||[]){
   const tr=document.createElement('tr'),name=document.createElement('td'),action=document.createElement('td');
   name.textContent=file.fileName;
   const button=document.createElement('button');button.type='button';button.className='btn btn-sm btn-outline-primary';
   button.textContent='Download';button.onclick=()=>downloadSubmissionFile(submission.id,file,button);
   action.append(button);tr.append(name,action);body.append(tr);
  }
  if(!body.childNodes.length){const tr=document.createElement('tr'),td=document.createElement('td');
   td.colSpan=2;td.className='text-muted';td.textContent='No files attached.';tr.append(td);body.append(tr);}
 }catch(e){if(seq===state.reviewFileLoadId){closeReviewFiles();notice('danger',e.message);}}
}
async function loadReviews(){
 if(!author)return;
 closeReviewFiles();state.reviewRows=[];state.reviewRubric=[];
 const reference=$('learningReviewAssignment').value,seq=++state.reviewLoadId;
 if(!reference){reviewEmpty('Choose an assignment.');return;}
 try{
  const rubric=await api('/api/lms/assignments/'+encodeURIComponent(reference)+'/rubric');
  if(seq!==state.reviewLoadId||reference!==$('learningReviewAssignment').value)return;
  state.reviewRubric=rubric?.criteria||[];
  const data=await api('/api/lms/assignments/'+encodeURIComponent(reference)+'/submissions?page='+state.reviewPage+'&pageSize=25');
  if(seq!==state.reviewLoadId||reference!==$('learningReviewAssignment').value)return;
  state.reviewRows=data?.items||[];
  state.reviewPages=Math.max(1,data?.totalPages||1);
  $('learningReviewPage').textContent='Page '+state.reviewPage+' / '+state.reviewPages+' ('+(data?.totalCount||0)+' submissions)';
  $('learningReviewPrev').disabled=state.reviewPage<=1;
  $('learningReviewNext').disabled=state.reviewPage>=state.reviewPages;
  const body=$('learningReviewRows');body.replaceChildren();
  const assignment=(state.detail?.assignments||[]).find(x=>x.reference===reference);
  for(const row of state.reviewRows){
   const tr=document.createElement('tr'),marks=document.createElement('td'),action=document.createElement('td');
   const editable=Number(row.state)===2||Number(row.state)===4;
   action.append(button('Review answer / files',()=>showReviewFiles(row)));
   if(editable){
    const input=document.createElement('input');input.type='number';input.min='0';input.step='0.01';
    input.max=String(assignment?.maxMarks??100000);input.className='form-control form-control-sm mb-1';
    input.value=row.marks==null?'':String(row.marks);input.placeholder='Marks';input.setAttribute('aria-label','Awarded marks');
    if(state.reviewRubric.length){input.readOnly=true;input.placeholder='Calculated total';}
    const criterionInputs=[];
    for(const criterion of state.reviewRubric){
     const label=document.createElement('label');label.className='d-block small mb-1';
     label.textContent=criterion.title+' / '+criterion.maxMarks;
     const part=document.createElement('input');part.type='number';part.min='0';part.max=String(criterion.maxMarks);
     part.step='.01';part.className='form-control form-control-sm';part.setAttribute('aria-label','Awarded '+criterion.title);
     part.oninput=()=>{const sum=criterionInputs.reduce((n,p)=>n+(Number(p.input.value)||0),0);
      input.value=criterionInputs.every(p=>p.input.value.trim())?sum.toFixed(2):'';};
     label.append(part);marks.append(label);criterionInputs.push({criterionReference:criterion.reference,input:part});
    }
    const feedback=document.createElement('textarea');feedback.className='form-control form-control-sm';
    feedback.maxLength=2000;feedback.rows=2;feedback.value=row.feedback||'';feedback.setAttribute('aria-label','Feedback');
    marks.append(input,feedback);
    const grade=button('Grade',async()=>{
     const value=Number(input.value),limit=Number(assignment?.maxMarks??0);
     const scores=criterionInputs.map(x=>({criterionReference:x.criterionReference,awardedMarks:Number(x.input.value)}));
     if(criterionInputs.some(x=>!x.input.value.trim()||!Number.isFinite(Number(x.input.value))||
        Number(x.input.value)<0||Number(x.input.value)>Number(x.input.max)))
      return notice('warning','Enter valid marks for every rubric criterion.');
     if(!input.value.trim()||!Number.isFinite(value)||value<0||value>limit||!row.rowVersion)
      return notice('warning','Enter valid marks within the assignment maximum.');
     grade.disabled=true;
     try{await api('/api/lms/assignments/submissions/'+encodeURIComponent(row.id)+'/grade','POST',
       {marks:value,feedback:feedback.value.trim()||null,rowVersion:row.rowVersion,rubricScores:scores});
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
function exportReviewPage(){
 if(!sessionValid())return clearSensitive();
 const cells=[['Student','Submitted UTC','Answer','Status','Marks','Feedback'],
  ...state.reviewRows.map(x=>[x.studentName,x.submittedAt,x.submissionText,x.state,x.marks,x.feedback])];
 const safe=value=>{
  let s=String(value??'');const trimmed=s.trimStart();
  if(/^[=+@\-]/.test(trimmed)||/^[\t\r\n]/.test(s))s="'"+s;
  return '"'+s.replace(/"/g,'""')+'"';
 };
 const csv='\uFEFF'+cells.map(row=>row.map(safe).join(',')).join('\r\n');
 const url=URL.createObjectURL(new Blob([csv],{type:'text/csv;charset=utf-8'}));
 const link=document.createElement('a');link.href=url;
 link.download='lms-submission-review-page-'+state.reviewPage+'.csv';
 document.body.append(link);link.click();link.remove();setTimeout(()=>URL.revokeObjectURL(url),1000);
}
async function complete(lesson){
 try{const saved=await api('/api/lms/lessons/progress','POST',{lessonReference:lesson.reference,progressPercent:100,isCompleted:true});
  state.lessonProgress.set(lesson.reference,saved);state.progressLoaded=true;render();
  notice('success','Lesson progress saved.');await loadMyProgress();
 }catch(e){notice('danger',e.message);}
}

function renderMyProgress(){
 const body=$('learningMyProgressRows');body.replaceChildren();
 for(const x of state.myProgressRows){
  const tr=document.createElement('tr');
  const lessons=x.completedLessons+' / '+x.requiredLessons;
  const assignments=x.gradedAssignments+' / '+x.requiredAssignments;
  const quizzes=x.passedQuizzes+' / '+x.requiredQuizzes;
  tr.append(cell(x.courseTitle+' ('+x.courseCode+')'),
   cell((Number(x.state)===2?'Completed':'Active')+(x.isArchived?' (archived)':'')),cell(lessons),
   cell(assignments),cell(quizzes),cell(x.lessonCompletionPercent+'%'));
  body.append(tr);
 }
 if(!body.childNodes.length){
  const tr=document.createElement('tr'),td=cell('No matching course enrollments on this page.');
  td.colSpan=6;tr.append(td);body.append(tr);
 }
 $('learningMyProgressPage').textContent='Page '+state.myProgressPage+' / '+state.myProgressPages;
 $('learningMyProgressPrev').disabled=state.myProgressPage<=1;
 $('learningMyProgressNext').disabled=state.myProgressPage>=state.myProgressPages;
}
async function loadMyProgress(){
 if(!student)return;
 const seq=++state.myProgressLoadId;
 try{
  const data=await api('/api/lms/my-progress?page='+state.myProgressPage+'&pageSize=20'+(state.includeArchived?'&includeArchived=true':''));
  if(seq!==state.myProgressLoadId)return;
  state.myProgressRows=data?.items||[];
  state.myProgressPages=Math.max(1,data?.totalPages||1);
  renderMyProgress();
 }catch(e){
  if(seq!==state.myProgressLoadId)return;
  state.myProgressRows=[];renderMyProgress();notice('danger','Unable to load learning progress: '+e.message);
 }
}
function exportMyProgress(){
 const rows=[['Course','Code','Enrollment','Completed lessons','Required lessons','Graded assignments',
  'Required assignments','Passed quizzes','Required quizzes','Lesson completion %','Archived','Completed at (UTC)'],
  ...state.myProgressRows.map(x=>[x.courseTitle,x.courseCode,Number(x.state)===2?'Completed':'Active',
   x.completedLessons,x.requiredLessons,x.gradedAssignments,x.requiredAssignments,
   x.passedQuizzes,x.requiredQuizzes,x.lessonCompletionPercent,x.isArchived?'Yes':'No',x.completedAt||''])];
 const safe=value=>{
  let s=String(value??'');
  if(/^[=+@\-\t\r]/.test(s))s="'"+s;
  return '"'+s.replaceAll('"','""')+'"';
 };
 const csv=rows.map(row=>row.map(safe).join(',')).join('\r\n');
 const url=URL.createObjectURL(new Blob(['\uFEFF'+csv],{type:'text/csv;charset=utf-8'}));
 const a=document.createElement('a');a.href=url;a.download='my-lms-progress-page-'+state.myProgressPage+'.csv';
 a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
}
if(student){
 $('learningMyProgressRefresh').addEventListener('click',()=>loadMyProgress());
 $('learningMyProgressPrev').addEventListener('click',()=>{if(state.myProgressPage>1){state.myProgressPage--;loadMyProgress();}});
 $('learningMyProgressNext').addEventListener('click',()=>{if(state.myProgressPage<state.myProgressPages){state.myProgressPage++;loadMyProgress();}});
 $('learningMyProgressExport').addEventListener('click',exportMyProgress);
 $('learningIncludeArchived').addEventListener('change',e=>{
  state.includeArchived=!!e.target.checked;state.page=1;state.myProgressPage=1;
  ++state.myProgressLoadId;state.myProgressRows=[];renderMyProgress();
  courses();loadMyProgress();
 });
 $('learningHistoricalExport').addEventListener('click',()=>{
  const rows=[['Code','Title','Program','Subject','Status'],...state.courses.map(x=>[
   x.code,x.title,x.academicProgramName||'',x.subjectName||'',x.isActive?'Active':'Archived'])];
  const csv=rows.map(row=>row.map(v=>{let s=String(v??'');if(/^[=+@\\-\\t\\r]/.test(s))s="'"+s;return '"'+s.replaceAll('"','""')+'"';}).join(',')).join('\r\n');
  const url=URL.createObjectURL(new Blob(['\uFEFF'+csv],{type:'text/csv;charset=utf-8'}));
  const a=document.createElement('a');a.href=url;a.download='my-lms-courses-page-'+state.page+'.csv';
  a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
 });
}
$('learningRefresh').addEventListener('click',()=>courses());
$('learningCourse').addEventListener('change',()=>{state.lessonPage=1;state.assignmentPage=1;state.enrollRequestId=null;load();});
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
 const safe=value=>{let s=String(value??'');if(/^[=+@\-\t\r]/.test(s))s="'"+s;return '"'+s.replaceAll('"','""')+'"';};
 const csv=rows.map(row=>row.map(safe).join(',')).join('\r\n');
 const url=URL.createObjectURL(new Blob(['\uFEFF'+csv],{type:'text/csv;charset=utf-8'}));
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
 $('learningReviewExport').addEventListener('click',exportReviewPage);
 $('learningReviewPrev').addEventListener('click',()=>{if(state.reviewPage>1){state.reviewPage--;loadReviews();}});
 $('learningReviewNext').addEventListener('click',()=>{if(state.reviewPage<state.reviewPages){state.reviewPage++;loadReviews();}});
 $('learningReviewFilesClose').addEventListener('click',closeReviewFiles);
 $('learningRubricAssignment').addEventListener('change',loadRubric);
 $('learningRubricAdd').addEventListener('click',()=>{if(state.rubricCriteria.length>=20)return;
  state.rubricCriteria.push({title:'',maxMarks:0});drawRubric();});
 $('learningRubricSave').addEventListener('click',saveRubric);
}
if(student)$('learningStudentRubricClose').addEventListener('click',()=>{
 state.studentRubricLoadId++;$('learningStudentRubricPanel').classList.add('d-none');$('learningStudentRubricRows').replaceChildren();
});
if(manager){
 $('learningInstructorSearch').addEventListener('input',()=>{
  clearTimeout(state.instructorTimer);
  state.instructorTimer=setTimeout(()=>instructors($('learningInstructorSearch').value),250);
 });
 $('learningStudentSearch').addEventListener('input',()=>{
  state.enrollRequestId=null;clearTimeout(state.studentTimer);
  state.studentTimer=setTimeout(()=>searchStudents($('learningStudentSearch').value),250);
 });
 $('learningStudentChoice').addEventListener('change',()=>{state.enrollRequestId=null;});
 $('learningEnrollForm').addEventListener('submit',enrollStudent);
}
new MutationObserver(()=>{if(!sessionValid())clearSensitive();})
 .observe(document.body,{attributes:true,attributeFilter:['data-auth-tenant-id','data-auth-session-reference']});
window.addEventListener('pagehide',clearSensitive);
catalog();instructors();courses();if(student)loadMyProgress();
})();
