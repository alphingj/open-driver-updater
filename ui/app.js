const $ = s => document.querySelector(s);
let state = { devices: [], updates: [] };

document.querySelectorAll('#nav button').forEach(b => b.onclick = () => {
  document.querySelectorAll('#nav button').forEach(x => x.classList.remove('active'));
  b.classList.add('active');
  const v = b.dataset.view;
  document.querySelectorAll('.view').forEach(x => x.classList.add('hidden'));
  $('#view-' + v).classList.remove('hidden');
  $('#title').textContent = b.textContent.trim().replace(/\d+$/, '').trim();
});

async function api(path, opts) {
  let r;
  try {
    r = await fetch(path, opts);
  } catch (e) {
    throw new Error('connection failed: ' + e.message);
  }
  let j;
  try {
    j = await r.json();
  } catch (e) {
    throw new Error('bad response (HTTP ' + r.status + ')');
  }
  if (!r.ok) throw new Error((j && j.error) || ('HTTP ' + r.status));
  return j;
}
// Escape all five HTML-significant chars, including quotes (values land in attributes).
function esc(s){ return String(s ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])); }

async function scan() {
  $('#btn-scan').textContent = 'Scanning…';
  $('#scan-msg').textContent = '';
  const live = $('#live').checked ? '?live=1' : '';
  try {
    const j = await api('/api/scan' + live);
    state = j;
    $('#arch').textContent = 'arch: ' + (j.arch || '?');
    render();
  } catch (e) {
    $('#scan-msg').textContent = 'Scan failed: ' + e.message;
  } finally { $('#btn-scan').textContent = 'Scan now'; }
}
function tagFor(status) {
  if (status === 'ERROR') return 'bad';
  if (status === 'DISCONNECTED') return 'muted-tag';
  return 'good';
}
function render() {
  $('#stat-devices').textContent = state.devices.length;
  $('#stat-updates').textContent = state.updates.length;
  $('#pill-count').textContent = state.updates.length;
  $('#updates').innerHTML = state.updates.length ? state.updates.map((u, i) => `
    <div class="row-item"><div><div class="t">${esc(u.available_version && u.update_id ? u.available_version : u.device_name)}</div>
    <div class="t-sub">${u.update_id ? esc(u.device_name) : ''}</div>
    <div class="s">${esc(u.hwid)} · installed: ${esc(u.installed_version || 'none')} · ${esc(u.arch)}</div></div>
    <div class="row"><span class="tag ${u.update_id ? 'good' : 'bad'}">${u.update_id ? 'ready' : 'manual'}</span>
    <a class="link" href="${esc(u.download_url)}" target="_blank" rel="noreferrer">Get driver ↗</a>
    ${u.vendor_url ? `<a class="link" href="${esc(u.vendor_url)}" target="_blank" rel="noreferrer">${esc(u.vendor)} ↗</a>` : ''}
    <button class="btn" data-exclude="${i}">Exclude</button></div></div>`).join('')
    : '<div class="card">All clear — no problem devices found.</div>';
  document.querySelectorAll('[data-exclude]').forEach(b => b.onclick = () => exclude(state.updates[+b.dataset.exclude].hwid));
  $('#devices').innerHTML = state.devices.map(d => `
    <div class="row-item"><div><div class="t">${esc(d.friendly_name || d.instance_id)}</div>
    <div class="s">${esc((d.hwid || []).join(' · ') || d.instance_id)} · ${esc(d.driver_version || 'no driver')} · ${esc(d.status)}</div></div>
    <span class="tag ${tagFor(d.status)}">${esc(d.status)}</span></div>`).join('')
    || '<div class="card">Press Scan now.</div>';
  loadBackups(); loadExcluded();
}
async function loadBackups() {
  try {
    const j = await api('/api/backups');
    $('#stat-backups').textContent = j.backups.length;
    $('#backups').innerHTML = j.backups.map(b => `<div class="row-item"><div><div class="t">${esc(b.name)}</div><div class="s">${esc(b.path)}</div></div><span class="tag">#${b.id}</span></div>`).join('');
  } catch (e) { $('#stat-backups').textContent = '?'; }
}
async function loadExcluded() {
  try {
    const j = await api('/api/exclusions');
    $('#excluded').innerHTML = j.exclusions.length ? j.exclusions.map((h, i) => `<div class="row-item"><div class="s">${esc(h)}</div><button class="btn" data-unexclude="${i}">Remove</button></div>`).join('') : '<div class="card">Nothing excluded.</div>';
    document.querySelectorAll('[data-unexclude]').forEach(b => b.onclick = () => unexclude(state._excluded[+b.dataset.unexclude]));
    state._excluded = j.exclusions;
  } catch (e) { /* leave previous list */ }
}
async function exclude(hwid){
  try {
    await api('/api/exclusions/add',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({hwid})});
    state.updates = state.updates.filter(u => u.hwid !== hwid);
    render();
  } catch (e) { $('#job-msg').textContent = 'Exclude failed: ' + e.message; }
}
async function unexclude(hwid){
  try {
    await api('/api/exclusions/remove',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({hwid})});
    scan();
  } catch (e) { /* silent */ }
}
$('#btn-scan').onclick = scan;
$('#btn-backup').onclick = async () => {
  $('#backup-msg').textContent = 'Exporting…';
  try {
    const j = await api('/api/backup',{method:'POST',headers:{'Content-Type':'application/json'},body:'{}'});
    $('#backup-msg').textContent = j.ok ? 'Done: ' + j.path : 'Failed. ' + (j.log || '');
    loadBackups();
  } catch (e) { $('#backup-msg').textContent = 'Backup failed: ' + e.message + ' (run as admin for a complete backup)'; }
};
$('#btn-sched-on').onclick = async () => {
  try {
    const j = await api('/api/schedule/enable',{method:'POST'});
    $('#sched-msg').textContent = j.ok ? 'Weekly scan enabled.' : 'Failed: ' + j.log;
  } catch (e) { $('#sched-msg').textContent = 'Failed: ' + e.message; }
};
$('#btn-sched-off').onclick = async () => {
  try {
    const j = await api('/api/schedule/disable',{method:'POST'});
    $('#sched-msg').textContent = j.ok ? 'Disabled.' : 'Failed: ' + j.log;
  } catch (e) { $('#sched-msg').textContent = 'Failed: ' + e.message; }
};
let jobTimer = null;
$('#btn-update-all').onclick = async () => {
  const live = state.updates.filter(u => u.update_id);
  if (!live.length) { $('#job-msg').textContent = 'Run a Live Catalog scan first.'; return; }
  if (!confirm(`Install ${live.length} driver updates? A backup runs first and stops everything if it fails. Admin rights required.`)) return;
  let j;
  try {
    j = await api('/api/update-all',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({updates: live})});
  } catch (e) { $('#job-msg').textContent = 'Could not start: ' + e.message; return; }
  $('#job-bar').classList.remove('hidden');
  clearInterval(jobTimer);
  jobTimer = setInterval(pollJob, 2000);
};
$('#btn-stop').onclick = async () => {
  $('#job-msg').textContent = 'Cancelling after the current driver…';
  try { await api('/api/cancel', { method: 'POST' }); } catch (e) { /* job will notice */ }
};
async function pollJob() {
    let s;
    try { s = await api('/api/job'); } catch (e) { return; }
    const pct = s.total ? Math.round(100 * s.done / s.total) : 0;
    $('#job-fill').style.width = pct + '%';
    $('#job-msg').textContent = s.running ? `Working ${s.done}/${s.total} — ${s.current}` : (s.total ? `Done ${s.done}/${s.total}.` : '');
    const failed = (s.results || []).some(r => !r.ok);
    $('#job-log').innerHTML = (s.results || []).map(r => `<div class="row-item"><div><div class="t">${esc(r.device)}</div><div class="s">${esc(r.detail || '')}</div></div><span class="tag ${r.ok ? 'good' : 'bad'}">${r.ok ? 'ok' : 'failed'}</span></div>`).join('')
      + ((!s.running && s.total) ? `<div class="card"><b>${failed ? 'Finished with errors — review the log above.' : 'All updates installed.'} A restart may be required for drivers to take effect.</b></div>` : '');
    if (!s.running && s.total) { clearInterval(jobTimer); scan(); }
}
scan();
