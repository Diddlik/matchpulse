/* MatchPulse UI – Verbindung zum Programm (Host).
 *
 * In WebView2 laufen alle Nachrichten über window.chrome.webview:
 *   Host → UI:  CoreWebView2.PostWebMessageAsJson(json)
 *   UI → Host:  window.chrome.webview.postMessage(obj)  → WebMessageReceived
 * Außerhalb von WebView2 (Datei im Browser geöffnet) antwortet ein Mock mit
 * den Demo-Daten aus mock-data.js. Alle Nachrichten: siehe README.md.
 */
(function () {
  const listeners = [];
  const emit = (msg) => listeners.forEach((fn) => fn(msg));
  const wv = window.chrome && window.chrome.webview;

  if (wv) {
    wv.addEventListener('message', (e) => emit(typeof e.data === 'string' ? JSON.parse(e.data) : e.data));
    window.MP_BRIDGE = {
      isMock: false,
      onMessage: (fn) => listeners.push(fn),
      send: (type, payload) => wv.postMessage({ type, payload: payload === undefined ? null : payload })
    };
    return;
  }

  // ---------- Mock ----------
  // Wie in WebView2 werden Nachrichten kopiert, nicht als Referenz geteilt.
  const copy = (v) => (v === undefined ? v : JSON.parse(JSON.stringify(v)));
  const emitCopy = (msg) => emit(copy(msg));
  const db = JSON.parse(JSON.stringify(window.MP_MOCK || {}));
  const nowIso = () => new Date().toISOString();
  const later = (fn, ms) => setTimeout(fn, ms || 250);
  const log = (level, message) => emitCopy({ type: 'log', payload: [{ at: nowIso(), level, message }] });

  const handlers = {
    ready: () => later(() => emitCopy({ type: 'snapshot', payload: JSON.parse(JSON.stringify(db)) }), 0),
    saveSettings: (s) => { db.settings = s; log('INFO', 'Settings saved'); },
    saveSinks: (sinks) => { db.sinks = sinks; emitCopy({ type: 'sinks', payload: sinks }); log('INFO', 'Targets saved (' + sinks.length + ')'); },
    sendTestEvent: ({ sinkIds }) => later(() => {
      db.sinks = db.sinks.map((k) => {
        if (sinkIds.indexOf(k.id) < 0) return k;
        if (k.id === 's2') { log('WARN', 'Webhook ' + k.target + ': no response (timeout)'); return Object.assign({}, k, { lastFailureAt: nowIso(), lastError: { code: 'timeout', message: 'No response (timeout)' } }); }
        log('INFO', 'Test event delivered to ' + k.target);
        return Object.assign({}, k, { lastSuccessAt: nowIso(), lastError: null });
      });
      emitCopy({ type: 'sinks', payload: db.sinks });
    }, 400),
    savePlugin: (p) => { db.plugins = db.plugins.map((x) => (x.id === p.id ? p : x)); },
    testPlugin: ({ id }) => later(() => {
      const p = db.plugins.find((x) => x.id === id); if (!p) return;
      const ok = id !== 'obs' || (p.config.host && p.config.port > 0 && p.config.port < 65536);
      p.status = ok ? (id === 'obs' ? 'connected' : 'ready') : 'error';
      log(ok ? 'INFO' : 'WARN', id === 'obs' ? 'OBS WebSocket ' + (ok ? 'connected' : 'connection failed') + ' (' + p.config.host + ':' + p.config.port + ')' : 'Audio control ready');
      emitCopy({ type: 'pluginStatus', payload: { id, status: p.status } });
    }, 500),
    setLiveMonitor: ({ enabled }) => { db.settings.liveMonitor = enabled; log('INFO', enabled ? 'Live monitor on: kill images are captured' : 'Live monitor off'); },
    openLiveWindow: () => log('INFO', 'Live monitor window requested'),
    askLivePreview: () => {},
    openDiagnosticsFolder: () => log('INFO', 'Open diagnostics folder requested'),
    clearDiagnostics: () => { db.diagnostics = []; emitCopy({ type: 'diagnostics', payload: [] }); log('INFO', 'Diagnostics folder cleared'); },
    openMainWindow: () => {}
  };

  window.MP_BRIDGE = {
    isMock: true,
    onMessage: (fn) => listeners.push(fn),
    send: (type, payload) => { const h = handlers[type]; if (h) h(copy(payload) || {}); else console.warn('[MP mock] unbekannte Nachricht', type, payload); },
    // Nur für Tests im Browser: MP_BRIDGE.simulateKill() in der Konsole.
    simulateKill: () => {
      const m = db.match; const seq = db.events.filter((e) => m && e.matchId === m.matchId).length + 1;
      const ev = { type: 'kill', matchId: m ? m.matchId : null, sequence: seq, occurredAt: nowIso(), data: {} };
      db.events.push(ev); emitCopy({ type: 'gameEvent', payload: ev });
      log('EVENT', 'kill  matchId=' + (m ? m.matchId.slice(0, 8) : 'null') + '  seq=' + seq);
      if (db.settings.liveMonitor) emitCopy({ type: 'killHighlight', payload: { matchId: ev.matchId, sequence: seq, capturedAt: ev.occurredAt, imageUrl: null } });
    }
  };
})();
