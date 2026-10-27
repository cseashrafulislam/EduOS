(() => {
    'use strict';
    const root = document.getElementById('instructionApp');
    if (!root) return;
    const get = id => document.getElementById(id);
    const manager = root.dataset.canManage === 'true';
    const statuses = {1:'Draft',2:'Submitted',3:'Approved',4:'Rejected',5:'In progress',6:'Completed'};
    const state = { batches: [], offerings: [], instructors: [], routines: [], lessons: [], substitutions: [], editingId: null, lessonPage: 1, lessonPages: 1, lessonCount: 0, requestSeq: 0, draftRequestId: null, substitutionRequestId: null };
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
        const id=Number(get('instructionBatch').value);
        get('lessonNew').disabled=!id;
        state.lessonPage=1;state.offerings=[];state.instructors=[];state.routines=[];state.lessons=[];state.substitutions=[];renderLessons([]);renderSubstitutions([]);updateLessonPager();
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
    async function loadOfferings(batchId) {
        const rows = [];
        for (let page = 1; page <= 5; page++) {
            const result = await api('/api/academic-setup/batches/' + batchId + '/offerings?' +
                new URLSearchParams({ page: String(page), pageSize: '100' }));
            rows.push(...(result?.items || []));
            if (page >= (result?.totalPages || 1)) break;
        }
        return rows;
    }
    async function findInstructors() {
        if (!manager) return;
        const search = get('substitutionTeacherSearch').value.trim();
        if (search.length > 100) throw new Error('Instructor search is too long.');
        const choices = await api('/api/academic-routines/instructors?' + new URLSearchParams({ search }));
        state.instructors = choices || [];
        setOptions('substitutionTeacher', state.instructors.map(x => ({
            id: x.employeeReference, name: [x.employeeCode, x.name].filter(Boolean).join(' / ')
        })), 'Select substitute instructor');
    }
    function updateLessonPager() {
        get('lessonPage').textContent = 'Page ' + state.lessonPage + ' / ' + state.lessonPages +
            ' (' + state.lessonCount + ' plans)';
        get('lessonPrevious').disabled = state.lessonPage <= 1;
        get('lessonNext').disabled = state.lessonPage >= state.lessonPages;
        get('lessonCsv').disabled = !state.lessons.length;
    }
    async function load() {
        let scope; try { scope = dateScope(); } catch (e) { status('danger', e.message); return; }
        const seq = ++state.requestSeq;
        const btn = get('instructionLoad'); btn.disabled = true; clearStatus();
        const q = new URLSearchParams({ academicBatchId: String(scope.batchId),
            fromDate: dateForApi(scope.from), toDate: dateForApi(scope.to) });
        const lessonQuery = new URLSearchParams(q);
        lessonQuery.set('page', String(state.lessonPage)); lessonQuery.set('pageSize', '20');
        try {
            const [lessonResult, subResult, offeringResult, routineResult] = await Promise.allSettled([
                api('/api/academic-instruction/lesson-plans?' + lessonQuery),
                api('/api/academic-instruction/substitutions?' + q),
                loadOfferings(scope.batchId),
                api('/api/academic-routines/batches/' + scope.batchId)
            ]);
            if (seq !== state.requestSeq) return;
            if (lessonResult.status === 'rejected') throw lessonResult.reason;
            if (subResult.status === 'rejected') throw subResult.reason;
            const page = lessonResult.value;
            if (!page || !Array.isArray(page.items)) throw new Error('Invalid lesson-plan register response.');
            state.lessons = page.items;
            state.lessonCount = Number(page.totalCount || 0);
            state.lessonPages = Math.max(1, Number(page.totalPages || 1));
            state.substitutions = subResult.value || [];
            state.offerings = offeringResult.status === 'fulfilled' ? offeringResult.value : [];
            state.routines = routineResult.status === 'fulfilled' ? (routineResult.value || []) : [];
            renderLessons(state.lessons); renderSubstitutions(state.substitutions);
            setAssignments(); updateLessonPager();
            if (manager) {
                try { await findInstructors(); }
                catch (e) { if (seq === state.requestSeq) status('warning', 'Instructor search: ' + e.message); }
            }
            if (offeringResult.status === 'rejected' || routineResult.status === 'rejected')
                status('warning', 'Some active offerings or timetable entries are unavailable. Existing lesson plans remain readable.');
        } catch (e) {
            if (seq === state.requestSeq) {
                state.lessons = []; state.substitutions = []; state.lessonPages = 1; state.lessonCount = 0;
                renderLessons([]); renderSubstitutions([]); updateLessonPager(); status('danger', e.message);
            }
        } finally { if (seq === state.requestSeq) btn.disabled = false; }
    }
    function setAssignments() {
        setOptions('lessonAssignment', state.offerings.filter(x => x.isActive).map(x => ({
            id: x.reference, name: [x.subjectCode, x.subjectName, x.code].filter(Boolean).join(' / ')
        })), 'Select subject offering');
        setOptions('substitutionRoutine', state.routines.filter(x => x.isActive).map(x => ({
            id: x.id, name: [x.subjectName, x.timeSlotName, x.dayOfWeek].filter(x => x !== null && x !== undefined).join(' / ')
        })), 'Select routine entry');
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
            // Approved plans remain read-only; no progress endpoint exists in the canonical API.
            if(!td.childNodes.length)td.textContent='—';
            tr.append(td);body.append(tr);
        }
    }
    function renderSubstitutions(rows) {
        const body = get('substitutionRows'); body.replaceChildren();
        if (!rows.length) {
            const tr = document.createElement('tr'), td = cell('No substitutions in this period.');
            td.colSpan = manager ? 6 : 5; td.className = 'text-muted text-center p-3';
            tr.append(td); body.append(tr); return;
        }
        for (const row of rows) {
            const tr = document.createElement('tr');
            tr.append(cell(dateOnly(row.date)), cell('Routine #' + row.routineEntryId),
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
        const offering = row ? state.offerings.find(x => Number(x.id) === Number(row.subjectOfferingId)) : null;
        if (row && !offering) {
            status('warning', 'The subject offering is not in the current active register; it cannot be edited here.');
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
        get('lessonAssignment').value = offering?.reference || '';
        get('lessonAssignment').disabled = !!row;
        get('lessonEditor').classList.remove('d-none'); get('lessonTitle').focus();
    }
    async function saveLesson(e) {
        e.preventDefault();
        const form = e.currentTarget;
        if (!form.reportValidity()) return;
        const row = state.lessons.find(x => Number(x.id) === state.editingId);
        const date = get('lessonDate').value, title = get('lessonTitle').value.trim();
        const offering = get('lessonAssignment').value;
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
        } else return;
        try {
            await api('/api/academic-instruction/lesson-plans/' + id + '/' + endpoint, 'POST', body);
            await load(); status('success', 'Lesson plan updated.');
        } catch (e) { status('danger', e.message); }
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
        const btn = get('substitutionSave'); btn.disabled = true;
        state.substitutionRequestId ||= crypto.randomUUID();
        try {
            await api('/api/academic-instruction/substitutions', 'POST', {
                clientRequestId: state.substitutionRequestId, routineEntryId,
                substituteEmployeeReference, date: dateForApi(date),
                reason: get('substitutionReason').value.trim() || null
            });
            state.substitutionRequestId = null; form.reset();
            await load(); status('success', 'Substitution scheduled.');
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
    function exportLessons() {
        const headers = ['Lesson date', 'Subject', 'Title', 'Teacher', 'State', 'Objectives', 'Content', 'Resources'];
        const rows = state.lessons.map(x => [dateOnly(x.lessonDate), x.subjectName, x.title,
            x.employeeName, statuses[Number(x.state)] || x.state, x.objectives, x.content, x.resources]);
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
    get('instructionFilters').addEventListener('submit',e=>{e.preventDefault();state.lessonPage=1;load();});
    get('instructionBatch').addEventListener('change',selectBatch);
    get('lessonNew').addEventListener('click',()=>openEditor(null));
    get('lessonCancel').addEventListener('click',resetEditor);
    get('lessonForm').addEventListener('submit',saveLesson);
    get('lessonRows').addEventListener('click',e=>{const button=e.target.closest('button[data-action]');if(button)lessonAction(button.dataset.action,Number(button.dataset.id));});
    get('substitutionRows').addEventListener('click',e=>{const button=e.target.closest('button[data-action]');if(button&&button.dataset.action==='cancelSub')substitutionAction(Number(button.dataset.id));});
    get('substitutionForm')?.addEventListener('submit',saveSub);
    get('substitutionTeacherFind')?.addEventListener('click',()=>findInstructors().catch(e=>status('danger',e.message)));
    get('lessonPrevious').addEventListener('click',()=>{if(state.lessonPage>1){state.lessonPage--;load();}});
    get('lessonNext').addEventListener('click',()=>{if(state.lessonPage<state.lessonPages){state.lessonPage++;load();}});
    get('lessonCsv').addEventListener('click',exportLessons);
    get('lessonPrint').addEventListener('click',()=>window.print());
    init();
})();
