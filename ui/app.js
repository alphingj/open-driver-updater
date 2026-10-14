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
  const r = await fetch(path, opts);
  return r.json();
}
function esc(s){ return String(s ?? '').replace(/[&<>"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c])); }

async function scan() {
  $('#btn-scan').textContent = 'Scanning…';
  const live = $('#live').checked ? '?live=1' : '';
  try {
    const j = await api('/api/scan' + live);
    state = j;
    $('#arch').textContent = 'arch: ' + (j.arch || '?');
    render();
  } finally { $('#btn-scan').textContent = 'Scan now'; }
}
function render() {
  $('#stat-devices').textContent = state.devices.length;
  $('#stat-updates').textContent = state.updates.length;
  $('#pill-count').textContent = state.updates.length;
  $('#updates').innerHTML = state.updates.length ? state.updates.map(u => `
    <div class="row-item"><div><div class="t">${esc(u.device_name)}</div>
    <div class="s">${esc(u.hwid)} · installed: ${esc(u.installed_version || 'none')} · ${esc(u.arch)}</div></div>
    <div class="row"><span class="tag bad">attention</span>
    <a class="link" href="${esc(u.download_url)}" target="_blank" rel="noreferrer">Get driver ↗</a>
    <button class="btn" onclick="exclude('${esc(u.hwid)}')">Exclude</button></div></div>`).join('')
    : '<div class="card">All clear — no problem devices found.</div>';
  $('#devices').innerHTML = state.devices.map(d => `
    <div class="row-item"><div><div class="t">${esc(d.friendly_name || d.instance_id)}</div>
    <div class="s">${esc((d.hwid || []).join(' · ') || d.instance_id)} · ${esc(d.driver_version || 'no driver')} · ${esc(d.status)}</div></div>
    <span class="tag ${d.status === 'ERROR' ? 'bad' : 'good'}">${esc(d.status)}</span></div>`).join('')
    || '<div class="card">Press Scan now.</div>';
  loadBackups(); loadExcluded();
}
async function loadBackups() {
  const j = await api('/api/backups');
  $('#stat-backups').textContent = j.backups.length;
  $('#backups').innerHTML = j.backups.map(b => `<div class="row-item"><div><div class="t">${esc(b.name)}</div><div class="s">${esc(b.path)}</div></div><span class="tag">#${b.id}</span></div>`).join('');
}
async function loadExcluded() {
  const j = await api('/api/exclusions');
  $('#excluded').innerHTML = j.exclusions.length ? j.exclusions.map(h => `<div class="row-item"><div class="s">${esc(h)}</div><button class="btn" onclick="unexclude('${esc(h)}')">Remove</button></div>`).join('') : '<div class="card">Nothing excluded.</div>';
}
async function exclude(hwid){ await api('/api/exclusions/add',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({hwid})}); scan(); }
async function unexclude(hwid){ await api('/api/exclusions/remove',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({hwid})}); scan(); }
$('#btn-scan').onclick = scan;
$('#btn-backup').onclick = async () => { $('#backup-msg').textContent = 'Exporting…'; const j = await api('/api/backup',{method:'POST',headers:{'Content-Type':'application/json'},body:'{}'}); $('#backup-msg').textContent = j.ok ? 'Done: ' + j.path : 'Failed — run as admin. ' + (j.log || ''); loadBackups(); };
$('#btn-sched-on').onclick = async () => { const j = await api('/api/schedule/enable',{method:'POST'}); $('#sched-msg').textContent = j.ok ? 'Weekly scan enabled.' : 'Failed: ' + j.log; };
$('#btn-sched-off').onclick = async () => { const j = await api('/api/schedule/disable',{method:'POST'}); $('#sched-msg').textContent = j.ok ? 'Disabled.' : 'Failed: ' + j.log; };
scan();
