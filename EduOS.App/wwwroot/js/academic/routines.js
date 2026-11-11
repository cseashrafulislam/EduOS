(() => {
    'use strict';
    const root=document.getElementById('routineApp');if(!root)return;
    const el=id=>document.getElementById(id),manager=root.dataset.canManage==='true';
    const days=['Sunday','Monday','Tuesday','Wednesday','Thursday','Friday','Saturday'];
    const state={batches:[],rooms:[],assignments:[],assignmentPage:1,assignmentTotal:0,entries:[],batchPage:1,batchTotal:0,slots:[],instructors:[],instructorPage:1,instructorTotal:0,offerings:[],offeringPage:1,offeringTotal:0,teacherEntries:[],teacherPage:1,teacherTotal:0,
        appliedBatch:'',mutationBusy:false,loadSeq:0,catalogSeq:0,slotSeq:0,assignmentSeq:0,offeringSeq:0,teacherTimetableSeq:0,
        assignmentRequest:null,entryRequest:null,draftAssignment:null};
    let batchRequestSequence=0,teacherRequestSequence=0;
    const identity=[document.body?.dataset?.authTenantId||'',document.body?.dataset?.authSessionReference||'',document.body?.dataset?.authUserId||''];
    const sameSession=()=>identity[0]===(document.body?.dataset?.authTenantId||'')&&
        identity[1]===(document.body?.dataset?.authSessionReference||'')&&
        identity[2]===(document.body?.dataset?.authUserId||'');
    const slotGuard=manager&&el('routineSlotForm')?window.EduOSFormGuard?.create(el('routineSlotForm')):null;
    const assignmentGuard=manager&&el('routineAssignmentForm')?window.EduOSFormGuard?.create(el('routineAssignmentForm')):null;
    const entryGuard=manager&&el('routineEntryForm')?window.EduOSFormGuard?.create(el('routineEntryForm')):null;
    const guards=[slotGuard,assignmentGuard,entryGuard].filter(Boolean);
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
    function status(type,message){if(!sameSession())return;const x=el('routineAlert');x.className='alert alert-'+type;x.textContent=message;x.focus();}
    function clearStatus(){const x=el('routineAlert');x.className='d-none';x.textContent='';}
    async function api(url,method='GET',body){
        if(!sameSession())throw new Error('Session changed. Reload the page.');
        const csrf=document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if(method!=='GET'&&!csrf)throw new Error('Security token missing. Reload the page.');
        const r=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json',...(method==='GET'?{}:{RequestVerificationToken:csrf}),...(body===undefined?{}:{'Content-Type':'application/json'})},...(body===undefined?{}:{body:JSON.stringify(body)})});
        const x=await r.json().catch(()=>null);
        if(!sameSession())throw new Error('Session changed. Reload the page.');
        if(!r.ok||!x?.success)throw new Error(x?.message||'Request failed.');return x.data;
    }
    function select(id,items,empty,preserveDraft=false){
        const box=el(id);if(!box)return;const prev=box.value;box.replaceChildren(option('',empty));
        for(const x of items)box.append(option(x.id,x.name));
        if(preserveDraft&&prev&&!items.some(x=>String(x.id)===prev))
            box.append(option(prev,'Previously selected (verify before saving)'));
        if(items.some(x=>String(x.id)===prev))box.value=prev;
        else if(preserveDraft&&prev)box.value=prev;
    }
    function renderTable(id,items,map,cols){
        const body=el(id);body.replaceChildren();
        if(!items.length){const tr=document.createElement('tr'),td=cell('No records.');td.colSpan=cols;td.className='text-muted text-center p-3';tr.append(td);body.append(tr);return;}
        items.forEach(item=>{const tr=document.createElement('tr');map(tr,item);body.append(tr);});
    }
    function selectedBatch(){return state.batches.find(x=>String(x.id)===el('routineBatch').value);}
    async function loadCatalog(search){
        const q='?take=100'+(search?'&search='+encodeURIComponent(search):'');
        const seq=++state.catalogSeq,previous=selectedBatch();
        const data=await api('/api/academic-setup/catalog'+q);
        if(!sameSession()||seq!==state.catalogSeq)return false;
        state.batches=(data.batches||[]).filter(x=>x.isActive).sort((a,b)=>(a.name||'').localeCompare(b.name||''));
        if(previous&&!state.batches.some(x=>String(x.id)===String(previous.id)))state.batches.push(previous);
        if(!search)state.rooms=(data.rooms||[]).filter(x=>x.isActive);
        select('routineBatch',state.batches.map(x=>({id:x.id,name:[x.academicYearName,x.academicProgramName,x.name].filter(Boolean).join(' / ')})),'Select academic batch');
        state.appliedBatch=el('routineBatch').value;
        if(state.batches.length===100)status('warning','Showing up to 100 batches. Search by name or code to find more.');
        else if(!state.batches.length)status('warning','No matching active academic batches.');
        return true;
    }
    async function init(){
        try{await loadCatalog('');}catch(e){status('danger',e.message);}
        await loadSlots();
        // Teacher lookup is available only after selecting an authorized academic batch.
    }
    async function loadSlots(){
        const seq=++state.slotSeq;
        try{const slots=await api('/api/academic-routines/time-slots')||[];
            if(!sameSession()||seq!==state.slotSeq)return false;
            state.slots=slots;el('routineSlotSummary').textContent=slots.map(x=>x.name+' ('+String(x.startTime).slice(0,5)+'–'+String(x.endTime).slice(0,5)+')').join(', ')||'No time slots.';
            const dirty=entryGuard?.isDirty();
            select('routineSlot',slots.filter(x=>!x.isBreak).map(x=>({id:x.id,name:x.name+' / '+String(x.startTime).slice(0,5)+'-'+String(x.endTime).slice(0,5)})),'Select class time slot',dirty);
            if(!dirty)entryGuard?.markClean();
            return true;
        }catch(e){if(seq===state.slotSeq)status('danger',e.message);return false;}
    }
    async function loadTeachers(page=1){
        if(!manager)return;
        const batch=selectedBatch();
        if(!batch)return status('warning','Select an active academic batch before searching teachers.');
        const search=el('routineTeacherSearch').value.trim();
        const q=new URLSearchParams({page:String(page),pageSize:'50'});
        if(search)q.set('search',search);
        const requestedBatch=String(batch.id),sequence=++teacherRequestSequence;
        try{
            const data=await api('/api/academic-routines/batches/'+batch.id+'/instructors/page?'+q);
            if(!sameSession()||sequence!==teacherRequestSequence||String(selectedBatch()?.id)!==requestedBatch||
               el('routineTeacherSearch').value.trim()!==search)return;
            state.instructors=data.items||[];
            state.instructorPage=data.page||page;
            state.instructorTotal=data.totalCount||0;
            const dirty=assignmentGuard?.isDirty();
            select('routineTeacher',state.instructors.map(x=>({id:x.employeeReference,name:x.name+' ('+x.employeeCode+')'})),'Select active teacher',dirty);
            if(!dirty)assignmentGuard?.markClean();
            const pages=Math.max(1,Math.ceil(state.instructorTotal/50));
            el('routineTeachersPage').textContent='Page '+state.instructorPage+' of '+pages+' ('+state.instructorTotal+' teachers)';
            el('routineTeachersPrev').disabled=state.instructorPage<=1;
            el('routineTeachersNext').disabled=state.instructorPage>=pages;
        }catch(e){if(sequence===teacherRequestSequence&&sameSession())status('danger',e.message);}
    }
    function populateScopedOptions(){
        const batch=selectedBatch();if(!batch)return;
        const dirty=entryGuard?.isDirty();
        select('routineRoom',state.rooms.filter(x=>Number(x.campusId)===Number(batch.campusId))
            .map(x=>({id:x.id,name:x.name+' ('+x.code+')'})),'No room',dirty);
        if(!dirty)entryGuard?.markClean();
    }
    async function loadOfferings(page=1){
        if(!manager)return;
        const batch=selectedBatch();if(!batch)return;
        const search=el('routineOfferingSearch').value.trim();
        const q=new URLSearchParams({page:String(page),pageSize:'50'});
        if(search)q.set('search',search);
        const seq=++state.offeringSeq,requestedBatch=String(batch.id);
        const data=await api('/api/academic-instruction/batches/'+batch.id+'/lesson-offerings?'+q);
        if(!sameSession()||seq!==state.offeringSeq||String(selectedBatch()?.id)!==requestedBatch||
            el('routineOfferingSearch').value.trim()!==search)return false;
        state.offerings=data.items||[];
        state.offeringPage=data.page||page;
        state.offeringTotal=data.totalCount||0;
        const dirty=assignmentGuard?.isDirty();
        select('routineSubject',state.offerings.filter(x=>x.isActive).map(x=>({
            id:x.reference,name:x.subjectName+' ('+x.subjectCode+') / '+x.code
        })),'Select active offered subject',dirty);
        if(!dirty)assignmentGuard?.markClean();
        const pages=Math.max(1,Math.ceil(state.offeringTotal/50));
        el('routineOfferingPage').textContent='Page '+state.offeringPage+' of '+pages+' ('+state.offeringTotal+' offerings)';
        el('routineOfferingsPrev').disabled=state.offeringPage<=1;
        el('routineOfferingsNext').disabled=state.offeringPage>=pages;
        return true;
    }
    async function loadAssignments(page=1){
        const batch=selectedBatch();if(!batch)return;
        const search=el('routineAssignmentSearch').value.trim();
        const q=new URLSearchParams({page:String(page),pageSize:'50'});
        if(batch.academicTermId)q.set('academicTermId',String(batch.academicTermId));
        if(search)q.set('search',search);
        const requestedBatch=String(batch.id),seq=++state.assignmentSeq;
        const data=await api('/api/academic-routines/batches/'+batch.id+'/assignments/page?'+q);
        if(!sameSession()||seq!==state.assignmentSeq||String(selectedBatch()?.id)!==requestedBatch||
            el('routineAssignmentSearch').value.trim()!==search)return false;
        const selected=el('routineAssignment')?.value;
        const previous=state.assignments.find(x=>String(x.id)===selected);
        if(previous&&entryGuard?.isDirty())state.draftAssignment={batchId:requestedBatch,record:previous};
        state.assignments=data.items||[];
        if(state.assignments.some(x=>String(x.id)===selected))state.draftAssignment=null;
        state.assignmentPage=data.page||page;
        state.assignmentTotal=data.totalCount||0;
        const pages=Math.max(1,Math.ceil(state.assignmentTotal/50));
        el('routineAssignmentsPage').textContent='Page '+state.assignmentPage+' of '+pages+' ('+state.assignmentTotal+')';
        el('routineAssignmentsPrev').disabled=state.assignmentPage<=1;
        el('routineAssignmentsNext').disabled=state.assignmentPage>=pages;
        renderTable('routineAssignments',state.assignments,(tr,x)=>
            tr.append(cell(x.employeeName),cell(x.employeeCode),cell(x.subjectOfferingId),cell(x.isPrimary?'Primary':'Assistant')),4);
        if(manager){
            const dirty=entryGuard?.isDirty();
            select('routineAssignment',state.assignments.filter(x=>x.isActive)
                .map(x=>({id:x.id,name:x.employeeName+' / Subject offering '+x.subjectOfferingId})),'Select instructor assignment',dirty);
            if(!dirty)entryGuard?.markClean();
        }
        const tm=new Map();state.assignments.forEach(x=>tm.set(Number(x.employeeId),x.employeeName||x.employeeCode));
        select('routineTeacherTimetable',[...tm].map(([id,name])=>({id,name})),'Select teacher');
        return true;
    }
    async function loadBatchTimetable(page=1){
        const batch=selectedBatch();if(!batch)return;
        const requestedBatch=String(batch.id),sequence=++batchRequestSequence;
        const q=new URLSearchParams({page:String(page),pageSize:'50'});
        if(batch.academicTermId)q.set('academicTermId',String(batch.academicTermId));
        const data=await api('/api/academic-routines/batches/'+batch.id+'/page?'+q);
        if(!sameSession()||sequence!==batchRequestSequence||String(selectedBatch()?.id)!==requestedBatch)return false;
        state.entries=data.items||[];state.batchPage=data.page||page;state.batchTotal=data.totalCount||0;
        const pages=Math.max(1,Math.ceil(state.batchTotal/50));
        el('routineBatchPage').textContent='Page '+state.batchPage+' of '+pages+' ('+state.batchTotal+' entries)';
        el('routineBatchPrev').disabled=state.batchPage<=1;
        el('routineBatchNext').disabled=state.batchPage>=pages;
        renderTable('routineEntries',state.entries,(tr,x)=>{
            tr.append(cell(days[Number(x.dayOfWeek)]||String(x.dayOfWeek)),cell(x.timeSlotName),
                cell(x.subjectName),cell(x.roomName||'—'),
                cell(dateOnly(x.effectiveFrom)+(x.effectiveTo?' – '+dateOnly(x.effectiveTo):'')));
            if(manager){
                const td=document.createElement('td'),button=document.createElement('button');
                button.type='button';button.className='btn btn-sm btn-outline-danger';
                button.textContent='Deactivate';button.dataset.deactivateId=x.id;
                td.append(button);tr.append(td);
            }
        },manager?6:5);
        return true;
    }
    async function load(afterWrite=false){
        if(!sameSession()||(state.mutationBusy&&!afterWrite))return false;
        const batch=selectedBatch();if(!batch){status('warning','Select an active academic batch.');return false;}
        const seq=++state.loadSeq,requestedBatch=String(batch.id);
        const current=()=>sameSession()&&seq===state.loadSeq&&String(selectedBatch()?.id)===requestedBatch;
        const assignmentDraft=assignmentGuard?.isDirty(),entryDraft=entryGuard?.isDirty();
        clearStatus();const btn=el('routineLoad');btn.disabled=true;populateScopedOptions();
        ++batchRequestSequence;
        ++teacherRequestSequence;
        if(manager){
            state.instructors=[];state.instructorPage=1;state.instructorTotal=0;
            select('routineTeacher',[],'Loading teachers…',assignmentDraft);
            if(!assignmentDraft)assignmentGuard?.markClean();
            el('routineTeachersPage').textContent='Loading teachers…';
            el('routineTeachersPrev').disabled=true;el('routineTeachersNext').disabled=true;
        }
        state.entries=[];state.batchPage=1;state.batchTotal=0;
        el('routineBatchPage').textContent='Loading timetable…';
        el('routineBatchPrev').disabled=true;el('routineBatchNext').disabled=true;
        renderTable('routineEntries',[],()=>{},manager?6:5);
        if(manager){
            if(!assignmentDraft){
                el('routineAssignFrom').value=dateOnly(batch.startDate);
                el('routineAssignTo').value=dateOnly(batch.endDate);
                assignmentGuard?.markClean();
            }
            if(!entryDraft){
                el('routineEntryFrom').value=dateOnly(batch.startDate);
                el('routineEntryTo').value=dateOnly(batch.endDate);
                entryGuard?.markClean();
            }
        }
        try{
            if(!await loadBatchTimetable(1)||!current())return false;
            if(!await loadAssignments(1)||!current())return false;
            if(manager){await loadTeachers(1);if(!current())return false;}
            state.teacherEntries=[];state.teacherPage=1;state.teacherTotal=0;
            el('routineTeacherPage').textContent='No timetable loaded';
            el('routineTeacherPrev').disabled=true;el('routineTeacherNext').disabled=true;
            renderTable('routineTeacherEntries',[],()=>{},4);
            if(manager){
                try{if(!await loadOfferings(1))return false;}
                catch(error){if(current())status('warning','Offered-subject selector unavailable: '+error.message);return false;}
            }
            return current();
        }catch(e){if(current())status('danger',e.message);return false;}
        finally{if(seq===state.loadSeq)btn.disabled=false;}
    }
    function changeBatch(){
        const selection=el('routineBatch');
        if(!sameSession()||state.mutationBusy||[assignmentGuard,entryGuard].some(g=>g?.isDirty()&&!g.confirmDiscard())){
            selection.value=state.appliedBatch;return;
        }
        for(const [id,guard] of [['routineAssignmentForm',assignmentGuard],['routineEntryForm',entryGuard]]){
            el(id)?.reset();guard?.markClean();
        }
        state.assignmentRequest=null;state.entryRequest=null;state.draftAssignment=null;
        state.appliedBatch=selection.value;
        ++state.loadSeq;++state.catalogSeq;++state.assignmentSeq;++state.offeringSeq;
        ++teacherRequestSequence;++batchRequestSequence;++state.teacherTimetableSeq;
        el('routineLoad').disabled=false;
        if(selection.value)load();
    }
    function beginMutation(buttonId){
        if(state.mutationBusy||!sameSession())return false;
        state.mutationBusy=true;
        ++state.loadSeq;++state.catalogSeq;++state.assignmentSeq;++state.offeringSeq;++state.slotSeq;
        ++teacherRequestSequence;++batchRequestSequence;
        for(const id of ['routineFilters','routineSlotForm','routineAssignmentForm','routineEntryForm'])
            if(el(id))el(id).inert=true;
        el('routineBatch').disabled=true;el(buttonId).disabled=true;
        return true;
    }
    function endMutation(buttonId){
        state.mutationBusy=false;
        for(const id of ['routineFilters','routineSlotForm','routineAssignmentForm','routineEntryForm'])
            if(el(id))el(id).inert=false;
        el('routineBatch').disabled=false;el(buttonId).disabled=false;
        el('routineLoad').disabled=false;
    }
    function requestIdFor(field,payload){
        const fingerprint=JSON.stringify(payload),old=state[field];
        if(old?.fingerprint===fingerprint)return old.id;
        const id=clientId();state[field]={fingerprint,id};return id;
    }
    async function saveSlot(e){
        e.preventDefault();if(state.mutationBusy||!sameSession()||!e.currentTarget.reportValidity())return;
        const form=e.currentTarget;
        const start=el('routineStartTime').value,end=el('routineEndTime').value;
        if(start>=end){status('danger','End time must be after start time.');return;}
        if(!beginMutation('routineSaveSlot'))return;
        try{await api('/api/academic-routines/time-slots','POST',{name:el('routineSlotName').value.trim(),startTime:start+':00',endTime:end+':00',isBreak:el('routineIsBreak').checked});
            if(!sameSession())return;
            form.reset();slotGuard?.markClean();const refreshed=await loadSlots();
            status(refreshed?'success':'warning',refreshed?'Time slot saved.':
                'Time slot saved, but the selector could not refresh. Reload before another change.');}
        catch(error){status('danger',error.message+' Check existing time slots before retrying; the form is preserved.');}
        finally{endMutation('routineSaveSlot');}
    }
    async function saveAssignment(e){
        e.preventDefault();const form=e.currentTarget;
        if(state.mutationBusy||!sameSession()||!form.reportValidity())return;
        const batch=selectedBatch();if(!batch)return status('danger','Select a batch first.');
        const from=el('routineAssignFrom').value,to=el('routineAssignTo').value;
        if(to&&to<from)return status('danger','Assignment end date must not precede start date.');
        const offering=el('routineSubject').value,employee=el('routineTeacher').value;
        if(!offering||!employee)return status('danger','Choose an offered subject and an active teacher.');
        const payload={subjectOfferingReference:offering,employeeReference:employee,
            effectiveFrom:from,effectiveTo:to||null,isPrimary:el('routinePrimary').checked,isActive:true};
        const clientRequestId=requestIdFor('assignmentRequest',payload);
        if(!beginMutation('routineAssign'))return;
        try{await api('/api/academic-routines/assignments','POST',{clientRequestId,...payload});
            if(!sameSession())return;
            state.assignmentRequest=null;form.reset();assignmentGuard?.markClean();
            const refreshed=await load(true);
            status(refreshed?'success':'warning',refreshed?'Instructor assigned.':
                'Instructor assigned, but the batch could not refresh. Reload before changing it again.');}
        catch(error){status('danger',error.message+' Check existing assignments before retrying; the draft remains.');}
        finally{endMutation('routineAssign');}
    }
    async function saveEntry(e){
        e.preventDefault();const form=e.currentTarget;
        if(state.mutationBusy||!sameSession()||!form.reportValidity())return;
        const selectedAssignment=el('routineAssignment').value;
        const assignment=state.assignments.find(x=>String(x.id)===selectedAssignment)||
            (entryGuard?.isDirty()&&state.draftAssignment?.batchId===String(selectedBatch()?.id)&&
                String(state.draftAssignment.record.id)===selectedAssignment?state.draftAssignment.record:null);
        if(!assignment?.subjectOfferingReference)return status('danger','Choose an active instructor assignment. Reload the batch.');
        const from=el('routineEntryFrom').value,to=el('routineEntryTo').value;
        if(to&&to<from)return status('danger','Routine end date must not precede start date.');
        const payload={subjectOfferingReference:assignment.subjectOfferingReference,
            instructorAssignmentId:assignment.id,routineTimeSlotId:Number(el('routineSlot').value),
            dayOfWeek:Number(el('routineDay').value),roomId:el('routineRoom').value?Number(el('routineRoom').value):null,
            effectiveFrom:from,effectiveTo:to||null,isActive:true};
        const clientRequestId=requestIdFor('entryRequest',payload);
        if(!beginMutation('routineAddEntry'))return;
        try{await api('/api/academic-routines/entries','POST',{clientRequestId,...payload});
            if(!sameSession())return;
            state.entryRequest=null;state.draftAssignment=null;form.reset();entryGuard?.markClean();
            const refreshed=await load(true);
            status(refreshed?'success':'warning',refreshed?'Class routine saved.':
                'Class routine saved, but the timetable could not refresh. Reload before changing it again.');}
        catch(error){status('danger',error.message+' Check existing timetable entries before retrying; the draft remains.');}
        finally{endMutation('routineAddEntry');}
    }
    async function deactivate(id){
        if(state.mutationBusy||!sameSession())return;
        if(!confirm('Deactivate this routine entry? Existing academic history will remain.'))return;
        const entry=state.entries.find(x=>Number(x.id)===Number(id));
        if(!entry?.rowVersion)return status('danger','Routine version is unavailable. Reload before deactivating.');
        if(!beginMutation('routineLoad'))return;
        try{await api('/api/academic-routines/entries/'+id+'/deactivate?rowVersion='+encodeURIComponent(entry.rowVersion),'POST');
            const refreshed=await load(true);
            status(refreshed?'success':'warning',refreshed?'Routine entry deactivated.':
                'Routine entry deactivated, but the timetable could not refresh. Reload before another action.');}
        catch(e){status('danger',e.message+' Verify the entry status before retrying.');}
        finally{endMutation('routineLoad');}
    }
    async function teacherTimetable(page=1){
        if(!sameSession())return false;
        const batch=selectedBatch(),id=Number(el('routineTeacherTimetable').value);
        if(!batch||!id)return status('warning','Select a batch and teacher first.');
        const q=new URLSearchParams({academicYearId:String(batch.academicYearId),page:String(page),pageSize:'50'});
        if(batch.academicTermId)q.set('academicTermId',String(batch.academicTermId));
        const requestedBatch=String(batch.id),requestedTeacher=String(id),seq=++state.teacherTimetableSeq;
        try{
            const data=await api('/api/academic-routines/teachers/'+id+'/page?'+q);
            if(!sameSession()||seq!==state.teacherTimetableSeq||String(selectedBatch()?.id)!==requestedBatch||
                el('routineTeacherTimetable').value!==requestedTeacher)return false;
            state.teacherEntries=data.items||[];state.teacherPage=data.page||page;state.teacherTotal=data.totalCount||0;
            renderTable('routineTeacherEntries',state.teacherEntries,(tr,x)=>tr.append(cell(days[Number(x.dayOfWeek)]||String(x.dayOfWeek)),cell(x.timeSlotName),cell(x.subjectName),cell(x.roomName||'—')),4);
            const pages=Math.max(1,Math.ceil(state.teacherTotal/50));
            el('routineTeacherPage').textContent='Page '+state.teacherPage+' of '+pages+' ('+state.teacherTotal+' entries)';
            el('routineTeacherPrev').disabled=state.teacherPage<=1;
            el('routineTeacherNext').disabled=state.teacherPage>=pages;
            return true;
        }catch(e){if(seq===state.teacherTimetableSeq)status('danger',e.message);return false;}
    }
    function csvEscape(value){
        let text=String(value===null||value===undefined?'':value);
        if(/^\s*[=+@-]/.test(text))text="'"+text;
        return '"'+text.replace(/"/g,'""')+'"';
    }
    function exportCsv(filename,headers,rows){
        if(!sameSession())return;
        if(!rows.length)return status('warning','Load a timetable with records before exporting.');
        const csv='\uFEFF'+[headers,...rows].map(row=>row.map(csvEscape).join(',')).join('\r\n');
        const url=URL.createObjectURL(new Blob([csv],{type:'text/csv;charset=utf-8'}));
        const a=document.createElement('a');a.href=url;a.download=filename;document.body.append(a);a.click();a.remove();
        setTimeout(()=>URL.revokeObjectURL(url),1000);
    }
    function exportBatch(){
        const batch=selectedBatch();if(!batch)return status('warning','Select a batch.');
        exportCsv('routine-batch-'+batch.id+'-page-'+state.batchPage+'.csv',['Day','Slot','Subject','Room','From','To','Active'],
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
    el('routineBatch').addEventListener('change',changeBatch);
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
    el('routineFindAssignments').addEventListener('click',()=>loadAssignments(1).catch(e=>status('danger',e.message)));
    el('routineAssignmentsPrev').addEventListener('click',()=>loadAssignments(state.assignmentPage-1).catch(e=>status('danger',e.message)));
    el('routineAssignmentsNext').addEventListener('click',()=>loadAssignments(state.assignmentPage+1).catch(e=>status('danger',e.message)));
    el('routineBatchPrev').addEventListener('click',()=>loadBatchTimetable(state.batchPage-1).catch(e=>status('danger',e.message)));
    el('routineBatchNext').addEventListener('click',()=>loadBatchTimetable(state.batchPage+1).catch(e=>status('danger',e.message)));
    el('routineExportBatch').addEventListener('click',exportBatch);
    el('routineExportTeacher').addEventListener('click',exportTeacher);
    el('routineSlotForm')?.addEventListener('submit',saveSlot);
    el('routineAssignmentForm')?.addEventListener('submit',saveAssignment);
    el('routineEntryForm')?.addEventListener('submit',saveEntry);
    el('routineFindTeachers')?.addEventListener('click',()=>loadTeachers(1));
    el('routineTeachersPrev')?.addEventListener('click',()=>loadTeachers(state.instructorPage-1));
    el('routineTeachersNext')?.addEventListener('click',()=>loadTeachers(state.instructorPage+1));
    el('routineTeacherSearch')?.addEventListener('keydown',e=>{
        if(e.key==='Enter'){e.preventDefault();loadTeachers(1);}
    });
    el('routineShowTeacher').addEventListener('click',()=>teacherTimetable(1));
    el('routineTeacherPrev').addEventListener('click',()=>teacherTimetable(state.teacherPage-1));
    el('routineTeacherNext').addEventListener('click',()=>teacherTimetable(state.teacherPage+1));
    el('routineTeacherTimetable').addEventListener('change',()=>{
        ++state.teacherTimetableSeq;
        state.teacherEntries=[];state.teacherPage=1;state.teacherTotal=0;
        el('routineTeacherPage').textContent='Select View schedule';
        el('routineTeacherPrev').disabled=true;el('routineTeacherNext').disabled=true;
        renderTable('routineTeacherEntries',[],()=>{},4);
    });
    el('routineEntries').addEventListener('click',e=>{const button=e.target.closest('button[data-deactivate-id]');if(button)deactivate(Number(button.dataset.deactivateId));});
    init();
})();
