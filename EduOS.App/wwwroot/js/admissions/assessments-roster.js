import { state, api, el, val, notice, cell } from './assessments-api.js';
export async function roster() {
 const body = el('assessmentRoster'); body.replaceChildren();
 if (!state.test) return;
 const qs = new URLSearchParams({ page: String(state.page), pageSize: '20', search: val('assessmentSearch') });
 const data = await api('/api/admission-assessments/tests/' + state.test.id + '/applicants?' + qs);
 state.pages = Math.max(1, data.totalPages || 1);
 for (const applicant of data.items) {
  const tr = document.createElement('tr');
  tr.dataset.id = String(applicant.applicantId);
  tr.append(cell(applicant.applicationNumber), cell(applicant.applicantName));
  for (const [key, type, value] of [
   ['marks', 'number', applicant.hasResult ? applicant.obtainedMarks : ''],
   ['grade', 'text', applicant.grade], ['remarks', 'text', applicant.remarks]
  ]) {
   const field = document.createElement('input');
   field.type = type; field.dataset.field = key;
   field.className = 'form-control form-control-sm'; field.value = String(value ?? '');
   field.disabled = state.test.isPublished;
   if (key === 'marks') { field.min = '0'; field.max = String(state.test.totalMarks); field.step = '0.01'; }
   if (key === 'grade') field.maxLength = 20;
   if (key === 'remarks') field.maxLength = 500;
   const td = document.createElement('td'); td.append(field); tr.append(td);
  }
  body.append(tr);
 }
 el('assessmentPrev').disabled = state.page <= 1;
 el('assessmentNext').disabled = state.page >= state.pages;
 el('assessmentPage').textContent = 'Page ' + state.page + '/' + state.pages + ' · ' + data.totalCount;
}

export { saveMarks } from './assessments-save.js';
