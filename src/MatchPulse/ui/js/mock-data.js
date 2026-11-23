/* MatchPulse UI – Demo-Daten.
   Wird nur benutzt, wenn die Seite NICHT in WebView2 läuft (z. B. direkt im Browser geöffnet).
   Die Form entspricht genau der Nachricht "snapshot" aus README.md. */
(function () {
  const now = Date.now();
  const ago = (min, sec) => new Date(now - (min * 60 + (sec || 0)) * 1000).toISOString();
  const A = 'a3c0e117-58d2-4b6e-8f10-2c9d4e7a1b03';
  const B = '91fdb6ce-4f2a-4e0b-9c1d-7a3e52b8f0aa';

  window.MP_MOCK = {
    appState: 'match',
    match: { matchId: B, startedAt: ago(13, 28) },
    hostProblems: [],
    events: [
      { type: 'kill', matchId: null, sequence: 1, occurredAt: ago(58, 18), data: {} },
      { type: 'match_started', matchId: A, sequence: 1, occurredAt: ago(47, 20), data: {} },
      { type: 'kill', matchId: A, sequence: 2, occurredAt: ago(40, 57), data: {} },
      { type: 'match_ended', matchId: A, sequence: 3, occurredAt: ago(30, 28), data: { result: 'defeat', reason: 'end_screen' } },
      { type: 'match_started', matchId: B, sequence: 1, occurredAt: ago(13, 28), data: {} },
      { type: 'kill', matchId: B, sequence: 2, occurredAt: ago(9, 50), data: {} },
      { type: 'kill', matchId: B, sequence: 3, occurredAt: ago(7, 23), data: {} },
      { type: 'kill', matchId: B, sequence: 4, occurredAt: ago(1, 35), data: {} }
    ],
    sinks: [
      { id: 's1', kind: 'webhook', target: 'http://localhost:8080/matchpulse', lastSuccessAt: ago(1, 35), lastFailureAt: null, lastError: null },
      { id: 's2', kind: 'webhook', target: 'https://stats.example.net/hooks/bf', lastSuccessAt: ago(13, 28), lastFailureAt: ago(1, 34), lastError: { code: 'timeout', message: 'No response (timeout)' } },
      { id: 's3', kind: 'script', target: 'C:\\Tools\\MatchPulse\\clip-kill.cmd --obs', lastSuccessAt: ago(1, 35), lastFailureAt: null, lastError: null }
    ],
    plugins: [
      { id: 'obs', enabled: true, status: 'connected',
        config: { host: 'localhost', port: 4455, password: 'matchpulse' },
        rules: [
          { id: 'r1', event: 'match_started', action: 'start_recording', param: '' },
          { id: 'r2', event: 'kill', action: 'save_replay', param: '' },
          { id: 'r3', event: 'match_ended', action: 'stop_recording', param: '' }
        ] },
      { id: 'mqtt', enabled: true, status: 'connected', lastError: null,
        config: { host: 'homeassistant.local', port: 1883, useTls: false, username: 'matchpulse', password: 'secret', topicPrefix: 'matchpulse', qos: 1 },
        rules: [] },
      { id: 'audio', enabled: false, status: 'ready',
        config: { apps: [{ name: 'Spotify.exe', volume: 20 }, { name: 'chrome.exe', volume: 40 }], restoreOnExit: true },
        rules: [
          { id: 'r4', event: 'match_started', action: 'duck', param: '' },
          { id: 'r5', event: 'match_ended', action: 'restore', param: '' }
        ] }
    ],
    settings: {
      gameLanguage: 'de', uiLanguage: 'de', theme: 'system', killMarkerColor: '#FF3030',
      checksPerSecond: 10, matchEndWithoutHudSeconds: 120, diagnosticMode: true, notifyOnProblems: true, liveMonitor: false
    },
    // Kill-Highlights für den Live-Monitor. imageUrl = null zeigt den Platzhalter.
    highlights: [
      { matchId: B, sequence: 2, capturedAt: ago(9, 50), imageUrl: null },
      { matchId: B, sequence: 3, capturedAt: ago(7, 23), imageUrl: null },
      { matchId: B, sequence: 4, capturedAt: ago(1, 35), imageUrl: null }
    ],
    diagnostics: [
      { id: 'd1', capturedAt: ago(7, 21), kind: 'marker_without_skull', imageUrl: null },
      { id: 'd2', capturedAt: ago(15, 46), kind: 'skull_without_marker', imageUrl: null },
      { id: 'd3', capturedAt: ago(36, 12), kind: 'skull_without_marker', imageUrl: null },
      { id: 'd4', capturedAt: ago(40, 59), kind: 'marker_without_skull', imageUrl: null }
    ],
    log: [
      { at: ago(58, 32), level: 'INFO', message: 'bf6.exe detected, screen capture started (10 checks/s)' },
      { at: ago(58, 18), level: 'EVENT', message: 'kill  matchId=null' },
      { at: ago(47, 20), level: 'EVENT', message: 'match_started  matchId=a3c0e117  seq=1' },
      { at: ago(40, 57), level: 'EVENT', message: 'kill  matchId=a3c0e117  seq=2' },
      { at: ago(36, 12), level: 'INFO', message: 'Diagnostic image saved: skull without kill marker' },
      { at: ago(30, 28), level: 'EVENT', message: 'match_ended  matchId=a3c0e117  seq=3  result=defeat  reason=end_screen' },
      { at: ago(24, 49), level: 'WARN', message: 'Screen capture failed, retrying' },
      { at: ago(24, 48), level: 'INFO', message: 'Screen capture recovered' },
      { at: ago(13, 28), level: 'EVENT', message: 'match_started  matchId=91fdb6ce  seq=1' },
      { at: ago(9, 50), level: 'EVENT', message: 'kill  matchId=91fdb6ce  seq=2' },
      { at: ago(9, 45), level: 'WARN', message: 'Webhook https://stats.example.net/hooks/bf: no response (timeout)' },
      { at: ago(7, 23), level: 'EVENT', message: 'kill  matchId=91fdb6ce  seq=3' },
      { at: ago(7, 21), level: 'INFO', message: 'Diagnostic image saved: kill marker without skull' },
      { at: ago(1, 35), level: 'EVENT', message: 'kill  matchId=91fdb6ce  seq=4' },
      { at: ago(1, 34), level: 'WARN', message: 'Webhook https://stats.example.net/hooks/bf: no response (timeout)' }
    ]
  };
})();
