(function () {
  'use strict';
  function visible(el) {
    if (!el) return false;
    var rect = el.getBoundingClientRect();
    return !!(el.offsetWidth || el.offsetHeight || rect.width || rect.height);
  }
  function accessibleName(el) {
    if (!el) return '';
    var direct = el.getAttribute('aria-label') || el.getAttribute('title') || el.getAttribute('name') || '';
    if (direct) return direct.trim();
    var id = el.id;
    if (id) {
      var lab = document.querySelector('label[for="' + id + '"]');
      if (lab && lab.innerText) return lab.innerText.trim();
    }
    if (el.tagName === 'BUTTON') return (el.innerText || '').trim();
    var wrapped = el.closest && el.closest('label');
    if (wrapped && wrapped.innerText) return wrapped.innerText.trim();
    // Placeholder fallback: Chromium uses placeholder as the accessible name
    // for unlabelled textboxes; mirror that for runtime discovery.
    return (el.getAttribute('placeholder') || '').trim();
  }
  function equalsIgnoreCase(a, b) {
    return (a || '').trim().toLowerCase() === (b || '').trim().toLowerCase();
  }
  function findInputByName(name) {
    var inputs = document.querySelectorAll('input, textarea');
    for (var i = 0; i < inputs.length; i++) {
      if (equalsIgnoreCase(accessibleName(inputs[i]), name)) return inputs[i];
    }
    return null;
  }
  function findInputByPlaceholder(needle) {
    var inputs = document.querySelectorAll('input');
    for (var i = 0; i < inputs.length; i++) {
      var placeholder = inputs[i].getAttribute('placeholder') || '';
      if (placeholder.toLowerCase().indexOf(needle.toLowerCase()) !== -1) return inputs[i];
    }
    return null;
  }
  function findButton(name) {
    var els = document.querySelectorAll('button, [role="button"], input[type="submit"], input[type="button"]');
    for (var i = 0; i < els.length; i++) {
      var el = els[i];
      var text = el.tagName === 'INPUT' ? (el.value || '') : (el.innerText || accessibleName(el));
      if (equalsIgnoreCase(text, name)) return el;
    }
    return null;
  }

  var username = findInputByName('Username');
  var password = findInputByName('Password');
  var feed = findInputByPlaceholder('Feed Id');
  var awb = findInputByPlaceholder('AWB');
  var loginBtn = findButton('Login');
  var connectBtn = findButton('Connect');
  var recordBtn = findButton('Record');

  return JSON.stringify({
    url: location.href,
    ready: document.readyState,
    username: !!username,
    password: !!password,
    feed: !!feed,
    awb: !!awb,
    feedFilled: feed ? !!feed.value : false,
    loginVisible: !!(loginBtn && !loginBtn.disabled && visible(loginBtn)),
    connectVisible: !!(connectBtn && !connectBtn.disabled && visible(connectBtn)),
    recordVisible: !!(recordBtn && !recordBtn.disabled && visible(recordBtn))
  });
})()
