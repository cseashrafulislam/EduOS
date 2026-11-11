import { state, api, el, val, notice, option } from './assessments-api.js';
import { roster, saveMarks } from './assessments-roster.js';
import { merit, publish } from './assessments-merit.js';
let creating = false;
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
 if (creating) return;
 const formElement = el('assessmentCreateForm');
 if (!formElement.reportValidity()) return;
 const form = state.forms.find(x => x.reference === val('assessmentForm'));
 const total = Number(val('assessmentTotal')), pass = Number(val('assessmentPass'));
 const duration = Number(val('assessmentDuration'));
 const date = val('assessmentDate');
 if (!form || !val('assessmentName') || !/^\d{4}-\d{2}-\d{2}$/.test(date) ||
  !Number.isFinite(total) || !Number.isFinite(pass) || total <= 0 || total > 1000000 ||
  pass < 0 || pass > total || !Number.isInteger(duration) || duration < 1 || duration > 1440 ||
  !/^\d+(?:\.\d{1,2})?$/.test(val('assessmentTotal')) ||
  !/^\d+(?:\.\d{1,2})?$/.test(val('assessmentPass')))
  return notice('Check intake, date, marks and duration.');
 const button = el('assessmentCreateButton'); creating = true; button.disabled = true;
 try {
  const test = await api('/api/admission-assessments/tests', 'POST', {
   admissionIntakeFormReference: form.reference, name: val('assessmentName'),
   academicYearId: form.academicYearId, campusId: form.campusId, academicLevelId: form.academicLevelId,
   testDate: val('assessmentDate') + 'T12:00:00Z', totalMarks: total, passMarks: pass,
   durationMinutes: duration, venue: val('assessmentVenue') || null
  });
  state.tests.unshift(test); showTests(test.id); formElement.reset(); notice('Assessment created.', true);
 } catch (e) { notice(e.message); } finally { creating = false; button.disabled = false; }
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
