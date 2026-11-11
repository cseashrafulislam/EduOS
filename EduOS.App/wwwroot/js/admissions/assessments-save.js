import { state, api, el, notice } from './assessments-api.js';
import { roster } from './assessments-roster.js';
let saving = false;
export async function saveMarks() {
 if (saving || !state.test || state.test.isPublished) return;
 const test = state.test;
 const page = state.page;
 const results = [];
 for (const tr of el('assessmentRoster').querySelectorAll('tr[data-id]')) {
  const marks = tr.querySelector('[data-field="marks"]').value.trim();
  if (!marks) {
   const grade = tr.querySelector('[data-field="grade"]').value.trim();
   const remarks = tr.querySelector('[data-field="remarks"]').value.trim();
   if (grade || remarks) return notice('Enter marks before grade or remarks.');
   continue;
  }
  const score = Number(marks);
  if (!Number.isFinite(score) || score < 0 || score > test.totalMarks || !/^\d+(?:\.\d{1,2})?$/.test(marks))
   return notice('Marks are outside the valid range.');
  results.push({ applicantId: Number(tr.dataset.id), obtainedMarks: score,
   grade: tr.querySelector('[data-field="grade"]').value.trim() || null,
   remarks: tr.querySelector('[data-field="remarks"]').value.trim() || null });
 }
 if (!results.length) return notice('Enter at least one mark.');
 const button = el('assessmentSave'); saving = true; button.disabled = true;
 try {
  await api('/api/admission-assessments/tests/' + test.id + '/results', 'PUT', { results });
  if (state.test?.id === test.id && state.page === page) {
   await roster(); notice('Marks saved.', true);
  }
 } catch (e) { notice(e.message); } finally { saving = false; button.disabled = !state.test || state.test.isPublished; }
}
