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
})();
