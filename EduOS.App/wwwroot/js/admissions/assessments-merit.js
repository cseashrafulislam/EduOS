import { state, api, el, notice, cell } from './assessments-api.js';
export async function merit() {
 if (!state.test) return;
 const data = await api('/api/admission-assessments/tests/' + state.test.id + '/merit');
 const target = el('assessmentMeritList');
 const table = document.createElement('table'); table.className = 'table table-sm';
 const header = document.createElement('thead'), heading = document.createElement('tr');
 for (const label of ['Position', 'Applicant', 'Marks', 'Result']) {
  const th = document.createElement('th'); th.scope = 'col'; th.textContent = label; heading.append(th);
 }
 header.append(heading); table.append(header);
 const body = document.createElement('tbody');
 for (const row of data.results) {
  const tr = document.createElement('tr');
  tr.append(cell(row.meritPosition || '—'), cell(row.applicantName),
   cell(row.obtainedMarks), cell(row.isPassed ? 'Pass' : 'Fail'));
  body.append(tr);
 }
 table.append(body);
 const title = document.createElement('p');
 title.textContent = data.test.isPublished ? 'Published merit list' : 'Draft merit preview';
 target.replaceChildren(title, table);
}
export async function publish(refresh) {
 if (!state.test || state.test.isPublished || !window.confirm('Publish merit? Marks become locked.')) return;
 const button = el('assessmentPublish'); button.disabled = true;
 try {
  await api('/api/admission-assessments/tests/' + state.test.id + '/publish', 'POST');
  state.test.isPublished = true; refresh(); await merit(); notice('Merit published.', true);
 } catch (e) { notice(e.message); button.disabled = false; }
}
