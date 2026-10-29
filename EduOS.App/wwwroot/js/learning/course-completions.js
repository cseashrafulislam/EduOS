(() => {
'use strict';
const root=document.getElementById('lmsCompletionApp');if(!root)return;
const $=id=>document.getElementById(id);
const state={courses:[],coursePage:1,coursePages:1,rows:[],page:1,pages:1,request:0,busy:false,exporting:false};
const option=(value,label)=>{const o=document.createElement('option');o.value=value;o.textContent=label;return o;};
const cell=(tr,value)=>{const td=document.createElement('td');td.textContent=String(value??'');tr.append(td);return td;};
function alertUser(type,message){const node=$('lmsCompletionAlert');node.className='alert alert-'+type;node.textContent=message;node.focus();}
async function api(url,method='GET',body,signal){
 const headers={Accept:'application/json'};
 if(method!=='GET'){const token=document.querySelector('meta[name="request-verification-token"]')?.content;
  if(!token)throw Error('Security token missing. Reload the page.');
  headers.RequestVerificationToken=token;headers['Content-Type']='application/json';}
 const response=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers,
  ...(body===undefined?{}:{body:JSON.stringify(body)}),...(signal?{signal}:{})});
 const result=await response.json().catch(()=>null);
 if(!response.ok||!result?.success)throw Error(result?.message||'Request failed.');
 return result.data;
}
async function loadCourses(){
 const old=$('lmsCompletionCourse').value;
 try{const data=await api('/api/lms/courses?page='+state.coursePage+'&pageSize=100');
  state.courses=data?.items||[];state.coursePages=Math.max(1,data?.totalPages||1);
  const select=$('lmsCompletionCourse');select.replaceChildren(option('','Choose a course'));
  state.courses.forEach(x=>select.append(option(x.reference,x.title+' ('+x.code+')')));
  if(state.courses.some(x=>x.reference===old))select.value=old;
  $('lmsCompletionCoursePage').textContent='Page '+state.coursePage+' / '+state.coursePages;
  $('lmsCompletionCoursePrev').disabled=state.coursePage<=1;
  $('lmsCompletionCourseNext').disabled=state.coursePage>=state.coursePages;
  state.page=1;await loadRegister();
 }catch(e){alertUser('danger',e.message);}
}
function render(){
 const body=$('lmsCompletionRows');body.replaceChildren();
 for(const row of state.rows){
  const tr=document.createElement('tr'),elig=row.eligibility||{},completed=row.state===2;
  cell(tr,row.studentName+' ('+row.studentCode+')');
  cell(tr,completed?'Completed':row.state===1?'Active':'Inactive');
  cell(tr,(elig.completedLessons||0)+' / '+(elig.requiredLessons||0));
  cell(tr,(elig.gradedAssignments||0)+' / '+(elig.requiredAssignments||0));
  cell(tr,(elig.passedQuizzes||0)+' / '+(elig.requiredQuizzes||0));
  cell(tr,completed?'Completed':elig.ready?'Ready':(elig.blockers||[]).join(' '));
  const td=cell(tr,'');
  if(row.state===1&&elig.ready){
   const btn=document.createElement('button');btn.type='button';btn.className='btn btn-sm btn-primary';
   btn.textContent='Complete';btn.disabled=state.busy;btn.addEventListener('click',()=>complete(row,btn));td.append(btn);
  }
  body.append(tr);
 }
 if(!state.rows.length){const tr=document.createElement('tr'),td=cell(tr,'No enrollments found.');td.colSpan=7;body.append(tr);}
 $('lmsCompletionPage').textContent='Page '+state.page+' / '+state.pages;
 $('lmsCompletionPrev').disabled=state.page<=1;$('lmsCompletionNext').disabled=state.page>=state.pages;
}
async function loadRegister(){
 const course=$('lmsCompletionCourse').value,seq=++state.request;
 if(!course){state.rows=[];state.pages=1;render();return;}
 const query=new URLSearchParams({page:String(state.page),pageSize:'20',search:$('lmsCompletionSearch').value.trim()});
 try{const data=await api('/api/lms/courses/'+encodeURIComponent(course)+'/completion-register?'+query);
  if(seq!==state.request)return;
  state.rows=data?.items||[];state.pages=Math.max(1,data?.totalPages||1);render();
 }catch(e){if(seq===state.request)alertUser('danger',e.message);}
}
async function complete(row,button){
 if(state.busy||!window.confirm('Record course completion for '+row.studentName+'?'))return;
 state.busy=true;button.disabled=true;
 try{await api('/api/lms/courses/enrollments/'+encodeURIComponent(row.enrollmentId)+'/complete','POST',
  {rowVersion:row.rowVersion});
  alertUser('success','Course completion recorded.');await loadRegister();
 }catch(e){alertUser('danger',e.message);await loadRegister();}
 finally{state.busy=false;}
}
async function exportFiltered(){
 if(state.exporting)return;
 const course=$('lmsCompletionCourse').value,search=$('lmsCompletionSearch').value.trim();
 if(!course){alertUser('danger','Choose a course before exporting.');return;}
 const report=window.EduOSLmsCompletionReport;
 if(!report){alertUser('danger','Report exporter is unavailable. Reload the page.');return;}
 const reference=document.body?.dataset.authSessionReference||'';
 const tenant=document.body?.dataset.authTenantId||'';
 const isCurrent=()=>/^[0-9A-F]{64}$/.test(reference)&&
  document.body?.dataset.authSessionReference===reference&&
  document.body?.dataset.authTenantId===tenant&&
  $('lmsCompletionCourse').value===course&&$('lmsCompletionSearch').value.trim()===search&&
  window.EduOSSessionTransitionPending?.()!==true;
 state.exporting=true;$('lmsCompletionCsv').disabled=true;
 try{
  const query=new URLSearchParams({search});
  const controller=new AbortController(),timeout=window.setTimeout(()=>controller.abort(),15000);
  let data;
  try{data=await api('/api/lms/courses/'+encodeURIComponent(course)+'/completion-export?'+query,'GET',undefined,controller.signal);}
  finally{window.clearTimeout(timeout);}
  const rows=report.validateSnapshot(data,isCurrent);
  if(!isCurrent())throw Error('Course, filter or account changed. Restart the export.');
  const blob=new Blob([report.toCsv(rows)],{type:'text/csv;charset=utf-8'});
  const url=URL.createObjectURL(blob),link=document.createElement('a');
  link.href=url;link.download='lms-course-completions-filtered.csv';link.click();
  window.setTimeout(()=>URL.revokeObjectURL(url),1000);
  alertUser('success','Exported '+rows.length+' matching enrollment(s).');
 }catch(e){alertUser('danger',e.message||'Unable to export the completion register.');}
 finally{state.exporting=false;$('lmsCompletionCsv').disabled=false;}
}
$('lmsCompletionCourse').addEventListener('change',()=>{state.page=1;loadRegister();});
$('lmsCompletionSearchButton').addEventListener('click',()=>{state.page=1;loadRegister();});
$('lmsCompletionSearch').addEventListener('keydown',e=>{if(e.key==='Enter'){e.preventDefault();state.page=1;loadRegister();}});
$('lmsCompletionCoursePrev').addEventListener('click',()=>{if(state.coursePage>1){state.coursePage--;loadCourses();}});
$('lmsCompletionCourseNext').addEventListener('click',()=>{if(state.coursePage<state.coursePages){state.coursePage++;loadCourses();}});
$('lmsCompletionPrev').addEventListener('click',()=>{if(state.page>1){state.page--;loadRegister();}});
$('lmsCompletionNext').addEventListener('click',()=>{if(state.page<state.pages){state.page++;loadRegister();}});
$('lmsCompletionCsv').addEventListener('click',exportFiltered);
$('lmsCompletionPrint').addEventListener('click',()=>window.print());
const help=$('lmsCompletionHelp');
document.addEventListener('keydown',event=>{if(event.key==='Escape'&&help)help.open=false;});
document.addEventListener('pointerdown',event=>{if(help&&!help.contains(event.target))help.open=false;});
loadCourses();
})();
