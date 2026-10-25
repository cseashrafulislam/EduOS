(() => {
'use strict';
const root=document.getElementById('liveClassApp');if(!root)return;
const $=id=>document.getElementById(id),author=root.dataset.author==='true',manager=root.dataset.manager==='true';
const state={courses:[],classes:[],recordings:[],recordingPage:1,recordingPages:1,recordingTotal:0,
 page:1,pages:1,editing:null,request:0,recordingRequest:0,pendingRequest:0,pending:[],pendingPage:1,pendingPages:1,pendingTotal:0,selectedPending:null,locked:author,lockReason:null};
function setLock(locked,reason){
 if(!author)return;
 state.locked=locked;state.lockReason=reason||null;
 const banner=$('liveLock');banner.classList.toggle('d-none',!locked);
 banner.textContent=reason||'Course authoring is unavailable until the current permissions are verified.';
 $('liveForm').querySelectorAll('input,select,button').forEach(x=>{x.disabled=locked;});
 $('livePublishForm').querySelectorAll('input,button').forEach(x=>{x.disabled=locked;});
 render();renderPending();
}
const element=(tag,value)=>{const x=document.createElement(tag);if(value!==undefined)x.textContent=value;return x;};
const notice=(type,value)=>{const x=$('liveAlert');x.className='alert alert-'+type;x.textContent=value;x.focus();};
async function api(url,method='GET',body){
 const headers={Accept:'application/json'};
 if(body!==undefined)headers['Content-Type']='application/json';
 if(method!=='GET'){
  const token=document.querySelector('meta[name="request-verification-token"]')?.content;
  if(!token)throw Error('Security token unavailable. Reload the page.');
  headers.RequestVerificationToken=token;
 }
 const response=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers,...(body===undefined?{}:{body:JSON.stringify(body)})});
 const data=await response.json().catch(()=>null);
 if(!response.ok||!data?.success)throw Error(data?.message||'Request failed.');
 return data.data;
}
function select(id,items,label){
 const target=$(id),old=target.value;target.replaceChildren(new Option(label,''));
 for(const item of items)target.add(new Option(item.name,item.id));
 if(items.some(x=>String(x.id)===old))target.value=old;
}
const localDate=iso=>{const date=new Date(iso);return Number.isFinite(date.getTime())?date.toLocaleString():'';};
const inputDate=iso=>{const d=new Date(iso);return Number.isFinite(d.getTime())?new Date(d.getTime()-d.getTimezoneOffset()*60000).toISOString().slice(0,16):'';};
const utc=id=>{const v=$(id).value;if(!v)return null;const d=new Date(v);return Number.isFinite(d.getTime())?d.toISOString():null;};
function link(url,label){
 if(!url)return element('span','—');
 try{const u=new URL(url);if(u.protocol!=='https:')return element('span','Invalid link');
  const a=element('a',label);a.href=u.href;a.target='_blank';a.rel='noopener noreferrer';return a;
 }catch{return element('span','Invalid link');}
}
function button(label,cls,callback){
 const x=element('button',label);x.type='button';x.className='btn btn-sm '+cls;x.onclick=callback;return x;
}
function empty(text){const tr=element('tr'),td=element('td',text);td.colSpan=6;td.className='text-muted text-center p-3';tr.append(td);$('liveRows').replaceChildren(tr);}
function render(){
 const body=$('liveRows');body.replaceChildren();
 if(!state.classes.length){empty('No live classes in this course and date range.');return;}
 for(const row of state.classes){
  const tr=element('tr');
  for(const value of [row.title,row.instructorName,localDate(row.startsAt),localDate(row.endsAt)])tr.append(element('td',value||''));
  const resources=element('td');resources.append(link(row.joinUrl,'Join'));
  if(row.recordingUrl)resources.append(document.createTextNode(' · '),link(row.recordingUrl,'Recording'));
  tr.append(resources);
  const actions=element('td');actions.className='live-actions';
  if(author&&!state.locked&&new Date(row.startsAt).getTime()>Date.now()){
   actions.append(button('Edit','btn-outline-primary me-1',()=>edit(row)));
   actions.append(button('Cancel','btn-outline-danger',()=>cancel(row)));
  }
  tr.append(actions);body.append(tr);
 }
}
function renderRecordings(){
 const body=$('liveRecordingRows');body.replaceChildren();
 $('liveRecordingCount').textContent=state.recordingTotal+' recording(s)';
 $('liveRecordingPage').textContent='Page '+state.recordingPage+' / '+state.recordingPages;
 $('liveRecordingPrev').disabled=state.recordingPage<=1;
 $('liveRecordingNext').disabled=state.recordingPage>=state.recordingPages;
 if(!state.recordings.length){
  const tr=element('tr'),td=element('td','No recordings available for this course and enrollment.');
  td.colSpan=4;td.className='text-muted text-center p-3';tr.append(td);body.append(tr);return;
 }
 for(const row of state.recordings){
  const tr=element('tr');
  for(const value of [row.title,row.instructorName,localDate(row.startsAt)])tr.append(element('td',value||''));
  const resource=element('td');resource.append(link(row.recordingUrl,'Watch recording'));tr.append(resource);body.append(tr);
 }
}
async function loadRecordings(){
 const ref=$('liveCourse').value,token=++state.recordingRequest;
 if(!ref){state.recordings=[];state.recordingTotal=0;state.recordingPages=1;renderRecordings();return;}
 try{
  const data=await api('/api/lms/live-classes/course/'+encodeURIComponent(ref)+
   '/recordings?page='+state.recordingPage+'&pageSize=20');
  if(token!==state.recordingRequest)return;
  state.recordings=data?.items||[];state.recordingTotal=data?.totalCount||0;
  state.recordingPages=Math.max(1,data?.totalPages||Math.ceil(state.recordingTotal/20));
  renderRecordings();
 }catch(e){if(token===state.recordingRequest){
  state.recordings=[];state.recordingTotal=0;state.recordingPages=1;renderRecordings();
  notice('danger','Unable to load recordings: '+e.message);
 }}
}

function renderPending(){
 if(!author)return;
 const body=$('livePendingRows');body.replaceChildren();
 $('livePendingCount').textContent=state.pendingTotal+' session(s)';
 $('livePendingPage').textContent='Page '+state.pendingPage+' / '+state.pendingPages;
 $('livePendingPrev').disabled=state.pendingPage<=1;
 $('livePendingNext').disabled=state.pendingPage>=state.pendingPages;
 if(!state.pending.length){
  const tr=element('tr'),td=element('td','No unpublished past sessions on this page.');
  td.colSpan=4;td.className='text-muted text-center p-3';tr.append(td);body.append(tr);return;
 }
 for(const row of state.pending){
  const tr=element('tr');
  for(const value of [row.title,row.instructorName,localDate(row.endsAt)])tr.append(element('td',value||''));
  const actions=element('td');actions.className='live-actions';
  const publish=button('Publish recording','btn-outline-primary',()=>selectPending(row));
  publish.disabled=state.locked;actions.append(publish);tr.append(actions);body.append(tr);
 }
}
function selectPending(row){
 if(state.locked)return notice('warning',state.lockReason||'Course authoring is locked.');
 state.selectedPending=row;
 $('livePublishClass').textContent=row.title+' — ended '+localDate(row.endsAt);
 $('livePublishUrl').value='';
 $('livePublishForm').classList.remove('d-none');
 $('livePublishUrl').focus();
}
async function loadPending(){
 if(!author)return;
 const ref=$('liveCourse').value,token=++state.pendingRequest;
 state.selectedPending=null;$('livePublishForm').classList.add('d-none');
 if(!ref){state.pending=[];state.pendingTotal=0;state.pendingPages=1;renderPending();return;}
 try{
  const data=await api('/api/lms/live-classes/course/'+encodeURIComponent(ref)+
   '/pending-recordings?page='+state.pendingPage+'&pageSize=20');
  if(token!==state.pendingRequest)return;
  state.pending=data?.items||[];state.pendingTotal=data?.totalCount||0;
  state.pendingPages=Math.max(1,data?.totalPages||Math.ceil(state.pendingTotal/20));
  renderPending();
 }catch(e){if(token===state.pendingRequest){
  state.pending=[];state.pendingTotal=0;state.pendingPages=1;renderPending();
  notice('danger','Unable to load pending recordings: '+e.message);
 }}
}
async function publishRecording(e){
 e.preventDefault();
 const row=state.selectedPending;
 if(!row||state.locked)return notice('warning',state.lockReason||'Choose an unlocked session.');
 if(!e.currentTarget.reportValidity())return;
 const url=$('livePublishUrl').value.trim();
 if(!url.startsWith('https://'))return notice('warning','An HTTPS recording URL is required.');
 $('livePublishSave').disabled=true;
 try{
  await api('/api/lms/live-classes/'+encodeURIComponent(row.reference)+'/recording',
   'POST',{recordingUrl:url,rowVersion:row.rowVersion});
  notice('success','Recording published. The URL is now immutable.');
  await loadClasses();
 }catch(error){notice('danger',error.message);}
 finally{$('livePublishSave').disabled=state.locked;}
}

function exportRecordings(){
 if(!state.recordings.length)return notice('warning','No recordings on this page to export.');
 const safe=value=>{
  let v=String(value??'');
  if(/^[=+@\-\t\r]/.test(v))v="'"+v;
  return '"'+v.replace(/"/g,'""')+'"';
 };
 const rows=[['Class','Instructor','Starts','Ends','Recording URL'],
  ...state.recordings.map(x=>[x.title,x.instructorName,x.startsAt,x.endsAt,x.recordingUrl])];
 const csv='\uFEFF'+rows.map(row=>row.map(safe).join(',')).join('\r\n');
 const blob=new Blob([csv],{type:'text/csv;charset=utf-8'}),url=URL.createObjectURL(blob);
 const a=document.createElement('a');a.href=url;a.download='live-recordings-page-'+state.recordingPage+'.csv';
 document.body.append(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),1000);
}
async function loadClasses(){
 const ref=$('liveCourse').value,token=++state.request;
 if(!ref){state.classes=[];state.recordings=[];state.recordingTotal=0;state.recordingPages=1;
  if(author){setLock(true,'Select a course to manage live classes.');loadPending();}
   empty('Select a course.');renderRecordings();return;}
 if(author)setLock(true,'Checking course authoring permissions…');
 try{const [rows,lock]=await Promise.all([
  api('/api/lms/live-classes/course/'+encodeURIComponent(ref)),
  author?api('/api/lms/courses/'+encodeURIComponent(ref)+'/authoring-lock')
   .catch(error=>({isLocked:true,reason:'Cannot verify course authoring permissions: '+error.message})):Promise.resolve(null)
 ]);
  if(token!==state.request)return;
  if(author)setLock(!!lock?.isLocked,lock?.reason||null);
  state.classes=rows||[];render();
 }catch(e){if(token===state.request){state.classes=[];empty('Unable to load classes.');notice('danger',e.message);}}
 if(token===state.request)await Promise.all([loadRecordings(),loadPending()]);
}
async function courses(){
 const old=$('liveCourse').value,token=++state.request;
 try{const data=await api('/api/lms/courses?page='+state.page+'&pageSize=20');
  if(token!==state.request)return;
  state.courses=data?.items||[];state.pages=Math.max(1,data?.totalPages||1);
  select('liveCourse',state.courses.map(x=>({id:x.reference,name:x.title+' — '+x.code})),'Select course');
  if(state.courses.some(x=>x.reference===old))$('liveCourse').value=old;
  $('liveCoursePage').textContent='Page '+state.page+' / '+state.pages;
  $('liveCoursePrev').disabled=state.page<=1;$('liveCourseNext').disabled=state.page>=state.pages;
  reset();await loadClasses();
 }catch(e){notice('danger',e.message);}
}
async function instructors(){
 if(!manager)return;
 try{const rows=await api('/api/lms/instructors?take=100');
  select('liveInstructor',(rows||[]).map(x=>({id:x.employeeReference,name:x.name+' ('+x.employeeCode+')'})),'Choose instructor');
 }catch(e){notice('warning','Instructor lookup unavailable: '+e.message);}
}
function reset(){
 state.editing=null;
 if(!author)return;
 $('liveForm').reset();$('liveFormTitle').textContent='Schedule a class';
 $('liveSave').textContent='Save class';
 if(manager){const course=state.courses.find(x=>x.reference===$('liveCourse').value);
  $('liveInstructor').value=course?.primaryInstructorReference||'';}
}
function edit(row){
 state.editing=row;$('liveFormTitle').textContent='Edit class';$('liveSave').textContent='Update class';
 $('liveTitle').value=row.title;$('liveStart').value=inputDate(row.startsAt);$('liveEnd').value=inputDate(row.endsAt);
 $('liveJoin').value=row.joinUrl||'';$('liveRecording').value=row.recordingUrl||'';
 if(manager)$('liveInstructor').value=row.instructorReference;
 $('liveTitle').focus();
}
async function save(e){
 e.preventDefault();if(state.locked)return notice('warning',state.lockReason||'Course authoring is locked.');
 if(!e.currentTarget.reportValidity())return;
 const course=$('liveCourse').value;
 if(!course)return notice('warning','Select a course.');
 const startsAt=utc('liveStart'),endsAt=utc('liveEnd');
 if(!startsAt||!endsAt||new Date(endsAt)<=new Date(startsAt))return notice('warning','End time must be after start time.');
 const previous=state.editing;
 const body={courseReference:course,instructorReference:manager?$('liveInstructor').value:'00000000-0000-0000-0000-000000000000',
  title:$('liveTitle').value.trim(),startsAt,endsAt,joinUrl:$('liveJoin').value.trim()||null,
  recordingUrl:$('liveRecording').value.trim()||null,rowVersion:previous?.rowVersion||null};
 if(manager&&!body.instructorReference)return notice('warning','Choose an instructor.');
 $('liveSave').disabled=true;
 try{await api('/api/lms/live-classes'+(previous?'/'+previous.reference:''),previous?'PUT':'POST',body);
  notice('success','Live class saved.');reset();await loadClasses();
 }catch(error){notice('danger',error.message);}finally{$('liveSave').disabled=state.locked;}
}
async function cancel(row){
 if(state.locked)return notice('warning',state.lockReason||'Course authoring is locked.');
 if(!confirm('Cancel this scheduled live class?'))return;
 try{await api('/api/lms/live-classes/'+row.reference+'/cancel','POST',{rowVersion:row.rowVersion});
  notice('success','Live class cancelled.');reset();await loadClasses();
 }catch(error){notice('danger',error.message);}
}
$('liveCourse').onchange=()=>{state.recordingPage=1;state.pendingPage=1;reset();loadClasses();};
$('liveRefresh').onclick=()=>courses();
$('liveCoursePrev').onclick=()=>{if(state.page>1){state.page--;courses();}};
$('liveCourseNext').onclick=()=>{if(state.page<state.pages){state.page++;courses();}};
$('livePrint').onclick=()=>window.print();
$('liveExport').onclick=exportRecordings;
$('liveRecordingPrev').onclick=()=>{if(state.recordingPage>1){state.recordingPage--;loadRecordings();}};
$('liveRecordingNext').onclick=()=>{if(state.recordingPage<state.recordingPages){state.recordingPage++;loadRecordings();}};
if(author){
 $('liveForm').onsubmit=save;$('liveClear').onclick=reset;
 $('livePublishForm').onsubmit=publishRecording;
 $('livePendingPrev').onclick=()=>{if(state.pendingPage>1){state.pendingPage--;loadPending();}};
 $('livePendingNext').onclick=()=>{if(state.pendingPage<state.pendingPages){state.pendingPage++;loadPending();}};
}
if(author)setLock(true,'Select a course to manage live classes.');
Promise.all([courses(),instructors()]);
})();
