import { state, api, el, val, notice, option } from './assessments-api.js';
import { roster, saveMarks } from './assessments-roster.js';
import { merit, publish } from './assessments-merit.js';
function selectTest() {
 state.test = state.tests.find(x => x.id === Number(val('assessmentSelect'))) || null;
 state.page = 1;
 el('assessmentSave').disabled = !state.test || state.test.isPublished;
 el('assessmentPublish').disabled = !state.test || state.test.isPublished;
 el('assessmentRoster').replaceChildren();
 el('assessmentMeritList').replaceChildren();
 roster().catch(e => notice(e.message));
}
function showTests(selected) {
 el('assessmentSelect').replaceChildren(option('Select assessment', ''), ...state.tests.map(x =>
  option(x.name + ' (' + String(x.testDate).slice(0, 10) + ')' + (x.isPublished ? ' [Published]' : ''), x.id)));
 el('assessmentSelect').value = String(selected || state.test?.id || state.tests[0]?.id || '');
 selectTest();
}
async function createTest(event) {
 event.preventDefault();
 const form = state.forms.find(x => x.reference === val('assessmentForm'));
 const total = Number(val('assessmentTotal')), pass = Number(val('assessmentPass'));
 if (!form || !(total > 0 && pass >= 0 && pass <= total)) return notice('Check intake and marks.');
 const button = el('assessmentCreateButton'); button.disabled = true;
 try {
  const test = await api('/api/admission-assessments/tests', 'POST', {
   admissionIntakeFormReference: form.reference, name: val('assessmentName'),
   academicYearId: form.academicYearId, campusId: form.campusId, academicLevelId: form.academicLevelId,
   testDate: val('assessmentDate') + 'T12:00:00Z', totalMarks: total, passMarks: pass,
   durationMinutes: Number(val('assessmentDuration')), venue: val('assessmentVenue') || null
  });
  state.tests.unshift(test); showTests(test.id); notice('Assessment created.', true);
 } catch (e) { notice(e.message); } finally { button.disabled = false; }
}
document.addEventListener('DOMContentLoaded', async () => {
 if (!el('assessmentCreateForm')) return;
 el('assessmentCreateForm').addEventListener('submit', createTest);
 el('assessmentSelect').addEventListener('change', selectTest);
 el('assessmentSearchButton').addEventListener('click', () => { state.page = 1; roster().catch(e => notice(e.message)); });
 el('assessmentSearch').addEventListener('keydown', e => {
  if (e.key === 'Enter') { e.preventDefault(); el('assessmentSearchButton').click(); }
 });
 el('assessmentSave').addEventListener('click', saveMarks);
 el('assessmentMerit').addEventListener('click', () => merit().catch(e => notice(e.message)));
 el('assessmentPublish').addEventListener('click', () => publish(() => showTests(state.test.id)));
 el('assessmentPrev').addEventListener('click', () => { if (state.page > 1) { state.page--; roster().catch(e => notice(e.message)); } });
 el('assessmentNext').addEventListener('click', () => { if (state.page < state.pages) { state.page++; roster().catch(e => notice(e.message)); } });
 try {
  [state.forms, state.tests] = await Promise.all([api('/api/admission-intake/forms'), api('/api/admission-assessments/tests')]);
  state.forms = state.forms.filter(x => Number(x.state) !== 4);
  el('assessmentForm').replaceChildren(option('Select intake', ''), ...state.forms.map(x => option(x.title, x.reference)));
  showTests();
 } catch (e) { notice(e.message); }
}, { once: true });
