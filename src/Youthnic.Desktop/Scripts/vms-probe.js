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
    return (el.getAttribute('placeholder') || '').trim();
  }
  function containsIgnoreCase(str, needle) {
    return (str || '').toLowerCase().indexOf((needle || '').toLowerCase()) !== -1;
  }
  function findUsernameInput() {
    var email = document.getElementById('email');
    if (email && visible(email)) return email;
    var inputs = document.querySelectorAll('input:not([type="password"]):not([type="hidden"])');
    for (var i = 0; i < inputs.length; i++) {
      var el = inputs[i];
      if (containsIgnoreCase(el.name, 'email') ||
          containsIgnoreCase(el.id, 'email') ||
          containsIgnoreCase(el.name, 'user') ||
          containsIgnoreCase(el.id, 'user') ||
          containsIgnoreCase(el.getAttribute('placeholder'), 'user') ||
          containsIgnoreCase(el.getAttribute('placeholder'), 'email') ||
          containsIgnoreCase(accessibleName(el), 'user')) {
        return el;
      }
    }
    // Fallback: first visible text input on login view
    for (var j = 0; j < inputs.length; j++) {
      if (visible(inputs[j])) return inputs[j];
    }
    return null;
  }
  function findPasswordInput() {
    var passById = document.getElementById('password');
    if (passById && visible(passById)) return passById;
    var pass = document.querySelector('input[type="password"]');
    if (pass && visible(pass)) return pass;
    var inputs = document.querySelectorAll('input');
    for (var i = 0; i < inputs.length; i++) {
      var el = inputs[i];
      if (containsIgnoreCase(el.name, 'pass') ||
          containsIgnoreCase(el.id, 'pass') ||
          containsIgnoreCase(el.getAttribute('placeholder'), 'pass')) {
        return el;
      }
    }
    return null;
  }
  function findFeedInput() {
    var inputs = document.querySelectorAll('input, textarea');
    for (var i = 0; i < inputs.length; i++) {
      var el = inputs[i];
      if (containsIgnoreCase(el.getAttribute('placeholder'), 'feed') ||
          containsIgnoreCase(el.name, 'feed') ||
          containsIgnoreCase(el.id, 'feed')) {
        return el;
      }
    }
    // Fallback: if connect button is visible, find the visible text input
    var connectBtn = findButton('Connect');
    if (connectBtn && visible(connectBtn)) {
      for (var j = 0; j < inputs.length; j++) {
        if (visible(inputs[j]) && inputs[j].type !== 'hidden') return inputs[j];
      }
    }
    return null;
  }
  function findAwbInput() {
    var byId = document.getElementById('awbInput');
    if (byId && visible(byId)) return byId;
    var inputs = document.querySelectorAll('input');
    for (var i = 0; i < inputs.length; i++) {
      var el = inputs[i];
      if (containsIgnoreCase(el.getAttribute('placeholder'), 'awb') ||
          containsIgnoreCase(el.getAttribute('placeholder'), 'tracking') ||
          containsIgnoreCase(el.name, 'awb') ||
          containsIgnoreCase(el.id, 'awb')) {
        return el;
      }
    }
    return null;
  }
  function findButton(needle) {
    var els = document.querySelectorAll('button, [role="button"], input[type="submit"], input[type="button"]');
    for (var i = 0; i < els.length; i++) {
      var el = els[i];
      if (!visible(el)) continue;
      var text = el.tagName === 'INPUT' ? (el.value || '') : (el.innerText || accessibleName(el));
      if (containsIgnoreCase(text, needle)) return el;
    }
    return null;
  }

  var username = findUsernameInput();
  var password = findPasswordInput();
  var feed = findFeedInput();
  var awb = findAwbInput();
  var loginBtn = findButton('Login');
  var connectBtn = findButton('Connect');
  var recordBtn = findButton('Record');
  var stopBtn = findButton('Stop');
  var disconnectBtn = findButton('Disconnect');

  var isPacking = !!(awb && visible(awb)) ||
                  !!(stopBtn && visible(stopBtn)) ||
                  !!(disconnectBtn && visible(disconnectBtn)) ||
                  !!(recordBtn && !recordBtn.disabled && visible(recordBtn));

  var loginLoading = !!(loginBtn && (loginBtn.classList.contains('ant-btn-loading') || loginBtn.disabled));

  // Detect 403 / "Unauthorized Access" screen (e.g. invalid/expired session or lack of permissions)
  var unauthorizedEl = document.querySelector('.ant-result-403') || document.querySelector('.ant-result');
  var bodyText = document.body ? (document.body.innerText || '') : '';
  var isUnauthorized = !!(unauthorizedEl && (
    containsIgnoreCase(unauthorizedEl.innerText, 'Unauthorized Access') ||
    containsIgnoreCase(unauthorizedEl.innerText, 'not authorized') ||
    containsIgnoreCase(unauthorizedEl.innerText, '403')
  )) || (
    containsIgnoreCase(bodyText, 'Unauthorized Access') &&
    containsIgnoreCase(bodyText, 'not authorized to access this page')
  );

  var hasToken = false;
  if (!isUnauthorized) {
    try {
      var rawUser = localStorage.getItem('fkuser');
      if (rawUser) {
        var u = JSON.parse(rawUser);
        if (u && (u.token || u.accessToken)) {
          var token = u.token || u.accessToken;
          var isExpired = false;
          try {
            var parts = token.split('.');
            if (parts.length === 3) {
              var b64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
              var payload = JSON.parse(decodeURIComponent(escape(atob(b64))));
              if (payload && payload.exp && (payload.exp * 1000) < Date.now()) {
                isExpired = true;
              }
            }
          } catch (pe) {}
          hasToken = !isExpired;
        }
      }
    } catch (e) {}
  }

  // Extract Pending Requests and Error Requests counts from VMS DOM & Storage
  var pendingCount = 0;
  var errorCount = 0;

  try {
    var rawPending = localStorage.getItem('pendingCountVMS');
    if (rawPending !== null) {
      var pVal = parseInt(rawPending, 10);
      if (Number.isFinite(pVal) && pVal > 0) pendingCount = Math.max(pendingCount, pVal);
    }
  } catch (e) {}

  try {
    var rawError = localStorage.getItem('errorCountVMS');
    if (rawError !== null) {
      var eVal = parseInt(rawError, 10);
      if (Number.isFinite(eVal) && eVal > 0) errorCount = Math.max(errorCount, eVal);
    }
  } catch (e) {}

  // Check buttons (e.g. "Mass Retry -0", "Mass Retry - 2", "Mass Retry -2")
  try {
    var buttons = document.querySelectorAll('button, [role="button"], a, span');
    for (var bi = 0; bi < buttons.length; bi++) {
      var bText = (buttons[bi].innerText || buttons[bi].textContent || '').trim();
      var retryMatch = bText.match(/Mass\s+Retry\s*[-–—:]*\s*(\d+)/i);
      if (retryMatch) {
        var rNum = parseInt(retryMatch[1], 10);
        if (Number.isFinite(rNum)) {
          errorCount = Math.max(errorCount, rNum);
        }
      }
    }
  } catch (e) {}

  // Check floating badges on left of camera area and Ant Design badges
  try {
    var allBadges = document.querySelectorAll('div, span, button');
    for (var bi2 = 0; bi2 < allBadges.length; bi2++) {
      var bEl = allBadges[bi2];
      var bTextContent = (bEl.innerText || '').trim();
      if (/^\d+$/.test(bTextContent)) {
        var numVal = parseInt(bTextContent, 10);
        var svgOrI = bEl.querySelector('svg, i, .anticon');
        if (svgOrI || bEl.className) {
          var cls = ((bEl.className || '') + ' ' + (svgOrI ? (svgOrI.className || '') : '')).toLowerCase();
          var style = window.getComputedStyle(bEl);
          var bg = (style.backgroundColor || '').toLowerCase();
          var col = (style.color || '').toLowerCase();
          var isAmber = cls.indexOf('warning') !== -1 || bg.indexOf('250, 173, 20') !== -1 || col.indexOf('250, 173, 20') !== -1 || bg.indexOf('faad14') !== -1 || bg.indexOf('e6a23c') !== -1;
          var isRed = cls.indexOf('error') !== -1 || cls.indexOf('danger') !== -1 || bg.indexOf('245, 34, 45') !== -1 || bg.indexOf('255, 77, 79') !== -1 || bg.indexOf('f5222d') !== -1 || bg.indexOf('ff4d4f') !== -1;
          if (isAmber) pendingCount = Math.max(pendingCount, numVal);
          if (isRed) errorCount = Math.max(errorCount, numVal);
        }
      }
    }
  } catch (e) {}

  // Check headers / labels like "Pending Requests (X)" or "Error Requests (X)"
  try {
    var headers = document.querySelectorAll('h1, h2, h3, h4, h5, div, span, p');
    for (var hi = 0; hi < headers.length; hi++) {
      var hText = (headers[hi].innerText || '').trim();
      var errMatch = hText.match(/Error\s+Requests?\s*\(?(\d+)\)?/i);
      if (errMatch) errorCount = Math.max(errorCount, parseInt(errMatch[1], 10));
      var pendMatch = hText.match(/Pending\s+Requests?\s*\(?(\d+)\)?/i);
      if (pendMatch) pendingCount = Math.max(pendingCount, parseInt(pendMatch[1], 10));
    }
  } catch (e) {}

  return JSON.stringify({
    url: location.href,
    ready: document.readyState,
    username: !!(username && visible(username)),
    password: !!(password && visible(password)),
    feed: !!(feed && visible(feed)),
    awb: !!(awb && visible(awb)),
    feedFilled: feed ? !!(feed.value && feed.value.trim().length > 0) : false,
    loginVisible: !!(loginBtn && visible(loginBtn)),
    loginLoading: loginLoading,
    connectVisible: !!(connectBtn && visible(connectBtn)),
    recordVisible: isPacking,
    isPacking: isPacking,
    hasToken: hasToken,
    isUnauthorized: isUnauthorized,
    pendingCount: pendingCount,
    errorCount: errorCount
  });
})()
