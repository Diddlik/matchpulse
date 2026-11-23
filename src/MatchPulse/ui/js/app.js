/* MatchPulse UI – Logik und Darstellung.
 * Kein Framework: Zustand in S, render() baut die aktive Seite neu.
 * Klicks:   data-act="name" [data-arg="…"]          → ACT[name](arg, el)
 * Änderung: data-change="name" (select, checkbox, range, Feld verlassen) → CHG[name](value, el)
 * Tippen:   data-input="name"  (Textfelder, ohne Neuaufbau)            → INP[name](value, el)
 */
(function () {
  'use strict';
  const I18N = window.MP_I18N;
  const B = window.MP_BRIDGE;

  // ---------- Hilfen ----------
  const esc = (v) => String(v == null ? '' : v).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
  const clone = (o) => JSON.parse(JSON.stringify(o));
  const lang = () => (S.settings && S.settings.uiLanguage) || 'de';
  const t = (k, vars) => {
    let s = (I18N[lang()] && I18N[lang()][k]) || I18N.de[k] || k;
    if (vars) s = s.replace(/\{(\w+)\}/g, (_, n) => (vars[n] !== undefined ? vars[n] : ''));
    return s;
  };
  const locale = () => (lang() === 'de' ? 'de-DE' : 'en-GB');
  const time = (iso) => (iso ? new Date(iso).toLocaleTimeString(locale(), { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false }) : t('never'));
  const hhmm = (iso) => new Date(iso).toLocaleTimeString(locale(), { hour: '2-digit', minute: '2-digit', hour12: false });
  const pad = (n) => String(n).padStart(2, '0');
  const duration = (iso) => { const s = Math.max(0, Math.floor((Date.now() - new Date(iso).getTime()) / 1000)); return Math.floor(s / 60) + ':' + pad(s % 60); };
  const hexRgb = (h) => { const m = /^#?([0-9a-f]{6})$/i.exec(h || ''); if (!m) return null; const n = parseInt(m[1], 16); return [(n >> 16) & 255, (n >> 8) & 255, n & 255]; };
  const colorIsBad = (hex) => {
    const rgb = hexRgb(hex); if (!rgb) return true;
    const [r, g, b] = rgb.map((v) => v / 255); const mx = Math.max(r, g, b), mn = Math.min(r, g, b);
    const l = (mx + mn) / 2, d = mx - mn, s = d === 0 ? 0 : d / (1 - Math.abs(2 * l - 1));
    return s < 0.25 || l < 0.2 || l > 0.92;
  };
  const fgFor = (hex) => { const c = hexRgb(hex) || [0, 0, 0]; return 0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2] > 140 ? '#111111' : '#ffffff'; };
  const uid = (p) => p + Date.now().toString(36) + Math.random().toString(36).slice(2, 6);
  const evKey = (e) => e.type + '|' + e.matchId + '|' + e.sequence + '|' + e.occurredAt;

  const ICONS = {
    status: 'M3 12h4l3-7 4 14 3-7h4', events: 'M4 6h16M4 12h16M4 18h10', live: 'M3 5h18v11H3zM8 20h8M12 16v4M10 8.5l4 2-4 2z',
    sinks: 'M4 12h12M12 6l6 6-6 6M21 4v16', plugins: 'M9 3v5M15 3v5M6 8h12v3a6 6 0 0 1-12 0zM12 17v4',
    setup: 'M4 7h9M17 7h3M4 17h3M11 17h9M15 5v4M9 15v4', diag: 'M3 8V4h4M21 8V4h-4M3 16v4h4M21 16v4h-4M9 12h6M12 9v6',
    log: 'M6 3h9l4 4v14H6zM9 12h7M9 16h7M9 8h3', obs: 'M4 7h11v10H4zM15 10.5l5-3v9l-5-3',
    audio: 'M4 9h4l5-4v14l-5-4H4zM16.5 9a4 4 0 0 1 0 6M19 6.5a7.5 7.5 0 0 1 0 11'
  };
  const icon = (name, size) => `<svg width="${size || 18}" height="${size || 18}" viewBox="0 0 24 24" aria-hidden="true"><path d="${ICONS[name]}" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"/></svg>`;
  const X_ICON = '<svg width="12" height="12" viewBox="0 0 10 10" aria-hidden="true"><path d="M0 0l10 10M10 0L0 10" stroke="currentColor" stroke-width="1.4"/></svg>';
  const WARN_ICON = '<svg width="18" height="18" viewBox="0 0 24 24" aria-hidden="true" style="flex:none"><path d="M12 3l10 18H2z" fill="none" stroke="var(--warn)" stroke-width="1.8" stroke-linejoin="round"/><path d="M12 10v5M12 18v.5" stroke="var(--warn)" stroke-width="1.8" stroke-linecap="round"/></svg>';
  const OK_ICON = '<svg width="16" height="16" viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12l5 5 9-10" fill="none" stroke="var(--ok)" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"/></svg>';
  const NAV = ['status', 'events', 'live', 'sinks', 'plugins', 'setup', 'diag', 'log'];
  const SWATCHES = [['#FF3030', 'c_red'], ['#FFE000', 'c_yellow'], ['#30FF60', 'c_green'], ['#00E5FF', 'c_cyan'], ['#FF40FF', 'c_magenta'], ['#FFFFFF', 'c_white']];

  // ---------- Zustand ----------
  const S = {
    loaded: false, page: 'status',
    appState: 'off', match: null, hostProblems: [], events: [], sinks: [], plugins: [], settings: null, draft: null,
    diagnostics: [], highlights: [], log: [], pending: [], update: null,
    selEvent: null, evFilter: 'all', copied: false,
    editId: null, editDraft: '', editError: '', addKind: 'webhook', addTarget: '', addError: '',
    hexDraft: '', justSaved: false, diagIdx: 0, confirmClear: false,
    autoRefresh: true, logFilter: 'all', plugSel: 'obs', newApp: '', liveIdx: -1, toast: ''
  };

  // ---------- Abgeleitete Werte ----------
  const plugStatus = (p) => (!p.enabled ? 'off' : p.status);
  const plugCls = (s) => (s === 'error' ? 'st-err' : s === 'disconnected' ? 'st-warn' : s === 'off' ? 'st-idle' : 'st-ok');
  const sinkStatus = (k) => (k.lastError ? 'err' : k.lastSuccessAt ? 'ok' : 'new');
  const sinkCls = (s) => (s === 'err' ? 'st-err' : s === 'ok' ? 'st-ok' : 'st-idle');
  const sinkError = (k) => (k.lastError ? (k.lastError.code === 'timeout' ? t('err_timeout') : k.lastError.code === 'script_timeout' ? t('err_script') : k.lastError.message) : '');
  const roundKills = () => S.events.filter((e) => e.type === 'kill' && (S.match ? e.matchId === S.match.matchId : true));
  const dirty = () => JSON.stringify(S.draft) !== JSON.stringify(S.settings);
  const problems = () => {
    const list = [];
    S.sinks.filter((k) => k.lastError).forEach((k) => list.push({ title: t('p_sink'), detail: k.target + ' – ' + sinkError(k), act: 'go', arg: 'sinks', action: t('to_sinks') }));
    if (S.settings && colorIsBad(S.settings.killMarkerColor)) list.push({ title: t('p_color'), detail: t('p_color_d'), act: 'go', arg: 'setup', action: t('to_setup') });
    const obs = S.plugins.find((p) => p.id === 'obs');
    if (obs && obs.enabled && obs.status === 'error') list.push({ title: t('p_obs'), detail: t('p_obs_d'), act: 'go', arg: 'plugins', action: t('to_plugins') });
    (S.hostProblems || []).forEach((c) => {
      if (c === 'capture_failed') list.push({ title: t('p_capture'), detail: t('p_capture_d'), act: 'go', arg: 'log', action: t('to_log') });
      if (c === 'config_invalid') list.push({ title: t('p_config'), detail: t('p_config_d') });
    });
    return list;
  };

  // ---------- Bausteine ----------
  const seg = (items, cur, act, role) => `<div class="segmented" role="${role === 'radio' ? 'radiogroup' : 'group'}" aria-label="${esc(t('filter'))}">${items.map(([id, label]) =>
    `<button type="button" ${role === 'radio' ? `role="radio" aria-checked="${id === cur}"` : `aria-pressed="${id === cur}"`} data-act="${act}" data-arg="${id}">${esc(label)}</button>`).join('')}</div>`;
  const evLabel = (e) => t('ev_' + e.type);
  const evDetail = (e) => {
    if (e.type === 'kill') return e.matchId ? t('kill_n', { n: (e.data && e.data.kills) || e.sequence - 1 }) : t('mp_kill');
    if (e.type === 'knockdown' || e.type === 'death') return t('round', { id: (e.matchId || '').slice(0, 8) });
    if (e.type === 'match_ended') return t(e.data.result || 'unknown') + ' · ' + t('reason_' + e.data.reason);
    return t('round', { id: (e.matchId || '').slice(0, 8) });
  };
  const evBadge = (e) => `<span class="badge ${e.type === 'kill' ? 'kill' : e.type === 'match_started' ? 'start' : ''}">${esc(evLabel(e))}</span>`;
  const killScene = (marker) => `
    <svg viewBox="0 0 1600 900" width="100%" role="img" aria-label="${esc(t('live_shot_alt'))}" style="display:block">
      <rect width="1600" height="900" fill="#39414b"/><rect y="560" width="1600" height="340" fill="#4a5046"/>
      <rect x="1040" y="300" width="240" height="260" fill="#535a63"/><rect x="260" y="380" width="180" height="180" fill="#4d545d"/>
      <path d="M800 432v14M800 454v14M768 450h14M818 450h14" stroke="#f2f2f2" stroke-width="4" stroke-linecap="round"/>
      <path d="M752 402l26 26M848 402l-26 26M752 498l26-26M848 498l-26-26" stroke="${esc(marker)}" stroke-width="10" stroke-linecap="round"/>
      <g transform="translate(800 590)"><circle r="22" fill="#f2f2f2"/><rect x="-12" y="12" width="24" height="14" rx="3" fill="#f2f2f2"/><circle cx="-8" cy="-2" r="5" fill="#39414b"/><circle cx="8" cy="-2" r="5" fill="#39414b"/></g>
      <g transform="translate(1220 60)"><rect width="330" height="34" rx="4" fill="rgba(0,0,0,.5)"/><rect x="14" y="13" width="110" height="8" rx="4" fill="#7fc4ff"/><rect x="138" y="13" width="26" height="8" rx="3" fill="#8a919b"/><rect x="178" y="13" width="130" height="8" rx="4" fill="#d8dce2"/></g>
    </svg>`;

  const updateLine = () => {
    const u = S.update; if (!u) return '';
    const head = t('up_installed', { v: u.current });
    const tail = { checking: t('up_checking'), upToDate: t('up_uptodate'), available: t('up_available', { v: u.latest }), waitingForGame: t('up_waiting'),
      downloading: t('up_downloading'), installing: t('up_installing'), error: u.error || t('up_error') }[u.status];
    return head + (tail ? ' · ' + tail : '') + (u.checkedAt && u.status !== 'checking' ? ' · ' + t('up_checked', { t: time(u.checkedAt) }) : '');
  };

  // ---------- Seiten ----------
  function pageStatus() {
    const st = S.appState, isMatch = st === 'match' && S.match;
    const kills = isMatch ? roundKills().length : 0;
    const latest = S.events.slice().sort((a, b) => b.occurredAt.localeCompare(a.occurredAt)).slice(0, 5);
    const probs = problems();
    const up = S.update;
    const upNotice = up && (up.status === 'available' || up.status === 'waitingForGame') ? `
      <section class="notice row-flex" style="padding:14px 18px">
        <span style="flex:1 1 300px"><strong>${esc(t('up_available', { v: up.latest }))}</strong> <span class="muted small">${esc(up.status === 'waitingForGame' ? t('up_waiting') : up.installable ? '' : t('up_portable'))}</span></span>
        ${up.installable ? (up.status === 'available' ? `<button type="button" class="btn s primary" data-act="installUpdate">${esc(t('up_install'))}</button>` : '') : `<button type="button" class="btn s" data-act="openReleasePage">${esc(t('up_release_page'))}</button>`}
      </section>` : '';
    const okSinks = S.sinks.filter((k) => !k.lastError).length;
    return `
      <h1>${esc(t('nav_status'))}</h1>
      ${upNotice}
      <section class="card s-${st}" style="flex-direction:row;flex-wrap:wrap;gap:24px;align-items:center;padding:24px">
        <div class="stack-s" style="flex:1 1 320px">
          <div class="row-flex"><span class="dot l ${st === 'off' ? '' : 'halo'}"></span><span style="font-size:24px;font-weight:600">${esc(t('st_' + st))}</span></div>
          <span class="muted">${esc(t('st_' + st + '_d'))}</span>
          <div class="steps" style="margin-top:8px">${['off', 'game', 'match'].map((k) => `<span class="step s-${k} ${k === st ? 'on' : ''}"><span class="dot"></span>${esc(t('st_' + k))}</span>`).join('')}</div>
        </div>
        ${isMatch ? `<div style="flex:1 1 360px;display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:12px">
          <div class="tile"><span class="caption muted">${esc(t('since'))}</span><span class="value">${esc(time(S.match.startedAt))}</span></div>
          <div class="tile"><span class="caption muted">${esc(t('duration'))}</span><span class="value" data-tick="duration">${esc(duration(S.match.startedAt))}</span></div>
          <div class="tile" style="background:color-mix(in srgb, var(--marker) 16%, var(--surface2))"><span class="caption muted">${esc(t('kills_round'))}</span><span class="value" style="font-weight:700">${kills}</span></div>
        </div>` : ''}
      </section>
      <div class="cols">
        <section class="card" style="flex:1 1 340px">
          <div class="row-flex"><h2 class="grow">${esc(t('latest'))}</h2><button type="button" class="btn s link" data-act="go" data-arg="events">${esc(t('show_all'))}</button></div>
          <div class="list" style="padding:0">${latest.map((e) => `
            <button type="button" class="list-item ${e.type === 'kill' ? 'ev-row kill' : ''}" data-act="openEvent" data-arg="${esc(evKey(e))}">
              <span class="num muted" style="width:64px">${esc(time(e.occurredAt))}</span>${evBadge(e)}
              <span class="ellipsis muted small" style="flex:1;font-weight:400">${esc(evDetail(e))}</span>
            </button>`).join('')}</div>
        </section>
        <div class="stack" style="flex:1 1 340px;gap:20px">
          <section class="card">
            <h2>${esc(t('problems'))}</h2>
            ${probs.length === 0 ? `<div class="row-flex muted" style="gap:10px">${OK_ICON}${esc(t('no_problems'))}</div>` : probs.map((p) => `
              <div class="notice" style="border:0">${WARN_ICON}
                <div class="stack-s grow" style="gap:2px;min-width:0"><strong>${esc(p.title)}</strong><span class="muted small wrap-any">${esc(p.detail)}</span></div>
                ${p.act ? `<button type="button" class="btn s" style="align-self:center" data-act="${p.act}" data-arg="${p.arg}">${esc(p.action)}</button>` : ''}
              </div>`).join('')}
          </section>
          <section class="card">
            <div class="row-flex"><h2 class="grow">${esc(t('nav_sinks'))}</h2><span class="muted small">${esc(t('sinks_summary', { n: okSinks, m: S.sinks.length }))}</span></div>
            <div class="stack-s">${S.sinks.map((k) => { const s = sinkStatus(k); return `
              <div class="row-flex ${sinkCls(s)}" style="gap:10px;flex-wrap:nowrap;font-size:13px"><span class="dot"></span><span class="mono ellipsis grow" style="font-size:12px">${esc(k.target)}</span><span class="status-text" style="font-weight:400">${esc(t('s_' + s))}</span></div>`; }).join('')}</div>
            ${S.plugins.length ? `<div class="stack-s" style="padding-top:10px;border-top:1px solid var(--border)">
              <span class="caption muted">${esc(t('nav_plugins'))}</span>
              ${S.plugins.map((p) => { const s = plugStatus(p); return `<div class="row-flex ${plugCls(s)}" style="gap:10px;flex-wrap:nowrap;font-size:13px"><span class="dot"></span><span class="grow">${esc(t('pl_' + p.id))}</span><span class="status-text" style="font-weight:400">${esc(t('ps_' + s))}</span></div>`; }).join('')}
            </div>` : ''}
            <button type="button" class="btn" style="align-self:flex-start" data-act="testAll">${esc(t('send_test_all'))}</button>
          </section>
        </div>
      </div>`;
  }

  function pageEvents() {
    const list = S.events.slice().sort((a, b) => b.occurredAt.localeCompare(a.occurredAt))
      .filter((e) => S.evFilter === 'all' || (S.evFilter === 'kills' ? e.type === 'kill' : e.type !== 'kill'));
    const sel = S.events.find((e) => evKey(e) === S.selEvent);
    return `
      <div class="row-flex"><h1 class="grow">${esc(t('nav_events'))}</h1>${seg([['all', t('f_all')], ['kills', t('f_kills')], ['match', t('f_match')]], S.evFilter, 'evFilter')}</div>
      <div class="cols">
        <section class="card flush" style="flex:3 1 420px;min-width:0">
          <div class="ev-grid ev-head"><span>${esc(t('col_time'))}</span><span>${esc(t('col_type'))}</span><span>${esc(t('col_detail'))}</span><span>${esc(t('col_match'))}</span></div>
          <div class="list">${list.map((e) => `
            <button type="button" class="list-item ev-grid ev-row ${e.type === 'kill' ? 'kill' : ''}" aria-pressed="${evKey(e) === S.selEvent}" data-act="selEvent" data-arg="${esc(evKey(e))}">
              <span class="num muted" style="font-weight:400">${esc(time(e.occurredAt))}</span><span>${evBadge(e)}</span>
              <span class="ellipsis">${esc(evDetail(e))}</span><span class="mono muted" style="font-size:12px;font-weight:400">${esc(e.matchId ? e.matchId.slice(0, 8) : '—')}</span>
            </button>`).join('') || `<p class="muted" style="padding:24px 12px">${esc(t('ev_empty'))}</p>`}</div>
        </section>
        <section class="card" style="flex:2 1 320px;min-width:0">
          <div class="row-flex"><h2 class="grow">JSON</h2>${sel ? `<button type="button" class="btn s" data-act="copyJson">${esc(S.copied ? t('copied') : t('copy'))}</button>` : ''}</div>
          ${sel ? `<pre class="json">${esc(JSON.stringify(sel, null, 2))}</pre>` : `<p class="muted">${esc(t('select_event'))}</p>`}
        </section>
      </div>`;
  }

  function pageLive() {
    const on = S.settings.liveMonitor;
    const hl = S.highlights.filter((h) => (S.match ? h.matchId === S.match.matchId : true)).slice().sort((a, b) => a.capturedAt.localeCompare(b.capturedAt));
    const n = hl.length, i = n ? (S.liveIdx < 0 || S.liveIdx >= n ? n - 1 : S.liveIdx) : 0, h = hl[i];
    const label = (x, k) => t('live_title', { n: x.matchId ? x.sequence - 1 : k + 1 });
    const marker = S.settings.killMarkerColor;
    return `
      <div class="row-flex"><h1 class="grow">${esc(t('nav_live'))}</h1>
        <label class="row-flex card" style="padding:0 14px;min-height:44px;flex-direction:row;gap:10px"><input id="live-on" class="check" type="checkbox" ${on ? 'checked' : ''} data-change="liveMonitor"><strong>${esc(t('live_toggle'))}</strong></label>
        ${B.isMock && on ? `<button type="button" class="btn" data-act="simulateKill">Demo: Kill</button>` : ''}
      </div>
      <p class="muted" style="max-width:75ch">${esc(t('live_intro'))}</p>
      ${!on ? `<div class="empty stack-s" style="align-items:flex-start;gap:12px"><strong style="color:var(--text)">${esc(t('live_off'))}</strong><span class="small" style="max-width:70ch">${esc(t('live_off_d'))}</span><button type="button" class="btn primary" data-act="liveOn">${esc(t('live_toggle'))}</button></div>`
      : !n ? `<p class="muted">${esc(t('live_empty'))}</p>` : `
        <section class="card flush">
          <div style="position:relative;background:var(--game-bg)">
            ${h.imageUrl ? `<img class="diag-shot" style="border-radius:0" src="${esc(h.imageUrl)}" alt="${esc(t('live_shot_alt'))}">` : killScene(marker)}
            <div style="position:absolute;left:16px;top:16px;display:flex;align-items:center;gap:8px;padding:6px 12px;border-radius:6px;background:rgba(0,0,0,.6);color:#fff;font-size:13px">
              <span class="badge kill" style="font-weight:700">Kill</span><strong>${esc(label(h, i))}</strong><span class="num" style="color:#d2d3d6">${esc(time(h.capturedAt))}</span>
            </div>
            ${h.imageUrl ? '' : `<span style="position:absolute;right:16px;bottom:12px;padding:4px 10px;border-radius:4px;background:rgba(0,0,0,.6);color:#d2d3d6;font-size:12px">${esc(t('live_placeholder'))}</span>`}
          </div>
          <div class="row-flex" style="padding:12px 16px">
            <span class="muted small" style="flex:1 1 240px">${esc(h.matchId ? t('live_sub', { id: h.matchId.slice(0, 8), k: n }) : t('live_sub_mp'))}</span>
            <button type="button" class="btn s" data-act="liveStep" data-arg="-1">${esc(t('live_prev'))}</button>
            <button type="button" class="btn s" data-act="liveStep" data-arg="1">${esc(t('live_next'))}</button>
            <button type="button" class="btn s" data-act="liveWindow">${esc(t('live_window'))}</button>
          </div>
        </section>
        <section class="stack-s"><h3>${esc(t('live_round'))}</h3>
          <div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(160px,1fr));gap:10px">
            ${hl.map((x, k) => ({ x, k })).reverse().map(({ x, k }) => `
              <button type="button" class="select-card" aria-current="${k === i}" style="padding:6px;display:flex;flex-direction:column;gap:6px;text-align:left;border-width:2px" data-act="liveIdx" data-arg="${k}">
                ${x.imageUrl ? `<img src="${esc(x.imageUrl)}" alt="" style="width:100%;border-radius:4px">` : `<svg viewBox="0 0 160 90" width="100%" aria-hidden="true" style="border-radius:4px;background:#39414b;display:block"><rect y="56" width="160" height="34" fill="#4a5046"/><path d="M75 40l5 5M85 40l-5 5M75 50l5-5M85 50l-5-5" stroke="${esc(marker)}" stroke-width="2.2" stroke-linecap="round"/></svg>`}
                <span class="small"><strong>${esc(label(x, k))}</strong> <span class="muted num">${esc(time(x.capturedAt))}</span></span>
              </button>`).join('')}
          </div>
        </section>`}`;
  }

  function pageSinks() {
    return `
      <div class="narrow stack">
        <div class="row-flex"><h1 class="grow">${esc(t('nav_sinks'))}</h1><button type="button" class="btn primary" data-act="testAll">${esc(t('send_test_all'))}</button></div>
        <p class="muted" style="max-width:70ch">${esc(t('sinks_intro'))}</p>
        ${S.sinks.length === 0 ? `<p class="empty">${esc(t('sinks_empty'))}</p>` : ''}
        <div class="stack" style="gap:10px">${S.sinks.map((k) => {
          const s = sinkStatus(k), editing = S.editId === k.id;
          return `<section class="card" style="padding:16px 18px;gap:10px">
            <div class="row-flex">
              <span class="badge">${esc(k.kind === 'webhook' ? 'Webhook' : t('script'))}</span>
              ${editing ? `<label class="field" style="flex:1 1 260px"><span>${esc(k.kind === 'webhook' ? t('url') : t('cmd'))}</span><input id="edit-${esc(k.id)}" class="input mono" type="text" value="${esc(S.editDraft)}" data-input="editDraft" style="border-color:var(--accent)"></label>`
                : `<span class="mono wrap-any" style="flex:1 1 260px">${esc(k.target)}</span>`}
              <span class="status-text ${sinkCls(s)}"><span class="dot"></span>${esc(t('s_' + s))}</span>
            </div>
            <div class="row-flex muted" style="gap:6px 24px;font-size:13px">
              <span>${esc(t('last_ok'))}: <strong style="color:var(--text);font-weight:500">${esc(time(k.lastSuccessAt))}</strong></span>
              <span>${esc(t('last_fail'))}: <strong style="color:var(--text);font-weight:500">${esc(time(k.lastFailureAt))}</strong></span>
            </div>
            ${k.lastError ? `<div class="notice error" style="border:0;padding:8px 12px;font-size:13px">${esc(sinkError(k))}</div>` : ''}
            ${editing && S.editError ? `<span class="small" style="color:var(--err)">${esc(S.editError)}</span>` : ''}
            <div class="row-flex" style="gap:8px">${editing
              ? `<button type="button" class="btn s primary" data-act="saveEdit">${esc(t('save'))}</button><button type="button" class="btn s" data-act="cancelEdit">${esc(t('cancel'))}</button>`
              : `<button type="button" class="btn s" data-act="testSink" data-arg="${esc(k.id)}">${esc(t('test'))}</button><button type="button" class="btn s" data-act="editSink" data-arg="${esc(k.id)}">${esc(t('edit'))}</button><button type="button" class="btn s danger" data-act="removeSink" data-arg="${esc(k.id)}">${esc(t('remove'))}</button>`}</div>
          </section>`; }).join('')}</div>
        <section class="card">
          <h2>${esc(t('add_title'))}</h2>
          <div class="row-flex" style="align-items:flex-end">
            <label class="field" style="flex:0 0 160px"><span>${esc(t('kind'))}</span><select class="select" data-change="addKind"><option value="webhook" ${S.addKind === 'webhook' ? 'selected' : ''}>Webhook</option><option value="script" ${S.addKind === 'script' ? 'selected' : ''}>${esc(t('script'))}</option></select></label>
            <label class="field" style="flex:1 1 280px"><span>${esc(S.addKind === 'webhook' ? t('url') : t('cmd'))}</span><input id="add-target" class="input mono" type="text" value="${esc(S.addTarget)}" placeholder="${S.addKind === 'webhook' ? 'https://…' : 'C:\\Pfad\\skript.cmd'}" data-input="addTarget"></label>
            <button type="button" class="btn primary" data-act="addSink">${esc(t('add'))}</button>
          </div>
          ${S.addError ? `<span class="small" style="color:var(--err)">${esc(S.addError)}</span>` : ''}
          <span class="muted small">${esc(t('sinks_apply'))}</span>
        </section>
      </div>`;
  }

  function pagePlugins() {
    const p = S.plugins.find((x) => x.id === S.plugSel) || S.plugins[0];
    if (!p) return `<h1>${esc(t('nav_plugins'))}</h1>`;
    const s = plugStatus(p);
    const actions = p.id === 'obs' ? ['start_recording', 'stop_recording', 'pause_recording', 'resume_recording', 'split_recording', 'record_chapter', 'start_replay_buffer', 'stop_replay_buffer', 'save_replay', 'switch_scene', 'set_text', 'show_source', 'start_streaming', 'stop_streaming'] : ['duck', 'restore'];
    const paramPh = { switch_scene: 'a_scene_ph', set_text: 'ph_source', show_source: 'ph_source', record_chapter: 'ph_chapter', save_replay: 'ph_replay_name' };
    return `
      <h1>${esc(t('nav_plugins'))}</h1>
      <p class="muted" style="max-width:75ch">${esc(t('pl_intro'))}</p>
      <div class="cols">
        <div class="stack" style="flex:1 1 260px;gap:10px">${S.plugins.map((x) => { const xs = plugStatus(x); return `
          <section class="select-card" aria-current="${x.id === p.id}">
            <button type="button" class="list-item" style="align-items:flex-start;padding:14px 16px 10px;border-radius:10px 10px 0 0" data-act="plugSel" data-arg="${x.id}">
              <span style="flex:none;width:36px;height:36px;border-radius:8px;background:var(--surface2);border:1px solid var(--border);display:flex;align-items:center;justify-content:center">${icon(x.id, 20)}</span>
              <span class="stack-s" style="gap:2px"><strong>${esc(t('pl_' + x.id))}</strong><span class="small muted">${esc(t('pl_' + x.id + '_d'))}</span></span>
            </button>
            <div class="row-flex" style="padding:0 16px 12px 64px;gap:10px">
              <span class="status-text grow ${plugCls(xs)}" style="font-weight:400;font-size:12.5px"><span class="dot"></span>${esc(t('ps_' + xs))}</span>
              <label class="row-flex small" style="gap:8px;min-height:36px"><input class="check" type="checkbox" ${x.enabled ? 'checked' : ''} data-change="plugEnabled" data-id="${x.id}"><span>${esc(t('pl_enabled'))}</span></label>
            </div>
          </section>`; }).join('')}</div>
        <section class="card" style="flex:3 1 520px;min-width:0;gap:18px">
          <div class="row-flex"><h2 class="grow" style="font-size:18px">${esc(t('pl_' + p.id))}</h2><span class="status-text ${plugCls(s)}"><span class="dot"></span>${esc(t('ps_' + s))}</span></div>
          ${!p.enabled ? `<div class="notice row-flex"><span class="small" style="flex:1 1 260px">${esc(t('pl_off_note'))}</span><button type="button" class="btn s primary" data-act="plugOn" data-arg="${p.id}">${esc(t('turn_on'))}</button></div>` : ''}
          ${p.id === 'obs' ? `
            <div class="stack" style="gap:12px">
              <h3>${esc(t('obs_conn'))}</h3>
              <div class="row-flex" style="align-items:flex-end">
                <label class="field" style="flex:1 1 200px"><span>${esc(t('obs_host'))}</span><input class="input mono" type="text" value="${esc(p.config.host)}" data-change="obsHost"></label>
                <label class="field" style="flex:0 0 100px"><span>${esc(t('obs_port'))}</span><input class="input mono" type="text" inputmode="numeric" value="${esc(p.config.port)}" data-change="obsPort"></label>
                <label class="field" style="flex:1 1 180px"><span>${esc(t('obs_pw'))}</span><input class="input" type="password" autocomplete="off" value="${esc(p.config.password)}" data-change="obsPw"></label>
                <button type="button" class="btn" data-act="testPlugin" data-arg="obs">${esc(t('obs_test'))}</button>
              </div>
              ${p.enabled && p.status === 'error' ? `<div class="notice error" style="border:0;padding:8px 12px;font-size:13px">${esc(p.lastError || t('obs_err'))}</div>` : ''}
              <span class="small muted">${esc(t('obs_hint'))}</span>
            </div>` : `
            <div class="stack" style="gap:12px">
              <h3>${esc(t('au_apps'))}</h3><span class="small muted">${esc(t('au_apps_d'))}</span>
              <div class="stack-s" style="gap:4px">${p.config.apps.map((a, i) => `
                <div class="row-flex" style="min-height:44px;padding:0 12px;border-radius:6px;background:var(--surface2)">
                  <span class="mono" style="flex:1 1 160px">${esc(a.name)}</span>
                  <label class="row-flex" style="gap:10px"><span class="caption muted">${esc(t('au_vol'))}</span>
                    <input class="range" type="range" min="0" max="100" step="5" value="${a.volume}" aria-label="${esc(t('au_vol_of', { n: a.name }))}" data-input="appVolLive" data-change="appVol" data-idx="${i}" style="width:160px">
                    <span class="num" style="width:40px;text-align:right;font-weight:600" id="vol-${i}">${a.volume} %</span></label>
                  <button type="button" class="icon-btn" aria-label="${esc(t('au_remove', { n: a.name }))}" data-act="removeApp" data-arg="${i}">${X_ICON}</button>
                </div>`).join('')}</div>
              <div class="row-flex" style="align-items:flex-end">
                <label class="field" style="flex:1 1 220px"><span>${esc(t('au_add_label'))}</span><input id="new-app" class="input mono" type="text" placeholder="Spotify.exe" value="${esc(S.newApp)}" data-input="newApp"></label>
                <button type="button" class="btn" data-act="addApp">${esc(t('add'))}</button>
              </div>
              <label class="row-flex" style="gap:10px;min-height:40px;flex-wrap:nowrap"><input class="check" type="checkbox" ${p.config.restoreOnExit ? 'checked' : ''} data-change="restoreOnExit"><span>${esc(t('au_restore'))}</span></label>
            </div>`}
          <div class="stack" style="gap:10px;padding-top:16px;border-top:1px solid var(--border)">
            <div class="row-flex"><h3 class="grow">${esc(t('pl_rules'))}</h3><button type="button" class="btn s" data-act="addRule">${esc(t('pl_add_rule'))}</button></div>
            ${p.rules.map((r) => `
              <div class="rule">
                <span class="small muted" style="width:40px">${esc(t('pl_when'))}</span>
                <select class="select" aria-label="${esc(t('pl_event'))}" data-change="ruleEvent" data-id="${esc(r.id)}">${['match_started', 'kill', 'knockdown', 'death', 'match_ended'].map((e) => `<option value="${e}" ${r.event === e ? 'selected' : ''}>${esc(t('ev_' + e))}</option>`).join('')}</select>
                <span class="small muted">→</span>
                <select class="select" aria-label="${esc(t('pl_action'))}" data-change="ruleAction" data-id="${esc(r.id)}">${actions.map((a) => `<option value="${a}" ${r.action === a ? 'selected' : ''}>${esc(t('a_' + a))}</option>`).join('')}</select>
                ${paramPh[r.action] ? `<input class="input" type="text" style="flex:1 1 140px" placeholder="${esc(t(paramPh[r.action]))}" aria-label="${esc(t(paramPh[r.action]))}" value="${esc(r.param)}" data-change="ruleParam" data-id="${esc(r.id)}">` : ''}
                ${r.action === 'set_text' ? `<input class="input" type="text" style="flex:1 1 140px" placeholder="${esc(t('ph_text'))}" aria-label="${esc(t('ph_text'))}" value="${esc(r.text || '')}" data-change="ruleText" data-id="${esc(r.id)}">` : ''}
                ${r.action === 'show_source' ? `<label class="row-flex small muted" style="gap:6px">${esc(t('pl_for'))}<input class="input num" type="number" min="0" max="60" step="0.5" style="width:70px" value="${r.durationSeconds || 0}" data-change="ruleDuration" data-id="${esc(r.id)}">s</label>` : ''}
                <span class="row-flex" style="gap:6px;flex-wrap:nowrap;margin-left:auto">
                  <label class="row-flex small muted" style="gap:6px;flex-wrap:nowrap">${esc(t('pl_after'))}<input class="input num" type="number" min="0" max="60" step="0.5" style="width:70px" value="${r.delaySeconds || 0}" data-change="ruleDelay" data-id="${esc(r.id)}">s</label>
                  <button type="button" class="icon-btn" aria-label="${esc(t('pl_remove_rule'))}" data-act="removeRule" data-arg="${esc(r.id)}">${X_ICON}</button>
                </span>
              </div>`).join('') || `<p class="small muted">${esc(t('pl_no_rules'))}</p>`}
            ${p.id === 'obs' ? `<span class="small muted">${esc(t('pl_placeholders'))}</span>` : ''}
            <span class="small muted">${esc(t('pl_apply'))}</span>
          </div>
        </section>
      </div>`;
  }

  function pageSetup() {
    const d = S.draft, isDirty = dirty();
    const sel = (id, key, opts) => `<select id="${id}" class="select" style="min-width:160px" data-change="draft" data-key="${key}">${opts.map(([v, l]) => `<option value="${v}" ${d[key] === v ? 'selected' : ''}>${esc(l)}</option>`).join('')}</select>`;
    const langs = [['de', 'Deutsch'], ['en', 'English']];
    return `
      <div class="narrow stack">
        <h1>${esc(t('nav_setup'))}</h1>
        <section class="card flush settings-group"><h2>${esc(t('sec_general'))}</h2>
          <div class="setting"><div class="text"><label for="gl">${esc(t('game_lang'))}</label><span class="desc">${esc(t('game_lang_d'))}</span></div><span class="next-start">${esc(t('apply_next'))}</span>${sel('gl', 'gameLanguage', langs)}</div>
          <div class="setting"><div class="text"><label for="ul">${esc(t('ui_lang'))}</label><span class="desc">${esc(t('ui_lang_d'))}</span></div>${sel('ul', 'uiLanguage', langs)}</div>
          <div class="setting"><div class="text"><span class="title">${esc(t('theme'))}</span></div>${seg([['system', t('th_system')], ['light', t('th_light')], ['dark', t('th_dark')]], d.theme, 'theme', 'radio')}</div>
          <div class="setting"><div class="text"><label for="nt">${esc(t('notify'))}</label><span class="desc">${esc(t('notify_d'))}</span></div><input id="nt" class="check" type="checkbox" ${d.notifyOnProblems ? 'checked' : ''} data-change="draftBool" data-key="notifyOnProblems"></div>
        </section>
        <section class="card flush settings-group"><h2>${esc(t('marker'))}</h2>
          <div class="row-flex" style="padding:14px 20px 20px;gap:24px;align-items:flex-start">
            <div class="stack" style="flex:1 1 340px;gap:14px">
              <span class="small muted">${esc(t('marker_d'))} <span class="next-start">${esc(t('apply_next'))}</span></span>
              <div class="row-flex" style="gap:8px">${SWATCHES.map(([hex, k]) => `<button type="button" class="swatch" aria-label="${esc(t(k))}" title="${esc(t(k))}" aria-pressed="${d.killMarkerColor.toUpperCase() === hex}" data-act="swatch" data-arg="${hex}"><span style="background:${hex}"></span></button>`).join('')}</div>
              <div class="row-flex" style="align-items:flex-end">
                <label class="field"><span>${esc(t('pick_color'))}</span><input type="color" value="${esc(d.killMarkerColor.toLowerCase())}" data-change="color" style="width:56px;height:40px;padding:2px;border-radius:6px;border:1px solid var(--border);background:var(--surface2)"></label>
                <label class="field"><span>Hex</span><input class="input mono" type="text" maxlength="7" value="${esc(S.hexDraft)}" data-change="hex" style="width:110px;text-transform:uppercase"></label>
              </div>
              ${colorIsBad(d.killMarkerColor) ? `<div class="notice warn" role="alert">${WARN_ICON}<span class="small">${esc(t('marker_warn'))}</span></div>` : ''}
            </div>
            <figure style="margin:0;display:flex;flex-direction:column;gap:6px;align-items:center">
              <svg class="marker-preview" width="200" height="200" viewBox="0 0 200 200" role="img" aria-label="${esc(t('preview_alt'))}">
                <rect y="120" width="200" height="80" fill="#4a4f45"/><rect x="128" y="62" width="44" height="58" fill="#545a63"/>
                <path d="M100 91v6M100 103v6M91 100h6M103 100h6" stroke="#f2f2f2" stroke-width="2" stroke-linecap="round"/>
                <path d="M84 84l9 9M116 84l-9 9M84 116l9-9M116 116l-9-9" stroke="${esc(d.killMarkerColor)}" stroke-width="4.5" stroke-linecap="round"/>
              </svg>
              <figcaption class="caption muted">${esc(t('preview'))}</figcaption>
            </figure>
          </div>
        </section>
        <section class="card flush settings-group"><h2>${esc(t('sec_detect'))}</h2>
          <div class="setting"><div class="text"><label for="rt">${esc(t('rate'))}</label><span class="desc">${esc(t('rate_d'))}</span></div><div class="row-flex"><input id="rt" class="range" type="range" min="5" max="10" step="1" value="${d.checksPerSecond}" data-input="rateLive" data-change="draftNum" data-key="checksPerSecond" style="width:180px"><strong class="num" id="rate-val" style="width:28px;text-align:right">${d.checksPerSecond}</strong></div></div>
          <div class="setting"><div class="text"><label for="hd">${esc(t('hud'))}</label><span class="desc">${esc(t('hud_d'))}</span></div><div class="row-flex" style="gap:8px"><input id="hd" class="input num" type="number" min="10" step="10" value="${d.matchEndWithoutHudSeconds}" data-change="draftNum" data-key="matchEndWithoutHudSeconds" style="width:90px"><span class="muted">${esc(t('seconds'))}</span></div></div>
          <div class="setting"><div class="text"><label for="dg">${esc(t('diag_mode'))}</label><span class="desc">${esc(t('diag_mode_d'))}</span></div><input id="dg" class="check" type="checkbox" ${d.diagnosticMode ? 'checked' : ''} data-change="draftBool" data-key="diagnosticMode"></div>
        </section>
        <section class="card flush settings-group"><h2>${esc(t('sec_updates'))}</h2>
          <div class="setting"><div class="text"><span class="title">${esc(t('up_version'))}</span><span class="desc">${esc(updateLine())}</span></div>
            <div class="row-flex" style="gap:8px">
              ${S.update && S.update.status === 'available' ? (S.update.installable ? `<button type="button" class="btn s primary" data-act="installUpdate">${esc(t('up_install'))}</button>` : `<button type="button" class="btn s" data-act="openReleasePage">${esc(t('up_release_page'))}</button>`) : ''}
              <button type="button" class="btn s" data-act="checkUpdates" ${S.update && ['checking', 'downloading', 'installing'].indexOf(S.update.status) >= 0 ? 'disabled' : ''}>${esc(t('up_check'))}</button>
            </div></div>
          <div class="setting"><div class="text"><label for="uc">${esc(t('up_auto_check'))}</label><span class="desc">${esc(t('up_auto_check_d'))}</span></div><input id="uc" class="check" type="checkbox" ${d.checkForUpdates ? 'checked' : ''} data-change="draftBool" data-key="checkForUpdates"></div>
          <div class="setting"><div class="text"><label for="ua">${esc(t('up_auto_install'))}</label><span class="desc">${esc(t('up_auto_install_d'))}</span></div><input id="ua" class="check" type="checkbox" ${d.autoInstallUpdates ? 'checked' : ''} data-change="draftBool" data-key="autoInstallUpdates"></div>
        </section>
        <div class="save-bar">
          <span class="small" style="flex:1 1 240px;color:${isDirty ? 'var(--warn)' : 'var(--muted)'}">${esc(isDirty ? t('unsaved') : S.justSaved ? t('saved') : t('all_saved'))}</span>
          <button type="button" class="btn" data-act="discard" ${isDirty ? '' : 'disabled'}>${esc(t('discard'))}</button>
          <button type="button" class="btn primary" data-act="saveSettings" ${isDirty ? '' : 'disabled'}>${esc(t('save'))}</button>
        </div>
      </div>`;
  }

  function pageDiag() {
    const list = S.diagnostics, n = list.length, i = Math.min(S.diagIdx, Math.max(0, n - 1)), c = list[i];
    const kindLabel = (k) => (k === 'skull_without_marker' ? t('case_skull') : t('case_marker'));
    const frames = lang() === 'de' ? ['−2,5 s', '−1,9 s', '−1,3 s', '−0,6 s', '0 s'] : ['−2.5 s', '−1.9 s', '−1.3 s', '−0.6 s', '0 s'];
    const marker = S.settings.killMarkerColor;
    return `
      <div class="row-flex"><h1 class="grow">${esc(t('nav_diag'))}</h1>
        <button type="button" class="btn" data-act="openDiagFolder">${esc(t('open_folder'))}</button>
        ${n && !S.confirmClear ? `<button type="button" class="btn danger" data-act="askClear">${esc(t('clear_folder'))}</button>` : ''}
        ${S.confirmClear ? `<div class="row-flex" style="gap:8px;padding:4px 4px 4px 12px;border-radius:8px;border:1px solid var(--err)"><span class="small">${esc(t('confirm_clear', { n }))}</span><button type="button" class="btn s danger-solid" data-act="doClear">${esc(t('delete'))}</button><button type="button" class="btn s" style="border:0" data-act="cancelClear">${esc(t('cancel'))}</button></div>` : ''}
      </div>
      ${!S.settings.diagnosticMode ? `<div class="card row-flex" style="flex-direction:row;padding:14px 18px"><span style="flex:1 1 300px">${esc(t('diag_off'))}</span><button type="button" class="btn primary" data-act="diagOn">${esc(t('turn_on'))}</button></div>` : ''}
      <p class="muted" style="max-width:75ch">${esc(t('diag_intro'))}</p>
      ${!n ? `<p class="empty" style="text-align:center;padding:32px">${esc(t('diag_empty'))}</p>` : `
      <div class="cols">
        <div class="stack-s" style="flex:1 1 240px;gap:6px">${list.map((x, k) => `
          <button type="button" class="select-card list-item" aria-current="${k === i}" style="flex-direction:column;align-items:flex-start;gap:2px;min-height:56px;padding:8px 12px" data-act="diagIdx" data-arg="${k}">
            <strong class="num">${esc(time(x.capturedAt))}</strong><span class="small muted">${esc(kindLabel(x.kind))}</span></button>`).join('')}</div>
        <section class="card" style="flex:3 1 480px;min-width:0;gap:14px">
          <div class="row-flex" style="align-items:baseline;gap:8px 16px">
            <h2 style="font-size:18px">${esc(kindLabel(c.kind))}</h2>
            <span class="muted">${esc(c.kind === 'skull_without_marker' ? t('case_skull_d') : t('case_marker_d'))}</span>
            <span class="grow"></span><span class="muted num">${esc(time(c.capturedAt))} · ${i + 1} ${esc(t('of'))} ${n}</span>
          </div>
          ${c.imageUrl ? `<img class="diag-shot" src="${esc(c.imageUrl)}" alt="${esc(kindLabel(c.kind))}">` : `
          <div class="game-view">
            <div class="stack-s" style="align-self:flex-end;width:260px;gap:6px"><span class="section-label">${esc(t('killfeed'))}</span>
              ${[64, 80, 52].slice(0, c.kind === 'skull_without_marker' ? 3 : 2).map((w) => `<div style="display:flex;align-items:center;gap:8px;height:22px;padding:0 8px;border-radius:3px;background:rgba(0,0,0,.45)"><span style="height:6px;border-radius:3px;background:#7fc4ff;width:${w}px"></span><span style="width:14px;height:6px;border-radius:2px;background:#8a919b"></span><span style="height:6px;border-radius:3px;background:#d8dce2;width:${140 - w}px"></span></div>`).join('')}
            </div>
            <div class="stack-s" style="gap:6px"><span class="section-label">${esc(t('crosshair'))}</span>
              <div class="frames">${frames.map((ft, k) => {
                const m = c.kind === 'marker_without_skull' && (k === 1 || k === 2), sk = c.kind === 'skull_without_marker' && k >= 3;
                return `<figure><svg viewBox="0 0 100 100" width="100%" role="img" aria-label="${esc(t('frame_alt', { t: ft }))}" style="max-width:120px">
                  <path d="M50 44v4M50 52v4M44 50h4M52 50h4" stroke="#f2f2f2" stroke-width="1.5" stroke-linecap="round"/>
                  ${m ? `<path d="M38 38l6 6M62 38l-6 6M38 62l6-6M62 62l-6-6" stroke="${esc(marker)}" stroke-width="3.5" stroke-linecap="round"/>` : ''}
                  ${sk ? '<g><circle cx="50" cy="78" r="7" fill="#f2f2f2"/><rect x="46" y="82" width="8" height="5" rx="1" fill="#f2f2f2"/><circle cx="47.5" cy="77.5" r="1.6" fill="#3b4148"/><circle cx="52.5" cy="77.5" r="1.6" fill="#3b4148"/></g>' : ''}
                </svg><figcaption class="num">${esc(ft)}</figcaption></figure>`; }).join('')}</div>
            </div>
          </div>
          <span class="caption muted">${esc(t('diag_schematic'))}</span>`}
          <div class="row-flex" style="gap:8px"><button type="button" class="btn" data-act="diagStep" data-arg="-1">${esc(t('prev'))}</button><button type="button" class="btn" data-act="diagStep" data-arg="1">${esc(t('next'))}</button></div>
        </section>
      </div>`}`;
  }

  function pageLog() {
    const rows = S.log.slice().reverse().filter((l) => S.logFilter === 'all' || (S.logFilter === 'events' ? l.level === 'EVENT' : l.level === 'WARN' || l.level === 'ERROR'));
    return `
      <div class="row-flex"><h1 class="grow">${esc(t('nav_log'))}</h1>
        ${seg([['all', t('f_all')], ['events', t('lf_events')], ['warn', t('lf_warn')]], S.logFilter, 'logFilter')}
        <label class="row-flex" style="gap:8px;min-height:40px"><input class="check" type="checkbox" style="width:18px;height:18px" ${S.autoRefresh ? 'checked' : ''} data-change="autoRefresh"><span>${esc(t('auto'))}</span></label>
        ${S.autoRefresh ? '' : `<button type="button" class="btn primary" data-act="refreshLog">${esc(S.pending.length ? t('refresh_n', { n: S.pending.length }) : t('refresh'))}</button>`}
      </div>
      <section class="log">${rows.map((l) => `<div class="log-line ${esc(l.level)}"><span class="muted num">${esc(time(l.at))}</span><span class="lvl">${esc(l.level)}</span><span class="wrap-any">${esc(l.message)}</span></div>`).join('')}</section>`;
  }

  const PAGES = { status: pageStatus, events: pageEvents, live: pageLive, sinks: pageSinks, plugins: pagePlugins, setup: pageSetup, diag: pageDiag, log: pageLog };

  // ---------- Darstellung ----------
  const $ = (id) => document.getElementById(id);
  function applyTheme() {
    const th = S.settings ? S.settings.theme : 'system';
    const dark = th === 'dark' || (th === 'system' && window.matchMedia('(prefers-color-scheme: dark)').matches);
    const root = document.documentElement;
    root.dataset.theme = dark ? 'dark' : 'light';
    root.lang = lang();
    if (S.settings) { root.style.setProperty('--marker', S.settings.killMarkerColor); root.style.setProperty('--marker-fg', fgFor(S.settings.killMarkerColor)); }
  }
  function render() {
    applyTheme();
    if (!S.loaded) return;
    const focusId = document.activeElement && document.activeElement.id;
    const probs = problems();
    const plugErr = S.plugins.filter((p) => p.enabled && p.status === 'error').length;
    const badges = { status: probs.length, sinks: S.sinks.filter((k) => k.lastError).length, plugins: plugErr };
    $('nav').innerHTML = NAV.map((id) => `<button type="button" class="nav-item" data-act="go" data-arg="${id}" ${S.page === id ? 'aria-current="page"' : ''}>
      <span class="bar"></span>${icon(id)}<span class="label">${esc(t('nav_' + id))}</span>${badges[id] ? `<span class="count">${badges[id]}</span>` : ''}</button>`).join('');
    $('nav').setAttribute('aria-label', t('nav_label'));
    const st = S.appState;
    $('side-status').className = 'side-status s-' + st;
    $('side-status').innerHTML = `<div class="row-flex" style="gap:8px;font-weight:600"><span class="dot" style="width:10px;height:10px"></span>${esc(t('st_' + st))}</div>
      ${S.update ? `<span class="caption muted" style="opacity:.7">MatchPulse ${esc(S.update.current)}</span>` : ''}
      <span class="caption muted">${esc(st === 'off' ? t('side_off') : st === 'game' || !S.match ? t('side_game', { r: S.settings.checksPerSecond }) : t('side_match', { t: hhmm(S.match.startedAt), k: roundKills().length }))}</span>`;
    $('toast').innerHTML = S.toast ? `<div class="toast">${OK_ICON}<span>${esc(S.toast)}</span></div>` : '';
    $('main-content').innerHTML = PAGES[S.page]();
    document.title = 'MatchPulse – ' + t('nav_' + S.page);
    if (focusId && $(focusId)) $(focusId).focus();
  }
  let toastTimer;
  function toast(msg) { S.toast = msg; clearTimeout(toastTimer); toastTimer = setTimeout(() => { S.toast = ''; render(); }, 3500); }

  // ---------- Aktionen ----------
  const curPlug = () => S.plugins.find((x) => x.id === S.plugSel) || S.plugins[0];
  const savePlug = (p) => { S.plugins = S.plugins.map((x) => (x.id === p.id ? p : x)); B.send('savePlugin', p); };
  const editPlug = (fn, id) => { const p = clone(id ? S.plugins.find((x) => x.id === id) : curPlug()); fn(p); savePlug(p); };
  const saveSinks = () => B.send('saveSinks', S.sinks);
  const validate = (kind, v) => {
    v = (v || '').trim();
    if (kind === 'webhook' && !/^https?:\/\/\S+$/i.test(v)) return t('invalid_url');
    if (kind === 'script' && !v) return t('invalid_empty');
    return '';
  };
  const setDraft = (k, v) => { S.draft = Object.assign({}, S.draft, { [k]: v }); S.justSaved = false; };

  const ACT = {
    go: (p) => { S.page = p; S.confirmClear = false; },
    openEvent: (k) => { S.page = 'events'; S.evFilter = 'all'; S.selEvent = k; S.copied = false; },
    selEvent: (k) => { S.selEvent = k; S.copied = false; },
    evFilter: (f) => { S.evFilter = f; },
    copyJson: () => { const e = S.events.find((x) => evKey(x) === S.selEvent); if (e && navigator.clipboard) navigator.clipboard.writeText(JSON.stringify(e, null, 2)); S.copied = true; },
    testAll: () => { B.send('sendTestEvent', { sinkIds: S.sinks.map((k) => k.id) }); toast(t('test_sent', { n: S.sinks.length })); },
    testSink: (id) => { B.send('sendTestEvent', { sinkIds: [id] }); toast(t('test_one')); },
    editSink: (id) => { const k = S.sinks.find((x) => x.id === id); S.editId = id; S.editDraft = k.target; S.editError = ''; },
    cancelEdit: () => { S.editId = null; S.editError = ''; },
    saveEdit: () => {
      const k = S.sinks.find((x) => x.id === S.editId); if (!k) return;
      const err = validate(k.kind, S.editDraft); if (err) { S.editError = err; return; }
      S.sinks = S.sinks.map((x) => (x.id === k.id ? Object.assign({}, x, { target: S.editDraft.trim(), lastSuccessAt: null, lastFailureAt: null, lastError: null }) : x));
      S.editId = null; saveSinks();
    },
    removeSink: (id) => { S.sinks = S.sinks.filter((x) => x.id !== id); saveSinks(); },
    addSink: () => {
      const err = validate(S.addKind, S.addTarget); if (err) { S.addError = err; return; }
      S.sinks = S.sinks.concat([{ id: uid('s'), kind: S.addKind, target: S.addTarget.trim(), lastSuccessAt: null, lastFailureAt: null, lastError: null }]);
      S.addTarget = ''; S.addError = ''; saveSinks();
    },
    plugSel: (id) => { S.plugSel = id; },
    plugOn: (id) => editPlug((p) => { p.enabled = true; }, id),
    testPlugin: (id) => B.send('testPlugin', { id }),
    addApp: () => { const n = S.newApp.trim(); if (!n) return; editPlug((p) => { p.config.apps.push({ name: n, volume: 30 }); }); S.newApp = ''; },
    removeApp: (i) => editPlug((p) => { p.config.apps.splice(Number(i), 1); }),
    addRule: () => editPlug((p) => { p.rules.push({ id: uid('r'), event: 'kill', action: p.id === 'obs' ? 'save_replay' : 'duck', param: '', text: '', delaySeconds: 0, durationSeconds: 0 }); }),
    removeRule: (id) => editPlug((p) => { p.rules = p.rules.filter((r) => r.id !== id); }),
    liveOn: () => { S.settings.liveMonitor = true; S.draft.liveMonitor = true; S.liveIdx = -1; B.send('setLiveMonitor', { enabled: true }); },
    liveStep: (d) => { const n = S.highlights.filter((h) => (S.match ? h.matchId === S.match.matchId : true)).length; if (!n) return; const cur = S.liveIdx < 0 ? n - 1 : S.liveIdx; S.liveIdx = (cur + Number(d) + n) % n; },
    liveIdx: (i) => { S.liveIdx = Number(i); },
    liveWindow: () => B.send('openLiveWindow'),
    simulateKill: () => B.simulateKill && B.simulateKill(),
    theme: (v) => setDraft('theme', v),
    swatch: (hex) => { setDraft('killMarkerColor', hex); S.hexDraft = hex; },
    discard: () => { S.draft = clone(S.settings); S.hexDraft = S.settings.killMarkerColor; },
    saveSettings: () => { S.settings = clone(S.draft); S.justSaved = true; B.send('saveSettings', S.settings); },
    diagOn: () => { S.settings.diagnosticMode = true; S.draft.diagnosticMode = true; B.send('saveSettings', S.settings); },
    diagIdx: (i) => { S.diagIdx = Number(i); },
    diagStep: (d) => { const n = S.diagnostics.length; if (n) S.diagIdx = (Math.min(S.diagIdx, n - 1) + Number(d) + n) % n; },
    openDiagFolder: () => B.send('openDiagnosticsFolder'),
    askClear: () => { S.confirmClear = true; },
    cancelClear: () => { S.confirmClear = false; },
    doClear: () => { S.confirmClear = false; S.diagIdx = 0; B.send('clearDiagnostics'); },
    logFilter: (f) => { S.logFilter = f; },
    checkUpdates: () => B.send('checkUpdates'),
    installUpdate: () => B.send('installUpdate'),
    openReleasePage: () => B.send('openReleasePage'),
    refreshLog: () => { S.log = S.log.concat(S.pending); S.pending = []; }
  };
  const CHG = {
    draft: (v, el) => setDraft(el.dataset.key, v),
    draftBool: (v, el) => setDraft(el.dataset.key, el.checked),
    draftNum: (v, el) => setDraft(el.dataset.key, Math.max(Number(el.min) || 0, Number(v) || 0)),
    color: (v) => { setDraft('killMarkerColor', v.toUpperCase()); S.hexDraft = v.toUpperCase(); },
    hex: (v) => { S.hexDraft = v; if (/^#[0-9a-f]{6}$/i.test(v)) setDraft('killMarkerColor', v.toUpperCase()); },
    addKind: (v) => { S.addKind = v; S.addError = ''; },
    autoRefresh: (v, el) => { S.autoRefresh = el.checked; if (el.checked) { S.log = S.log.concat(S.pending); S.pending = []; } },
    liveMonitor: (v, el) => { S.settings.liveMonitor = el.checked; S.draft.liveMonitor = el.checked; S.liveIdx = -1; B.send('setLiveMonitor', { enabled: el.checked }); },
    plugEnabled: (v, el) => editPlug((p) => { p.enabled = el.checked; }, el.dataset.id),
    obsHost: (v) => editPlug((p) => { p.config.host = v.trim(); }, 'obs'),
    obsPort: (v) => editPlug((p) => { p.config.port = Number(String(v).replace(/[^0-9]/g, '')) || 0; }, 'obs'),
    obsPw: (v) => editPlug((p) => { p.config.password = v; }, 'obs'),
    appVol: (v, el) => editPlug((p) => { p.config.apps[Number(el.dataset.idx)].volume = Number(v); }),
    restoreOnExit: (v, el) => editPlug((p) => { p.config.restoreOnExit = el.checked; }),
    ruleEvent: (v, el) => editPlug((p) => { p.rules.find((r) => r.id === el.dataset.id).event = v; }),
    ruleAction: (v, el) => editPlug((p) => { p.rules.find((r) => r.id === el.dataset.id).action = v; }),
    ruleParam: (v, el) => editPlug((p) => { p.rules.find((r) => r.id === el.dataset.id).param = v; }),
    ruleText: (v, el) => editPlug((p) => { p.rules.find((r) => r.id === el.dataset.id).text = v; }),
    ruleDelay: (v, el) => editPlug((p) => { p.rules.find((r) => r.id === el.dataset.id).delaySeconds = Math.min(60, Math.max(0, Number(v) || 0)); }),
    ruleDuration: (v, el) => editPlug((p) => { p.rules.find((r) => r.id === el.dataset.id).durationSeconds = Math.min(60, Math.max(0, Number(v) || 0)); })
  };
  const INP = {
    editDraft: (v) => { S.editDraft = v; },
    addTarget: (v) => { S.addTarget = v; },
    newApp: (v) => { S.newApp = v; },
    rateLive: (v) => { $('rate-val').textContent = v; },
    appVolLive: (v, el) => { $('vol-' + el.dataset.idx).textContent = v + ' %'; }
  };

  document.addEventListener('click', (e) => {
    const el = e.target.closest('[data-act]'); if (!el || el.disabled) return;
    const fn = ACT[el.dataset.act]; if (!fn) return;
    fn(el.dataset.arg, el); render();
  });
  document.addEventListener('change', (e) => {
    const el = e.target.closest('[data-change]'); if (!el) return;
    const fn = CHG[el.dataset.change]; if (fn) { fn(el.value, el); render(); }
  });
  document.addEventListener('input', (e) => {
    const el = e.target.closest('[data-input]'); if (!el) return;
    const fn = INP[el.dataset.input]; if (fn) fn(el.value, el);
  });
  document.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' && e.target.id === 'add-target') { ACT.addSink(); render(); }
    if (e.key === 'Enter' && e.target.id === 'new-app') { ACT.addApp(); render(); }
    if (e.key === 'Enter' && e.target.id && e.target.id.indexOf('edit-') === 0) { ACT.saveEdit(); render(); }
    if (e.key === 'Escape' && S.editId) { ACT.cancelEdit(); render(); }
  });
  window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', applyTheme);
  setInterval(() => { if (S.match) document.querySelectorAll('[data-tick="duration"]').forEach((n) => { n.textContent = duration(S.match.startedAt); }); }, 1000);

  // ---------- Nachrichten vom Host ----------
  const MSG = {
    snapshot: (p) => {
      Object.assign(S, { appState: p.appState, match: p.match, hostProblems: p.hostProblems || [], events: p.events || [], sinks: p.sinks || [], plugins: p.plugins || [],
        settings: p.settings, draft: clone(p.settings), hexDraft: p.settings.killMarkerColor, diagnostics: p.diagnostics || [], highlights: p.highlights || [], log: p.log || [], update: p.update || null, loaded: true });
      const lastKill = S.events.slice().reverse().find((e) => e.type === 'kill'); if (lastKill) S.selEvent = evKey(lastKill);
    },
    state: (p) => { S.appState = p.appState; S.match = p.match; },
    gameEvent: (ev) => { S.events = S.events.concat([ev]); if (ev.type === 'kill') S.liveIdx = -1; },
    killHighlight: (h) => { S.highlights = S.highlights.concat([h]); S.liveIdx = -1; },
    sinks: (list) => { S.sinks = list; },
    pluginStatus: ({ id, status, lastError }) => { S.plugins = S.plugins.map((p) => (p.id === id ? Object.assign({}, p, { status, lastError: lastError || null }) : p)); if (status !== 'error' && id === 'obs') toast(t('obs_ok')); },
    settings: (s) => { const wasDirty = dirty(); S.settings = s; if (!wasDirty) { S.draft = clone(s); S.hexDraft = s.killMarkerColor; } },
    diagnostics: (list) => { S.diagnostics = list; },
    hostProblems: (list) => { S.hostProblems = list; },
    log: (lines) => { if (S.autoRefresh) S.log = S.log.concat(lines); else S.pending = S.pending.concat(lines); },
    update: (u) => { S.update = u; },
    navigate: ({ page }) => { if (NAV.indexOf(page) >= 0) S.page = page; }
  };
  B.onMessage((m) => {
    const fn = MSG[m.type]; if (!fn) return;
    // Tippen nicht unterbrechen: Log-Zeilen nur einreihen, solange ein Textfeld den Fokus hat
    fn(m.payload);
    const a = document.activeElement;
    if (!(a && a.matches('input[type=text], input[type=password], input[type=number]'))) render();
  });

  const startPage = new URLSearchParams(location.search).get('page');
  if (startPage && NAV.indexOf(startPage) >= 0) S.page = startPage;
  render();
  B.send('ready');
})();
