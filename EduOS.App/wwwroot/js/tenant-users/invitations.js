(() => {
  'use strict';
  const app = document.getElementById('invitationApp');
  if (!app) return;
  const el = id => document.getElementById(id);
  const csrf = app.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
  let page = 1, total = 0, roles = [], sequence = 0;
  const cell = value => { const td = document.createElement('td'); td.textContent = String(value ?? ''); return td; };
  const alert = (message, failed = false) => {
    const box = el('invitationNotice');
    box.textContent = message;
    box.className = 'alert ' + (failed ? 'alert-danger' : 'alert-success');
  };
  async function request(url, options = {}) {
    const response = await fetch('/api/tenant-users' + url, {
      credentials: 'same-origin', cache: 'no-store', ...options,
      headers: { Accept: 'application/json',
        ...(options.body ? { 'Content-Type': 'application/json', RequestVerificationToken: csrf } : {}) }
    });
    const result = await response.json().catch(() => null);
    if (!response.ok || !result?.success) throw Error(result?.message || 'Request failed');
    return result.data;
  }
  function utc(value) {
    const raw = String(value || '');
    return new Date(/[zZ]$|[+-]\d{2}:\d{2}$/.test(raw) ? raw : raw + 'Z');
  }
  function state(item) {
    if (item.acceptedAt) return 'Accepted';
    if (item.revokedAt) return 'Revoked';
    return utc(item.expiresAt).getTime() <= Date.now() ? 'Expired' : 'Pending';
  }
  async function load() {
    const ticket = ++sequence;
    const data = await request('/invitations?page=' + page + '&pageSize=20');
    if (ticket !== sequence) return;
    total = data.totalCount || 0;
    const body = el('invitationRows');
    body.replaceChildren();
    for (const item of data.items || []) {
      const row = document.createElement('tr');
      row.append(cell(item.email), cell(roles.find(r => r.id === item.roleId)?.name || 'No initial role'),
        cell(utc(item.expiresAt).toLocaleString()), cell(state(item)));
      const action = document.createElement('td');
      if (state(item) === 'Pending') {
        const button = document.createElement('button');
        button.type = 'button'; button.className = 'btn btn-outline-danger btn-sm';
        button.textContent = 'Revoke';
        button.addEventListener('click', async () => {
          if (!confirm('Revoke invitation for ' + item.email + '?')) return;
          button.disabled = true;
          try {
            await request('/invitations/' + encodeURIComponent(item.reference) + '/revoke',
              { method: 'POST', body: JSON.stringify({ rowVersion: item.rowVersion }) });
            alert('Invitation revoked.'); await load();
          } catch (error) { alert(error.message, true); await load().catch(() => {}); }
          finally { button.disabled = false; }
        });
        action.append(button);
      }
      row.append(action); body.append(row);
    }
    if (!body.children.length) {
      const row = document.createElement('tr'), td = cell('No invitations found.');
      td.colSpan = 5; row.append(td); body.append(row);
    }
    el('invitationCount').textContent = 'Page ' + page + ' • ' + total + ' invitations';
    el('prevInvitations').disabled = page <= 1;
    el('nextInvitations').disabled = page * 20 >= total;
  }
  const refresh = () => load().catch(error => alert(error.message, true));
  el('refreshInvitations').addEventListener('click', refresh);
  el('prevInvitations').addEventListener('click', () => { if (page > 1) { page--; refresh(); } });
  el('nextInvitations').addEventListener('click', () => { if (page * 20 < total) { page++; refresh(); } });
  request('/roles').then(data => { roles = data; refresh(); }).catch(error => alert(error.message, true));
})();
