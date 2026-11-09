(()=>{'use strict';const root=document.getElementById('adaptiveStudy');if(!root)return;
const $=id=>document.getElementById(id),bn=root.dataset.lang==='bn',node=(tag,value)=>{const el=document.createElement(tag);if(value!==undefined)el.textContent=String(value);return el;};
const identity=[document.body?.dataset.authTenantId||'',document.body?.dataset.authSessionReference||''];
const safe=()=>identity[0]===(document.body?.dataset.authTenantId||'')&&identity[1]===(document.body?.dataset.authSessionReference||'')&&!window.EduOSSessionTransitionPending?.();
let plan=null,page=1,pages=1,epoch=0,saveKey=null,coursePage=0;
const alert=(kind,message)=>{const el=$('adaptiveAlert');el.className='alert alert-'+kind;el.textContent=message;el.focus();};
async function api(url,method='GET',body){if(!safe())throw Error('Session changed. Reload.');
 const headers={Accept:'application/json'};
 if(method!=='GET'){const token=document.querySelector('meta[name="request-verification-token"]')?.content;
  if(!token)throw Error('Security token missing. Reload.');headers.RequestVerificationToken=token;headers['Content-Type']='application/json';}
 const result=await fetch(url,{method,headers,credentials:'same-origin',cache:'no-store',...(body?{body:JSON.stringify(body)}:{})});
 const json=await result.json().catch(()=>null);if(!safe())throw Error('Session changed. Reload.');
 if(!result.ok||!json?.success)throw Error(json?.message||'Request failed.');return json.data;}
function render(data){plan=data;const body=$('adaptiveSkillRows');body.replaceChildren();$('adaptiveSave').disabled=!data||!data.skills?.length;
 for(const item of data?.skills||[]){const tr=node('tr');const title=node('td');title.append(node('strong',item.chapter),node('div',item.skillCode));
  const observed=node('td',item.observedPercent==null?'—':item.observedPercent+'%');
  const goal=node('td',item.targetPercent+'%'),priority=node('td',item.priority==='NoEvidence'?(bn?'তথ্য নেই':'No evidence'):item.priority);
  const practice=node('td');practice.append(node('span',String(item.practiceAttemptsRemaining||0)));
  if(item.recommendedQuizReference){const link=node('a',bn?'কুইজ খুলুন':'Open quiz');link.href='/Learning/Quizzes';link.className='btn btn-sm btn-outline-primary ms-2';
   link.title=bn?'কোর্স ও কুইজ বেছে নিন':'Choose this course and quiz on the Quiz page';practice.append(link);}
  if(item.teacherNote){const note=node('div',item.teacherNote);note.className='small text-muted';priority.append(note);}
  tr.append(title,observed,goal,priority,practice);body.append(tr);}
 if(!data?.skills?.length){const tr=node('tr'),td=node('td',bn?'প্রকাশিত অধ্যায়-দক্ষতা যুক্ত কুইজ পাওয়া যায়নি।':'No published chapter/skill-mapped quiz.');td.colSpan=5;tr.append(td);body.append(tr);}
}
async function refresh(){const ref=$('adaptiveCourse').value,k=++epoch;plan=null;saveKey=null;render(null);$('adaptiveHistory').replaceChildren();
 if(!ref)return;try{const data=await api('/api/lms/adaptive/course/'+encodeURIComponent(ref)+'/plan');if(k!==epoch)return;render(data);await history();}
 catch(e){if(k===epoch)alert('danger',e.message);}}
async function history(){const ref=$('adaptiveCourse').value,k=epoch;if(!ref)return;
 const data=await api('/api/lms/adaptive/course/'+encodeURIComponent(ref)+'/history?page='+page+'&pageSize=15');
 if(k!==epoch)return;pages=Math.max(1,data.totalPages||1);const box=$('adaptiveHistory');box.replaceChildren();
 for(const item of data.items||[]){const button=node('button',new Date(item.generatedAt).toLocaleString()+' · '+item.skillCount+' '+(bn?'দক্ষতা':'skills'));
  button.type='button';button.className='btn btn-sm btn-outline-secondary text-start';button.onclick=async()=>{try{const snapshot=await api('/api/lms/adaptive/history/'+item.id);if(k!==epoch)return;render(snapshot);
   $('adaptiveSave').disabled=true;alert('info',bn?'সংরক্ষিত পরিকল্পনা দেখানো হচ্ছে।':'Showing an immutable saved plan.');}catch(e){alert('danger',e.message);}};box.append(button);}
 if(!data.items?.length)box.append(node('p',bn?'কোনো সংরক্ষিত পরিকল্পনা নেই।':'No saved plans yet.'));
 $('adaptivePage').textContent=page+' / '+pages;$('adaptivePrev').disabled=page<=1;$('adaptiveNext').disabled=page>=pages;}
async function save(){if(!plan||!$('adaptiveCourse').value)return;
 const ref=$('adaptiveCourse').value,k=epoch;saveKey??=crypto.randomUUID();$('adaptiveSave').disabled=true;
 try{const result=await api('/api/lms/adaptive/plans','POST',{courseReference:ref,clientRequestId:saveKey});
  if(k!==epoch||ref!==$('adaptiveCourse').value)return;
  render(result);$('adaptiveSave').disabled=true;saveKey=null;alert('success',bn?'পরিকল্পনা সংরক্ষিত হয়েছে।':'Study plan snapshot saved.');page=1;await history();}
 catch(e){if(k===epoch){$('adaptiveSave').disabled=false;alert('danger',e.message);}}}
async function moreCourses(){const button=$('adaptiveMoreCourses'),next=coursePage+1;button.disabled=true;
 try{const data=await api('/api/lms/adaptive/courses?page='+next+'&pageSize=30');
  const select=$('adaptiveCourse');for(const c of data.items||[])select.add(new Option(c.title,c.reference));
  coursePage=next;button.classList.toggle('d-none',!data.hasNextPage);
 }catch(e){alert('danger',e.message);}finally{button.disabled=false;}}
$('adaptiveCourse').onchange=()=>{page=1;refresh();};$('adaptivePreview').onclick=()=>{page=1;refresh();};$('adaptiveSave').onclick=save;
$('adaptivePrev').onclick=()=>{if(page>1){page--;history().catch(e=>alert('danger',e.message));}};
$('adaptiveNext').onclick=()=>{if(page<pages){page++;history().catch(e=>alert('danger',e.message));}};
$('adaptiveMoreCourses').onclick=moreCourses;
window.addEventListener('pagehide',()=>{epoch++;plan=null;render(null);});
moreCourses();
})();
