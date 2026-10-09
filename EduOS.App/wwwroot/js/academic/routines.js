(() => {
    'use strict';
    const root=document.getElementById('routineApp');if(!root)return;
    const el=id=>document.getElementById(id),manager=root.dataset.canManage==='true';
    const days=['Sunday','Monday','Tuesday','Wednesday','Thursday','Friday','Saturday'];
    const state={batches:[],rooms:[],subjects:[],curricula:[],curriculumSubjects:[],assignments:[],entries:[],slots:[],instructors:[]};
    const option=(id,name)=>{const v=document.createElement('option');v.value=String(id);v.textContent=name;return v;};
    const dateOnly=x=>x?String(x).slice(0,10):'';
    const cell=x=>{const y=document.createElement('td');y.textContent=x===undefined||x===null?'':String(x);return y;};
    function status(type,message){const x=el('routineAlert');x.className='alert alert-'+type;x.textContent=message;x.focus();}
    function clearStatus(){const x=el('routineAlert');x.className='d-none';x.textContent='';}
    async function api(url,method='GET',body){
        const r=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json',...(body===undefined?{}:{'Content-Type':'application/json'})},...(body===undefined?{}:{body:JSON.stringify(body)})});
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
    async function init(){
        try{
            const data=await api('/api/academic-setup/catalog');
            state.batches=(data.batches||[]).filter(x=>x.isActive).sort((a,b)=>(a.name||'').localeCompare(b.name||''));
            state.rooms=(data.rooms||[]).filter(x=>x.isActive);
            state.subjects=(data.subjects||[]).filter(x=>x.isActive);
            state.curricula=(data.curricula||[]).filter(x=>x.isActive);
            state.curriculumSubjects=(data.curriculumSubjects||[]).filter(x=>x.isActive);
            select('routineBatch',state.batches.map(x=>({id:x.id,name:[x.academicYearName,x.academicProgramName,x.name].filter(Boolean).join(' / ')})),'Select academic batch');
            if(!state.batches.length)status('warning','No academic batch found; create one in Academic Setup.');
        }catch(e){status('danger',e.message);}
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
            select('routineTeacher',state.instructors.map(x=>({id:x.id,name:x.name+' ('+x.employeeCode+')'})),'Select active teacher');
            if(state.instructors.length===100)status('warning','Showing the first 100 teachers. Search by name or employee code to narrow the list.');
        }catch(e){status('danger',e.message);}
    }
    function filterSubjects(batch){
        if(!batch)return state.subjects;
        const validCurricula=new Set(state.curricula.filter(x=>Number(x.academicProgramId)===Number(batch.academicProgramId)).map(x=>Number(x.id)));
        const allowed=new Set(state.curriculumSubjects.filter(x=>validCurricula.has(Number(x.academicCurriculumId))&&Number(x.academicLevelId)===Number(batch.academicLevelId)).map(x=>Number(x.subjectId)));
        return state.subjects.filter(x=>allowed.has(Number(x.id)));
    }
    function populateScopedOptions(){
        const batch=selectedBatch();if(!batch)return;
        select('routineSubject',filterSubjects(batch).map(x=>({id:x.id,name:x.name+' ('+x.code+')'})),'Select offered subject');
        select('routineRoom',state.rooms.filter(x=>Number(x.campusId)===Number(batch.campusId)).map(x=>({id:x.id,name:x.name+' ('+x.code+')'})),'No room');
    }
    async function load(){
        const batch=selectedBatch();if(!batch){status('warning','Select an active academic batch.');return;}
        clearStatus();const btn=el('routineLoad');btn.disabled=true;populateScopedOptions();
        try{
            const results=await Promise.all([api('/api/academic-routines/batches/'+batch.id+'/assignments'),api('/api/academic-routines/batches/'+batch.id)]);
            state.assignments=results[0]||[];state.entries=results[1]||[];
            renderTable('routineAssignments',state.assignments,(tr,x)=>tr.append(cell(x.employeeName),cell(x.employeeCode),cell(x.subjectOfferingId),cell(x.isPrimary?'Primary':'Assistant')),4);
            renderTable('routineEntries',state.entries,(tr,x)=>{
                tr.append(cell(days[Number(x.dayOfWeek)]||String(x.dayOfWeek)),cell(x.timeSlotName),cell(x.subjectName),cell(x.roomName||'—'),cell(dateOnly(x.effectiveFrom)+(x.effectiveTo?' – '+dateOnly(x.effectiveTo):'')));
                if(manager){const td=document.createElement('td');const button=document.createElement('button');button.type='button';button.className='btn btn-sm btn-outline-danger';button.textContent='Deactivate';button.dataset.deactivateId=x.id;td.append(button);tr.append(td);}
            },manager?6:5);
            select('routineAssignment',state.assignments.filter(x=>x.isActive).map(x=>({id:x.id,name:x.employeeName+' / Subject offering '+x.subjectOfferingId})),'Select instructor assignment');
            const tm=new Map();state.assignments.forEach(x=>tm.set(Number(x.employeeId),x.employeeName||x.employeeCode));
            select('routineTeacherTimetable',[...tm].map(([id,name])=>({id,name})),'Select teacher');
        }catch(e){status('danger',e.message);}
        finally{btn.disabled=false;}
    }
    async function saveSlot(e){
        e.preventDefault();if(!e.currentTarget.reportValidity())return;
        const start=el('routineStartTime').value,end=el('routineEndTime').value;
        if(start>=end){status('danger','End time must be after start time.');return;}
        const button=el('routineSaveSlot');button.disabled=true;
        try{await api('/api/academic-routines/time-slots','POST',{name:el('routineSlotName').value.trim(),startTime:start+':00',endTime:end+':00',isBreak:el('routineIsBreak').checked});
            e.currentTarget.reset();await loadSlots();status('success','Time slot saved.');}
        catch(error){status('danger',error.message);}finally{button.disabled=false;}
    }
    async function saveAssignment(e){
        e.preventDefault();if(!e.currentTarget.reportValidity())return;
        const batch=selectedBatch();if(!batch)return status('danger','Select a batch first.');
        const btn=el('routineAssign');btn.disabled=true;
        try{await api('/api/academic-routines/assignments','POST',{academicBatchId:Number(batch.id),subjectId:Number(el('routineSubject').value),
            employeeId:Number(el('routineTeacher').value),academicTermId:batch.academicTermId||null,isPrimary:el('routinePrimary').checked,isClassAdvisor:false});
            await load();status('success','Instructor assigned.');}
        catch(error){status('danger',error.message);}finally{btn.disabled=false;}
    }
    async function saveEntry(e){
        e.preventDefault();if(!e.currentTarget.reportValidity())return;
        const btn=el('routineAddEntry');btn.disabled=true;
        try{await api('/api/academic-routines/entries','POST',{instructorAssignmentId:Number(el('routineAssignment').value),
            routineTimeSlotId:Number(el('routineSlot').value),dayOfWeek:Number(el('routineDay').value),roomId:el('routineRoom').value?Number(el('routineRoom').value):null,remarks:null});
            await load();status('success','Class routine saved.');}
        catch(error){status('danger',error.message);}finally{btn.disabled=false;}
    }
    async function deactivate(id){
        if(!confirm('Deactivate this routine entry? Existing academic history will remain.'))return;
        try{await api('/api/academic-routines/entries/'+id+'/deactivate','POST');await load();status('success','Routine entry deactivated.');}
        catch(e){status('danger',e.message);}
    }
    async function teacherTimetable(){
        const batch=selectedBatch(),id=Number(el('routineTeacherTimetable').value);
        if(!batch||!id)return status('warning','Select a batch and teacher first.');
        const q=new URLSearchParams({academicYearId:String(batch.academicYearId)});
        if(batch.academicTermId)q.set('academicTermId',String(batch.academicTermId));
        try{const rows=await api('/api/academic-routines/teachers/'+id+'?'+q)||[];
            renderTable('routineTeacherEntries',rows,(tr,x)=>tr.append(cell(days[Number(x.dayOfWeek)]||String(x.dayOfWeek)),cell(x.timeSlotName),cell(x.subjectName),cell(x.roomName||'—')),4);
        }catch(e){status('danger',e.message);}
    }
    el('routineFilters').addEventListener('submit',e=>{e.preventDefault();load();});
    el('routineBatch').addEventListener('change',load);
    el('routineSlotForm')?.addEventListener('submit',saveSlot);
    el('routineAssignmentForm')?.addEventListener('submit',saveAssignment);
    el('routineEntryForm')?.addEventListener('submit',saveEntry);
    el('routineFindTeachers')?.addEventListener('click',loadTeachers);
    el('routineShowTeacher').addEventListener('click',teacherTimetable);
    el('routineEntries').addEventListener('click',e=>{const button=e.target.closest('button[data-deactivate-id]');if(button)deactivate(Number(button.dataset.deactivateId));});
    init();
})();
