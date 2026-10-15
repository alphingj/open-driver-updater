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

let scanTimer = null;
async function scan() {
  $('#btn-scan').textContent = 'Scanning…';
  $('#scan-msg').textContent = '';
  const live = $('#live').checked ? '?live=1' : '';
  const t0 = Date.now();
  clearInterval(scanTimer);
  scanTimer = setInterval(() => { $('#scan-msg').textContent = 'Working… ' + Math.round((Date.now() - t0) / 1000) + 's'; }, 1000);
  try {
    const j = await api('/api/scan' + live);
    state = j;
    $('#arch').textContent = 'arch: ' + (j.arch || '?') + (j.cached ? ' (cached)' : '');
    render();
    checkSystem();
    checkAppUpdate();
  } catch (e) {
    $('#scan-msg').textContent = 'Scan failed: ' + e.message;
  } finally { clearInterval(scanTimer); $('#btn-scan').textContent = 'Scan now'; }
}
async function checkSystem() {
  try {
    const j = await api('/api/system');
    $('#reboot-banner').innerHTML = (j.reboot && j.reboot.pending)
      ? `<div class="banner">Restart required — Windows reports a pending reboot (${esc((j.reboot.reasons || []).join(', '))}). Reboot before installing drivers.</div>` : '';
  } catch (e) { }
}
async function checkAppUpdate() {
  try {
    const j = await api('/api/app-update');
    if (j.update) $('#scan-msg').innerHTML = `Update available: v${esc(j.current)} → v${esc(j.latest)} — <a class="link" href="${esc(j.url)}" target="_blank" rel="noreferrer">download</a>`;
  } catch (e) { }
}
function tagFor(status) {
  if (status === 'ERROR') return 'bad';
  if (status === 'DISCONNECTED') return 'muted-tag';
  return 'good';
}
function updateCard(u, i, review) {
  const gpu = u.is_gpu ? '<span class="tag bad">GPU · confirm twice</span>' : '';
  const sig = '<span class="tag good">Catalog · hash-checked on download</span>';
  return `
    <div class="row-item"><input type="checkbox" data-sel="${i}" ${review ? '' : 'checked'} title="include in Update selected">
    <div><div class="t">${esc(u.available_version && u.update_id ? u.available_version : u.device_name)}</div>
    <div class="t-sub">${u.update_id ? esc(u.device_name) : ''}</div>
    <div class="s">${esc(u.hwid)} · installed: ${esc(u.installed_version || 'none')} · ${esc(u.arch)}</div></div>
    <div class="row"><span class="tag ${u.update_id ? 'good' : 'bad'}">${u.update_id ? (review ? 'review' : 'ready') : 'manual'}</span>${gpu}${sig}
    ${u.update_id ? `<button class="btn primary" data-update="${i}">Update</button>` : ''}
    <a class="link" href="${esc(u.download_url)}" target="_blank" rel="noreferrer">Get driver ↗</a>
    ${u.vendor_url ? `<a class="link" href="${esc(u.vendor_url)}" target="_blank" rel="noreferrer">${esc(u.vendor)} ↗</a>` : ''}
    <button class="btn" data-exclude="${i}">Exclude</button>
    <button class="btn" data-skipver="${i}" title="skip only this version">Skip version</button></div></div>`;
}
async function updateOne(u) {
  const rev = (u.confidence || 'high') !== 'high';
  let msg = `Download and install now?\n${u.device_name}\n→ ${u.available_version}` + (u.is_gpu ? '\n\nWARNING: display/GPU driver — can black-screen mid-install.' : '') + (rev ? '\n\nNote: needs-review match, not auto-install grade.' : '') + '\nA restore point + backup run first. Admin rights required.';
  if (!confirm(msg)) return;
  if (u.is_gpu && !confirm('Confirm the GPU update a second time to proceed.')) return;
  try {
    await api('/api/update-one', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ update: u }) });
  } catch (e) { $('#job-msg').textContent = 'Could not start: ' + e.message; return; }
  $('#job-bar').classList.remove('hidden');
  clearInterval(jobTimer);
  jobTimer = setInterval(pollJob, 2000);
}
function render() {
  $('#stat-devices').textContent = state.devices.length;
  const high = state.updates.filter(u => (u.confidence || 'high') === 'high');
  const rev = state.updates.filter(u => (u.confidence || 'high') !== 'high');
  $('#stat-updates').textContent = state.updates.length;
  $('#pill-count').textContent = state.updates.length;
  $('#n-high').textContent = high.length;
  $('#n-review').textContent = rev.length;
  $('#updates').innerHTML = high.length ? high.map((u) => updateCard(u, state.updates.indexOf(u), false)).join('')
    : '<div class="card">All clear — no problem devices found.</div>';
  $('#reviews').innerHTML = rev.length ? rev.map((u) => updateCard(u, state.updates.indexOf(u), true)).join('')
    : '<div class="card">Nothing needs a human look.</div>';
  document.querySelectorAll('[data-exclude]').forEach(b => b.onclick = () => exclude(state.updates[+b.dataset.exclude].hwid));
  document.querySelectorAll('[data-skipver]').forEach(b => b.onclick = () => {
    const u = state.updates[+b.dataset.skipver];
    const key = u.hwid + '||' + (u.available_version || '');
    exclude(key);
    state.updates = state.updates.filter(x => (x.hwid + '||' + (x.available_version || '')) !== key && x.hwid !== u.hwid);
    render();
  });
  document.querySelectorAll('[data-update]').forEach(b => b.onclick = () => updateOne(state.updates[+b.dataset.update]));
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
    const fmt = h => {
      const p = String(h).split('||');
      return p.length > 1 ? esc(p[0]) + ' <span class="tag">version: ' + esc(p[1]) + '</span>' : esc(h);
    };
    $('#excluded').innerHTML = j.exclusions.length ? j.exclusions.map((h, i) => `<div class="row-item"><div class="s">${fmt(h)}</div><button class="btn" data-unexclude="${i}">Remove</button></div>`).join('') : '<div class="card">Nothing excluded.</div>';
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
function selectedUpdates(onlyHigh) {
  const boxes = document.querySelectorAll('[data-sel]');
  const out = [];
  boxes.forEach(b => {
    if (!b.checked) return;
    const u = state.updates[+b.dataset.sel];
    if (!u || !u.update_id) return;
    if (onlyHigh && (u.confidence || 'high') !== 'high') return;
    out.push(u);
  });
  return out;
}
async function startJob(updates) {
  if (!updates.length) { $('#job-msg').textContent = 'Nothing selected (review items are manual-only).'; return; }
  const gpus = updates.filter(u => u.is_gpu);
  let msg = `Install ${updates.length} driver updates? A restore point + backup run first. Admin rights required.`;
  if (gpus.length) msg += `\n\nWARNING: ${gpus.length} display/GPU driver(s):\n` + gpus.map(g => ' - ' + g.device_name).join('\n') + '\nGPU updates can black-screen mid-install. Continue only if you can reboot.';
  if (!confirm(msg)) return;
  if (gpus.length && !confirm('Confirm GPU updates a second time to proceed.')) return;
  try {
    await api('/api/update-all',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({updates})});
  } catch (e) { $('#job-msg').textContent = 'Could not start: ' + e.message; return; }
  $('#job-bar').classList.remove('hidden');
  clearInterval(jobTimer);
  jobTimer = setInterval(pollJob, 2000);
}
$('#btn-update-all').onclick = async () => {
  const live = state.updates.filter(u => u.update_id && (u.confidence || 'high') === 'high');
  if (!live.length) { $('#job-msg').textContent = 'Run a Live Catalog scan first.'; return; }
  startJob(live);
};
$('#btn-update-sel').onclick = async () => startJob(selectedUpdates(true));
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
    $('#job-log').innerHTML = (s.results || []).map(r => `<div class="row-item"><div><div class="t">${esc(r.device)}</div><div class="s">${esc(r.detail || '')}</div></div><div class="row"><span class="tag ${r.ok ? 'good' : 'bad'}">${r.ok ? 'ok' : 'failed'}</span>${r.published ? `<button class="btn" data-rb="${esc(r.published)}" data-dev="${esc(r.device)}">Rollback</button>` : ''}</div></div>`).join('')
      + ((!s.running && s.total) ? `<div class="card"><b>${failed ? 'Finished with errors — review the log above.' : 'All updates installed.'} A restart may be required for drivers to take effect.</b></div>` : '');
    document.querySelectorAll('[data-rb]').forEach(b => b.onclick = async () => {
      const oem = b.dataset.rb;
      if (!confirm('Roll back ' + oem + ' (' + b.dataset.dev + ')? Windows reverts to the previous driver. If the device had no driver before, it will be left without one.')) return;
      try {
        const j = await api('/api/rollback', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ published: oem }) });
        b.outerHTML = '<span class="tag ' + (j.ok ? 'good' : 'bad') + '">' + (j.ok ? 'rolled back' : 'failed') + '</span>';
      } catch (e) { b.outerHTML = '<span class="tag bad">failed</span>'; }
    });
    if (!s.running && s.total) { clearInterval(jobTimer); scanFast(); }
}
async function scanFast() {
  // After a job: quick non-live rescan (no Catalog hammering). Installed
  // drivers now read current, so the update list settles by itself.
  try {
    const j = await api('/api/scan');
    state = j;
    render();
  } catch (e) { /* keep old state */ }
}
$('#btn-report').onclick = async () => {
  try {
    const live = $('#live').checked;
    const j = await api('/api/report', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ live }) });
    const blob = new Blob([j.report], { type: 'text/html' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'driver-report.html';
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 5000);
  } catch (e) { $('#job-msg').textContent = 'Report failed: ' + e.message; }
};
$('#btn-folder').onclick = async () => {
  const folder = $('#folder-path').value.trim();
  if (!folder) { $('#folder-msg').textContent = 'Enter a folder path first.'; return; }
  $('#folder-msg').textContent = 'Installing…';
  try {
    const j = await api('/api/folder-install', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ folder }) });
    $('#folder-msg').textContent = j.ok ? 'Installed.' : 'Failed: ' + (j.log || '');
  } catch (e) { $('#folder-msg').textContent = 'Failed: ' + e.message; }
};
$('#btn-comp').onclick = async () => {
  $('#components').innerHTML = '<div class="card">Checking…</div>';
  try {
    const j = await api('/api/components');
    $('#components').innerHTML = j.components.map(c => `<div class="row-item"><div><div class="t">${esc(c.name)}</div><div class="s">${esc(c.detail || '')}</div></div><div class="row"><span class="tag ${c.status === 'ok' ? 'good' : 'bad'}">${esc(c.status)}</span><a class="link" href="${esc(c.url)}" target="_blank" rel="noreferrer">Microsoft download ↗</a></div></div>`).join('');
  } catch (e) { $('#components').innerHTML = '<div class="card">Check failed: ' + esc(e.message) + '</div>'; }
};
$('#btn-drivers').onclick = async () => {
  $('#driverpkgs').innerHTML = '<div class="card">Enumerating driver store…</div>';
  try {
    const j = await api('/api/drivers');
    $('#drivers-msg').textContent = j.drivers.length + ' published packages.';
    $('#driverpkgs').innerHTML = j.drivers.map((d, i) => `<div class="row-item"><div><div class="t">${esc(d.original || d.oem)} <span class="muted">${esc(d.version || '')}</span></div><div class="s">${esc(d.oem)} · ${esc(d.provider || '')} · ${esc(d.class || '')}</div></div><button class="btn" data-exp="${i}">Export</button></div>`).join('') || '<div class="card">No third-party packages.</div>';
    state._pkgs = j.drivers;
    document.querySelectorAll('[data-exp]').forEach(b => b.onclick = async () => {
      const oem = state._pkgs[+b.dataset.exp].oem;
      b.textContent = 'Exporting…';
      try {
        const r = await api('/api/backup-one', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ oem }) });
        b.textContent = r.ok ? 'Done' : 'Failed';
        $('#drivers-msg').textContent = r.ok ? 'Exported: ' + r.path : 'Failed: ' + (r.log || '');
        loadBackups();
      } catch (e) { b.textContent = 'Failed'; }
    });
  } catch (e) { $('#driverpkgs').innerHTML = '<div class="card">Failed: ' + esc(e.message) + '</div>'; }
};
$('#btn-procs').onclick = async () => {
  $('#procs').innerHTML = '<div class="card">Listing…</div>';
  try {
    const j = await api('/api/procs');
    $('#procs').innerHTML = j.procs.map((p, i) => `<div class="row-item"><div><div class="t">${esc(p.name)}</div><div class="s">PID ${p.pid} · ${p.mb} MB</div></div><button class="btn" data-kill="${i}">End task</button></div>`).join('');
    state._procs = j.procs;
    document.querySelectorAll('[data-kill]').forEach(b => b.onclick = async () => {
      const p = state._procs[+b.dataset.kill];
      if (!confirm('End ' + p.name + ' (PID ' + p.pid + ')? Unsaved work in it will be lost.')) return;
      try {
        const r = await api('/api/kill', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ pid: p.pid }) });
        b.textContent = r.ok ? 'Ended' : 'Failed';
      } catch (e) { b.textContent = 'Failed'; }
    });
  } catch (e) { $('#procs').innerHTML = '<div class="card">Failed: ' + esc(e.message) + '</div>'; }
};
let _wu = [];
$('#btn-wu').onclick = async () => {
  $('#wu-list').innerHTML = '<div class="card">Asking Windows Update (can take minutes)…</div>';
  try {
    const j = await api('/api/wu');
    _wu = j.updates || [];
    $('#wu-list').innerHTML = _wu.length ? _wu.map((w, i) => `<div class="row-item"><input type="checkbox" data-wu="${i}" checked><div><div class="t">${esc(w.title)}</div><div class="s">KB ${esc(w.kb)} · ${Math.round((w.size || 0) / 1048576)} MB${w.reboot ? ' · needs reboot' : ''}</div></div></div>`).join('')
      : '<div class="card">No driver updates offered by Windows Update.</div>';
  } catch (e) { $('#wu-list').innerHTML = '<div class="card">Check failed: ' + esc(e.message) + '</div>'; }
};
$('#btn-wu-install').onclick = async () => {
  const titles = [];
  document.querySelectorAll('[data-wu]').forEach(b => { if (b.checked) titles.push(_wu[+b.dataset.wu].title); });
  if (!titles.length) { $('#wu-msg').textContent = 'Nothing selected.'; return; }
  if (!confirm(`Download and install ${titles.length} Windows Update driver(s)? Admin rights required.`)) return;
  $('#wu-msg').textContent = 'Installing (minutes)…';
  try {
    const j = await api('/api/wu-install', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ titles }) });
    $('#wu-msg').textContent = j.ok ? 'Installed.' : 'Failed: ' + (j.log || '');
  } catch (e) { $('#wu-msg').textContent = 'Failed: ' + e.message; }
};
scan();
