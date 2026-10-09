(() => {
    'use strict';
    const root = document.getElementById('instructionApp');
    if (!root) return;
    const get = id => document.getElementById(id);
    const manager = root.dataset.canManage === 'true';
    const statuses = {1:'Draft',2:'Submitted',3:'Approved',4:'Rejected',5:'In progress',6:'Completed'};
    const state = { batches: [], assignments: [], routines: [], lessons: [], substitutions: [], editingId: null, working: false };
    const dateOnly = value => value ? String(value).slice(0,10) : '';
    const dateForApi = value => value + 'T00:00:00';
    const option = (value, text) => { const e=document.createElement('option');e.value=String(value);e.textContent=text;return e; };
    function status(kind,message) { const x=get('instructionAlert');x.className='alert alert-'+kind;x.textContent=message;x.focus(); }
    function clearStatus() {const x=get('instructionAlert');x.className='d-none';x.textContent='';}
    async function api(url,method='GET',body) {
        const result=await fetch(url,{method,credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json',...(body===undefined?{}:{'Content-Type':'application/json'})},...(body===undefined?{}:{body:JSON.stringify(body)})});
        const payload=await result.json().catch(()=>null);
        if(!result.ok||!payload?.success)throw new Error(payload?.message||'Request failed.');
        return payload.data;
    }
    function setOptions(id,rows,empty) {
        const select=get(id);if(!select)return;
        select.replaceChildren(option('',empty));
        for(const row of rows)select.append(option(row.id,row.name));
    }
    function cell(text) { const td=document.createElement('td');td.textContent=text===null||text===undefined?'':String(text);return td; }
    function actionButton(label,action,id,disabled=false) {
        const button=document.createElement('button');button.type='button';button.className='btn btn-sm btn-outline-primary me-1 mb-1';
        button.textContent=label;button.dataset.action=action;button.dataset.id=String(id);button.disabled=disabled;return button;
    }
    function dateScope() {
        const batchId=Number(get('instructionBatch').value),from=get('instructionFrom').value,to=get('instructionTo').value;
        if(!batchId||!from||!to||from>to)throw new Error('Select a batch and valid date range.');
        if((new Date(to+'T00:00:00')-new Date(from+'T00:00:00'))/86400000>90)throw new Error('Use a range of at most 90 days for substitutions.');
        return{batchId,from,to};
    }
    function selectBatch() {
        resetEditor();
        const id=Number(get('instructionBatch').value);
        get('lessonNew').disabled=!id;
        state.assignments=[];state.routines=[];renderLessons([]);renderSubstitutions([]);
        if(id)load();
    }
    async function init() {
        const now=new Date(),local=new Date(now.getFullYear(),now.getMonth(),now.getDate());
        const iso=d=>[d.getFullYear(),String(d.getMonth()+1).padStart(2,'0'),String(d.getDate()).padStart(2,'0')].join('-');
        const from=new Date(local);from.setDate(from.getDate()-30);
        get('instructionFrom').value=iso(from);get('instructionTo').value=iso(local);
        try {
            const catalog=await api('/api/academic-setup/catalog');
            state.batches=(catalog?.batches||[]).filter(x=>x.isActive).sort((a,b)=>(a.name||'').localeCompare(b.name||''));
            setOptions('instructionBatch',state.batches.map(x=>({id:x.id,name:[x.academicYearName,x.name].filter(Boolean).join(' / ')})),'Select academic batch');
            if(!state.batches.length)status('warning','No active batch available. Configure an academic batch first.');
        } catch(e){status('danger',e.message);}
    }
    async function load() {
        let scope;try{scope=dateScope();}catch(e){status('danger',e.message);return;}
        const btn=get('instructionLoad');btn.disabled=true;clearStatus();
        const q=new URLSearchParams({academicBatchId:String(scope.batchId),fromDate:dateForApi(scope.from),toDate:dateForApi(scope.to)});
        try {
            const [lessonResult,subResult,assignmentResult,routineResult]=await Promise.allSettled([
                api('/api/academic-instruction/lesson-plans?'+q),
                api('/api/academic-instruction/substitutions?'+q),
                api('/api/academic-routines/batches/'+scope.batchId+'/assignments'),
                api('/api/academic-routines/batches/'+scope.batchId)
            ]);
            if(lessonResult.status==='rejected')throw lessonResult.reason;
            if(subResult.status==='rejected')throw subResult.reason;
            state.lessons=lessonResult.value||[];state.substitutions=subResult.value||[];
            state.assignments=assignmentResult.status==='fulfilled'?(assignmentResult.value||[]):[];
            state.routines=routineResult.status==='fulfilled'?(routineResult.value||[]):[];
            renderLessons(state.lessons);renderSubstitutions(state.substitutions);setAssignments();
            if(assignmentResult.status==='rejected'||routineResult.status==='rejected')status('warning','Some routine setup data is unavailable. Review permissions and academic configuration.');
        }catch(e){status('danger',e.message);renderLessons([]);renderSubstitutions([]);}
        finally{btn.disabled=false;}
    }
    function setAssignments() {
        setOptions('lessonAssignment',state.assignments.filter(x=>x.isActive).map(x=>({id:x.id,name:x.employeeName+' / '+(x.subjectOfferingId||'Subject offering')})),'Select instructor assignment');
        setOptions('substitutionRoutine',state.routines.filter(x=>x.isActive).map(x=>({id:x.id,name:(x.subjectName||'Subject')+' / '+(x.timeSlotName||'Time slot')+' / '+(x.dayOfWeek??'')})),'Select routine entry');
        const teachers=new Map();
        for(const a of state.assignments)if(a.isActive&&Number(a.employeeId)>0)teachers.set(Number(a.employeeId),a.employeeName||a.employeeCode||'Teacher '+a.employeeId);
        setOptions('substitutionTeacher',[...teachers].map(([id,name])=>({id,name})),'Select eligible instructor');
    }
    function renderLessons(rows) {
        const body=get('lessonRows');body.replaceChildren();
        if(!rows.length){const tr=document.createElement('tr'),td=cell('No lesson plans in this period.');td.colSpan=6;td.className='text-muted text-center p-3';tr.append(td);body.append(tr);return;}
        for(const row of rows){
            const tr=document.createElement('tr'),td=document.createElement('td');
            tr.append(cell(dateOnly(row.lessonDate)),cell(row.subjectName),cell(row.title),cell(row.employeeName),cell(statuses[Number(row.state)]||row.state));
            const st=Number(row.state);
            if(st===1||st===4){td.append(actionButton('Edit','editLesson',row.id),actionButton('Submit','submitLesson',row.id));}
            if(manager&&st===2){td.append(actionButton('Approve','approveLesson',row.id),actionButton('Reject','rejectLesson',row.id));}
            if(st===3||st===5)td.append(actionButton('Record progress','progressLesson',row.id));
            if(!td.childNodes.length)td.textContent='—';
            tr.append(td);body.append(tr);
        }
    }
    function renderSubstitutions(rows) {
        const body=get('substitutionRows');body.replaceChildren();
        if(!rows.length){const tr=document.createElement('tr'),td=cell('No substitutions in this period.');td.colSpan=manager?8:7;td.className='text-muted text-center p-3';tr.append(td);body.append(tr);return;}
        for(const row of rows){
            const tr=document.createElement('tr');
            tr.append(cell(dateOnly(row.date)),cell(row.batchName+' / '+row.subjectName),cell(row.timeSlotName),cell(row.originalTeacherName),cell(row.substituteTeacherName),cell(row.reason||'—'),cell(row.isActive?'Active':'Cancelled'));
            if(manager){const td=document.createElement('td');if(row.isActive)td.append(actionButton('Cancel','cancelSub',row.id));else td.textContent='—';tr.append(td);}
            body.append(tr);
        }
    }
    function resetEditor(){state.editingId=null;get('lessonEditor').classList.add('d-none');}
    function openEditor(row) {
        state.editingId=row?.id||null;
        get('lessonEditorTitle').textContent=row?'Edit draft lesson plan':'New lesson plan';
        get('lessonTitle').value=row?.title||'';
        get('lessonDate').value=dateOnly(row?.lessonDate)||get('instructionTo').value;
        get('lessonObjectives').value=row?.objectives||'';
        get('lessonContent').value=row?.content||'';
        get('lessonResources').value=row?.resources||'';
        if(row){
            const match=state.assignments.find(x=>Number(x.employeeId)===Number(row.employeeId)&&Number(x.subjectOfferingId)===Number(row.subjectOfferingId));
            if(!match){status('warning','Instructor assignment is no longer active; this lesson cannot be edited until the assignment is available.');return;}
            get('lessonAssignment').value=String(match.id);get('lessonAssignment').disabled=true;
        }else{get('lessonAssignment').disabled=false;get('lessonAssignment').value='';}
        get('lessonEditor').classList.remove('d-none');get('lessonTitle').focus();
    }
    async function saveLesson(e) {
        e.preventDefault();if(!e.currentTarget.reportValidity())return;
        const row=state.lessons.find(x=>Number(x.id)===state.editingId);
        const date=get('lessonDate').value,title=get('lessonTitle').value.trim();
        const body={chapterName:title,topic:null,startDate:dateForApi(date),endDate:dateForApi(date),
            description:get('lessonContent').value.trim()||null,learningObjectives:get('lessonObjectives').value.trim()||null,resources:get('lessonResources').value.trim()||null};
        if(!body.chapterName){status('danger','Chapter title is required.');return;}
        if(row)body.rowVersion=row.rowVersion;
        else{body.clientRequestId=crypto.randomUUID();body.instructorAssignmentId=Number(get('lessonAssignment').value);}
        const btn=get('lessonSave');btn.disabled=true;
        try{await api(row?'/api/academic-instruction/lesson-plans/'+row.id:'/api/academic-instruction/lesson-plans','POST',body);
            resetEditor();await load();status('success','Lesson plan saved.');}
        catch(e){status('danger',e.message);}finally{btn.disabled=false;}
    }
    async function lessonAction(action,id) {
        const row=state.lessons.find(x=>Number(x.id)===id);if(!row)return;
        let endpoint='',body={rowVersion:row.rowVersion};
        if(action==='editLesson'){openEditor(row);return;}
        if(action==='submitLesson')endpoint='submit';
        if(action==='approveLesson'||action==='rejectLesson'){
            endpoint='review';body.approve=action==='approveLesson';
            body.remarks=action==='rejectLesson'?window.prompt('Reason for rejection (required):',''):null;
            if(body.remarks===null&&action==='rejectLesson')return;
            if(action==='rejectLesson'&&!body.remarks?.trim()){status('danger','A rejection reason is required.');return;}
        }
        if(action==='progressLesson'){
            endpoint='progress';const percent=window.prompt('Progress percentage (0–100):',row.state===5?'50':'0');
            if(percent===null)return;const value=Number(percent);if(percent.trim()===''||!Number.isInteger(value)||value<0||value>100){status('danger','Enter an integer percentage between 0 and 100.');return;}
            body.progressPercent=value;body.notes=null;
        }
        if(!endpoint)return;
        try{await api('/api/academic-instruction/lesson-plans/'+id+'/'+endpoint,'POST',body);await load();status('success','Lesson plan updated.');}
        catch(e){status('danger',e.message);}
    }
    async function saveSub(e){
        e.preventDefault();if(!e.currentTarget.reportValidity())return;
        const routineEntryId=Number(get('substitutionRoutine').value),substituteTeacherId=Number(get('substitutionTeacher').value),date=get('substitutionDate').value;
        const selected=state.routines.find(x=>Number(x.id)===routineEntryId);
        if(!selected||!substituteTeacherId){status('danger','Choose an active routine and substitute instructor.');return;}
        const d=new Date(date+'T12:00:00').getDay();
        if(d!==Number(selected.dayOfWeek)){status('danger','Substitution date must match the routine day of the week.');return;}
        const btn=get('substitutionSave');btn.disabled=true;
        try{await api('/api/academic-instruction/substitutions','POST',{clientRequestId:crypto.randomUUID(),routineEntryId,substituteTeacherId,date:dateForApi(date),reason:get('substitutionReason').value.trim()||null});
            get('substitutionForm').reset();await load();status('success','Substitution scheduled.');}
        catch(e){status('danger',e.message);}finally{btn.disabled=false;}
    }
    async function substitutionAction(id) {
        const row=state.substitutions.find(x=>Number(x.id)===id);if(!row)return;
        const reason=window.prompt('Cancellation reason (required):','');if(reason===null)return;
        if(!reason.trim()){status('danger','Cancellation reason is required.');return;}
        try{await api('/api/academic-instruction/substitutions/'+id+'/cancel','POST',{rowVersion:row.rowVersion,reason:reason.trim()});await load();status('success','Substitution cancelled.');}
        catch(e){status('danger',e.message);}
    }
    get('instructionFilters').addEventListener('submit',e=>{e.preventDefault();load();});
    get('instructionBatch').addEventListener('change',selectBatch);
    get('lessonNew').addEventListener('click',()=>openEditor(null));
    get('lessonCancel').addEventListener('click',resetEditor);
    get('lessonForm').addEventListener('submit',saveLesson);
    get('lessonRows').addEventListener('click',e=>{const button=e.target.closest('button[data-action]');if(button)lessonAction(button.dataset.action,Number(button.dataset.id));});
    get('substitutionRows').addEventListener('click',e=>{const button=e.target.closest('button[data-action]');if(button&&button.dataset.action==='cancelSub')substitutionAction(Number(button.dataset.id));});
    get('substitutionForm')?.addEventListener('submit',saveSub);
    init();
})();
