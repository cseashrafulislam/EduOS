import { state, api, el, notice } from './assessments-api.js';
import { roster } from './assessments-roster.js';
export async function saveMarks() {
 if (!state.test || state.test.isPublished) return;
 const results = [];
 for (const tr of el('assessmentRoster').querySelectorAll('tr[data-id]')) {
  const marks = tr.querySelector('[data-field="marks"]').value.trim();
  if (!marks) continue;
  const score = Number(marks);
  if (!Number.isFinite(score) || score < 0 || score > state.test.totalMarks)
   return notice('Marks are outside the valid range.');
  results.push({ applicantId: Number(tr.dataset.id), obtainedMarks: score,
   grade: tr.querySelector('[data-field="grade"]').value.trim() || null,
   remarks: tr.querySelector('[data-field="remarks"]').value.trim() || null });
 }
 if (!results.length) return notice('Enter at least one mark.');
 const button = el('assessmentSave'); button.disabled = true;
 try {
  await api('/api/admission-assessments/tests/' + state.test.id + '/results', 'PUT', { results });
  await roster(); notice('Marks saved.', true);
 } catch (e) { notice(e.message); } finally { button.disabled = false; }
}
