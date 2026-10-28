(() => {
    'use strict';
    const root = document.getElementById('instructionApp');
    if (!root) return;
    const get = id => document.getElementById(id);
    const manager = root.dataset.canManage === 'true';
    const teacher = root.dataset.canTeach === 'true';
    const statuses = {1:'Draft',2:'Submitted',3:'Approved',4:'Rejected',5:'In progress',6:'Completed'};
    const state = { batches: [], offerings: [], instructors: [], routines: [], lessons: [], substitutions: [], editingId: null, lessonPage: 1, lessonPages: 1, lessonCount: 0, substitutionPage: 1, substitutionPages: 1, substitutionCount: 0, requestSeq: 0, draftRequestId: null, substitutionRequestId: null, substitutionRequestPayload: null, routinePage: 1, routinePages: 1, routineCount: 0, routineRequestSeq: 0, offeringPage: 1, offeringPages: 1, offeringCount: 0, offeringRequestSeq: 0, teacherPage: 1, teacherPages: 1, teacherCount: 0, teacherRequestSeq: 0 };
    const dateOnly = value => value ? String(value).slice(0,10) : '';
    const dateForApi = value => value; // ASP.NET DateOnly accepts yyyy-MM-dd, not a timestamp.
    const option = (value, text) => { const e=document.createElement('option');e.value=String(value);e.textContent=text;return e; };
    function status(kind,message) { const x=get('instructionAlert');x.className='alert alert-'+kind;x.textContent=message;x.focus(); }
    function clearStatus() {const x=get('instructionAlert');x.className='d-none';x.textContent='';}
    async function api(url,method='GET',body) {
        const headers = { Accept: 'application/json' };
        if (method !== 'GET') {
            const token = document.querySelector('meta[name="request-verification-token"]')?.content;
            if (!token) throw new Error('Security token missing. Reload the page.');
            headers.RequestVerificationToken = token;
            headers['Content-Type'] = 'application/json';
        }
        const result = await fetch(url, { method, credentials: 'same-origin', cache: 'no-store', headers,
            ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
        const payload = await result.json().catch(() => null);
        if (!result.ok || !payload?.success) throw new Error(payload?.message || 'Request failed.');
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
        state.substitutionRequestId = null; state.substitutionRequestPayload = null;
        const id=Number(get('instructionBatch').value);
        get('lessonNew').disabled=!id;
        state.lessonPage=1;state.substitutionPage=1;state.offeringPage=1;state.offeringPages=1;state.offeringCount=0;state.offeringRequestSeq++;state.routineRequestSeq++;state.teacherRequestSeq++;
        state.routinePage=1;state.teacherPage=1;state.teacherPages=1;state.teacherCount=0;state.routinePages=1;state.routineCount=0;
        if(manager){get('substitutionRoutineSearch').value='';if(!get('substitutionDate').value)get('substitutionDate').value=get('instructionTo').value;}
        get('lessonOfferingSearch').value='';state.offerings=[];state.instructors=[];state.routines=[];state.lessons=[];state.substitutions=[];get('substitutionSummary').textContent='';setOptions('substitutionTeacher',[],'Select substitute instructor');if(manager)updateTeacherPager();renderLessons([]);renderSubstitutions([]);updateLessonPager();updateSubstitutionPager();updateOfferingPager();if(manager)updateRoutinePager();
        if(id)load();
    }
    async function init() {
        const now=new Date(),local=new Date(now.getFullYear(),now.getMonth(),now.getDate());
        const iso=d=>[d.getFullYear(),String(d.getMonth()+1).padStart(2,'0'),String(d.getDate()).padStart(2,'0')].join('-');
        const from=new Date(local);from.setDate(from.getDate()-30);
        get('instructionFrom').value=iso(from);get('instructionTo').value=iso(local);
        if(manager)get('substitutionDate').value=iso(local);
        try {
            const catalog=await api('/api/academic-setup/catalog');
            state.batches=(catalog?.batches||[]).filter(x=>x.isActive).sort((a,b)=>(a.name||'').localeCompare(b.name||''));
            setOptions('instructionBatch',state.batches.map(x=>({id:x.id,name:[x.academicYearName,x.name].filter(Boolean).join(' / ')})),'Select academic batch');
            if(!state.batches.length)status('warning','No active batch available. Configure an academic batch first.');
        } catch(e){status('danger',e.message);}
    }
    async function fetchOfferingPage(batchId, page, search) {
        if (!Number.isSafeInteger(page) || page < 1) throw new Error('Invalid offering page.');
        if (search.length > 100) throw new Error('Offering search is too long.');
        const query = new URLSearchParams({ page: String(page), pageSize: '50' });
        if (search) query.set('search', search);
        const result = await api('/api/academic-instruction/batches/' + batchId + '/lesson-offerings?' + query);
        if (!result || !Array.isArray(result.items)) throw new Error('Invalid subject-offering response.');
        return result;
    }
    function updateOfferingPager() {
        get('lessonOfferingPage').textContent = 'Page ' + state.offeringPage + ' / ' +
            state.offeringPages + ' (' + state.offeringCount + ' offerings)';
        get('lessonOfferingsPrevious').disabled = state.offeringPage <= 1;
        get('lessonOfferingsNext').disabled = state.offeringPage >= state.offeringPages;
    }
    function applyOfferingPage(result, requestedPage) {
        state.offerings = result.items;
        state.offeringPage = Number(result.page || requestedPage);
        state.offeringPages = Math.max(1, Number(result.totalPages || 1));
        state.offeringCount = Number(result.totalCount || 0);
        setAssignments();
        updateOfferingPager();
    }
    async function findOfferings(page = 1) {
        const batchId = Number(get('instructionBatch').value);
        if (!batchId) throw new Error('Select an academic batch first.');
        const search = get('lessonOfferingSearch').value.trim();
        const sequence = ++state.offeringRequestSeq;
        const result = await fetchOfferingPage(batchId, page, search);
        if (sequence !== state.offeringRequestSeq ||
            Number(get('instructionBatch').value) !== batchId ||
            get('lessonOfferingSearch').value.trim() !== search) return;
        applyOfferingPage(result, page);
    }
    function updateTeacherPager() {
        if (!manager) return;
        get('substitutionTeacherPage').textContent = 'Page ' + state.teacherPage + ' / ' +
            state.teacherPages + ' (' + state.teacherCount + ' teachers)';
        get('substitutionTeacherPrevious').disabled = state.teacherPage <= 1;
        get('substitutionTeacherNext').disabled = state.teacherPage >= state.teacherPages;
    }
    async function findInstructors(page = 1) {
        if (!manager) return;
        const batchId = Number(get('instructionBatch').value);
        if (!batchId) throw new Error('Select an academic batch first.');
        const search = get('substitutionTeacherSearch').value.trim();
        if (search.length > 100 || !Number.isSafeInteger(page) || page < 1)
            throw new Error('Invalid instructor search or page.');
        const seq = ++state.teacherRequestSeq;
        const query = new URLSearchParams({ page: String(page), pageSize: '50' });
        if (search) query.set('search', search);
        const result = await api('/api/academic-routines/batches/' + batchId + '/instructors/page?' + query);
        if (seq !== state.teacherRequestSeq || Number(get('instructionBatch').value) !== batchId ||
            get('substitutionTeacherSearch').value.trim() !== search) return;
        if (!result || !Array.isArray(result.items)) throw new Error('Invalid instructor page response.');
        const selected = get('substitutionTeacher').value;
        state.instructors = result.items;
        state.teacherPage = Number(result.page || page);
        state.teacherPages = Math.max(1, Number(result.totalPages || 1));
        state.teacherCount = Number(result.totalCount || 0);
        setOptions('substitutionTeacher', state.instructors.map(x => ({
            id: x.employeeReference, name: [x.employeeCode, x.name].filter(Boolean).join(' / ')
        })), 'Select substitute instructor');
        if (state.instructors.some(x => x.employeeReference === selected))
            get('substitutionTeacher').value = selected;
        updateTeacherPager();
    }

    async function fetchRoutinePage(batchId, date, page, search) {
        if(!manager)return { items: [], page: 1, totalPages: 1, totalCount: 0 };
        if(!date)throw new Error('Select a substitution date.');
        if(!Number.isSafeInteger(page)||page<1||search.length>100)throw new Error('Invalid routine page or search.');
        const query=new URLSearchParams({date,page:String(page),pageSize:'50'});
        if(search)query.set('search',search);
        const result=await api('/api/academic-instruction/batches/'+batchId+'/substitution-routines?'+query);
        if(!result||!Array.isArray(result.items))throw new Error('Invalid routine choices response.');
        return result;
    }
    function updateRoutinePager() {
        if(!manager)return;
        get('substitutionRoutinePage').textContent='Page '+state.routinePage+' / '+
            state.routinePages+' ('+state.routineCount+' routines)';
        get('substitutionRoutinePrevious').disabled=state.routinePage<=1;
        get('substitutionRoutineNext').disabled=state.routinePage>=state.routinePages;
    }
    function setRoutineOptions() {
        if(!manager)return;
        const selected=get('substitutionRoutine').value;
        setOptions('substitutionRoutine',state.routines.map(x=>({
            id:x.id,name:[x.subjectName,x.timeSlotName,x.originalTeacherName].filter(Boolean).join(' / ')
        })),'Select scheduled routine');
        if(state.routines.some(x=>String(x.id)===selected))get('substitutionRoutine').value=selected;
    }
    function applyRoutinePage(result,requestedPage) {
        state.routines=result.items;
        state.routinePage=Number(result.page||requestedPage);
        state.routinePages=Math.max(1,Number(result.totalPages||1));
        state.routineCount=Number(result.totalCount||0);
        setRoutineOptions();updateRoutinePager();
    }
    async function findRoutines(page=1) {
        if(!manager)return;
        const batchId=Number(get('instructionBatch').value);
        if(!batchId)throw new Error('Select an academic batch first.');
        const date=get('substitutionDate').value,search=get('substitutionRoutineSearch').value.trim();
        const seq=++state.routineRequestSeq;
        const result=await fetchRoutinePage(batchId,date,page,search);
        if(seq!==state.routineRequestSeq||Number(get('instructionBatch').value)!==batchId||
           get('substitutionDate').value!==date||
           get('substitutionRoutineSearch').value.trim()!==search)return;
        applyRoutinePage(result,page);
    }
    function updateLessonPager() {
        get('lessonPage').textContent = 'Page ' + state.lessonPage + ' / ' + state.lessonPages +
            ' (' + state.lessonCount + ' plans)';
        get('lessonPrevious').disabled = state.lessonPage <= 1;
        get('lessonNext').disabled = state.lessonPage >= state.lessonPages;
        get('lessonCsv').disabled = !state.lessons.length;
    }
    function updateSubstitutionPager() {
        get('substitutionPage').textContent = 'Page ' + state.substitutionPage + ' / ' + state.substitutionPages +
            ' (' + state.substitutionCount + ' substitutions)';
        get('substitutionPrevious').disabled = state.substitutionPage <= 1;
        get('substitutionNext').disabled = state.substitutionPage >= state.substitutionPages;
        get('substitutionCsv').disabled = !state.substitutions.length;
    }
    async function load() {
        let scope; try { scope = dateScope(); } catch (e) { status('danger', e.message); return; }
        const seq = ++state.requestSeq;
        const offeringSeq = ++state.offeringRequestSeq;
        const routineSeq = ++state.routineRequestSeq;
        const btn = get('instructionLoad'); btn.disabled = true; clearStatus();
        const q = new URLSearchParams({ academicBatchId: String(scope.batchId),
            fromDate: dateForApi(scope.from), toDate: dateForApi(scope.to) });
        const lessonQuery = new URLSearchParams(q);
        lessonQuery.set('page', String(state.lessonPage)); lessonQuery.set('pageSize', '20');
        const substitutionQuery = new URLSearchParams(q);
        substitutionQuery.set('page', String(state.substitutionPage)); substitutionQuery.set('pageSize', '20');
        const statusFilter = get('substitutionStatus').value;
        if (statusFilter) substitutionQuery.set('isCancelled', statusFilter);
        try {
            const [lessonResult, subResult, summaryResult, offeringResult, routineResult] = await Promise.allSettled([
                api('/api/academic-instruction/lesson-plans?' + lessonQuery),
                api('/api/academic-instruction/substitutions/page?' + substitutionQuery),
                api('/api/academic-instruction/substitutions/summary?' + q),
                fetchOfferingPage(scope.batchId, state.offeringPage, get('lessonOfferingSearch').value.trim()),
                fetchRoutinePage(scope.batchId, manager ? get('substitutionDate').value : scope.to,
                    state.routinePage, manager ? get('substitutionRoutineSearch').value.trim() : '')
            ]);
            if (seq !== state.requestSeq) return;
            if (lessonResult.status === 'rejected') throw lessonResult.reason;
            if (subResult.status === 'rejected') throw subResult.reason;
            const page = lessonResult.value;
            if (!page || !Array.isArray(page.items)) throw new Error('Invalid lesson-plan register response.');
            state.lessons = page.items;
            state.lessonCount = Number(page.totalCount || 0);
            state.lessonPages = Math.max(1, Number(page.totalPages || 1));
            const subPage = subResult.value;
            if (!subPage || !Array.isArray(subPage.items)) throw new Error('Invalid substitution register response.');
            state.substitutions = subPage.items;
            state.substitutionCount = Number(subPage.totalCount || 0);
            state.substitutionPages = Math.max(1, Number(subPage.totalPages || 1));
            if (summaryResult.status === 'fulfilled' && summaryResult.value) {
                const summary = summaryResult.value;
                get('substitutionSummary').textContent = Number(summary.activeCount || 0) +
                    ' active · ' + Number(summary.cancelledCount || 0) + ' cancelled';
            } else get('substitutionSummary').textContent = 'Summary unavailable';
            if (offeringSeq === state.offeringRequestSeq) {
                if (offeringResult.status === 'fulfilled') applyOfferingPage(offeringResult.value, state.offeringPage);
                else {
                    state.offerings = []; state.offeringPage = 1; state.offeringPages = 1;
                    state.offeringCount = 0; updateOfferingPager();
                }
            }
            if(manager&&routineSeq===state.routineRequestSeq){
                if(routineResult.status==='fulfilled')applyRoutinePage(routineResult.value,state.routinePage);
                else{state.routines=[];state.routinePage=1;state.routinePages=1;state.routineCount=0;
                    setRoutineOptions();updateRoutinePager();}
            }
            renderLessons(state.lessons); renderSubstitutions(state.substitutions);
            setAssignments(); updateLessonPager(); updateSubstitutionPager();
            if (manager) {
                try { await findInstructors(state.teacherPage); }
                catch (e) { if (seq === state.requestSeq) status('warning', 'Instructor search: ' + e.message); }
            }
            if (offeringResult.status === 'rejected' || (manager && routineResult.status === 'rejected'))
                status('warning', 'Some active offerings or timetable entries are unavailable. Existing lesson plans remain readable.');
        } catch (e) {
            if (seq === state.requestSeq) {
                state.lessons = []; state.substitutions = []; state.lessonPages = 1; state.lessonCount = 0;
                state.substitutionPages = 1; state.substitutionCount = 0;
                get('substitutionSummary').textContent = '';
                renderLessons([]); renderSubstitutions([]); updateLessonPager(); updateSubstitutionPager(); status('danger', e.message);
            }
        } finally { if (seq === state.requestSeq) btn.disabled = false; }
    }
    function setAssignments() {
        const editing = state.editingId === null ? null :
            state.lessons.find(x => Number(x.id) === Number(state.editingId));
        if (editing) {
            const ref = editing.subjectOfferingReference;
            setOptions('lessonAssignment', ref ? [{
                id: ref, name: editing.subjectName || 'Saved subject offering'
            }] : [], 'Subject offering unavailable');
            get('lessonAssignment').value = ref || '';
        } else {
            const selected = get('lessonAssignment').value;
            const choices = state.offerings.filter(x => x.isActive).map(x => ({
                id: x.reference, name: [x.subjectCode, x.subjectName, x.code].filter(Boolean).join(' / ')
            }));
            setOptions('lessonAssignment', choices, 'Select subject offering');
            if (choices.some(x => String(x.id) === selected)) get('lessonAssignment').value = selected;
        }
        setRoutineOptions();
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
            if(teacher&&st===3) td.append(actionButton('Start','startLesson',row.id));
            if(teacher&&st===5) td.append(actionButton('Complete','completeLesson',row.id));
            if(!td.childNodes.length)td.textContent='—';
            tr.append(td);body.append(tr);
        }
    }
    function renderSubstitutions(rows) {
        const body = get('substitutionRows'); body.replaceChildren();
        if (!rows.length) {
            const tr = document.createElement('tr'), td = cell('No substitutions in this period.');
            td.colSpan = manager ? 8 : 7; td.className = 'text-muted text-center p-3';
            tr.append(td); body.append(tr); return;
        }
        for (const row of rows) {
            const tr = document.createElement('tr');
            tr.append(cell(dateOnly(row.date)), cell(row.contextUnavailable ? 'Archived routine context' :
                [row.batchName, row.subjectName].filter(Boolean).join(' / ')),
                cell(row.contextUnavailable ? 'Unavailable' : row.timeSlotName),
                cell(row.contextUnavailable ? 'Unavailable' : row.originalTeacherName),
                cell(row.substituteEmployeeName), cell(row.reason || '—'),
                cell(row.isCancelled ? 'Cancelled' : 'Active'));
            if (manager) {
                const td = document.createElement('td');
                if (!row.isCancelled) td.append(actionButton('Cancel', 'cancelSub', row.id));
                else td.textContent = '—';
                tr.append(td);
            }
            body.append(tr);
        }
    }
    function resetEditor(){state.editingId=null;get('lessonEditor').classList.add('d-none');}
    function openEditor(row) {
        if (row && !row.subjectOfferingReference) {
            status('warning', 'The saved subject offering is unavailable. Reload before editing.');
            return;
        }
        state.editingId = row?.id || null;
        state.draftRequestId = row ? null : crypto.randomUUID();
        get('lessonEditorTitle').textContent = row ? 'Edit draft lesson plan' : 'New lesson plan';
        get('lessonTitle').value = row?.title || '';
        get('lessonDate').value = dateOnly(row?.lessonDate) || get('instructionTo').value;
        get('lessonObjectives').value = row?.objectives || '';
        get('lessonContent').value = row?.content || '';
        get('lessonResources').value = row?.resources || '';
        setAssignments();
        get('lessonAssignment').value = row?.subjectOfferingReference || '';
        get('lessonAssignment').disabled = !!row;
        get('lessonEditor').classList.remove('d-none'); get('lessonTitle').focus();
    }
    async function saveLesson(e) {
        e.preventDefault();
        const form = e.currentTarget;
        if (!form.reportValidity()) return;
        const row = state.lessons.find(x => Number(x.id) === state.editingId);
        const date = get('lessonDate').value, title = get('lessonTitle').value.trim();
        const offering = row ? row.subjectOfferingReference : get('lessonAssignment').value;
        if (!title || !date || !offering) { status('danger', 'Title, date and subject offering are required.'); return; }
        const body = {
            clientRequestId: row ? crypto.randomUUID() : state.draftRequestId,
            subjectOfferingReference: offering, lessonDate: dateForApi(date), title,
            objectives: get('lessonObjectives').value.trim() || null,
            content: get('lessonContent').value.trim() || null,
            resources: get('lessonResources').value.trim() || null
        };
        if (row) body.rowVersion = row.rowVersion;
        const btn = get('lessonSave'); btn.disabled = true;
        try {
            await api(row ? '/api/academic-instruction/lesson-plans/' + row.id :
                '/api/academic-instruction/lesson-plans', 'POST', body);
            resetEditor(); state.draftRequestId = null; await load();
            status('success', 'Lesson plan saved.');
        } catch (e) { status('danger', e.message); }
        finally { btn.disabled = false; }
    }
    async function lessonAction(action, id) {
        const row = state.lessons.find(x => Number(x.id) === id);
        if (!row) return;
        if (action === 'editLesson') { openEditor(row); return; }
        let endpoint, body;
        if (action === 'submitLesson') { endpoint = 'submit'; body = row.rowVersion; }
        else if (manager && (action === 'approveLesson' || action === 'rejectLesson')) {
            endpoint = 'review';
            const approve = action === 'approveLesson';
            const note = approve ? null : window.prompt('Reason for rejection (required):', '');
            if (!approve && note === null) return;
            if (!approve && !note?.trim()) { status('danger', 'A rejection reason is required.'); return; }
            body = { approve, note: note?.trim() || null, rowVersion: row.rowVersion };
        } else if (teacher && (action === 'startLesson' || action === 'completeLesson')) {
            endpoint = action === 'startLesson' ? 'start' : 'complete';
            body = { rowVersion: row.rowVersion };
        } else return;
        try {
            await api('/api/academic-instruction/lesson-plans/' + id + '/' + endpoint, 'POST', body);
            await load(); status('success', 'Lesson plan updated.');
        } catch (e) { status('danger', e.message); }
    }
    function substitutionRequestIdFor(payload) {
        // Reuse the key only for an identical retry after an uncertain response.
        // A changed date, routine, teacher or reason is a new business request.
        const fingerprint = JSON.stringify(payload);
        if (state.substitutionRequestPayload !== fingerprint || !state.substitutionRequestId) {
            state.substitutionRequestId = crypto.randomUUID();
            state.substitutionRequestPayload = fingerprint;
        }
        return state.substitutionRequestId;
    }
    async function saveSub(e) {
        e.preventDefault();
        const form = e.currentTarget;
        if (!form.reportValidity()) return;
        const routineEntryId = Number(get('substitutionRoutine').value);
        const substituteEmployeeReference = get('substitutionTeacher').value;
        const date = get('substitutionDate').value;
        const selected = state.routines.find(x => Number(x.id) === routineEntryId);
        if (!selected || !substituteEmployeeReference || !date) {
            status('danger', 'Choose a routine, eligible substitute and date.'); return;
        }
        const day = new Date(date + 'T12:00:00').getDay();
        if (day !== Number(selected.dayOfWeek)) {
            status('danger', 'Substitution date must match the routine day of week.'); return;
        }
        const payload = { routineEntryId, substituteEmployeeReference, date: dateForApi(date),
            reason: get('substitutionReason').value.trim() || null };
        const clientRequestId = substitutionRequestIdFor(payload);
        const btn = get('substitutionSave'); btn.disabled = true;
        try {
            await api('/api/academic-instruction/substitutions', 'POST', {
                clientRequestId, ...payload
            });
            state.substitutionRequestId = null; state.substitutionRequestPayload = null; form.reset();
            get('substitutionDate').value=get('instructionTo').value;
            state.routinePage=1;await load(); status('success', 'Substitution scheduled.');
        } catch (e) { status('danger', e.message); }
        finally { btn.disabled = false; }
    }
    async function substitutionAction(id) {
        const row = state.substitutions.find(x => Number(x.id) === id);
        if (!row || row.isCancelled || !window.confirm('Cancel this substitution?')) return;
        try {
            await api('/api/academic-instruction/substitutions/' + id + '/cancel',
                'POST', { rowVersion: row.rowVersion });
            await load(); status('success', 'Substitution cancelled.');
        } catch (e) { status('danger', e.message); }
    }
    function exportSubstitutions() {
        const headers = ['Date', 'Batch', 'Subject', 'Time slot', 'Original teacher', 'Substitute', 'Reason', 'Status', 'Routine context'];
        const rows = state.substitutions.map(x => [dateOnly(x.date), x.batchName, x.subjectName,
            x.timeSlotName, x.originalTeacherName, x.substituteEmployeeName,
            x.reason, x.isCancelled ? 'Cancelled' : 'Active',
            x.contextUnavailable ? 'Archived / unavailable' : 'Available']);
        const safe = value => {
            let s = String(value ?? '');
            if (/^[=+@\-\t\r]/.test(s)) s = "'" + s;
            return '"' + s.replaceAll('"', '""') + '"';
        };
        const csv = String.fromCharCode(0xfeff) + [headers, ...rows]
            .map(x => x.map(safe).join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
        const a = document.createElement('a'); a.href = url;
        a.download = 'academic-substitutions-' +
            (get('substitutionStatus').value === 'true' ? 'cancelled' :
                get('substitutionStatus').value === 'false' ? 'active' : 'all') +
            '-page-' + state.substitutionPage + '.csv'; a.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    function exportLessons() {
        const headers = ['Lesson date', 'Subject', 'Title', 'Teacher', 'State', 'Completed UTC', 'Objectives', 'Content', 'Resources'];
        const rows = state.lessons.map(x => [dateOnly(x.lessonDate), x.subjectName, x.title,
            x.employeeName, statuses[Number(x.state)] || x.state, x.completedAt || '', x.objectives, x.content, x.resources]);
        const safe = value => {
            let s = String(value ?? '');
            if (/^[=+@\-\t\r]/.test(s)) s = "'" + s;
            return '"' + s.replaceAll('"', '""') + '"';
        };
        const csv = String.fromCharCode(0xfeff) + [headers, ...rows]
            .map(x => x.map(safe).join(',')).join('\r\n');
        const url = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
        const a = document.createElement('a'); a.href = url;
        a.download = 'academic-lesson-plans-page-' + state.lessonPage + '.csv'; a.click();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }
    get('instructionFilters').addEventListener('submit',e=>{e.preventDefault();state.lessonPage=1;state.substitutionPage=1;load();});
    get('instructionBatch').addEventListener('change',selectBatch);
    get('lessonNew').addEventListener('click',()=>openEditor(null));
    get('lessonCancel').addEventListener('click',resetEditor);
    get('lessonForm').addEventListener('submit',saveLesson);
    get('lessonRows').addEventListener('click',e=>{const button=e.target.closest('button[data-action]');if(button)lessonAction(button.dataset.action,Number(button.dataset.id));});
    get('substitutionRows').addEventListener('click',e=>{const button=e.target.closest('button[data-action]');if(button&&button.dataset.action==='cancelSub')substitutionAction(Number(button.dataset.id));});
    get('substitutionForm')?.addEventListener('submit',saveSub);
    get('substitutionTeacherFind')?.addEventListener('click',()=>findInstructors(1).catch(e=>status('danger',e.message)));
    get('substitutionTeacherSearch')?.addEventListener('keydown',e=>{
        if(e.key!=='Enter')return;e.preventDefault();
        findInstructors(1).catch(error=>status('danger',error.message));
    });
    get('substitutionTeacherPrevious')?.addEventListener('click',()=>{
        if(state.teacherPage>1)findInstructors(state.teacherPage-1).catch(e=>status('danger',e.message));
    });
    get('substitutionTeacherNext')?.addEventListener('click',()=>{
        if(state.teacherPage<state.teacherPages)findInstructors(state.teacherPage+1).catch(e=>status('danger',e.message));
    });
    get('substitutionRoutineFind')?.addEventListener('click',()=>findRoutines(1).catch(e=>status('danger',e.message)));
    get('substitutionDate')?.addEventListener('change',()=>{
        state.routineRequestSeq++; state.routinePage=1; state.routines=[];
        setRoutineOptions(); updateRoutinePager();
        findRoutines(1).catch(e=>status('danger',e.message));
    });
    get('substitutionRoutineSearch')?.addEventListener('input',()=>{
        state.routineRequestSeq++; state.routines=[]; state.routinePage=1;
        setRoutineOptions(); updateRoutinePager();
    });
    get('substitutionTeacherSearch')?.addEventListener('input',()=>{
        state.teacherRequestSeq++; state.instructors=[]; state.teacherPage=1;
        setOptions('substitutionTeacher',[],'Search teachers'); updateTeacherPager();
    });
    get('substitutionRoutineSearch')?.addEventListener('keydown',e=>{
        if(e.key!=='Enter')return;e.preventDefault();
        findRoutines(1).catch(error=>status('danger',error.message));
    });
    get('substitutionRoutinePrevious')?.addEventListener('click',()=>{
        if(state.routinePage>1)findRoutines(state.routinePage-1).catch(e=>status('danger',e.message));
    });
    get('substitutionRoutineNext')?.addEventListener('click',()=>{
        if(state.routinePage<state.routinePages)findRoutines(state.routinePage+1).catch(e=>status('danger',e.message));
    });
    get('lessonFindOfferings').addEventListener('click', () =>
        findOfferings(1).catch(e => status('danger', e.message)));
    get('lessonOfferingSearch').addEventListener('keydown', e => {
        if (e.key !== 'Enter') return;
        e.preventDefault();
        findOfferings(1).catch(error => status('danger', error.message));
    });
    get('lessonOfferingsPrevious').addEventListener('click', () => {
        if (state.offeringPage > 1) findOfferings(state.offeringPage - 1).catch(e => status('danger', e.message));
    });
    get('lessonOfferingsNext').addEventListener('click', () => {
        if (state.offeringPage < state.offeringPages)
            findOfferings(state.offeringPage + 1).catch(e => status('danger', e.message));
    });
    get('instructionHelp').addEventListener('click', () => {
        const guide = get('instructionHelpText');
        const expanded = get('instructionHelp').getAttribute('aria-expanded') === 'true';
        guide.classList.toggle('d-none', expanded);
        get('instructionHelp').setAttribute('aria-expanded', String(!expanded));
    });
    get('lessonPrevious').addEventListener('click',()=>{if(state.lessonPage>1){state.lessonPage--;load();}});
    get('lessonNext').addEventListener('click',()=>{if(state.lessonPage<state.lessonPages){state.lessonPage++;load();}});
    get('substitutionStatus').addEventListener('change', () => {
        state.substitutionPage = 1;
        load();
    });
    get('substitutionPrevious').addEventListener('click',()=>{if(state.substitutionPage>1){state.substitutionPage--;load();}});
    get('substitutionNext').addEventListener('click',()=>{if(state.substitutionPage<state.substitutionPages){state.substitutionPage++;load();}});
    get('lessonCsv').addEventListener('click',exportLessons);
    get('substitutionCsv').addEventListener('click',exportSubstitutions);
    get('lessonPrint').addEventListener('click',()=>window.print());
    init();
})();
