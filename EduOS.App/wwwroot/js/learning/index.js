(() => {
 'use strict';const root=document.getElementById('learningApp');if(!root)return;
 const el=id=>document.getElementById(id),author=root.dataset.author==='true',student=root.dataset.student==='true';
 const state={courses:[],detail:null,batches:[],subjects:[],editingLesson:null,editingAssignment:null,editingCourse:null};
 const option=(value,label)=>{const x=document.createElement('option');x.value=String(value);x.textContent=label;return x;};
 const cell=value=>{const x=document.createElement('td');x.textContent=value==null?'':String(value);return x;};
 const date=value=>value?String(value).slice(0,10):'';
 const msg=(kind,value)=>{const x=el('learningAlert');x.className='alert alert-'+kind;x.textContent=value;x.focus();};
 async function api(url,method='GET',body){
   const response=await fetch(url,{method,cache:'no-store',credentials:'same-origin',headers:{Accept:'application/json',...(body===undefined?{}:{'Content-Type':'application/json'})},...(body===undefined?{}:{body:JSON.stringify(body)})});
   const result=await response.json().catch(()=>null);if(!response.ok||!result?.success)throw new Error(result?.message||'Request failed.');
   return result.data;
 }
 function choices(id,items,empty){
   const select=el(id),previous=select.value;select.replaceChildren(option('',empty));
   for(const item of items)select.append(option(item.id,item.name));
   if(items.some(x=>String(x.id)===previous))select.value=previous;
 }
 const selected=()=>state.courses.find(x=>x.reference===el('learningCourse').value);
 async function courses(){
   try{const current=el('learningCourse').value;state.courses=await api('/api/lms/courses')||[];
     choices('learningCourse',state.courses.map(x=>({id:x.reference,name:x.title+' — '+x.subjectName})),'Select course');
     if(current&&state.courses.some(x=>x.reference===current))el('learningCourse').value=current;
     if(el('learningCourse').value)await load();
   }catch(e){msg('danger',e.message);}
 }
 async function catalog(){
   if(!author)return;
   try{const data=await api('/api/academic-setup/catalog');state.batches=(data.batches||[]).filter(x=>x.isActive);
     state.subjects=(data.subjects||[]).filter(x=>x.isActive);
     choices('learningBatch',state.batches.map(x=>({id:x.id,name:[x.academicYearName,x.name].filter(Boolean).join(' / ')})),'Select academic batch');
     choices('learningSubject',state.subjects.map(x=>({id:x.id,name:x.name+' ('+x.code+')'})),'Select subject');
   }catch(e){msg('warning','Academic catalog unavailable: '+e.message);}
 }
 async function load(){
   const course=selected();if(!course)return;
   try{state.detail=await api('/api/lms/courses/'+encodeURIComponent(course.reference));el('learningProgress').textContent=(Number(state.detail.course.progressPercentage)||0)+'% completed';
     if(el('learningSync'))el('learningSync').disabled=false;render();}
   catch(e){state.detail=null;msg('danger',e.message);}
 }
 function button(label,fn){const x=document.createElement('button');x.type='button';x.className='btn btn-sm btn-outline-primary';x.textContent=label;x.onclick=fn;return x;}
 function render(){
   const lessons=el('learningLessons'),assignments=el('learningAssignments');lessons.replaceChildren();assignments.replaceChildren();
   for(const x of state.detail?.lessons||[]){
     const tr=document.createElement('tr'),content=cell((x.content||'').slice(0,180)),action=document.createElement('td');
     if(x.videoUrl){const a=document.createElement('a');a.href=x.videoUrl;a.target='_blank';a.rel='noopener noreferrer';a.textContent=' Open video';content.append(a);}
     if(student&&!x.isCompleted)action.append(button('Mark completed',()=>complete(x)));
     if(student&&x.isCompleted)action.textContent='Completed';
     if(author)action.append(button('Edit',()=>editLesson(x)));
     tr.append(cell(x.orderNo),cell(x.title),content,action);lessons.append(tr);
   }
   for(const x of state.detail?.assignments||[]){
     const tr=document.createElement('tr'),action=document.createElement('td');
     if(student&&!x.submissionStatus)action.append(button('Submit answer',()=>submit(x)));
     if(author)action.append(button('Edit',()=>editAssignment(x)));
     tr.append(cell(x.title),cell(date(x.dueDate)),cell(x.totalMark),cell(x.submissionStatus||'Not submitted'),action);assignments.append(tr);
   }
   if(!lessons.childNodes.length){const tr=document.createElement('tr'),td=cell('No lessons.');td.colSpan=4;tr.append(td);lessons.append(tr);}
   if(!assignments.childNodes.length){const tr=document.createElement('tr'),td=cell('No assignments.');td.colSpan=5;tr.append(td);assignments.append(tr);}
 }
 async function saveCourse(e){
   e.preventDefault();if(!e.currentTarget.reportValidity())return;
   const batch=state.batches.find(x=>String(x.id)===el('learningBatch').value);if(!batch)return msg('danger','Select active academic batch.');
   const x=state.editingCourse,body={academicYearId:batch.academicYearId,classId:batch.academicLevelId,sectionId:batch.id,subjectId:Number(el('learningSubject').value),
     teacherId:null,title:el('learningTitle').value.trim(),description:el('learningDescription').value.trim()||null,reference:x?.reference||null,rowVersion:x?.rowVersion||null};
   el('learningSaveCourse').disabled=true;
   try{const c=await api('/api/lms/courses','PUT',body);state.editingCourse=null;await courses();el('learningCourse').value=c.reference;await load();msg('success','Course saved.');}
   catch(e){msg('danger',e.message);}finally{el('learningSaveCourse').disabled=false;}
 }
 function editLesson(x){
   state.editingLesson=x;el('learningOrder').value=x.orderNo;el('learningLessonTitle').value=x.title;
   el('learningVideo').value=x.videoUrl||'';el('learningContent').value=x.content||'';el('learningLessonTitle').focus();
 }
 async function saveLesson(e){
   e.preventDefault();if(!e.currentTarget.reportValidity()||!selected())return;
   const x=state.editingLesson,body={courseReference:selected().reference,reference:x?.reference||null,rowVersion:x?.rowVersion||null,
     title:el('learningLessonTitle').value.trim(),content:el('learningContent').value.trim()||null,videoUrl:el('learningVideo').value.trim()||null,
     attachmentUrl:null,orderNo:Number(el('learningOrder').value),duration:0};
   el('learningSaveLesson').disabled=true;
   try{await api('/api/lms/lessons','PUT',body);state.editingLesson=null;e.currentTarget.reset();await load();msg('success','Lesson saved.');}
   catch(e){msg('danger',e.message);}finally{el('learningSaveLesson').disabled=false;}
 }
 function editAssignment(x){
   state.editingAssignment=x;el('learningAssignmentTitle').value=x.title;el('learningInstructions').value=x.description||'';
   el('learningTotal').value=x.totalMark;const d=new Date(x.dueDate);
   if(Number.isFinite(d.getTime()))el('learningDue').value=d.getFullYear()+'-'+String(d.getMonth()+1).padStart(2,'0')+'-'+String(d.getDate()).padStart(2,'0')+'T'+String(d.getHours()).padStart(2,'0')+':'+String(d.getMinutes()).padStart(2,'0');
 }
 async function saveAssignment(e){
   e.preventDefault();if(!e.currentTarget.reportValidity()||!selected())return;
   const d=new Date(el('learningDue').value);if(!Number.isFinite(d.getTime())||d<=new Date())return msg('danger','Due date must be in the future.');
   const x=state.editingAssignment,body={courseReference:selected().reference,reference:x?.reference||null,rowVersion:x?.rowVersion||null,
     title:el('learningAssignmentTitle').value.trim(),description:el('learningInstructions').value.trim()||null,
     totalMark:Number(el('learningTotal').value),dueDate:d.toISOString(),attachmentUrl:null};
   el('learningSaveAssignment').disabled=true;
   try{await api('/api/lms/assignments','PUT',body);state.editingAssignment=null;e.currentTarget.reset();await load();msg('success','Assignment saved.');}
   catch(e){msg('danger',e.message);}finally{el('learningSaveAssignment').disabled=false;}
 }
 async function complete(x){
   try{await api('/api/lms/lessons/complete','POST',{lessonReference:x.reference});await load();msg('success','Lesson completed.');}
   catch(e){msg('danger',e.message);}
 }
 async function submit(x){
   const value=prompt('Enter your answer (maximum 4000 characters):','');
   if(value===null)return;if(!value.trim()||value.length>4000)return msg('warning','Answer must contain 1–4000 characters.');
   try{await api('/api/lms/assignments/submit','POST',{assignmentReference:x.reference,clientRequestId:crypto.randomUUID(),submissionText:value.trim(),submissionFile:null});
     await load();msg('success','Assignment submitted.');}catch(e){msg('danger',e.message);}
 }
 async function sync(){
   if(!selected()||!confirm('Synchronize this course with active academic enrollments?'))return;
   try{const count=await api('/api/lms/courses/'+encodeURIComponent(selected().reference)+'/sync-enrollment','POST');msg('success',count+' student(s) enrolled.');}
   catch(e){msg('danger',e.message);}
 }
 el('learningCourse').addEventListener('change',load);el('learningRefresh').addEventListener('click',courses);
 el('learningCourseForm')?.addEventListener('submit',saveCourse);
 el('learningLessonForm')?.addEventListener('submit',saveLesson);
 el('learningAssignmentForm')?.addEventListener('submit',saveAssignment);
 el('learningResetLesson')?.addEventListener('click',()=>{state.editingLesson=null;el('learningLessonForm').reset();});
 el('learningSync')?.addEventListener('click',sync);
 courses();catalog();
})();
