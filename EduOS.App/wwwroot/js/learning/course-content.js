(() => {
'use strict';
const root=document.getElementById('contentApp');if(!root)return;
const el=id=>document.getElementById('content'+id),author=root.dataset.author==='true',student=root.dataset.student==='true';
const state={courses:[],page:1,pages:1,course:null,lessons:[],seq:0,includeArchived:false,fileRequestId:null,revisionPage:1,revisionTotal:0};
const initialTenant=String(document.body?.dataset.authTenantId||'');
const initialSession=String(document.body?.dataset.authSessionReference||'');
const sessionValid=()=>String(document.body?.dataset.authTenantId||'')===initialTenant&&
 String(document.body?.dataset.authSessionReference||'')===initialSession&&
 window.EduOSSessionTransitionPending?.()!==true;
const node=(tag,text)=>{const e=document.createElement(tag);if(text!==undefined)e.textContent=text;return e;};
const alert=(message,kind='danger')=>{const e=el('Alert');e.className='alert alert-'+kind;e.textContent=message;e.focus();};
async function api(url,method='GET',body){
 if(!sessionValid())throw Error('Session changed. Reload before accessing course content.');
 const headers={Accept:'application/json'};
 if(method!=='GET'){
  const token=document.querySelector('meta[name="request-verification-token"]')?.content;
  if(!token)throw Error('Security token missing; reload the page.');
  headers.RequestVerificationToken=token;headers['Content-Type']='application/json';
 }
 const response=await fetch(url,{method,headers,credentials:'same-origin',cache:'no-store',...(body===undefined?{}:{body:JSON.stringify(body)})});
 const data=await response.json().catch(()=>null);
 if(!sessionValid())throw Error('Session changed. Reload before accessing course content.');
 if(!response.ok||!data?.success)throw Error(data?.message||'Request failed.');
 return data.data;
}
function fill(id,items,label,formatter){
 const e=el(id),old=e.value;e.replaceChildren(new Option(label,''));
 for(const item of items)e.add(new Option(formatter(item),String(item.reference??item.id)));
 if(items.some(x=>String(x.reference??x.id)===old))e.value=old;
}
function allLessons(){
 const d=state.course;if(!d)return [];
 return [...(d.unsectionedLessons||[]),...(d.sections||[]).flatMap(s=>s.lessons||[])];
}
function resetSection(){
 el('SectionForm').reset();el('SectionId').value='';el('SectionVersion').value='';
 el('SectionOrder').value=1;
}
function resetResource(){
 el('ResourceForm').reset();el('ResourceId').value='';el('ResourceVersion').value='';
 el('ResourceOrder').value=1;state.fileRequestId=null;
}
function btn(label,action,style='btn-outline-primary'){
 const e=node('button',label);e.type='button';e.className='btn btn-sm '+style;e.addEventListener('click',action);return e;
}
function td(text){return node('td',text==null?'':String(text));}
function link(url,title){
 try{const parsed=new URL(url);if(parsed.protocol!=='https:')return node('span','Invalid link');
  const a=node('a',title);a.href=parsed.href;a.target='_blank';a.rel='noopener noreferrer';return a;
 }catch{return node('span','Invalid link');}
}
function editSection(row){
 el('SectionId').value=row.id;el('SectionVersion').value=row.rowVersion;
 el('SectionTitle').value=row.title;el('SectionOrder').value=row.displayOrder;
 el('SectionPublished').checked=row.isPublished;el('SectionTitle').focus();
}
function editResource(row){
 el('ResourceId').value=row.id;el('ResourceVersion').value=row.rowVersion;
 el('ResourceLesson').value=row.lessonReference;el('ResourceTitle').value=row.title||'';
 el('ResourceOrder').value=row.displayOrder;el('ResourceUrl').value=row.externalUrl||'';
 el('ResourceFile').value=row.fileAssetReference||'';el('ResourceUpload').value='';state.fileRequestId=null;el('ResourceTitle').focus();
}
async function removeResource(row){
 if(!confirm('Remove this lesson resource?'))return;
 await run(null,async()=>{
  await api('/api/lms/course-content/resources/'+row.id+'/remove','POST',{rowVersion:row.rowVersion});
  await loadCourse();alert('Resource removed.','success');
 });
}
function render(){
 const data=state.course,body=el('Rows');body.replaceChildren();
 el('Heading').textContent=data?.courseTitle||'Course content';
 if(student)el('HistoricalNotice').classList.toggle('d-none',!data?.isArchived);
 if(!data){state.lessons=[];const tr=node('tr'),cell=td('Select a course.');cell.colSpan=5;tr.append(cell);body.append(tr);return;}
 const sections=data.sections||[],unsectioned=data.unsectionedLessons||[];
 state.lessons=allLessons();
 if(author){el('Author').classList.toggle('d-none',!data.canEdit);
  fill('LessonSelect',state.lessons,'Select existing lesson',x=>x.title);
  fill('ResourceLesson',state.lessons,'Select existing lesson',x=>x.title);
  fill('RevisionLesson',state.lessons,'Select lesson / পাঠ',x=>x.title);
  el('RevisionPanel').classList.remove('d-none');
  fill('SectionSelect',sections,'Unsectioned',x=>x.displayOrder+' · '+x.title);
 }
 function appendLesson(section,lesson){
  const tr=node('tr');tr.append(td(section||'Unsectioned'),td(lesson.title));
  const resourceCell=td('');
  for(const resource of lesson.resources||[]){
   const line=node('div');line.className='mb-1';
   const label=resource.title||resource.fileName||resource.externalUrl||'Resource';
   if(resource.externalUrl)line.append(link(resource.externalUrl,label));
   else if(resource.fileAssetReference&&resource.fileDownloadable){
    const a=node('a',label);a.href='/api/lms/course-content/resources/'+encodeURIComponent(resource.id)+'/download';
    a.rel='nofollow';line.append(a);
   } else line.append(node('span',label+' (file unavailable)'));
   if(data.canEdit){line.append(document.createTextNode(' '),
    btn('Edit',()=>editResource(resource)),document.createTextNode(' '),
    btn('Remove',()=>removeResource(resource),'btn-outline-danger'));}
   resourceCell.append(line);
  }
  if(!resourceCell.childNodes.length)resourceCell.append(node('span','—'));
  tr.append(resourceCell,td(lesson.isPublished?'Published':'Draft'));
  const action=td('');action.className='content-actions';
  if(data.canEdit)action.append(btn('Move lesson',()=>{
   el('LessonSelect').value=lesson.reference;
   el('SectionSelect').value=lesson.courseSectionId||'';
   el('LessonSelect').focus();
  }));
  tr.append(action);body.append(tr);
 }
 for(const section of sections){
  const tr=node('tr');const title=td(section.displayOrder+' · '+section.title);
  title.className='fw-semibold';tr.append(title,td((section.lessons||[]).length+' lessons'),td('—'),td(section.isPublished?'Published':'Draft'));
  const action=td('');action.className='content-actions';
  if(data.canEdit)action.append(btn('Edit section',()=>editSection(section)));
  tr.append(action);body.append(tr);
  for(const lesson of section.lessons||[])appendLesson(section.title,lesson);
 }
 for(const lesson of unsectioned)appendLesson('',lesson);
 if(!sections.length&&!unsectioned.length){const tr=node('tr'),cell=td('No sections or lessons. Create lessons in Learning.');cell.colSpan=5;tr.append(cell);body.append(tr);}
}
async function loadCourse(){
 const ref=el('Course').value,seq=++state.seq;
 if(!ref){state.course=null;render();return;}
 try{const data=await api('/api/lms/course-content/'+encodeURIComponent(ref));
  if(seq!==state.seq||!sessionValid())return;state.course=data;render();
 }catch(error){if(seq!==state.seq)return;state.course=null;render();alert(error.message);}
}
async function loadCourses(){
 const old=el('Course').value,seq=++state.seq;
 try{
  const data=await api('/api/lms/courses?page='+state.page+'&pageSize=20'+(student&&state.includeArchived?'&includeArchived=true':''));
  if(seq!==state.seq||!sessionValid())return;
  state.courses=data?.items||[];
  state.pages=Math.max(1,data?.totalPages||Math.ceil((data?.totalCount||0)/20));
  fill('Course',state.courses,'Select course',x=>x.title+' — '+x.code+(x.isActive?'':' [Archived]'));
  if(state.courses.some(x=>x.reference===old))el('Course').value=old;
  else if(state.courses.length)el('Course').value=state.courses[0].reference;
  el('Page').textContent='Page '+state.page+' / '+state.pages;
  el('Prev').disabled=state.page<=1;el('Next').disabled=state.page>=state.pages;
  if(author){resetSection();resetResource();state.revisionPage=1;}await loadCourse();
 }catch(error){alert(error.message);}
}
async function run(form,action){
 const button=form?.querySelector('[type="submit"]');if(button)button.disabled=true;
 try{await action();}catch(error){alert(error.message);}finally{if(button)button.disabled=false;}
}
if(author){
 el('SectionClear').onclick=resetSection;el('ResourceClear').onclick=resetResource;
 el('ResourceUpload').addEventListener('change',()=>{state.fileRequestId=null;el('ResourceFile').value='';});
 el('SectionForm').onsubmit=e=>{e.preventDefault();run(e.currentTarget,async()=>{
  const id=el('SectionId').value,courseReference=el('Course').value;
  if(!courseReference)throw Error('Select a course.');
  const request={courseReference,title:el('SectionTitle').value.trim(),displayOrder:Number(el('SectionOrder').value),
   isPublished:el('SectionPublished').checked,rowVersion:id?el('SectionVersion').value:null};
  await api('/api/lms/course-content/sections'+(id?'/'+id:''),id?'PUT':'POST',request);
  resetSection();await loadCourse();alert('Section saved.','success');
 });};
 el('AssignForm').onsubmit=e=>{e.preventDefault();run(e.currentTarget,async()=>{
  const lesson=state.lessons.find(x=>x.reference===el('LessonSelect').value);
  if(!lesson)throw Error('Select an existing lesson.');
  const request={courseReference:el('Course').value,lessonReference:lesson.reference,
   courseSectionId:el('SectionSelect').value?Number(el('SectionSelect').value):null,rowVersion:lesson.rowVersion};
  await api('/api/lms/course-content/lessons/section','POST',request);
  await loadCourse();alert('Lesson assignment saved.','success');
 });};
 el('ResourceForm').onsubmit=e=>{e.preventDefault();run(e.currentTarget,async()=>{
  const id=el('ResourceId').value,url=el('ResourceUrl').value.trim();
  let file=el('ResourceFile').value.trim();
  const selected=el('ResourceUpload').files?.[0];
  if(selected){
   if(url)throw Error('Choose either a private file or HTTPS URL.');
   if(selected.size<1||selected.size>5242880)throw Error('File must be 5 MB or smaller.');
   const lessonReference=el('ResourceLesson').value;
   if(!lessonReference)throw Error('Select an existing lesson.');
   if(!state.fileRequestId)state.fileRequestId=crypto.randomUUID();
   const token=document.querySelector('meta[name="request-verification-token"]')?.content;
   if(!token)throw Error('Security token missing; reload the page.');
   const payload=new FormData();payload.append('file',selected);payload.append('requestId',state.fileRequestId);
   const response=await fetch('/api/lms/course-content/lessons/'+encodeURIComponent(lessonReference)+'/files',
    {method:'POST',headers:{RequestVerificationToken:token,Accept:'application/json'},
     credentials:'same-origin',cache:'no-store',body:payload});
   const result=await response.json().catch(()=>null);
   if(!response.ok||!result?.success)throw Error(result?.message||'Private upload failed.');
   file=result.data?.reference||'';
   el('ResourceFile').value=file;
  }
  if(Boolean(url)===Boolean(file))throw Error('Choose either a private file or HTTPS URL.');
  const request={lessonReference:el('ResourceLesson').value,title:el('ResourceTitle').value.trim()||null,
   externalUrl:url||null,fileAssetReference:file||null,displayOrder:Number(el('ResourceOrder').value),
   rowVersion:id?el('ResourceVersion').value:null};
  if(!request.lessonReference)throw Error('Select an existing lesson.');
  await api('/api/lms/course-content/resources'+(id?'/'+id:''),id?'PUT':'POST',request);
  resetResource();await loadCourse();alert('Resource saved.','success');
 });};
}
if(student)el('IncludeArchived').addEventListener('change',e=>{state.includeArchived=e.target.checked;state.page=1;loadCourses();});
el('Course').onchange=()=>{if(author){resetSection();resetResource();}loadCourse();};
el('Refresh').onclick=loadCourses;
el('Prev').onclick=()=>{if(state.page>1){state.page--;loadCourses();}};
el('Next').onclick=()=>{if(state.page<state.pages){state.page++;loadCourses();}};
el('Print').onclick=()=>window.print();
el('Csv').onclick=async()=>{
 const ref=el('Course').value,seq=state.seq;
 if(!ref)return alert('Select a course before exporting.');
 el('Csv').disabled=true;
 try{
  const response=await fetch('/api/lms/course-content/'+encodeURIComponent(ref)+'/export',
   {headers:{Accept:'text/csv'},credentials:'same-origin',cache:'no-store'});
  if(!response.ok){const error=await response.json().catch(()=>null);throw Error(error?.message||'Export unavailable.');}
  const blob=await response.blob();
  if(seq!==state.seq||ref!==el('Course').value||!sessionValid())return;
  const url=URL.createObjectURL(blob),a=node('a');
  a.href=url;a.download='lms-course-content.csv';document.body.append(a);a.click();a.remove();
  setTimeout(()=>URL.revokeObjectURL(url),1000);
 }catch(error){if(seq===state.seq)alert(error.message);}
 finally{el('Csv').disabled=false;}
};
async function loadRevisions(){
 if(!author||!sessionValid())return;
 const ref=el('RevisionLesson').value,seq=state.seq,body=el('RevisionRows');
 body.replaceChildren();
 if(!ref){const tr=node('tr'),cell=td('Select a lesson / পাঠ নির্বাচন করুন');cell.colSpan=6;tr.append(cell);body.append(tr);return;}
 try{
  const data=await api('/api/lms/course-content/lessons/'+encodeURIComponent(ref)+'/revisions?page='+state.revisionPage+'&pageSize=20');
  if(seq!==state.seq||ref!==el('RevisionLesson').value||!sessionValid())return;
  state.revisionTotal=data.totalCount||0;
  for(const rev of data.items||[]){
   const tr=node('tr');
   tr.append(td(rev.revisionNo),td(rev.capturedAt?new Date(rev.capturedAt).toISOString():''),
    td(rev.changeKind),td(rev.title),td(rev.isPublished?'Published / প্রকাশিত':'Draft / খসড়া'),
    td((rev.resources||[]).length));
   body.append(tr);
  }
  if(!body.childNodes.length){const tr=node('tr'),cell=td('No revisions / কোনো সংস্করণ নেই');cell.colSpan=6;tr.append(cell);body.append(tr);}
  el('RevisionPage').textContent=String(state.revisionPage)+' / '+Math.max(1,Math.ceil(state.revisionTotal/20));
  el('RevisionPrev').disabled=state.revisionPage<=1;
  el('RevisionNext').disabled=state.revisionPage*20>=state.revisionTotal;
 }catch(error){if(seq===state.seq)alert(error.message);}
}
async function exportRevisions(){
 const ref=el('RevisionLesson').value,seq=state.seq;
 if(!ref)return alert('Select a lesson / পাঠ নির্বাচন করুন');
 el('RevisionExport').disabled=true;
 try{
  const response=await fetch('/api/lms/course-content/lessons/'+encodeURIComponent(ref)+'/revisions/export',
   {credentials:'same-origin',cache:'no-store',headers:{Accept:'text/csv'}});
  if(!response.ok){const error=await response.json().catch(()=>null);throw Error(error?.message||'Export unavailable');}
  const blob=await response.blob();
  if(seq!==state.seq||ref!==el('RevisionLesson').value||!sessionValid())return;
  const url=URL.createObjectURL(blob),a=node('a');
  a.href=url;a.download='lms-lesson-revisions.csv';document.body.append(a);a.click();a.remove();
  setTimeout(()=>URL.revokeObjectURL(url),1000);
 }catch(error){if(seq===state.seq)alert(error.message);}
 finally{el('RevisionExport').disabled=false;}
}
if(author){
 el('RevisionLesson').addEventListener('change',()=>{state.revisionPage=1;loadRevisions();});
 el('RevisionRefresh').onclick=()=>{state.revisionPage=1;loadRevisions();};
 el('RevisionPrev').onclick=()=>{if(state.revisionPage>1){state.revisionPage--;loadRevisions();}};
 el('RevisionNext').onclick=()=>{if(state.revisionPage*20<state.revisionTotal){state.revisionPage++;loadRevisions();}};
 el('RevisionExport').onclick=exportRevisions;
}

function clearPrivate(){
 state.seq++;state.course=null;state.courses=[];state.lessons=[];state.fileRequestId=null;state.revisionTotal=0;state.revisionPage=1;
 if(author){el('RevisionRows').replaceChildren();el('RevisionPanel').classList.add('d-none');}
 el('Course').replaceChildren(new Option('Select course',''));
 if(author)el('Author').classList.add('d-none');
 render();
}
const observer=new MutationObserver(()=>{if(!sessionValid()){clearPrivate();observer.disconnect();}});
observer.observe(document.body,{attributes:true,attributeFilter:['data-auth-session-reference','data-auth-tenant-id']});
window.addEventListener('pagehide',clearPrivate);
if(sessionValid())loadCourses();
})();