(() => {
    'use strict';
    const root=document.getElementById('routineApp');if(!root)return;
    const el=id=>document.getElementById(id),manager=root.dataset.canManage==='true';
    const days=['Sunday','Monday','Tuesday','Wednesday','Thursday','Friday','Saturday'];
    const state={batches:[],rooms:[],assignments:[],entries:[],slots:[],instructors:[],offerings:[],offeringPage:1,offeringTotal:0,teacherEntries:[],teacherPage:1,teacherTotal:0};
    const option=(id,name)=>{const v=document.createElement('option');v.value=String(id);v.textContent=name;return v;};
    const dateOnly=x=>x?String(x).slice(0,10):'';
    function clientId(){
        const random=globalThis.crypto;
        if(typeof random?.randomUUID==='function')return random.randomUUID();
        if(!random?.getRandomValues)throw new Error('Secure request identifier is unavailable.');
        const bytes=random.getRandomValues(new Uint8Array(16));
        bytes[6]=(bytes[6]&15)|64;bytes[8]=(bytes[8]&63)|128;
        const hex=Array.from(bytes,x=>x.toString(16).padStart(2,'0')).join('');
        return hex.slice(0,8)+'-'+hex.slice(8,12)+'-'+hex.slice(12,16)+'-'+hex.slice(16,20)+'-'+hex.slice(20);
    }
    const cell=x=>{const y=document.createElement('td');y.textContent=x===undefined||x===null?'':String(x);return y;};
    function status(type,message){const x=el('routineAlert');x.className='alert alert-'+type;x.textContent=message;x.focus();}
    function clearStatus(){const x=el('routineAlert');x.className='d-none';x.textContent='';}
    async function api(url,method='GET',body){
        const csrf=document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if(method!=='GET'&&!csrf)throw new Error('Security token missing. Reload the page.');
        const r=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json',...(method==='GET'?{}:{RequestVerificationToken:csrf}),...(body===undefined?{}:{'Content-Type':'application/json'})},...(body===undefined?{}:{body:JSON.stringify(body)})});
        const x=await r.json().catch(()=>null);if(!r.ok||!x?.success)throw new Error(x?.message||'Request failed.');return x.data;
    }
    function select(id,items,empty){
        const box=el(id);if(!box)return;const prev=box.value;box.replaceChildren(option('',empty));
        for(const x of items)box.append(option(x.id,x.name));
        if(items.some(x=>String(x.id)===prev))box.value=prev;
    }
    function renderTable(id,items,map,cols){
        const body=el(id);body.replaceChildren();
        if(!items.length){const tr=document.createElement('tr'),td=cell('No records.');td.colSpan=cols;td.className='text-muted text-center p-3';tr.append(td);body.append(tr);return;}
        items.forEach(item=>{const tr=document.createElement('tr');map(tr,item);body.append(tr);});
    }
    function selectedBatch(){return state.batches.find(x=>String(x.id)===el('routineBatch').value);}
    async function loadCatalog(search){
        const q='?take=100'+(search?'&search='+encodeURIComponent(search):'');
        const data=await api('/api/academic-setup/catalog'+q);
        state.batches=(data.batches||[]).filter(x=>x.isActive).sort((a,b)=>(a.name||'').localeCompare(b.name||''));
        if(!search)state.rooms=(data.rooms||[]).filter(x=>x.isActive);
        select('routineBatch',state.batches.map(x=>({id:x.id,name:[x.academicYearName,x.academicProgramName,x.name].filter(Boolean).join(' / ')})),'Select academic batch');
        if(state.batches.length===100)status('warning','Showing up to 100 batches. Search by name or code to find more.');
        else if(!state.batches.length)status('warning','No matching active academic batches.');
    }
    async function init(){
        try{await loadCatalog('');}catch(e){status('danger',e.message);}
        await loadSlots();
        if(manager)await loadTeachers();
    }
    async function loadSlots(){
        try{state.slots=await api('/api/academic-routines/time-slots')||[];el('routineSlotSummary').textContent=state.slots.map(x=>x.name+' ('+String(x.startTime).slice(0,5)+'–'+String(x.endTime).slice(0,5)+')').join(', ')||'No time slots.';
            select('routineSlot',state.slots.filter(x=>!x.isBreak).map(x=>({id:x.id,name:x.name+' / '+String(x.startTime).slice(0,5)+'-'+String(x.endTime).slice(0,5)})),'Select class time slot');
        }catch(e){status('danger',e.message);}
    }
    async function loadTeachers(){
        if(!manager)return;
        const search=el('routineTeacherSearch').value.trim(),q=search?'?search='+encodeURIComponent(search):'';
        try{
            state.instructors=await api('/api/academic-routines/instructors'+q)||[];
            select('routineTeacher',state.instructors.map(x=>({id:x.employeeReference,name:x.name+' ('+x.employeeCode+')'})),'Select active teacher');
            if(state.instructors.length>=50)status('warning','Showing up to 50 teachers. Search by name or employee code to narrow the list.');
        }catch(e){status('danger',e.message);}
    }
    function populateScopedOptions(){
        const batch=selectedBatch();if(!batch)return;
        select('routineRoom',state.rooms.filter(x=>Number(x.campusId)===Number(batch.campusId))
            .map(x=>({id:x.id,name:x.name+' ('+x.code+')'})),'No room');
    }
    async function loadOfferings(page=1){
        if(!manager)return;
        const batch=selectedBatch();if(!batch)return;
        const search=el('routineOfferingSearch').value.trim();
        const q=new URLSearchParams({page:String(page),pageSize:'50'});
        if(search)q.set('search',search);
        const data=await api('/api/academic-instruction/batches/'+batch.id+'/lesson-offerings?'+q);
        state.offerings=data.items||[];
        state.offeringPage=data.page||page;
        state.offeringTotal=data.totalCount||0;
        select('routineSubject',state.offerings.filter(x=>x.isActive).map(x=>({
            id:x.reference,name:x.subjectName+' ('+x.subjectCode+') / '+x.code
        })),'Select active offered subject');
        const pages=Math.max(1,Math.ceil(state.offeringTotal/50));
        el('routineOfferingPage').textContent='Page '+state.offeringPage+' of '+pages+' ('+state.offeringTotal+' offerings)';
        el('routineOfferingsPrev').disabled=state.offeringPage<=1;
        el('routineOfferingsNext').disabled=state.offeringPage>=pages;
    }
    async function load(){
        const batch=selectedBatch();if(!batch){status('warning','Select an active academic batch.');return;}
        clearStatus();const btn=el('routineLoad');btn.disabled=true;populateScopedOptions();
        if(manager){
            for(const id of ['routineAssignFrom','routineEntryFrom'])el(id).value=batch.startDate||'';
            for(const id of ['routineAssignTo','routineEntryTo'])el(id).value=batch.endDate||'';
        }
        try{
            const results=await Promise.all([api('/api/academic-routines/batches/'+batch.id+'/assignments'),api('/api/academic-routines/batches/'+batch.id)]);
            state.assignments=results[0]||[];state.entries=results[1]||[];
            state.teacherEntries=[];state.teacherPage=1;state.teacherTotal=0;
            el('routineTeacherPage').textContent='No timetable loaded';
            el('routineTeacherPrev').disabled=true;el('routineTeacherNext').disabled=true;
            renderTable('routineTeacherEntries',[],()=>{},4);
            if(state.assignments.length>=500)status('warning','Showing at most 500 assignments; narrow the selected batch where possible.');
            renderTable('routineAssignments',state.assignments,(tr,x)=>tr.append(cell(x.employeeName),cell(x.employeeCode),cell(x.subjectOfferingId),cell(x.isPrimary?'Primary':'Assistant')),4);
            renderTable('routineEntries',state.entries,(tr,x)=>{
                tr.append(cell(days[Number(x.dayOfWeek)]||String(x.dayOfWeek)),cell(x.timeSlotName),cell(x.subjectName),cell(x.roomName||'—'),cell(dateOnly(x.effectiveFrom)+(x.effectiveTo?' – '+dateOnly(x.effectiveTo):'')));
                if(manager){const td=document.createElement('td');const button=document.createElement('button');button.type='button';button.className='btn btn-sm btn-outline-danger';button.textContent='Deactivate';button.dataset.deactivateId=x.id;td.append(button);tr.append(td);}
            },manager?6:5);
            if(manager){
                try{await loadOfferings(1);}
                catch(error){status('warning','Offered-subject selector unavailable: '+error.message);}
            }
            select('routineAssignment',state.assignments.filter(x=>x.isActive).map(x=>({id:x.id,name:x.employeeName+' / Subject offering '+x.subjectOfferingId})),'Select instructor assignment');
            const tm=new Map();state.assignments.forEach(x=>tm.set(Number(x.employeeId),x.employeeName||x.employeeCode));
            select('routineTeacherTimetable',[...tm].map(([id,name])=>({id,name})),'Select teacher');
        }catch(e){status('danger',e.message);}
        finally{btn.disabled=false;}
    }
    async function saveSlot(e){
        e.preventDefault();if(!e.currentTarget.reportValidity())return;
        const form=e.currentTarget;
        const start=el('routineStartTime').value,end=el('routineEndTime').value;
        if(start>=end){status('danger','End time must be after start time.');return;}
        const button=el('routineSaveSlot');button.disabled=true;
        try{await api('/api/academic-routines/time-slots','POST',{name:el('routineSlotName').value.trim(),startTime:start+':00',endTime:end+':00',isBreak:el('routineIsBreak').checked});
            form.reset();await loadSlots();status('success','Time slot saved.');}
        catch(error){status('danger',error.message);}finally{button.disabled=false;}
    }
    async function saveAssignment(e){
        e.preventDefault();if(!e.currentTarget.reportValidity())return;
        const batch=selectedBatch();if(!batch)return status('danger','Select a batch first.');
        const from=el('routineAssignFrom').value,to=el('routineAssignTo').value;
        if(to&&to<from)return status('danger','Assignment end date must not precede start date.');
        const offering=el('routineSubject').value,employee=el('routineTeacher').value;
        if(!offering||!employee)return status('danger','Choose an offered subject and an active teacher.');
        const btn=el('routineAssign');btn.disabled=true;
        try{await api('/api/academic-routines/assignments','POST',{
            clientRequestId:clientId(),subjectOfferingReference:offering,employeeReference:employee,
            effectiveFrom:from,effectiveTo:to||null,isPrimary:el('routinePrimary').checked,isActive:true
        });
            await load();status('success','Instructor assigned.');}
        catch(error){status('danger',error.message);}finally{btn.disabled=false;}
    }
    async function saveEntry(e){
        e.preventDefault();if(!e.currentTarget.reportValidity())return;
        const assignment=state.assignments.find(x=>Number(x.id)===Number(el('routineAssignment').value));
        if(!assignment?.subjectOfferingReference)return status('danger','Choose an active instructor assignment. Reload the batch.');
        const from=el('routineEntryFrom').value,to=el('routineEntryTo').value;
        if(to&&to<from)return status('danger','Routine end date must not precede start date.');
        const btn=el('routineAddEntry');btn.disabled=true;
        try{await api('/api/academic-routines/entries','POST',{
            clientRequestId:clientId(),subjectOfferingReference:assignment.subjectOfferingReference,
            instructorAssignmentId:assignment.id,routineTimeSlotId:Number(el('routineSlot').value),
            dayOfWeek:Number(el('routineDay').value),roomId:el('routineRoom').value?Number(el('routineRoom').value):null,
            effectiveFrom:from,effectiveTo:to||null,isActive:true
        });
            await load();status('success','Class routine saved.');}
        catch(error){status('danger',error.message);}finally{btn.disabled=false;}
    }
    async function deactivate(id){
        if(!confirm('Deactivate this routine entry? Existing academic history will remain.'))return;
        const entry=state.entries.find(x=>Number(x.id)===Number(id));
        if(!entry?.rowVersion)return status('danger','Routine version is unavailable. Reload before deactivating.');
        try{await api('/api/academic-routines/entries/'+id+'/deactivate?rowVersion='+encodeURIComponent(entry.rowVersion),'POST');await load();status('success','Routine entry deactivated.');}
        catch(e){status('danger',e.message);}
    }
    async function teacherTimetable(page=1){
        const batch=selectedBatch(),id=Number(el('routineTeacherTimetable').value);
        if(!batch||!id)return status('warning','Select a batch and teacher first.');
        const q=new URLSearchParams({academicYearId:String(batch.academicYearId),page:String(page),pageSize:'50'});
        if(batch.academicTermId)q.set('academicTermId',String(batch.academicTermId));
        const requestedBatch=String(batch.id),requestedTeacher=String(id);
        try{
            const data=await api('/api/academic-routines/teachers/'+id+'/page?'+q);
            if(String(selectedBatch()?.id)!==requestedBatch||el('routineTeacherTimetable').value!==requestedTeacher)return;
            state.teacherEntries=data.items||[];state.teacherPage=data.page||page;state.teacherTotal=data.totalCount||0;
            renderTable('routineTeacherEntries',state.teacherEntries,(tr,x)=>tr.append(cell(days[Number(x.dayOfWeek)]||String(x.dayOfWeek)),cell(x.timeSlotName),cell(x.subjectName),cell(x.roomName||'—')),4);
            const pages=Math.max(1,Math.ceil(state.teacherTotal/50));
            el('routineTeacherPage').textContent='Page '+state.teacherPage+' of '+pages+' ('+state.teacherTotal+' entries)';
            el('routineTeacherPrev').disabled=state.teacherPage<=1;
            el('routineTeacherNext').disabled=state.teacherPage>=pages;
        }catch(e){status('danger',e.message);}
    }
    function csvEscape(value){
        let text=String(value===null||value===undefined?'':value);
        if(/^\s*[=+@-]/.test(text))text="'"+text;
        return '"'+text.replace(/"/g,'""')+'"';
    }
    function exportCsv(filename,headers,rows){
        if(!rows.length)return status('warning','Load a timetable with records before exporting.');
        const csv='\uFEFF'+[headers,...rows].map(row=>row.map(csvEscape).join(',')).join('\r\n');
        const url=URL.createObjectURL(new Blob([csv],{type:'text/csv;charset=utf-8'}));
        const a=document.createElement('a');a.href=url;a.download=filename;document.body.append(a);a.click();a.remove();
        setTimeout(()=>URL.revokeObjectURL(url),1000);
    }
    function exportBatch(){
        const batch=selectedBatch();if(!batch)return status('warning','Select a batch.');
        exportCsv('routine-batch-'+batch.id+'.csv',['Day','Slot','Subject','Room','From','To','Active'],
            state.entries.map(x=>[days[Number(x.dayOfWeek)]||x.dayOfWeek,x.timeSlotName,x.subjectName,x.roomName||'',
                dateOnly(x.effectiveFrom),dateOnly(x.effectiveTo),x.isActive?'Yes':'No']));
    }
    function exportTeacher(){
        const id=el('routineTeacherTimetable').value;
        exportCsv('routine-teacher-'+id+'-page-'+state.teacherPage+'.csv',['Day','Slot','Subject','Room','From','To'],
            state.teacherEntries.map(x=>[days[Number(x.dayOfWeek)]||x.dayOfWeek,x.timeSlotName,x.subjectName,
                x.roomName||'',dateOnly(x.effectiveFrom),dateOnly(x.effectiveTo)]));
    }
    el('routineFilters').addEventListener('submit',e=>{e.preventDefault();load();});
    el('routineBatch').addEventListener('change',load);
    el('routineFindBatches').addEventListener('click',async()=>{
        try{await loadCatalog(el('routineBatchSearch').value.trim());}catch(e){status('danger',e.message);}
    });
    el('routineFindOfferings')?.addEventListener('click',async()=>{
        try{await loadOfferings(1);}catch(e){status('danger',e.message);}
    });
    el('routineOfferingsPrev')?.addEventListener('click',async()=>{
        try{await loadOfferings(state.offeringPage-1);}catch(e){status('danger',e.message);}
    });
    el('routineOfferingsNext')?.addEventListener('click',async()=>{
        try{await loadOfferings(state.offeringPage+1);}catch(e){status('danger',e.message);}
    });
    el('routineExportBatch').addEventListener('click',exportBatch);
    el('routineExportTeacher').addEventListener('click',exportTeacher);
    el('routineSlotForm')?.addEventListener('submit',saveSlot);
    el('routineAssignmentForm')?.addEventListener('submit',saveAssignment);
    el('routineEntryForm')?.addEventListener('submit',saveEntry);
    el('routineFindTeachers')?.addEventListener('click',loadTeachers);
    el('routineShowTeacher').addEventListener('click',()=>teacherTimetable(1));
    el('routineTeacherPrev').addEventListener('click',()=>teacherTimetable(state.teacherPage-1));
    el('routineTeacherNext').addEventListener('click',()=>teacherTimetable(state.teacherPage+1));
    el('routineTeacherTimetable').addEventListener('change',()=>{
        state.teacherEntries=[];state.teacherPage=1;state.teacherTotal=0;
        el('routineTeacherPage').textContent='Select View schedule';
        el('routineTeacherPrev').disabled=true;el('routineTeacherNext').disabled=true;
        renderTable('routineTeacherEntries',[],()=>{},4);
    });
    el('routineEntries').addEventListener('click',e=>{const button=e.target.closest('button[data-deactivate-id]');if(button)deactivate(Number(button.dataset.deactivateId));});
    init();
})();
