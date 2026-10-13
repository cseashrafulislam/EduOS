(() => {
'use strict';
const root=document.getElementById('hrSetupRoot'); if(!root)return;
const $=id=>document.getElementById('hrSetup'+id);
const edit=root.dataset.canEdit==='true',token=document.querySelector('meta[name="request-verification-token"]')?.content;
let page=1,pages=1;
function notice(message,ok=false){const e=$('Alert');e.className='alert alert-'+(ok?'success':'danger');e.textContent=message;e.focus();}
async function call(route,method='GET',body){
 const headers={Accept:'application/json'};
 if(method!=='GET'){if(!token)throw Error('Security token missing. Reload.');headers.RequestVerificationToken=token;headers['Content-Type']='application/json';}
 const r=await fetch('/api/hr/setup/'+route,{method,headers,credentials:'same-origin',cache:'no-store',...(body===undefined?{}:{body:JSON.stringify(body)})});
 const v=await r.json().catch(()=>null);if(!r.ok||!v?.success)throw Error(v?.message||'Request failed.');return v.data;
}
function cell(value){const c=document.createElement('td');c.textContent=value==null?'—':String(value);return c;}
function table(id,items,columns,editRow){
 const body=$(id);body.replaceChildren();
 if(!items.length){const row=document.createElement('tr'),c=cell('No records');c.colSpan=columns.length+1;row.append(c);body.append(row);}
 for(const item of items){const row=document.createElement('tr');for(const fn of columns)row.append(cell(fn(item)));
 const last=cell('');if(edit){const b=document.createElement('button');b.type='button';b.className='btn btn-outline-primary btn-sm';b.textContent='Edit';b.addEventListener('click',()=>editRow(item));last.append(b);}
 row.append(last);body.append(row);}
}
function setFields(type,item,fields){$(type+'Id').value=item.id;$(type+'Version').value=item.rowVersion;
 for(const [field,value] of Object.entries(fields))$(type+field).value=value;
 $(type+'Active').checked=item.isActive;$(type+'Form').scrollIntoView({block:'center'});}
function reset(type){const f=$(type+'Form');f.reset();$(type+'Id').value='';$(type+'Version').value='';$(type+'Active').checked=true;}
async function departments(){
 const r=await call('departments?page='+page+'&pageSize=50');pages=Math.max(1,r.totalPages||1);
 $('DepartmentPage').textContent='Page '+page+' of '+pages;
 $('DepartmentPrev').disabled=page<=1;$('DepartmentNext').disabled=page>=pages;
 table('DepartmentRows',r.items||[], [x=>x.code,x=>x.name,x=>x.parentName,x=>x.isActive?'Active':'Inactive'],x=>setFields('Department',x,{Code:x.code,Name:x.name,Parent:x.parentId||''}));
 if(edit){const all=[...(r.items||[])];for(let p=1;p<=pages;p++)if(p!==page){const other=await call('departments?page='+p+'&pageSize=50');all.push(...other.items);}
 const sel=$('DepartmentParent'),chosen=sel.value;sel.replaceChildren(new Option('None',''));
 for(const x of all.filter(x=>x.isActive))sel.add(new Option(x.name+' ('+x.code+')',String(x.id)));sel.value=chosen;}
}
async function designations(){const data=await call('designations');table('DesignationRows',data,[x=>x.code,x=>x.name,x=>x.rank,x=>x.isActive?'Active':'Inactive'],x=>setFields('Designation',x,{Code:x.code,Name:x.name,Rank:x.rank}));}
async function shifts(){const data=await call('shifts');table('ShiftRows',data,[x=>x.code,x=>x.name,x=>String(x.startTime).slice(0,5)+'–'+String(x.endTime).slice(0,5),x=>x.graceMinutes,x=>x.isActive?'Active':'Inactive'],x=>setFields('Shift',x,{Code:x.code,Name:x.name,Start:String(x.startTime).slice(0,5),End:String(x.endTime).slice(0,5),Grace:x.graceMinutes}));}
function bind(type,route,body,refresh){const f=$(type+'Form');f.addEventListener('reset',()=>{$(type+'Id').value='';$(type+'Version').value='';});
 f.addEventListener('submit',async event=>{event.preventDefault();if(!f.reportValidity())return;const id=$(type+'Id').value,b=f.querySelector('[type=submit]');b.disabled=true;
 try{await call(route+(id?'/'+id:''),id?'PUT':'POST',body());reset(type);await refresh();notice(type+' saved.',true);}
 catch(e){notice(e.message);}finally{b.disabled=false;}});}
if(edit){
 bind('Department','departments',()=>({code:$('DepartmentCode').value.trim(),name:$('DepartmentName').value.trim(),parentId:$('DepartmentParent').value?Number($('DepartmentParent').value):null,isActive:$('DepartmentActive').checked,rowVersion:$('DepartmentVersion').value||null}),departments);
 bind('Designation','designations',()=>({code:$('DesignationCode').value.trim(),name:$('DesignationName').value.trim(),rank:Number($('DesignationRank').value),isActive:$('DesignationActive').checked,rowVersion:$('DesignationVersion').value||null}),designations);
 bind('Shift','shifts',()=>({code:$('ShiftCode').value.trim(),name:$('ShiftName').value.trim(),startTime:$('ShiftStart').value+':00',endTime:$('ShiftEnd').value+':00',graceMinutes:Number($('ShiftGrace').value),isActive:$('ShiftActive').checked,rowVersion:$('ShiftVersion').value||null}),shifts);
}
$('DepartmentPrev').addEventListener('click',()=>{if(page>1){page--;departments().catch(e=>notice(e.message));}});
$('DepartmentNext').addEventListener('click',()=>{if(page<pages){page++;departments().catch(e=>notice(e.message));}});
Promise.allSettled([departments(),designations(),shifts()]).then(a=>{for(const x of a)if(x.status==='rejected')notice(x.reason?.message||'Unable to load HR masters.');});
})();