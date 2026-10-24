(function () {
  'use strict';
  // Idempotent guard: never inject twice, never attach duplicate listeners.
  if (window.__youthnicScanObserver) return;
  window.__youthnicScanObserver = { version: 2 };

  var MIN_LEN = 4;
  var LISTENER_TAG = '__youthnicAwbListenersAttached';
  var lastEmittedAwb = '';
  var emitDebounceTimer = null;

  function findAwbInput() {
    var byId = document.getElementById('awbInput');
    if (byId) return byId;
    var inputs = document.querySelectorAll('input');
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
    return null;
  }

  function emitScan(value) {
    if (!value) return;
    var clean = (value || '').trim();
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
    }, delayMs || 250);
  }

  function attach(input) {
    if (!input || input[LISTENER_TAG]) return;
    input[LISTENER_TAG] = true;

    // Capture phase, passive: we observe, we never interfere or block VMS keystrokes.
    input.addEventListener('keydown', function (e) {
      var key = e.key || '';
      var code = e.keyCode || 0;
      if (key === 'Enter' || key === 'Tab' || code === 13 || code === 9) {
        if (emitDebounceTimer) clearTimeout(emitDebounceTimer);
        emitScan(input.value);
      }
    }, true);

    input.addEventListener('input', function () {
      var val = (input.value || '').trim();
      if (val.length >= 6) {
        scheduleEmit(val, 250);
      }
    }, true);

    input.addEventListener('change', function () {
      if (emitDebounceTimer) clearTimeout(emitDebounceTimer);
      emitScan(input.value);
    }, true);

    input.addEventListener('paste', function () {
      scheduleEmit(input.value, 150);
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

  // 150 ms polling fallback ensures scanners that do not trigger Enter or DOM mutations are never missed
  setInterval(function () {
    var input = findAwbInput();
    if (!input) {
      lastEmittedAwb = '';
      return;
    }
    attach(input);
    var current = (input.value || '').trim();
    if (current.length >= 6 && current !== lastEmittedAwb) {
      scheduleEmit(current, 250);
    } else if (current === '' && lastEmittedAwb !== '') {
      lastEmittedAwb = '';
    }
  }, 150);
})();
