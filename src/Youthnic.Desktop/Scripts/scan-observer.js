(function () {
  'use strict';
  // Idempotent guard: never inject twice, never attach duplicate listeners.
  if (window.__youthnicScanObserver) return;
  window.__youthnicScanObserver = { version: 3 };

  var MIN_LEN = 4;
  var LISTENER_TAG = '__youthnicAwbListenersAttached';
  var lastEmittedAwb = '';
  var emitDebounceTimer = null;

  function findAwbInput() {
    var byId = document.getElementById('awbInput');
    if (byId && byId.offsetParent !== null) return byId;
    var inputs = document.querySelectorAll('input:not([type="hidden"]):not([type="password"])');
    for (var i = 0; i < inputs.length; i++) {
      var el = inputs[i];
      var placeholder = (el.getAttribute('placeholder') || '').toUpperCase();
      var id = (el.id || '').toUpperCase();
      var name = (el.name || '').toUpperCase();
      if (placeholder.indexOf('AWB') !== -1 ||
          id.indexOf('AWB') !== -1 ||
          name.indexOf('AWB') !== -1 ||
          placeholder.indexOf('TRACKING') !== -1) {
        return el;
      }
    }
    // Fallback: visible text input on the packing view (e.g. adjacent to Disconnect / Stop buttons)
    for (var j = 0; j < inputs.length; j++) {
      var inp = inputs[j];
      if (inp.offsetParent !== null && (inp.type === 'text' || inp.type === 'search' || !inp.type)) {
        return inp;
      }
    }
    return null;
  }

  function emitScan(value) {
    if (!value) return;
    var clean = (value || '').trim().toUpperCase();
    if (clean.length < MIN_LEN) return;
    if (clean === lastEmittedAwb) return;
    lastEmittedAwb = clean;
    try {
      if (window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === 'function') {
        window.chrome.webview.postMessage({ type: 'scan', awb: clean });
      }
    } catch (err) {
      /* host not listening yet; scan is lost, VMS is unaffected */
    }
  }

  function scheduleEmit(value, delayMs) {
    if (emitDebounceTimer) clearTimeout(emitDebounceTimer);
    emitDebounceTimer = setTimeout(function () {
      emitScan(value);
    }, delayMs || 150);
  }

  function handleImmediateEmit(input) {
    if (emitDebounceTimer) clearTimeout(emitDebounceTimer);
    emitScan(input.value);
    // Zero-delay timeout ensures any scanner character committed on the next microtick is caught
    setTimeout(function () { emitScan(input.value); }, 0);
  }

  function attach(input) {
    if (!input || input[LISTENER_TAG]) return;
    input[LISTENER_TAG] = true;

    // Capture phase, passive: we observe, we never interfere or block VMS keystrokes.
    input.addEventListener('keydown', function (e) {
      var key = e.key || '';
      var code = e.keyCode || 0;
      if (key === 'Enter' || key === 'Tab' || code === 13 || code === 9) {
        handleImmediateEmit(input);
      }
    }, true);

    input.addEventListener('keyup', function (e) {
      var key = e.key || '';
      var code = e.keyCode || 0;
      if (key === 'Enter' || key === 'Tab' || code === 13 || code === 9) {
        handleImmediateEmit(input);
      }
    }, true);

    input.addEventListener('input', function () {
      var val = (input.value || '').trim();
      if (val.length >= 10) {
        // Fast-path for complete tracking numbers (AWBs) - 50ms latency
        scheduleEmit(val, 50);
      } else if (val.length >= 6) {
        scheduleEmit(val, 150);
      }
    }, true);

    input.addEventListener('change', function () {
      handleImmediateEmit(input);
    }, true);

    input.addEventListener('paste', function () {
      setTimeout(function () { emitScan(input.value); }, 0);
    }, true);
  }

  function attachToCurrent() {
    var input = findAwbInput();
    if (input) attach(input);
  }

  attachToCurrent();

  // Watch DOM mutations in case the SPA replaces the packing container
  var observer = new MutationObserver(function () {
    attachToCurrent();
  });
  var root = document.documentElement || document.body;
  if (root) observer.observe(root, { childList: true, subtree: true });

  // 100 ms polling fallback ensures scanners that do not trigger Enter or DOM mutations are never missed
  setInterval(function () {
    var input = findAwbInput();
    if (!input) {
      lastEmittedAwb = '';
      return;
    }
    attach(input);
    var current = (input.value || '').trim();
    if (current.length >= 10 && current !== lastEmittedAwb) {
      scheduleEmit(current, 50);
    } else if (current.length >= 6 && current !== lastEmittedAwb) {
      scheduleEmit(current, 150);
    } else if (current === '' && lastEmittedAwb !== '') {
      lastEmittedAwb = '';
    }
  }, 100);

  // Keep pointer / focus on the barcode scan bar during packing
  function ensureScanBarFocus() {
    var input = findAwbInput();
    if (!input) return;
    var active = document.activeElement;
    if (!active || active === document.body || active.tagName === 'BODY' || active.tagName === 'BUTTON') {
      try { input.focus(); } catch (e) {}
    }
  }

  setInterval(ensureScanBarFocus, 400);

  window.addEventListener('focus', function () {
    setTimeout(ensureScanBarFocus, 50);
  });

  document.addEventListener('click', function (e) {
    if (e.target && e.target.tagName !== 'INPUT' && e.target.tagName !== 'TEXTAREA' && e.target.tagName !== 'BUTTON') {
      setTimeout(ensureScanBarFocus, 100);
    }
  });

  // ---- Session Guardian & Keep-Alive ---------------------------------------
  // Automatically keeps the VMS JWT token fresh, protects credentials against
  // sudden network/timeout evictions, and ensures shifts run without logouts.
  (function initSessionGuardian() {
    var BACKUP_KEY = '__youthnic_fkuser_backup';

    // 1. Preserve and protect authentication tokens
    function backupToken() {
      try {
        var current = localStorage.getItem('fkuser');
        if (current && current.indexOf('token') !== -1) {
          localStorage.setItem(BACKUP_KEY, current);
        } else {
          // If fkuser was cleared unexpectedly, restore from backup
          var backup = localStorage.getItem(BACKUP_KEY);
          if (backup && (!current || current === '{}')) {
            localStorage.setItem('fkuser', backup);
          }
        }
      } catch (e) {}
    }

    // 2. Wrap localStorage.clear to protect backup from unexpected evictions
    try {
      if (!window.__youthnicClearWrapped && window.localStorage) {
        window.__youthnicClearWrapped = true;
        var originalClear = window.localStorage.clear.bind(window.localStorage);
        window.localStorage.clear = function () {
          var saved = window.localStorage.getItem(BACKUP_KEY) || window.localStorage.getItem('fkuser');
          originalClear();
          if (saved) {
            try { window.localStorage.setItem(BACKUP_KEY, saved); } catch (e) {}
          }
        };
      }
    } catch (e) {}

    // Backup immediately and on storage changes
    backupToken();
    window.addEventListener('storage', backupToken);

    // 3. Proactive Keep-Alive: Ping session every 2.5 minutes
    function performKeepAlive() {
      backupToken();
      try {
        var userStr = localStorage.getItem('fkuser') || localStorage.getItem(BACKUP_KEY);
        if (!userStr) return;
        var u = JSON.parse(userStr);
        if (!u || !u.token) return;

        // Ping lightweight authenticated user endpoint
        fetch('/api/user/menu', {
          method: 'GET',
          headers: {
            'Authorization': 'Bearer ' + u.token,
            'Content-Type': 'application/json'
          }
        }).then(function (res) {
          if (res.status === 401 && u.refreshToken) {
            // Proactively refresh JWT before Axios interceptor aborts
            return fetch('/auth/refresh', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({ refreshToken: u.refreshToken })
            }).then(function (r) {
              if (r.ok) return r.json();
              return null;
            }).then(function (data) {
              if (data && data.token) {
                localStorage.setItem('fkuser', JSON.stringify(data));
                localStorage.setItem(BACKUP_KEY, JSON.stringify(data));
              }
            });
          }
        }).catch(function () {});
      } catch (e) {}
    }

    // Run keep-alive every 150 seconds (2.5 minutes)
    setInterval(performKeepAlive, 150000);
    // Initial keepalive after 10 seconds
    setTimeout(performKeepAlive, 10000);

    // 4. Ensure authenticated operators stay on operator/vms
    setInterval(function () {
      try {
        var userStr = localStorage.getItem('fkuser') || localStorage.getItem(BACKUP_KEY);
        if (userStr && userStr.indexOf('token') !== -1) {
          var hash = location.hash || '';
          if (hash === '' || hash === '#/' || hash === '#/home') {
            location.hash = '#/operator/vms';
          }
        }
      } catch (e) {}
    }, 2000);
  })();
})();
