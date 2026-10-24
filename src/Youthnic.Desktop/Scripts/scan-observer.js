(function () {
  'use strict';
  // Idempotent guard: never inject twice, never attach duplicate listeners.
  if (window.__youthnicScanObserver) return;
  window.__youthnicScanObserver = { version: 1 };

  var MIN_LEN = 4;
  var LISTENER_TAG = '__youthnicAwbKeydown';

  function findAwbInput() {
    var inputs = document.querySelectorAll('input');
    for (var i = 0; i < inputs.length; i++) {
      var placeholder = inputs[i].getAttribute('placeholder') || '';
      if (placeholder.indexOf('AWB') !== -1) return inputs[i];
    }
    return null;
  }

  function attach(input) {
    if (!input || input[LISTENER_TAG]) return; // one listener per element, ever
    input[LISTENER_TAG] = true;
    // Capture phase, passive: we observe, we never interfere.
    // No preventDefault / stopPropagation — the VMS must receive every keystroke.
    input.addEventListener('keydown', function (e) {
      if (e.key !== 'Enter' && e.key !== 'Tab') return;
      var value = (input.value || '').trim();
      if (value.length < MIN_LEN) return;
      try {
        window.chrome.webview.postMessage({ type: 'scan', awb: value });
      } catch (err) {
        /* host not listening yet; scan is lost, VMS is unaffected */
      }
    }, true);
  }

  function attachToCurrent() {
    attach(findAwbInput());
  }

  attachToCurrent();

  // The SPA may replace the packing DOM; re-attach to whatever AWB input exists.
  var observer = new MutationObserver(function () {
    attachToCurrent();
  });
  var root = document.documentElement || document.body;
  if (root) observer.observe(root, { childList: true, subtree: true });
})();
