// -------------------------------------------------------------------------
// Map
// -------------------------------------------------------------------------
const map = L.map('map', { center: [20, 0], zoom: 2, worldCopyJump: true });
L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', {
  attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a>',
  maxZoom: 19,
}).addTo(map);
const clusterGroup = L.markerClusterGroup({ chunkedLoading: true });
map.addLayer(clusterGroup);

// -------------------------------------------------------------------------
// Render scheduler — coalesces rapid SSE updates into one DOM pass per frame
// -------------------------------------------------------------------------
let renderRafId = null;

function scheduleRender() {
  if (renderRafId !== null) return;
  renderRafId = requestAnimationFrame(() => {
    renderRafId = null;
    if (selectedEntityId) renderDetailView();
    else renderPanel();
  });
}

// -------------------------------------------------------------------------
// Auto-fit: zoom to show all entities on initial load
// -------------------------------------------------------------------------
let hasAutoFitted = false;
let autoFitTimer  = null;

function scheduleAutoFit() {
  if (hasAutoFitted) return;
  clearTimeout(autoFitTimer);
  autoFitTimer = setTimeout(() => {
    if (hasAutoFitted) return;
    hasAutoFitted = true;
    const bounds = clusterGroup.getBounds();
    if (bounds.isValid()) map.fitBounds(bounds, { padding: [40, 40], maxZoom: 14 });
  }, 600);
}

// -------------------------------------------------------------------------
// Entity state  —  entityId -> { entity, marker, lastUpdated }
// -------------------------------------------------------------------------
const entityMap = new Map();
let selectedEntityId  = null;
let currentDetailTab  = 'entity';   // 'entity' | 'tasks'

// Stable reference to #panel-empty: getElementById can't find it once it's
// been detached from the DOM (e.g. after list.innerHTML = ''), so we cache
// it here at load time.
const panelEmptyEl = document.getElementById('panel-empty');

// -------------------------------------------------------------------------
// Task type config  —  typeUrl -> { type, displayName, description, executionTag }
// -------------------------------------------------------------------------
const taskTypeConfig = new Map();

async function loadTaskTypeConfig() {
  try {
    const res = await fetch('/task-types.json');
    if (!res.ok) return;
    const entries = await res.json();
    for (const entry of entries) taskTypeConfig.set(entry.type, entry);
  } catch { /* non-fatal: fall back to raw type names */ }
}

function getTaskDisplayName(url) {
  return taskTypeConfig.get(url)?.displayName || (url ? url.split('.').pop() : null);
}

function getTaskDescription(url) {
  return taskTypeConfig.get(url)?.description || null;
}

function getTaskExecutionTag(url) {
  return taskTypeConfig.get(url)?.executionTag || null;
}

// Task state  —  taskId -> Task  (highest definitionVersion kept)
// -------------------------------------------------------------------------
const taskCache = new Map();
let tasksInitiallyFetched = false;
let taskStreamActive      = false;

const collapsedGroups       = new Set();
const collapsedTaskSections = new Set();

// -------------------------------------------------------------------------
// Task wizard state
// -------------------------------------------------------------------------
let taskWizardActive    = false;
let taskWizardEntityId  = null;
let taskWizardSpecUrl   = null;
let taskFormValues      = {};   // fieldKey -> objective val, or '_input_'+fieldKey -> raw string
let mapPickField        = null; // fieldKey currently being picked from map
let mapPickHandler      = null; // leaflet handler ref for cleanup
const objectiveMarkers  = new Map(); // fieldKey -> L.Marker overlay shown during wizard

// -------------------------------------------------------------------------
// Helpers
// -------------------------------------------------------------------------
function getLatLng(entity) {
  const pos = entity?.location?.position;
  if (!pos) return null;
  const lat = pos.latitudeDegrees, lng = pos.longitudeDegrees;
  if (lat === undefined || lng === undefined || (lat === 0 && lng === 0)) return null;
  return [lat, lng];
}

function getDisplayName(entity) {
  return entity?.aliases?.name || entity?.entityId || 'Unknown';
}

function escapeHtml(str) {
  return String(str).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}

function relativeTime(date) {
  const s = Math.floor((Date.now() - date.getTime()) / 1000);
  if (s < 5)  return 'just now';
  if (s < 60) return `${s}s ago`;
  const m = Math.floor(s / 60);
  if (m < 60) return `${m}m ago`;
  return `${Math.floor(m / 60)}h ago`;
}

function formatCoords(latlng) {
  if (!latlng) return null;
  return `${Math.abs(latlng[0]).toFixed(4)}${latlng[0]>=0?'° N':'° S'}, ${Math.abs(latlng[1]).toFixed(4)}${latlng[1]>=0?'° E':'° W'}`;
}

function fmtEnum(value, prefix) {
  if (!value || value.endsWith('_INVALID') || value.endsWith('_UNKNOWN')) return null;
  const s = prefix && value.startsWith(prefix) ? value.slice(prefix.length) : value;
  return s.split('_').map(w => w.charAt(0) + w.slice(1).toLowerCase()).join(' ');
}

function formatTimestamp(ts) {
  if (!ts) return null;
  try { return new Date(ts).toLocaleString(); } catch { return ts; }
}

// -------------------------------------------------------------------------
// Marker icons
// -------------------------------------------------------------------------
function makeMarkerIcon(highlighted) {
  return L.divIcon({
    className: '',
    html: `<div class="entity-marker${highlighted ? ' highlighted' : ''}"></div>`,
    iconSize:   [26, 26],
    iconAnchor: [13, 13],
  });
}

function updateMarkerHighlight(entityId, highlighted) {
  const entry = entityMap.get(entityId);
  if (entry?.marker) entry.marker.setIcon(makeMarkerIcon(highlighted));
}

// -------------------------------------------------------------------------
// Entity selection
// -------------------------------------------------------------------------
function selectEntity(entityId) {
  if (selectedEntityId && selectedEntityId !== entityId) updateMarkerHighlight(selectedEntityId, false);
  selectedEntityId = entityId;
  updateMarkerHighlight(entityId, true);
  currentDetailTab = 'entity';
  renderDetailView();
  renderPanel();
  const entry = entityMap.get(entityId);
  if (entry?.marker) clusterGroup.zoomToShowLayer(entry.marker, () => {});
}

function clearSelection() {
  if (selectedEntityId) updateMarkerHighlight(selectedEntityId, false);
  selectedEntityId = null;
  currentDetailTab = 'entity';
  renderDetailView();
  renderPanel();
}

function handleBackBtn() {
  if (taskWizardActive) cancelTaskWizard();
  else clearSelection();
}

function switchDetailTab(tab) {
  currentDetailTab = tab;
  // Update tab active states
  for (const btn of document.querySelectorAll('.detail-tab')) {
    btn.classList.toggle('active', btn.dataset.tab === tab);
  }
  // Lazy-load tasks on first visit
  if (tab === 'tasks') ensureTasksLoaded();
  renderDetailTabContent();
}

// -------------------------------------------------------------------------
// Detail view — hero + tabs shell (structure always in DOM)
// -------------------------------------------------------------------------
function renderDetailView() {
  const detailEl = document.getElementById('detail-view');
  const wizardEl = document.getElementById('task-wizard');
  const listEl   = document.getElementById('entity-list');
  const backBtn  = document.getElementById('back-btn');
  const titleEl  = document.getElementById('panel-title');
  const countEl  = document.getElementById('panel-count');

  if (!selectedEntityId) {
    detailEl.classList.remove('visible');
    wizardEl.classList.remove('visible');
    listEl.style.display = '';
    backBtn.style.display = 'none';
    titleEl.textContent = 'Entities';
    countEl.style.display = '';
    return;
  }

  const entry = entityMap.get(selectedEntityId);
  if (!entry) { clearSelection(); return; }

  listEl.style.display = 'none';
  backBtn.style.display = 'flex';
  countEl.style.display = 'none';

  if (taskWizardActive) {
    detailEl.classList.remove('visible');
    wizardEl.classList.add('visible');
    titleEl.textContent = taskWizardSpecUrl
      ? (getTaskDisplayName(taskWizardSpecUrl) || 'New Task')
      : 'New Task';
    renderTaskWizardContent();
    return;
  }

  wizardEl.classList.remove('visible');
  detailEl.classList.add('visible');
  titleEl.textContent = '';

  document.getElementById('detail-hero-name').textContent = getDisplayName(entry.entity);
  document.getElementById('detail-hero-id').textContent   = entry.entity.entityId || '';

  for (const btn of document.querySelectorAll('.detail-tab')) {
    btn.classList.toggle('active', btn.dataset.tab === currentDetailTab);
  }

  renderDetailTabContent();
}

function renderDetailTabContent() {
  if (!selectedEntityId) return;
  const entry = entityMap.get(selectedEntityId);
  if (!entry) return;

  const content = document.getElementById('detail-tab-content');
  if (currentDetailTab === 'entity') {
    content.innerHTML = buildEntityTabHtml(entry.entity, entry.lastUpdated);
  } else if (currentDetailTab === 'dev') {
    content.innerHTML = buildDevTabHtml(selectedEntityId);
  } else {
    content.innerHTML = buildTasksTabHtml(selectedEntityId);
  }
}

// -------------------------------------------------------------------------
// Entity tab HTML
// -------------------------------------------------------------------------
function buildEntityTabHtml(entity, lastUpdated) {
  const parts = [];

  const latlng = getLatLng(entity);
  const pos    = entity?.location?.position;
  const locRows = [];
  if (latlng)                            locRows.push(detailRow('Coordinates', formatCoords(latlng), true, 'coords'));
  if (pos?.altitudeHaeMeters != null)    locRows.push(detailRow('Altitude (HAE)', `${Number(pos.altitudeHaeMeters).toFixed(1)} m`, true, 'alt-hae'));
  if (pos?.altitudeAglMeters != null)    locRows.push(detailRow('Altitude (AGL)', `${Number(pos.altitudeAglMeters).toFixed(1)} m`, true, 'alt-agl'));
  if (entity?.location?.speedMps != null) locRows.push(detailRow('Speed', `${Number(entity.location.speedMps).toFixed(1)} m/s`, true, 'speed'));
  if (locRows.length) parts.push(detailSection('Location', locRows));

  const milView  = entity?.milView;
  const ontology = entity?.ontology;
  const classRows = [];
  const disp = fmtEnum(milView?.disposition, 'DISPOSITION_');
  if (disp) classRows.push(detailRowDisposition(disp));
  const env = fmtEnum(milView?.environment, 'ENVIRONMENT_');
  if (env) classRows.push(detailRow('Environment', env, false));
  const tmpl = fmtEnum(ontology?.template, 'TEMPLATE_');
  if (tmpl) classRows.push(detailRow('Template', tmpl, false));
  if (ontology?.platformType) classRows.push(detailRow('Platform type', ontology.platformType, false));
  if (ontology?.specificType) classRows.push(detailRow('Specific type', ontology.specificType, false));
  if (classRows.length) parts.push(detailSection('Classification', classRows));

  const status = entity?.status;
  const statusRows = [];
  if (status?.platformActivity) statusRows.push(detailRow('Activity', status.platformActivity, false));
  if (status?.role)             statusRows.push(detailRow('Role', status.role, false));
  if (statusRows.length) parts.push(detailSection('Status', statusRows));

  const prov = entity?.provenance;
  const provRows = [];
  if (prov?.integrationName)  provRows.push(detailRow('Integration', prov.integrationName, false));
  if (prov?.dataType)         provRows.push(detailRow('Data type', prov.dataType, false));
  if (prov?.sourceId)         provRows.push(detailRow('Source ID', prov.sourceId));
  if (prov?.sourceUpdateTime) provRows.push(detailRow('Source updated', formatTimestamp(prov.sourceUpdateTime), false));
  if (provRows.length) parts.push(detailSection('Provenance', provRows));

  const lcRows = [];
  if (entity?.createdTime)  lcRows.push(detailRow('Created', formatTimestamp(entity.createdTime), false));
  if (entity?.noExpiry)     lcRows.push(detailRow('Expiry', 'No expiry', false));
  else if (entity?.expiryTime) lcRows.push(detailRow('Expires', formatTimestamp(entity.expiryTime), false));
  lcRows.push(detailRow('Last seen', relativeTime(lastUpdated), false, 'last-seen'));
  parts.push(detailSection('Lifecycle', lcRows));

  const catalog = entity?.taskCatalog?.taskDefinitions || [];
  if (catalog.length > 0) {
    const eid = escapeHtml(entity.entityId);
    const items = catalog.map(def => {
      const url   = def.taskSpecificationUrl;
      const label = getTaskDisplayName(url);
      const desc  = getTaskDescription(url);
      return `<button class="task-dropdown-item" onclick="pickTaskTypeFromDropdown('${eid}','${escapeHtml(url)}')">
        <div>${escapeHtml(label)}</div>
        ${desc ? `<div class="task-dropdown-item-desc">${escapeHtml(desc)}</div>` : ''}
      </button>`;
    }).join('');
    parts.push(`<div class="entity-tab-actions">
      <div class="task-entity-dropdown" id="task-entity-dropdown">
        <div class="task-dropdown-header">Select Task Type</div>
        ${items}
      </div>
      <button class="task-entity-btn" id="task-entity-btn" onclick="toggleTaskEntityDropdown(event)">
        <span>⚡ Task Entity</span><span class="btn-caret">▲</span>
      </button>
    </div>`);
  }

  return parts.join('');
}

function detailSection(title, rows) {
  return `<div class="detail-section"><div class="detail-section-title">${escapeHtml(title)}</div>${rows.join('')}</div>`;
}
function detailRow(label, value, mono = true, fieldKey = null) {
  if (value == null || value === '') return '';
  const dataAttr = fieldKey ? ` data-field="${fieldKey}"` : '';
  return `<div class="detail-row"><span class="detail-label">${escapeHtml(label)}</span><span class="detail-value${mono?'':' plain'}"${dataAttr}>${escapeHtml(String(value))}</span></div>`;
}
function detailRowDisposition(disp) {
  const cls = { 'Friendly':'tag-friendly','Assumed Friendly':'tag-friendly','Hostile':'tag-hostile','Suspicious':'tag-suspicious','Neutral':'tag-neutral' }[disp] || 'tag-unknown';
  return `<div class="detail-row"><span class="detail-label">Disposition</span><span class="detail-value tag ${cls}">${escapeHtml(disp)}</span></div>`;
}

// -------------------------------------------------------------------------
// Tasks tab HTML
// -------------------------------------------------------------------------

// Status sets
const STATUS_CURRENT     = new Set(['STATUS_EXECUTING']);
const STATUS_PAST        = new Set(['STATUS_DONE_OK','STATUS_DONE_NOT_OK','STATUS_REPLACED','STATUS_VERSION_REJECTED']);

function classifyTask(task) {
  const s = task?.status?.status;
  if (STATUS_CURRENT.has(s)) return 'current';
  if (STATUS_PAST.has(s))    return 'past';
  return 'pending';
}

function getTaskAssigneeEntityId(task) {
  const assignee = task?.relations?.assignee;
  if (!assignee) return null;
  return assignee.system?.entityId || assignee.team?.entityId || null;
}

function fmtTaskStatus(status) {
  if (!status) return 'Unknown';
  const s = status.replace('STATUS_', '');
  return s.split('_').map(w => w.charAt(0) + w.slice(1).toLowerCase()).join(' ');
}

function taskBadgeClass(status) {
  if (!status) return 'badge-unknown';
  if (status === 'STATUS_EXECUTING')    return 'badge-executing';
  if (status === 'STATUS_DONE_OK')      return 'badge-done-ok';
  if (status === 'STATUS_DONE_NOT_OK')  return 'badge-done-not-ok';
  if (STATUS_PAST.has(status))          return 'badge-terminal';
  if (['STATUS_ACK','STATUS_WILCO','STATUS_MACHINE_RECEIPT'].includes(status)) return 'badge-ack';
  if (['STATUS_CANCEL_REQUESTED','STATUS_COMPLETE_REQUESTED'].includes(status)) return 'badge-transitioning';
  return 'badge-pending';
}

function fmtSpecType(task) {
  const url = task?.specification?.['@type'] || task?.specification?.typeUrl;
  if (!url) return null;
  const parts = url.split('.');
  return parts[parts.length - 1] || null;
}

function getExecutingTaskVerb(entityId) {
  for (const task of taskCache.values()) {
    if (task?.status?.status === 'STATUS_EXECUTING' && getTaskAssigneeEntityId(task) === entityId) {
      const url = task?.specification?.['@type'] || task?.specification?.typeUrl;
      return getTaskExecutionTag(url) || null;
    }
  }
  return null;
}

function buildTasksTabHtml(entityId) {
  if (!tasksInitiallyFetched) {
    return `<div class="tasks-loading">Loading tasks…</div>`;
  }

  // Filter tasks assigned to this entity
  const allForEntity = [];
  for (const task of taskCache.values()) {
    if (getTaskAssigneeEntityId(task) === entityId) {
      allForEntity.push(task);
    }
  }

  if (allForEntity.length === 0) {
    return `<div class="tasks-empty">No tasks assigned to this entity</div>`;
  }

  const buckets = { current: [], pending: [], past: [] };
  for (const t of allForEntity) buckets[classifyTask(t)].push(t);

  // Sort each bucket by lastUpdateTime descending
  const byUpdateDesc = (a, b) => {
    const ta = a.lastUpdateTime ? new Date(a.lastUpdateTime).getTime() : 0;
    const tb = b.lastUpdateTime ? new Date(b.lastUpdateTime).getTime() : 0;
    return tb - ta;
  };
  buckets.current.sort(byUpdateDesc);
  buckets.pending.sort(byUpdateDesc);
  buckets.past.sort(byUpdateDesc);

  const SECTIONS = [
    { key: 'current', label: 'Current Tasks' },
    { key: 'pending', label: 'Pending Tasks' },
    { key: 'past',    label: 'Past Tasks'    },
  ];

  return SECTIONS
    .filter(s => buckets[s.key].length > 0)
    .map(({ key, label }) => buildTaskSection(key, label, buckets[key]))
    .join('');
}

function buildTaskSection(key, label, tasks) {
  const collapsed = collapsedTaskSections.has(key);
  const cards = tasks.map(buildTaskCardHtml).join('');
  return `
    <div class="task-section-header${collapsed ? ' collapsed' : ''}"
         data-task-section="${key}"
         onclick="toggleTaskSection('${key}')">
      <span class="group-chevron">▼</span>
      <span>${escapeHtml(label)}</span>
      <span class="group-count">${tasks.length}</span>
    </div>
    <div class="task-section-cards${collapsed ? ' collapsed' : ''}" data-task-section-cards="${key}">
      ${cards}
    </div>`;
}

function buildTaskCardHtml(task) {
  const id      = task?.version?.taskId || '—';
  const status  = task?.status?.status;
  const defVer  = task?.version?.definitionVersion;
  const desc    = task?.description;
  const specUrl = task?.specification?.['@type'] || task?.specification?.typeUrl;
  const specType = getTaskDisplayName(specUrl) || fmtSpecType(task);
  const updated = task?.lastUpdateTime ? relativeTime(new Date(task.lastUpdateTime)) : null;
  const errMsg  = task?.status?.taskError?.message;
  const badgeCls = taskBadgeClass(status);
  const statusLabel = fmtTaskStatus(status);
  const canChangeStatus = status !== 'STATUS_DONE_OK' && status !== 'STATUS_DONE_NOT_OK';

  const metaParts = [];
  if (defVer)   metaParts.push(`v${defVer}`);
  if (updated)  metaParts.push(`Updated ${escapeHtml(updated)}`);

  return `<div class="task-card">
    <div class="task-card-header">
      <span class="task-card-id">${escapeHtml(id)}</span>
      <span class="task-badge ${badgeCls}">${escapeHtml(statusLabel)}</span>
      ${canChangeStatus ? `<button class="task-status-btn" title="Change status" onclick="openStatusPicker(event,'${escapeHtml(id)}')">⋯</button>` : ''}
    </div>
    ${specType ? `<div class="task-card-type">${escapeHtml(specType)}</div>` : ''}
    ${desc ? `<div class="task-card-desc">${escapeHtml(desc)}</div>` : ''}
    ${metaParts.length ? `<div class="task-card-meta">${metaParts.join(' · ')}</div>` : ''}
    ${errMsg ? `<div class="task-card-error">${escapeHtml(errMsg)}</div>` : ''}
  </div>`;
}

// -------------------------------------------------------------------------
// Dev tab
// -------------------------------------------------------------------------
function buildDevTabHtml(entityId) {
  const id = escapeHtml(entityId);
  return `<div class="dev-tab-content">
    <button class="dev-btn" onclick="openEntityJsonModal('${id}')">
      <span class="dev-btn-icon">{ }</span> Show Entity JSON
    </button>
    <button class="dev-btn dev-btn-warn" onclick="clearEntityTasks('${id}')">
      <span class="dev-btn-icon">🗑</span> Clear Tasks
    </button>
    <button class="dev-btn dev-btn-danger" onclick="deleteEntity('${id}')">
      <span class="dev-btn-icon">✕</span> Delete Entity
    </button>
  </div>`;
}

// ── Entity JSON modal ──
let jsonModalEntityId = null;

function syntaxHighlightJson(json) {
  return json
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/("(\\u[a-zA-Z0-9]{4}|\\[^u]|[^\\"])*"(\s*:)?|\b(true|false|null)\b|-?\d+(?:\.\d*)?(?:[eE][+\-]?\d+)?)/g,
      (match) => {
        if (/^"/.test(match)) {
          return /:$/.test(match)
            ? `<span class="json-key">${match}</span>`
            : `<span class="json-string">${match}</span>`;
        }
        if (/true|false/.test(match)) return `<span class="json-bool">${match}</span>`;
        if (/null/.test(match))       return `<span class="json-null">${match}</span>`;
        return `<span class="json-number">${match}</span>`;
      });
}

function refreshJsonModalContent() {
  if (!jsonModalEntityId) return;
  const entry = entityMap.get(jsonModalEntityId);
  if (!entry) return;
  const json = JSON.stringify(entry.entity, null, 2);
  document.getElementById('entity-json-pre').innerHTML = syntaxHighlightJson(json);
}

function openEntityJsonModal(entityId) {
  jsonModalEntityId = entityId;
  refreshJsonModalContent();
  document.getElementById('entity-json-modal').classList.add('visible');
}

function closeEntityJsonModal() {
  document.getElementById('entity-json-modal').classList.remove('visible');
  jsonModalEntityId = null;
}

function handleJsonModalBackdropClick(event) {
  if (event.target === document.getElementById('entity-json-modal')) {
    closeEntityJsonModal();
  }
}

function copyEntityJson() {
  const text = document.getElementById('entity-json-pre').textContent;
  navigator.clipboard.writeText(text).catch(() => {});
}

// ── Dev actions ──
async function clearEntityTasks(entityId) {
  try {
    const res = await fetch(`/api/v1/tasks/byAssignee/${encodeURIComponent(entityId)}`, {
      method: 'DELETE',
    });
    if (res.ok) {
      const data = await res.json();
      // Remove cleared tasks from local cache
      for (const [id, task] of taskCache.entries()) {
        if (getTaskAssigneeEntityId(task) === entityId) taskCache.delete(id);
      }
      if (selectedEntityId === entityId && currentDetailTab === 'tasks') {
        renderDetailTabContent();
      }
      console.info(`Cleared ${data.deletedCount} task(s) for entity ${entityId}`);
    } else {
      const err = await res.json().catch(() => ({}));
      console.error('Clear tasks failed:', err.message || res.status);
    }
  } catch (err) {
    console.error('Clear tasks request failed:', err);
  }
}

async function deleteEntity(entityId) {
  try {
    const res = await fetch(`/api/v1/entities/${encodeURIComponent(entityId)}`, {
      method: 'DELETE',
    });
    if (res.ok) {
      // SSE will broadcast the delete event which calls removeEntity + clearSelection
    } else {
      const err = await res.json().catch(() => ({}));
      console.error('Delete entity failed:', err.message || res.status);
    }
  } catch (err) {
    console.error('Delete entity request failed:', err);
  }
}

function toggleTaskSection(key) {
  collapsedTaskSections.has(key) ? collapsedTaskSections.delete(key) : collapsedTaskSections.add(key);
  const header = document.querySelector(`.task-section-header[data-task-section="${key}"]`);
  const cards  = document.querySelector(`.task-section-cards[data-task-section-cards="${key}"]`);
  if (header) header.classList.toggle('collapsed', collapsedTaskSections.has(key));
  if (cards)  cards.classList.toggle('collapsed',  collapsedTaskSections.has(key));
}

// -------------------------------------------------------------------------
// Task status picker
// -------------------------------------------------------------------------
const ALL_STATUSES = [
  { value: 'STATUS_SENT',               label: 'Sent' },
  { value: 'STATUS_ACK',                label: 'Ack' },
  { value: 'STATUS_WILCO',              label: 'Wilco' },
  { value: 'STATUS_MACHINE_RECEIPT',    label: 'Machine Receipt' },
  { value: 'STATUS_EXECUTING',          label: 'Executing' },
  { value: 'STATUS_CANCEL_REQUESTED',   label: 'Cancel Requested' },
  { value: 'STATUS_COMPLETE_REQUESTED', label: 'Complete Requested' },
  { value: 'STATUS_DONE_OK',            label: 'Done Ok' },
  { value: 'STATUS_DONE_NOT_OK',        label: 'Done Not Ok' },
  { value: 'STATUS_REPLACED',           label: 'Replaced' },
  { value: 'STATUS_VERSION_REJECTED',   label: 'Version Rejected' },
];

let statusPickerTaskId = null;

function openStatusPicker(event, taskId) {
  event.stopPropagation();
  const task = taskCache.get(taskId);
  if (!task) return;

  const currentStatus = task?.status?.status;
  statusPickerTaskId = taskId;

  const optionsEl = document.getElementById('status-picker-options');
  optionsEl.innerHTML = ALL_STATUSES
    .filter(s => s.value !== currentStatus)
    .map(s => {
      const cls = taskBadgeClass(s.value);
      return `<button class="status-picker-option ${cls}" onclick="applyTaskStatus('${escapeHtml(taskId)}','${s.value}')">${escapeHtml(s.label)}</button>`;
    })
    .join('');

  const picker = document.getElementById('status-picker');
  picker.style.display = 'block';

  // Position: prefer below the button, flip up if near bottom edge
  const rect = event.currentTarget.getBoundingClientRect();
  const pickerH = 300; // max-height
  const spaceBelow = window.innerHeight - rect.bottom;
  if (spaceBelow < pickerH && rect.top > pickerH) {
    picker.style.top  = `${rect.top - picker.offsetHeight - 4}px`;
  } else {
    picker.style.top  = `${rect.bottom + 4}px`;
  }
  picker.style.left = `${Math.min(rect.left, window.innerWidth - 180)}px`;
}

function closeStatusPicker() {
  document.getElementById('status-picker').style.display = 'none';
  statusPickerTaskId = null;
}

async function applyTaskStatus(taskId, newStatus) {
  closeStatusPicker();
  const task = taskCache.get(taskId);
  if (!task) return;

  const statusVersion = task?.version?.statusVersion || 0;

  try {
    const res = await fetch(`/api/v1/tasks/${encodeURIComponent(taskId)}/status`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        version: { statusVersion },
        newStatus: { status: newStatus },
      }),
    });
    if (!res.ok) {
      const err = await res.json().catch(() => ({}));
      console.error('Status update failed:', err.message || res.status);
    }
    // SSE will deliver the updated task automatically
  } catch (err) {
    console.error('Status update request failed:', err);
  }
}

// -------------------------------------------------------------------------
// Task wizard — schemas
// -------------------------------------------------------------------------
const ISR = [
  { key: 'parameters.speedMS',          label: 'Speed (m/s)',           type: 'float_opt',  group: 'ISR Parameters' },
  { key: 'parameters.standoffDistanceM', label: 'Standoff Distance (m)', type: 'float_opt',  group: 'ISR Parameters' },
  { key: 'parameters.standoffAngle',     label: 'Standoff Angle (rad)',  type: 'float_opt',  group: 'ISR Parameters' },
  { key: 'parameters.expirationTimeMs',  label: 'Expiration (ms)',       type: 'uint64_opt', group: 'ISR Parameters' },
];

const TASK_SCHEMAS = {
  'type.googleapis.com/anduril.tasks.v2.Investigate': {
    fields: [{ key: 'objective', label: 'Objective', type: 'objective', required: true }, ...ISR],
  },
  'type.googleapis.com/anduril.tasks.v2.VisualId': {
    fields: [{ key: 'objective', label: 'Objective', type: 'objective', required: true }, ...ISR],
  },
  'type.googleapis.com/anduril.tasks.v2.Map': {
    fields: [
      { key: 'objective',  label: 'Objective',  type: 'objective',   required: true },
      { key: 'minNiirs',   label: 'Min NIIRS',  type: 'uint32_opt' },
      ...ISR,
    ],
  },
  'type.googleapis.com/anduril.tasks.v2.Monitor': {
    fields: [{ key: 'objective', label: 'Objective', type: 'objective', required: true }],
  },
  'type.googleapis.com/anduril.tasks.v2.Shadow': {
    fields: [{ key: 'objective', label: 'Objective', type: 'objective', required: true }, ...ISR],
  },
  'type.googleapis.com/anduril.tasks.v2.Scan': {
    fields: [{ key: 'objective', label: 'Objective', type: 'objective', required: true }, ...ISR],
  },
  'type.googleapis.com/anduril.tasks.v2.BattleDamageAssessment': {
    fields: [{ key: 'objective', label: 'Objective', type: 'objective', required: true }, ...ISR],
  },
  'type.googleapis.com/anduril.tasks.v2.Loiter': {
    fields: [{ key: 'objective', label: 'Objective', type: 'objective', required: true }, ...ISR],
  },
  'type.googleapis.com/anduril.tasks.v2.ImproveTrackQuality': {
    fields: [
      { key: 'objective',                label: 'Objective',              type: 'objective',   required: true },
      { key: 'terminationTrackQuality',  label: 'Target Track Quality',   type: 'uint32_opt' },
    ],
  },
  'type.googleapis.com/anduril.tasks.v2.AreaSearch': {
    fields: [{ key: 'objective', label: 'Search Area', type: 'objective', required: true }],
  },
  'type.googleapis.com/anduril.tasks.v2.VolumeSearch': {
    fields: [{ key: 'objective', label: 'Search Volume', type: 'objective', required: true }],
  },
  'type.googleapis.com/anduril.tasks.v2.Marshal': {
    fields: [{ key: 'objective', label: 'Marshal Point', type: 'objective', required: true }],
  },
  'type.googleapis.com/anduril.tasks.v2.Transit': {
    fields: [{ key: '_destination', label: 'Destination', type: 'objective', required: true }],
  },
  'type.googleapis.com/anduril.tasks.v2.Strike': {
    fields: [
      { key: 'objective',                 label: 'Strike Target',          type: 'objective',   required: true },
      { key: 'parameters.runInBearing',   label: 'Run-In Bearing (°)',     type: 'float_opt',   group: 'Strike Parameters' },
      { key: 'parameters.glideSlopeAngle', label: 'Glide Slope Angle (°)', type: 'float_opt',   group: 'Strike Parameters' },
    ],
  },
  'type.googleapis.com/anduril.tasks.v2.Smack': {
    fields: [
      { key: 'objective',                label: 'Strike Target',       type: 'objective',  required: true },
      { key: 'parameters.runInBearing',  label: 'Run-In Bearing (°)', type: 'float_opt',  group: 'Strike Parameters' },
    ],
  },
  'type.googleapis.com/anduril.tasks.v2.ReleasePayload': {
    fields: [{ key: 'objective', label: 'Drop Point (optional)', type: 'objective' }],
  },
  'type.googleapis.com/anduril.tasks.v2.GimbalPoint': {
    fields: [{ key: 'lookAt', label: 'Look At', type: 'objective', required: true }],
  },
  'type.googleapis.com/anduril.tasks.v2.GimbalZoom': {
    fields: [
      { key: 'setHorizontalFov',   label: 'Horizontal FOV (°)',  type: 'float_opt' },
      { key: 'setMagnification',   label: 'Magnification',       type: 'float_opt' },
    ],
  },
};

// -------------------------------------------------------------------------
// Task wizard — state management
// -------------------------------------------------------------------------
function openTaskWizard(entityId) {
  stopMapPick();
  taskWizardActive   = true;
  taskWizardEntityId = entityId;
  taskWizardSpecUrl  = null;
  taskFormValues     = {};
  renderDetailView();
}

function cancelTaskWizard() {
  stopMapPick();
  clearObjectiveMarkers();
  taskWizardActive   = false;
  taskWizardEntityId = null;
  taskWizardSpecUrl  = null;
  taskFormValues     = {};
  currentDetailTab   = 'entity';
  renderDetailView();
}

function toggleTaskEntityDropdown(e) {
  e.stopPropagation();
  const dropdown = document.getElementById('task-entity-dropdown');
  const btn      = document.getElementById('task-entity-btn');
  if (!dropdown) return;
  const isOpen = dropdown.classList.toggle('open');
  btn.classList.toggle('open', isOpen);
}

function pickTaskTypeFromDropdown(entityId, specUrl) {
  stopMapPick();
  clearObjectiveMarkers();
  taskWizardActive   = true;
  taskWizardEntityId = entityId;
  taskWizardSpecUrl  = specUrl;
  taskFormValues     = {};
  const schema = TASK_SCHEMAS[specUrl];
  const firstObjField = schema?.fields.find(f => f.type === 'objective');
  if (firstObjField) {
    enterMapPickMode();
    setMapPickField(firstObjField.key);
  }
  renderDetailView();
}

function selectTaskType(specUrl) {
  saveFormInputs();
  stopMapPick();
  clearObjectiveMarkers();
  taskWizardSpecUrl = specUrl;
  taskFormValues    = {};
  const schema = TASK_SCHEMAS[specUrl];
  const firstObjField = schema?.fields.find(f => f.type === 'objective');
  if (firstObjField) {
    enterMapPickMode();
    setMapPickField(firstObjField.key);
  }
  renderDetailView();
}

// -------------------------------------------------------------------------
// Task wizard — rendering
// -------------------------------------------------------------------------
function renderTaskWizardContent() {
  const wizardEl = document.getElementById('task-wizard');

  if (!taskWizardSpecUrl) {
    const entry = entityMap.get(taskWizardEntityId);
    const catalog = entry?.entity?.taskCatalog?.taskDefinitions || [];
    wizardEl.innerHTML = buildTypePickerHtml(catalog) + wizardFooterHtml();
    return;
  }

  const schema = TASK_SCHEMAS[taskWizardSpecUrl];
  if (!schema) {
    wizardEl.innerHTML = `<div style="padding:14px;color:#484f58;font-size:13px">Unknown task type: ${escapeHtml(taskWizardSpecUrl)}</div>` + wizardFooterHtml(true);
    return;
  }

  wizardEl.innerHTML = buildTaskFormHtml(schema) + wizardFooterHtml(true);
}

function wizardFooterHtml(showSubmit = false) {
  return `<div class="wizard-footer">
    <button class="wizard-cancel-btn" onclick="cancelTaskWizard()">Cancel</button>
    ${showSubmit ? `<button class="wizard-submit-btn" id="wizard-submit-btn" onclick="submitTask()">Create Task</button>` : ''}
  </div>`;
}

function buildTypePickerHtml(catalog) {
  if (!catalog.length) {
    return `<div class="tasks-empty">No task types in this entity's catalog</div>`;
  }
  const items = catalog.map(def => {
    const url   = def.taskSpecificationUrl;
    const label = getTaskDisplayName(url);
    const desc  = getTaskDescription(url);
    return `<button class="task-type-btn" onclick="selectTaskType('${escapeHtml(url)}')">
      <div class="task-type-name">${escapeHtml(label)}</div>
      ${desc ? `<div class="task-type-desc">${escapeHtml(desc)}</div>` : ''}
    </button>`;
  }).join('');
  return `<div class="wizard-type-list">${items}</div>`;
}

function buildTaskFormHtml(schema) {
  let html = '<div class="wizard-form-scroll">';
  let currentGroup = null;
  let inGroup = false;

  for (const field of schema.fields) {
    if (field.group && field.group !== currentGroup) {
      if (inGroup) html += '</div>';
      currentGroup = field.group;
      inGroup = true;
      html += `<div class="form-group-header">${escapeHtml(field.group)}</div><div class="form-group-fields">`;
    } else if (!field.group && inGroup) {
      html += '</div>';
      inGroup = false;
      currentGroup = null;
    }
    html += renderFieldHtml(field);
  }

  if (inGroup) html += '</div>';
  html += '</div>';
  return html;
}

function renderFieldHtml(field) {
  const domId  = 'tf-' + field.key.replace(/\./g, '__');
  const reqStar = field.required ? ' <span class="required">*</span>' : '';

  if (field.type === 'objective') {
    const val         = taskFormValues[field.key];
    const isPickingMap = mapPickField === field.key;

    let valueText  = 'Not set';
    let valueCls   = 'obj-field-value';
    let selValue   = '';

    if (val?.type === 'entity' && val.entityId) {
      const e   = entityMap.get(val.entityId);
      valueText = getDisplayName(e?.entity || { entityId: val.entityId });
      valueCls += ' set';
      selValue  = val.entityId;
    } else if (val?.type === 'point') {
      valueText = `${val.lat.toFixed(5)}°, ${val.lon.toFixed(5)}°`;
      valueCls += ' set';
    }

    const opts = [...entityMap.values()]
      .sort((a, b) => getDisplayName(a.entity).localeCompare(getDisplayName(b.entity)))
      .map(e => {
        const eid  = e.entity.entityId;
        const name = getDisplayName(e.entity);
        const sel  = selValue === eid ? ' selected' : '';
        return `<option value="${escapeHtml(eid)}"${sel}>${escapeHtml(name)}</option>`;
      }).join('');

    const altRow = val?.type === 'point' ? `
      <div class="obj-alt-row">
        <span class="obj-alt-label">Alt (m AGL)</span>
        <input class="form-input obj-alt-input" type="number" step="any" value="${val.alt ?? 0}"
          oninput="setObjectiveAlt('${escapeHtml(field.key)}', this.value)" />
      </div>` : '';

    return `<div class="form-field">
      <div class="form-label">${isPickingMap ? '<span class="map-pick-indicator"></span>' : ''}${escapeHtml(field.label)}${reqStar}</div>
      <div class="${valueCls}">${escapeHtml(valueText)}</div>
      ${altRow}
      <div class="obj-btns">
        <select class="form-select" style="flex:1;min-width:0" onchange="onObjectivePick('${escapeHtml(field.key)}',this.value)">
          <option value="">— Pick entity —</option>
          <option value="__map__"${isPickingMap ? ' selected' : ''}>📍 Choose on Map…</option>
          ${opts}
        </select>
        <button class="obj-btn-map${isPickingMap ? ' active' : ''}" onclick="toggleMapPick('${escapeHtml(field.key)}')">📍 Map</button>
      </div>
    </div>`;
  }

  const savedVal = taskFormValues['_input_' + field.key] ?? '';

  if (field.type === 'float_opt' || field.type === 'uint32_opt' || field.type === 'uint64_opt') {
    const step = field.type === 'float_opt' ? 'any' : '1';
    return `<div class="form-field">
      <div class="form-label">${escapeHtml(field.label)}${reqStar}</div>
      <input id="${domId}" class="form-input" type="number" step="${step}" placeholder="Optional" value="${escapeHtml(String(savedVal))}" />
    </div>`;
  }

  if (field.type === 'string') {
    return `<div class="form-field">
      <div class="form-label">${escapeHtml(field.label)}${reqStar}</div>
      <input id="${domId}" class="form-input" type="text" placeholder="Optional" value="${escapeHtml(String(savedVal))}" />
    </div>`;
  }

  return '';
}

// -------------------------------------------------------------------------
// Task wizard — objective + map pick
// -------------------------------------------------------------------------
function setObjectiveAlt(fieldKey, altStr) {
  const val = taskFormValues[fieldKey];
  if (val?.type === 'point') val.alt = parseFloat(altStr) || 0;
}

const _objIconShapes = `
  <circle cx="20" cy="20" r="11"/>
  <line x1="20" y1="0"    x2="20" y2="9"/>
  <line x1="20" y1="31"   x2="20" y2="40"/>
  <line x1="0"  y1="20"   x2="9"  y2="20"/>
  <line x1="31" y1="20"   x2="40" y2="20"/>
  <line x1="20" y1="12"   x2="20" y2="16.5"/>
  <line x1="20" y1="23.5" x2="20" y2="28"/>
  <line x1="12" y1="20"   x2="16.5" y2="20"/>
  <line x1="23.5" y1="20" x2="28"   y2="20"/>`;
const objectiveMarkerIcon = L.divIcon({
  className: '',
  html: `<svg xmlns="http://www.w3.org/2000/svg" width="40" height="40" viewBox="0 0 40 40">
    <g fill="none" stroke="rgba(0,0,0,0.55)" stroke-width="6"   stroke-linecap="round">${_objIconShapes}</g>
    <g fill="none" stroke="#39d353"           stroke-width="3.5" stroke-linecap="round">${_objIconShapes}</g>
  </svg>`,
  iconSize: [40, 40],
  iconAnchor: [20, 20],
});

function clearObjectiveMarkers() {
  objectiveMarkers.forEach(m => m.remove());
  objectiveMarkers.clear();
}

function updateObjectiveMarkers() {
  const schema = TASK_SCHEMAS[taskWizardSpecUrl];
  const fields = schema?.fields.filter(f => f.type === 'objective') || [];
  const activeKeys = new Set();

  for (const field of fields) {
    const val = taskFormValues[field.key];
    let latlng = null;

    if (val?.type === 'point') {
      latlng = [val.lat, val.lon];
    } else if (val?.type === 'entity' && val.entityId) {
      const entry = entityMap.get(val.entityId);
      if (entry?.entity) latlng = getLatLng(entry.entity);
    }

    if (!latlng) {
      objectiveMarkers.get(field.key)?.remove();
      objectiveMarkers.delete(field.key);
      continue;
    }

    activeKeys.add(field.key);
    if (objectiveMarkers.has(field.key)) {
      objectiveMarkers.get(field.key).setLatLng(latlng);
    } else {
      const m = L.marker(latlng, { icon: objectiveMarkerIcon, interactive: false, zIndexOffset: 1000 });
      m.addTo(map);
      objectiveMarkers.set(field.key, m);
    }
  }

  // Remove markers for fields no longer in schema.
  objectiveMarkers.forEach((m, key) => {
    if (!activeKeys.has(key)) { m.remove(); objectiveMarkers.delete(key); }
  });
}

function onObjectivePick(fieldKey, entityId) {
  if (entityId === '__map__') {
    toggleMapPick(fieldKey);
    return;
  }
  saveFormInputs();
  taskFormValues[fieldKey] = entityId ? { type: 'entity', entityId } : null;
  updateObjectiveMarkers();
  if (entityId && mapPickField) advanceMapPick(fieldKey);
  renderTaskWizardContent();
}

// Enter the persistent map-picking UI state (adds CSS class + shows banner).
function enterMapPickMode() {
  document.getElementById('map-wrapper').classList.add('picking');
  document.getElementById('map-pick-banner').classList.add('visible');
}

// Switch the active field within an already-active picking session.
// Updates the banner label and re-registers the map click handler.
function setMapPickField(fieldKey) {
  if (mapPickHandler) { map.off('click', mapPickHandler); mapPickHandler = null; }
  mapPickField = fieldKey;
  const schema = TASK_SCHEMAS[taskWizardSpecUrl];
  const label  = schema?.fields.find(f => f.key === fieldKey)?.label || fieldKey;
  const banner = document.getElementById('map-pick-banner');
  if (banner) banner.textContent = `Picking "${label}" · click an entity marker or the map to place a point · Esc to cancel`;
  mapPickHandler = (e) => {
    const { lat, lng } = e.latlng;
    const prev = mapPickField;
    taskFormValues[prev] = { type: 'point', lat, lon: lng, alt: 0 };
    updateObjectiveMarkers();
    advanceMapPick(prev);
    renderTaskWizardContent();
  };
  map.once('click', mapPickHandler);
}

// Advance to the next objective field (wraps around). Stays in picking mode.
function advanceMapPick(fromFieldKey) {
  const schema = TASK_SCHEMAS[taskWizardSpecUrl];
  if (!schema) { stopMapPick(); return; }
  const objFields = schema.fields.filter(f => f.type === 'objective');
  if (!objFields.length) { stopMapPick(); return; }
  const idx = objFields.findIndex(f => f.key === fromFieldKey);
  const nextIdx = (idx + 1) % objFields.length;
  setMapPickField(objFields[nextIdx].key);
}

function toggleMapPick(fieldKey) {
  saveFormInputs();
  if (mapPickField === fieldKey) {
    // Explicit toggle off — exit picking mode entirely.
    stopMapPick();
    renderTaskWizardContent();
    return;
  }
  if (!mapPickField) enterMapPickMode();
  setMapPickField(fieldKey);
  renderTaskWizardContent();
}

function stopMapPick() {
  if (mapPickHandler) { map.off('click', mapPickHandler); mapPickHandler = null; }
  mapPickField = null;
  document.getElementById('map-wrapper')?.classList.remove('picking');
  document.getElementById('map-pick-banner')?.classList.remove('visible');
}

// Persist text/number input values before a re-render
function saveFormInputs() {
  const schema = TASK_SCHEMAS[taskWizardSpecUrl];
  if (!schema) return;
  for (const field of schema.fields) {
    if (field.type === 'objective') {
      // Persist any in-progress altitude edit for point objectives.
      const altEl = document.querySelector(`.obj-alt-input[oninput*="'${field.key}'"]`);
      if (altEl && taskFormValues[field.key]?.type === 'point') {
        taskFormValues[field.key].alt = parseFloat(altEl.value) || 0;
      }
      continue;
    }
    const el = document.getElementById('tf-' + field.key.replace(/\./g, '__'));
    if (el) taskFormValues['_input_' + field.key] = el.value;
  }
}

// -------------------------------------------------------------------------
// Task wizard — payload + submit
// -------------------------------------------------------------------------
function setNestedKey(obj, dotPath, value) {
  const parts = dotPath.split('.');
  let cur = obj;
  for (let i = 0; i < parts.length - 1; i++) {
    if (cur[parts[i]] == null || typeof cur[parts[i]] !== 'object') cur[parts[i]] = {};
    cur = cur[parts[i]];
  }
  cur[parts[parts.length - 1]] = value;
}

function cleanEmptyObjects(obj) {
  if (typeof obj !== 'object' || obj === null || Array.isArray(obj)) return;
  for (const k of Object.keys(obj)) {
    if (k === '@type') continue;
    cleanEmptyObjects(obj[k]);
    if (typeof obj[k] === 'object' && !Array.isArray(obj[k]) && obj[k] !== null
        && Object.keys(obj[k]).length === 0) {
      delete obj[k];
    }
  }
}

function buildTaskPayload() {
  const schema = TASK_SCHEMAS[taskWizardSpecUrl];
  if (!schema) return null;

  saveFormInputs();
  const spec = { '@type': taskWizardSpecUrl };

  for (const field of schema.fields) {
    const key = field.key;

    if (field.type === 'objective') {
      const val = taskFormValues[key];
      let objJson = null;
      if (val?.type === 'entity' && val.entityId) {
        objJson = { entityId: val.entityId };
      } else if (val?.type === 'point' && val.lat !== undefined) {
        objJson = { point: { referenceName: 'Selected Point', lla: { lat: val.lat, lon: val.lon, alt: val.alt || 0 } } };
      }
      if (objJson !== null) setNestedKey(spec, key, objJson);
      continue;
    }

    const raw = (taskFormValues['_input_' + key] ?? '').toString().trim();
    if (raw === '') continue;

    if (field.type === 'float_opt') {
      const n = parseFloat(raw);
      if (!isNaN(n)) setNestedKey(spec, key, n);
    } else if (field.type === 'uint32_opt' || field.type === 'uint64_opt') {
      const n = parseInt(raw, 10);
      if (!isNaN(n)) setNestedKey(spec, key, n);
    } else if (field.type === 'string') {
      setNestedKey(spec, key, raw);
    }
  }

  // Special: Transit — convert _destination objective into nested RoutePlan
  if (taskWizardSpecUrl === 'type.googleapis.com/anduril.tasks.v2.Transit') {
    const dest = spec['_destination'];
    delete spec['_destination'];
    let lla = null;
    if (dest?.entityId) {
      const entry = entityMap.get(dest.entityId);
      const ll = entry ? getLatLng(entry.entity) : null;
      if (ll) lla = { lat: ll[0], lon: ll[1], alt: 0 };
    } else if (dest?.point?.lla) {
      lla = dest.point.lla;
    }
    if (lla) {
      spec.plan = { route: { path: [{ waypoint: { llaPoint: { lla } } }] } };
    }
  }

  cleanEmptyObjects(spec);
  return spec;
}

async function submitTask() {
  const spec = buildTaskPayload();
  if (!spec) return;

  const btn = document.getElementById('wizard-submit-btn');
  if (btn) btn.disabled = true;

  const body = {
    relations: { assignee: { system: { entityId: taskWizardEntityId } } },
    specification: spec,
  };

  try {
    const res = await fetch('/api/v1/tasks', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    });

    if (res.ok) {
      cancelTaskWizard();
      currentDetailTab = 'tasks';
      tasksInitiallyFetched = false; // force re-fetch so new task appears
      renderDetailView();
      ensureTasksLoaded();
    } else {
      const err = await res.json().catch(() => ({}));
      console.error('Create task failed:', err.message || res.status);
      if (btn) btn.disabled = false;
    }
  } catch (err) {
    console.error('Create task request failed:', err);
    if (btn) btn.disabled = false;
  }
}

// ESC cancels map pick
document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape' && mapPickField) {
    saveFormInputs();
    stopMapPick();
    if (taskWizardActive) renderTaskWizardContent();
  }
});

// -------------------------------------------------------------------------
// Task cache
// -------------------------------------------------------------------------
function upsertTaskInCache(task) {
  const id = task?.version?.taskId;
  if (!id) return;
  const existing = taskCache.get(id);
  const newVer   = task?.version?.definitionVersion || 0;
  if (!existing || newVer >= (existing?.version?.definitionVersion || 0)) {
    taskCache.set(id, task);
  }
}

async function ensureTasksLoaded() {
  if (tasksInitiallyFetched) return;
  // Fetch all tasks once so we have terminal tasks too
  try {
    const res = await fetch('/api/v1/tasks/query', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({}),
    });
    if (res.ok) {
      const data = await res.json();
      for (const t of data.tasks || []) upsertTaskInCache(t);
    }
  } catch { /* ignore */ }
  tasksInitiallyFetched = true;

  if (!taskStreamActive) connectTaskStream();

  // Re-render tasks tab if it's currently visible, or refresh panel badges
  if (selectedEntityId && currentDetailTab === 'tasks') renderDetailTabContent();
  else if (!selectedEntityId) renderPanel();
}

// -------------------------------------------------------------------------
// Task SSE stream
// -------------------------------------------------------------------------
async function connectTaskStream() {
  taskStreamActive = true;

  let response;
  try {
    response = await fetch('/api/v1/tasks/stream', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ heartbeatIntervalMs: 15000 }),
    });
  } catch {
    taskStreamActive = false;
    setTimeout(connectTaskStream, 5000);
    return;
  }

  if (!response.ok) {
    taskStreamActive = false;
    setTimeout(connectTaskStream, 5000);
    return;
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });
      const blocks = buffer.split('\n\n');
      buffer = blocks.pop();
      for (const block of blocks) handleTaskSseBlock(block);
    }
  } catch { /* stream closed */ }

  taskStreamActive = false;
  setTimeout(connectTaskStream, 3000);
}

function handleTaskSseBlock(block) {
  let eventType = '', data = '';
  for (const line of block.split('\n')) {
    if (line.startsWith('event: '))     eventType = line.slice(7).trim();
    else if (line.startsWith('data: ')) data      = line.slice(6).trim();
  }
  if (eventType === 'heartbeat' || !data) return;
  if (!['CREATE','UPDATE','PREEXISTING'].includes(eventType)) return;

  let taskEvent;
  try { taskEvent = JSON.parse(data); } catch { return; }

  const task = taskEvent.task;
  if (!task) return;

  upsertTaskInCache(task);

  // Re-render if this task's assignee is the currently-selected entity on the Tasks tab
  if (selectedEntityId && currentDetailTab === 'tasks') {
    if (getTaskAssigneeEntityId(task) === selectedEntityId) {
      renderDetailTabContent();
    }
  }

  // Update the task badge on the entity card in the list panel
  if (!selectedEntityId) {
    const assigneeId = getTaskAssigneeEntityId(task);
    if (assigneeId) {
      const card = document.querySelector(`.entity-card[data-entity-id="${CSS.escape(assigneeId)}"]`);
      if (card) {
        const taskBadgeEl = card.querySelector('.card-task-badge');
        const verb = getExecutingTaskVerb(assigneeId);
        if (verb) {
          taskBadgeEl.textContent = verb;
          taskBadgeEl.hidden = false;
        } else {
          taskBadgeEl.hidden = true;
        }
      }
    }
  }
}

// -------------------------------------------------------------------------
// Entity list panel
// -------------------------------------------------------------------------
const GROUPS = [
  { key: 'assets', label: 'Assets' },
  { key: 'tracks', label: 'Tracks' },
  { key: 'other',  label: 'Other'  },
];

function getGroupKey(entity) {
  const t = entity?.ontology?.template;
  if (t === 'TEMPLATE_ASSET') return 'assets';
  if (t === 'TEMPLATE_TRACK') return 'tracks';
  return 'other';
}

// -------------------------------------------------------------------------
// Surgical patch helpers — update only changed DOM nodes in place
// -------------------------------------------------------------------------

// Update a single entity card's dynamic fields without rebuilding it.
// Returns false if the card isn't in the DOM or needs a structural change
// (e.g. moved to a different group), so the caller can fall back to a full
// renderPanel().
function patchCard(id) {
  const card = document.querySelector(`.entity-card[data-entity-id="${id}"]`);
  if (!card) return false;

  const entry = entityMap.get(id);
  if (!entry) return false;

  const { entity, lastUpdated } = entry;
  const latlng   = getLatLng(entity);
  const coordStr = formatCoords(latlng);

  // Name — also guards against a rename causing a re-sort that would need
  // a full render; if it changed we let renderPanel() handle ordering.
  const nameEl = card.querySelector('.card-name');
  const newName = getDisplayName(entity);
  if (nameEl.textContent !== newName) return false;

  // Location
  const locEl = card.querySelector('.card-location');
  const newLoc = coordStr || 'No location';
  if (locEl.textContent !== newLoc) {
    locEl.textContent = newLoc;
    locEl.classList.toggle('missing', !coordStr);
    card.classList.toggle('no-location', !latlng);
  }

  // Timestamp
  card.querySelector('.card-updated').textContent = `Updated ${relativeTime(lastUpdated)}`;

  // Task badge
  const taskBadgeEl = card.querySelector('.card-task-badge');
  const verb = getExecutingTaskVerb(id);
  if (verb) {
    taskBadgeEl.textContent = verb;
    taskBadgeEl.hidden = false;
  } else {
    taskBadgeEl.hidden = true;
  }

  return true;
}

// Update only the volatile fields in the entity detail tab without
// rebuilding innerHTML.  Returns false if a structural change is needed
// (a field appeared or disappeared), so the caller falls back to a full
// renderDetailTabContent().
function patchDetailEntityTab(entry) {
  const content = document.getElementById('detail-tab-content');
  if (!content) return false;

  const { entity, lastUpdated } = entry;

  // Hero row
  const heroName = document.getElementById('detail-hero-name');
  if (heroName) heroName.textContent = getDisplayName(entity);

  const latlng = getLatLng(entity);
  const pos    = entity?.location?.position;

  // Helper: find a [data-field] span, update its text.
  // Returns false if the field's presence has changed (was present, now
  // absent, or vice-versa) — that means we need a full rebuild.
  function patchField(key, newValue) {
    const el = content.querySelector(`[data-field="${key}"]`);
    if (el && (newValue == null || newValue === '')) return false; // disappeared
    if (!el && newValue != null && newValue !== '')  return false; // appeared
    if (el) el.textContent = escapeHtml(String(newValue));
    return true;
  }

  const coordStr  = formatCoords(latlng);
  const altHae    = pos?.altitudeHaeMeters != null ? `${Number(pos.altitudeHaeMeters).toFixed(1)} m` : null;
  const altAgl    = pos?.altitudeAglMeters != null ? `${Number(pos.altitudeAglMeters).toFixed(1)} m` : null;
  const speed     = entity?.location?.speedMps  != null ? `${Number(entity.location.speedMps).toFixed(1)} m/s` : null;
  const lastSeen  = relativeTime(lastUpdated);

  if (!patchField('coords',   coordStr)) return false;
  if (!patchField('alt-hae',  altHae))   return false;
  if (!patchField('alt-agl',  altAgl))   return false;
  if (!patchField('speed',    speed))    return false;
  if (!patchField('last-seen', lastSeen)) return false;

  return true;
}

function renderPanel() {
  if (selectedEntityId) return;

  const list    = document.getElementById('entity-list');
  const countEl = document.getElementById('panel-count');

  countEl.textContent = entityMap.size;
  if (entityMap.size === 0) {
    list.innerHTML = '';
    list.appendChild(panelEmptyEl);
    document.getElementById('located-count').textContent = 0;
    return;
  }

  const sorted = [...entityMap.values()].sort(
    (a, b) => getDisplayName(a.entity).localeCompare(getDisplayName(b.entity))
  );
  const buckets = { assets: [], tracks: [], other: [] };
  for (const e of sorted) buckets[getGroupKey(e.entity)].push(e);

  const existingCards = new Map();
  for (const el of list.querySelectorAll('.entity-card')) existingCards.set(el.dataset.entityId, el);

  const fragment = document.createDocumentFragment();
  for (const { key, label } of GROUPS) {
    const entries = buckets[key];
    if (!entries.length) continue;
    const collapsed = collapsedGroups.has(key);

    const header = document.createElement('div');
    header.className = 'group-header' + (collapsed ? ' collapsed' : '');
    header.dataset.groupKey = key;
    header.innerHTML = `<span class="group-chevron">▼</span><span>${label}</span><span class="group-count">${entries.length}</span>`;
    header.addEventListener('click', () => toggleGroup(key));
    fragment.appendChild(header);

    const cardsEl = document.createElement('div');
    cardsEl.className = 'group-cards' + (collapsed ? ' collapsed' : '');
    cardsEl.dataset.groupCards = key;

    for (const { entity, lastUpdated } of entries) {
      const id       = entity.entityId;
      const latlng   = getLatLng(entity);
      const coordStr = formatCoords(latlng);

      let card = existingCards.get(id);
      if (!card) {
        card = document.createElement('div');
        card.className = 'entity-card';
        card.dataset.entityId = id;
        card.innerHTML = `<div class="card-header"><div class="card-name"></div><div class="card-task-badge" hidden></div></div><div class="card-meta"><div class="card-location"></div><div class="card-updated"></div></div>`;
        card.addEventListener('click', () => selectEntity(id));
      }
      card.classList.toggle('no-location', !latlng);
      card.classList.toggle('selected', id === selectedEntityId);
      card.querySelector('.card-name').textContent = getDisplayName(entity);
      const locEl = card.querySelector('.card-location');
      locEl.textContent = coordStr || 'No location';
      locEl.classList.toggle('missing', !coordStr);
      card.querySelector('.card-updated').textContent = `Updated ${relativeTime(lastUpdated)}`;
      const taskBadgeEl = card.querySelector('.card-task-badge');
      const verb = getExecutingTaskVerb(id);
      if (verb) {
        taskBadgeEl.textContent = verb;
        taskBadgeEl.hidden = false;
      } else {
        taskBadgeEl.hidden = true;
      }
      cardsEl.appendChild(card);
    }
    fragment.appendChild(cardsEl);
  }

  list.innerHTML = '';
  list.appendChild(fragment);

  const located = sorted.filter(e => getLatLng(e.entity)).length;
  document.getElementById('located-count').textContent = located;
}

function toggleGroup(key) {
  collapsedGroups.has(key) ? collapsedGroups.delete(key) : collapsedGroups.add(key);
  document.querySelector(`.group-header[data-group-key="${key}"]`)?.classList.toggle('collapsed', collapsedGroups.has(key));
  document.querySelector(`.group-cards[data-group-cards="${key}"]`)?.classList.toggle('collapsed', collapsedGroups.has(key));
}

// Periodic timestamp refresh
setInterval(() => {
  if (selectedEntityId) {
    // Nothing to refresh in Tasks tab (timestamps re-render on events)
    return;
  }
  for (const card of document.querySelectorAll('.entity-card')) {
    const entry = entityMap.get(card.dataset.entityId);
    if (entry) card.querySelector('.card-updated').textContent = `Updated ${relativeTime(entry.lastUpdated)}`;
  }
}, 10_000);

// -------------------------------------------------------------------------
// Entity upsert / remove
// -------------------------------------------------------------------------
function upsertEntity(entity, eventTime) {
  const id = entity.entityId;
  if (!id) return;

  const isNew = !entityMap.has(id);
  const existing = entityMap.get(id) || { entity: null, marker: null, lastUpdated: null };
  existing.entity = entity;
  existing.lastUpdated = eventTime || new Date();

  const latlng = getLatLng(entity);
  if (latlng) {
    if (existing.marker) {
      existing.marker.setLatLng(latlng);
      existing.marker.setIcon(makeMarkerIcon(id === selectedEntityId));
    } else {
      const marker = L.marker(latlng, { icon: makeMarkerIcon(id === selectedEntityId) });
      marker.on('click', (e) => {
        if (mapPickField) {
          L.DomEvent.stopPropagation(e);
          saveFormInputs();
          const prev = mapPickField;
          taskFormValues[prev] = { type: 'entity', entityId: id };
          updateObjectiveMarkers();
          advanceMapPick(prev);
          renderTaskWizardContent();
        } else if (!taskWizardActive) {
          selectEntity(id);
        }
      });
      existing.marker = marker;
      clusterGroup.addLayer(marker);
    }
  } else if (existing.marker) {
    clusterGroup.removeLayer(existing.marker);
    existing.marker = null;
  }

  entityMap.set(id, existing);

  if (existing.marker) scheduleAutoFit();
  if (jsonModalEntityId === id) refreshJsonModalContent();
  // If this entity is used as an objective, reposition its overlay marker.
  if (taskWizardActive && objectiveMarkers.size > 0) {
    const usedAsObjective = [...objectiveMarkers.keys()].some(
      key => taskFormValues[key]?.type === 'entity' && taskFormValues[key]?.entityId === id
    );
    if (usedAsObjective) updateObjectiveMarkers();
  }

  // For new entities (or structural changes) we need a full render.
  // For existing entities, try surgical in-place patches first; fall back
  // to a full scheduled render only if the patch detects a structural change.
  if (isNew) {
    scheduleRender();
  } else if (selectedEntityId === id) {
    if (currentDetailTab === 'entity' && !taskWizardActive) {
      const dropdownOpen = document.getElementById('task-entity-dropdown')?.classList.contains('open');
      if (!patchDetailEntityTab(existing) && !dropdownOpen) renderDetailTabContent();
    }
    // Tasks/dev tabs don't auto-update on entity ticks; no action needed.
  } else {
    if (!patchCard(id)) scheduleRender();
  }
}

function removeEntity(entity) {
  const id = entity?.entityId;
  if (!id) return;
  const existing = entityMap.get(id);
  if (existing?.marker) clusterGroup.removeLayer(existing.marker);
  entityMap.delete(id);
  if (jsonModalEntityId === id) closeEntityJsonModal();
  if (selectedEntityId === id) clearSelection();
  else scheduleRender();
}

// -------------------------------------------------------------------------
// Entity SSE stream
// -------------------------------------------------------------------------
function setConnState(state) {
  document.getElementById('conn-dot').className = state;
  document.getElementById('conn-label').textContent =
    { connecting: 'Connecting…', connected: 'Connected', disconnected: 'Disconnected' }[state] || state;
}

async function connectStream() {
  setConnState('connecting');
  let response;
  try {
    response = await fetch('/api/v1/entities/stream', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ heartbeatIntervalMs: 15000 }),
    });
  } catch {
    setConnState('disconnected');
    setTimeout(connectStream, 5000);
    return;
  }
  if (!response.ok) { setConnState('disconnected'); setTimeout(connectStream, 5000); return; }

  setConnState('connected');
  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';
  try {
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      buffer += decoder.decode(value, { stream: true });
      const blocks = buffer.split('\n\n');
      buffer = blocks.pop();
      for (const block of blocks) handleEntitySseBlock(block);
    }
  } catch { /* closed */ }

  setConnState('disconnected');
  setTimeout(connectStream, 3000);
}

function handleEntitySseBlock(block) {
  let eventType = '', data = '';
  for (const line of block.split('\n')) {
    if (line.startsWith('event: '))     eventType = line.slice(7).trim();
    else if (line.startsWith('data: ')) data      = line.slice(6).trim();
  }
  if (eventType === 'heartbeat' || eventType !== 'entity' || !data) return;

  let entityEvent;
  try { entityEvent = JSON.parse(data); } catch { return; }

  const entity = entityEvent.entity;
  if (!entity) return;
  const eventTime = entityEvent.time ? new Date(entityEvent.time) : new Date();

  if (entityEvent.eventType === 'EVENT_TYPE_DELETED') removeEntity(entity);
  else upsertEntity(entity, eventTime);
}

// Close status picker on outside click
document.addEventListener('click', (e) => {
  const picker = document.getElementById('status-picker');
  if (picker.style.display !== 'none' && !picker.contains(e.target)) {
    closeStatusPicker();
  }
  // Close task-entity dropdown on outside click
  const dropdown = document.getElementById('task-entity-dropdown');
  const btn      = document.getElementById('task-entity-btn');
  if (dropdown?.classList.contains('open') && !dropdown.contains(e.target) && e.target !== btn && !btn?.contains(e.target)) {
    dropdown.classList.remove('open');
    btn?.classList.remove('open');
  }
  // Close server-tools menu on outside click
  const stMenu = document.getElementById('server-tools-menu');
  const stBtn  = document.getElementById('server-tools-btn');
  if (stMenu?.classList.contains('open') && !stMenu.contains(e.target) && !stBtn?.contains(e.target)) {
    stMenu.classList.remove('open');
  }
  // Close add-entity menu on outside click
  const aeMenu = document.getElementById('add-entity-menu');
  const aeBtn  = document.getElementById('add-entity-btn');
  const clickedFlyout = e.target.closest('.ae-flyout');
  if (aeMenu?.classList.contains('open') && !aeMenu.contains(e.target) && !aeBtn?.contains(e.target) && !clickedFlyout) {
    aeMenu.classList.remove('open');
    closeAllFlyouts();
  }
  // Close map context menu on outside click
  const ctxMenu = document.getElementById('map-context-menu');
  if (ctxMenu?.classList.contains('open') && !ctxMenu.contains(e.target) && !clickedFlyout) {
    closeMapContextMenu();
  }
});

function toggleServerToolsMenu(e) {
  e.stopPropagation();
  const menu = document.getElementById('server-tools-menu');
  const btn  = document.getElementById('server-tools-btn');
  const isOpen = menu.classList.toggle('open');
  if (isOpen) {
    const rect = btn.getBoundingClientRect();
    menu.style.top  = (rect.bottom + 6) + 'px';
    menu.style.right = (window.innerWidth - rect.right) + 'px';
    menu.style.left = 'auto';
  }
}

async function serverToolDeleteAllEntities() {
  document.getElementById('server-tools-menu').classList.remove('open');
  try {
    const res = await fetch('/api/v1/entities', { method: 'DELETE' });
    if (res.ok) {
      const data = await res.json();
      for (const { marker } of entityMap.values()) {
        if (marker) clusterGroup.removeLayer(marker);
      }
      entityMap.clear();
      closeEntityJsonModal();
      clearSelection();
      renderPanel();
      console.info(`Deleted ${data.deletedCount} entity/entities`);
    } else {
      const err = await res.json().catch(() => ({}));
      console.error('Delete all entities failed:', err.message || res.status);
    }
  } catch (err) {
    console.error('Delete all entities request failed:', err);
  }
}

async function serverToolDeleteAllTasks() {
  document.getElementById('server-tools-menu').classList.remove('open');
  try {
    const res = await fetch('/api/v1/tasks', { method: 'DELETE' });
    if (res.ok) {
      const data = await res.json();
      taskCache.clear();
      if (currentDetailTab === 'tasks') renderDetailTabContent();
      console.info(`Deleted ${data.deletedCount} task(s)`);
    } else {
      const err = await res.json().catch(() => ({}));
      console.error('Delete all tasks failed:', err.message || res.status);
    }
  } catch (err) {
    console.error('Delete all tasks request failed:', err);
  }
}

// -------------------------------------------------------------------------
// Add Entity — template dropdown + map placement
// -------------------------------------------------------------------------
let entityTemplates = [];
let spawnPickHandler = null;

async function loadEntityTemplates() {
  try {
    const res = await fetch('/api/v1/templates');
    if (!res.ok) { console.error('Failed to load templates:', res.status); return; }
    entityTemplates = await res.json();
    renderAddEntityMenu();
  } catch (err) {
    console.error('Failed to load templates:', err);
  }
}

const CHEVRON_RIGHT = `<svg width="8" height="8" viewBox="0 0 8 8" fill="none" aria-hidden="true"><path d="M2.5 1.5L5.5 4L2.5 6.5" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/></svg>`;

// Returns { sortedCategories, categorized, uncategorized } with templates sorted alphabetically.
function groupTemplatesByCategory() {
  const categorized = new Map();
  const uncategorized = [];
  for (const t of entityTemplates) {
    if (t.category) {
      if (!categorized.has(t.category)) categorized.set(t.category, []);
      categorized.get(t.category).push(t);
    } else {
      uncategorized.push(t);
    }
  }
  const sortedCategories = [...categorized.keys()].sort((a, b) => a.localeCompare(b));
  for (const cat of sortedCategories) {
    categorized.get(cat).sort((a, b) => (a.displayName || a.templateId).localeCompare(b.displayName || b.templateId));
  }
  uncategorized.sort((a, b) => (a.displayName || a.templateId).localeCompare(b.displayName || b.templateId));
  return { sortedCategories, categorized, uncategorized };
}

// Builds a list of <button> items for a flyout. actionFn is the JS function name to call onclick.
function buildFlyoutItems(templates, actionFn) {
  return templates.map(t => {
    const name = t.displayName || t.templateId;
    return `<button class="add-entity-item" onclick="${actionFn}('${escHtml(t.templateId)}','${escHtml(name)}')">${escHtml(name)}</button>`;
  }).join('');
}

// Builds a category row with an expandable flyout.
function buildCategoryRow(label, templates, actionFn) {
  return `
    <div class="ae-category-row"
         onmouseenter="openCategoryFlyout(this)"
         onmouseleave="scheduleFlyoutClose(this)">
      <span>${escHtml(label)}</span>
      ${CHEVRON_RIGHT}
      <div class="ae-flyout"
           onmouseenter="cancelFlyoutClose()"
           onmouseleave="scheduleFlyoutClose(this.parentElement)">
        ${buildFlyoutItems(templates, actionFn)}
      </div>
    </div>`;
}

// Builds the full category HTML (rows + optional uncategorized section) for a given actionFn.
function buildEntityCategoryRows(actionFn) {
  const { sortedCategories, categorized, uncategorized } = groupTemplatesByCategory();
  let html = '';
  for (const cat of sortedCategories) {
    html += buildCategoryRow(cat, categorized.get(cat), actionFn);
  }
  if (uncategorized.length > 0) {
    if (sortedCategories.length > 0) html += '<hr class="add-entity-divider">';
    html += buildCategoryRow('Uncategorized', uncategorized, actionFn);
  }
  return html;
}

function renderAddEntityMenu() {
  const menu = document.getElementById('add-entity-menu');
  if (!menu) return;
  menu.innerHTML = '<div class="add-entity-label">Add Entity</div>' + buildEntityCategoryRows('beginEntityPlacement');
}

let _flyoutCloseTimer = null;

function openCategoryFlyout(rowEl) {
  cancelFlyoutClose();
  closeAllFlyouts();
  const flyout = rowEl.querySelector('.ae-flyout');
  if (!flyout) return;

  // Measure flyout before committing position
  flyout.style.visibility = 'hidden';
  flyout.style.left = '-9999px';
  flyout.style.top  = '-9999px';
  flyout.classList.add('open');
  const fw = flyout.offsetWidth;
  const fh = flyout.offsetHeight;
  flyout.style.visibility = '';

  const rect = rowEl.getBoundingClientRect();
  const vw = window.innerWidth;
  const vh = window.innerHeight;

  // Flip left if opening right would overflow the viewport
  const left = (rect.right + 4 + fw > vw)
    ? rect.left - fw - 4
    : rect.right + 4;

  // Clamp vertically so the flyout doesn't overflow the bottom
  const top = Math.min(rect.top, vh - fh - 4);

  flyout.style.left = left + 'px';
  flyout.style.top  = top  + 'px';
  rowEl.classList.add('ae-active');
}

function closeAllFlyouts() {
  document.querySelectorAll('.ae-flyout.open').forEach(f => {
    f.classList.remove('open');
    f.closest('.ae-category-row')?.classList.remove('ae-active');
  });
}

function scheduleFlyoutClose(rowEl) {
  clearTimeout(_flyoutCloseTimer);
  _flyoutCloseTimer = setTimeout(() => {
    rowEl.querySelector('.ae-flyout')?.classList.remove('open');
    rowEl.classList.remove('ae-active');
  }, 120);
}

function cancelFlyoutClose() {
  clearTimeout(_flyoutCloseTimer);
  _flyoutCloseTimer = null;
}

function toggleAddEntityMenu(e) {
  e.stopPropagation();
  const menu = document.getElementById('add-entity-menu');
  const btn  = document.getElementById('add-entity-btn');
  const isOpen = menu.classList.toggle('open');
  if (isOpen) {
    const rect = btn.getBoundingClientRect();
    menu.style.top   = (rect.bottom + 6) + 'px';
    menu.style.right = (window.innerWidth - rect.right) + 'px';
    menu.style.left  = 'auto';
  } else {
    closeAllFlyouts();
  }
}

function beginEntityPlacement(templateId, displayName) {
  document.getElementById('add-entity-menu').classList.remove('open');

  if (spawnPickHandler) { map.off('click', spawnPickHandler); spawnPickHandler = null; }

  const banner = document.getElementById('map-pick-banner');
  if (banner) banner.textContent = `Click the map to place "${displayName}" · Esc to cancel`;
  document.getElementById('map-wrapper').classList.add('picking');
  banner?.classList.add('visible');

  spawnPickHandler = async (e) => {
    spawnPickHandler = null;
    document.getElementById('map-wrapper').classList.remove('picking');
    document.getElementById('map-pick-banner')?.classList.remove('visible');
    resetBannerText();
    await spawnEntityAtLocation(templateId, e.latlng.lat, e.latlng.lng);
  };
  map.once('click', spawnPickHandler);
}

function resetBannerText() {
  const banner = document.getElementById('map-pick-banner');
  if (banner) banner.textContent = 'Click an entity marker to select it, or click the map to place a point · Esc to cancel';
}

async function spawnEntityAtLocation(templateId, lat, lng) {
  try {
    const res = await fetch(`/api/v1/templates/${encodeURIComponent(templateId)}/spawn`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ latitudeDegrees: lat, longitudeDegrees: lng }),
    });
    if (res.ok) {
      const data = await res.json();
      console.info(`Spawned entity '${data.entityId}' from template '${templateId}'`);
    } else {
      const err = await res.json().catch(() => ({}));
      console.error('Spawn failed:', err.message || res.status);
    }
  } catch (err) {
    console.error('Spawn request failed:', err);
  }
}

// -------------------------------------------------------------------------
// Map context menu — right-click to spawn entity at a location
// -------------------------------------------------------------------------
let mapContextLatLng = null;

map.on('contextmenu', (e) => {
  if (spawnPickHandler || mapPickField) return; // already in a placement mode
  closeMapContextMenu();
  closeAllFlyouts();
  document.getElementById('add-entity-menu')?.classList.remove('open');
  mapContextLatLng = e.latlng;
  showMapContextMenu(e.originalEvent.clientX, e.originalEvent.clientY);
});

function showMapContextMenu(clientX, clientY) {
  const menu = document.getElementById('map-context-menu');
  if (!menu) return;
  menu.innerHTML = '<div class="add-entity-label">Add Entity</div>' + buildEntityCategoryRows('spawnFromContextMenu');

  // Position so the menu stays within the viewport
  const vw = window.innerWidth, vh = window.innerHeight;
  menu.style.left = 'auto'; menu.style.right = 'auto';
  menu.style.top  = 'auto'; menu.style.bottom = 'auto';
  // Temporarily show off-screen to measure size
  menu.style.visibility = 'hidden';
  menu.classList.add('open');
  const mw = menu.offsetWidth, mh = menu.offsetHeight;
  menu.style.visibility = '';
  menu.style.left = (clientX + mw > vw ? vw - mw - 4 : clientX) + 'px';
  menu.style.top  = (clientY + mh > vh ? vh - mh - 4 : clientY) + 'px';
}

function closeMapContextMenu() {
  const menu = document.getElementById('map-context-menu');
  if (!menu) return;
  menu.classList.remove('open');
  closeAllFlyouts();
  mapContextLatLng = null;
}

function spawnFromContextMenu(templateId, _displayName) {
  const latlng = mapContextLatLng;
  closeMapContextMenu();
  if (!latlng) return;
  spawnEntityAtLocation(templateId, latlng.lat, latlng.lng);
}

// Cancel placement on Escape (alongside existing escape handling)
document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') {
    if (spawnPickHandler) {
      map.off('click', spawnPickHandler);
      spawnPickHandler = null;
      document.getElementById('map-wrapper').classList.remove('picking');
      document.getElementById('map-pick-banner')?.classList.remove('visible');
      resetBannerText();
    }
    closeMapContextMenu();
  }
}, true);

function escHtml(str) {
  return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

connectStream();
loadTaskTypeConfig().then(() => { if (!selectedEntityId) renderPanel(); });
ensureTasksLoaded();
loadEntityTemplates();
