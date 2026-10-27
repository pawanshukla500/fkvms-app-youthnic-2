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

  var hasToken = false;
  try {
    var rawUser = localStorage.getItem('fkuser') || localStorage.getItem('__youthnic_fkuser_backup');
    if (rawUser) {
      var u = JSON.parse(rawUser);
      hasToken = !!(u && (u.token || u.accessToken));
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
    hasToken: hasToken
  });
})()
