(() => {
'use strict';
const root=document.getElementById('liveClassApp');if(!root)return;
const $=id=>document.getElementById(id),author=root.dataset.author==='true',manager=root.dataset.manager==='true';
const state={courses:[],classes:[],page:1,pages:1,editing:null,request:0};
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
  if(author&&new Date(row.startsAt).getTime()>Date.now()){
   actions.append(button('Edit','btn-outline-primary me-1',()=>edit(row)));
   actions.append(button('Cancel','btn-outline-danger',()=>cancel(row)));
  }
  tr.append(actions);body.append(tr);
 }
}
async function loadClasses(){
 const ref=$('liveCourse').value,token=++state.request;
 if(!ref){state.classes=[];empty('Select a course.');return;}
 try{const rows=await api('/api/lms/live-classes/course/'+encodeURIComponent(ref));
  if(token!==state.request)return;state.classes=rows||[];render();
 }catch(e){if(token===state.request){state.classes=[];empty('Unable to load classes.');notice('danger',e.message);}}
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
 e.preventDefault();if(!e.currentTarget.reportValidity())return;
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
 }catch(error){notice('danger',error.message);}finally{$('liveSave').disabled=false;}
}
async function cancel(row){
 if(!confirm('Cancel this scheduled live class?'))return;
 try{await api('/api/lms/live-classes/'+row.reference+'/cancel','POST',{rowVersion:row.rowVersion});
  notice('success','Live class cancelled.');reset();await loadClasses();
 }catch(error){notice('danger',error.message);}
}
$('liveCourse').onchange=()=>{reset();loadClasses();};
$('liveRefresh').onclick=()=>courses();
$('liveCoursePrev').onclick=()=>{if(state.page>1){state.page--;courses();}};
$('liveCourseNext').onclick=()=>{if(state.page<state.pages){state.page++;courses();}};
$('livePrint').onclick=()=>window.print();
if(author){$('liveForm').onsubmit=save;$('liveClear').onclick=reset;}
Promise.all([courses(),instructors()]);
})();
