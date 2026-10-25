using System.IO;
using System.Text.Json;

namespace Youthnic.Desktop.Services;

/// <summary>
/// Builds the small JS snippets the automation service evaluates at runtime. Every
/// element is located by accessible name / placeholder text at execution time —
/// no CSS classes, no XPath. Typing is performed with realistic human keystroke
/// intervals and full keyboard event sequences so Ant-Design/React frameworks pick
/// the value up identically to manual operator typing.
/// </summary>
public static class VmsScripts
{
    private const string ProbeResourceName = "Youthnic.Desktop.Scripts.vms-probe.js";
    private static string? _probe;

    public static string Probe =>
        _probe ??= LoadProbe();

    private const string Helpers = """
        function __yVisible(el) {
          if (!el) return false;
          var rect = el.getBoundingClientRect();
          return !!(el.offsetWidth || el.offsetHeight || rect.width || rect.height);
        }
        function __yAccessibleName(el) {
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
        function __yEq(a, b) {
          return (a || '').trim().toLowerCase() === (b || '').trim().toLowerCase();
        }
        function __yFindInputByName(name) {
          var n = (name || '').trim().toLowerCase();
          if (n.indexOf('user') !== -1 || n.indexOf('email') !== -1) {
            var em = document.getElementById('email') ||
                     document.getElementById('username') ||
                     document.querySelector('input[name="username"], input[name="email"], input[type="email"], input[id="email"], input[id="username"]');
            if (em && __yVisible(em)) return em;
          }
          if (n.indexOf('pass') !== -1) {
            var pw = document.getElementById('password') ||
                     document.querySelector('input[type="password"], input[name="password"], input[id="password"]');
            if (pw && __yVisible(pw)) return pw;
          }
          var inputs = document.querySelectorAll('input:not([type="hidden"]), textarea');
          for (var i = 0; i < inputs.length; i++) {
            if (__yEq(__yAccessibleName(inputs[i]), name) && __yVisible(inputs[i])) return inputs[i];
          }
          for (var i = 0; i < inputs.length; i++) {
            var el = inputs[i];
            if (!__yVisible(el)) continue;
            var acc = (__yAccessibleName(el) || '').toLowerCase();
            var nm = (el.name || '').toLowerCase();
            var id = (el.id || '').toLowerCase();
            var ph = (el.getAttribute('placeholder') || '').toLowerCase();
            if (acc.indexOf(n) !== -1 || nm.indexOf(n) !== -1 || id.indexOf(n) !== -1 || ph.indexOf(n) !== -1) {
              return el;
            }
          }
          if (n.indexOf('user') !== -1 || n.indexOf('email') !== -1) {
            for (var j = 0; j < inputs.length; j++) {
              if (__yVisible(inputs[j]) && inputs[j].type !== 'password' && inputs[j].type !== 'hidden') return inputs[j];
            }
          }
          if (n.indexOf('pass') !== -1) {
            var p = document.querySelector('input[type="password"]');
            if (p && __yVisible(p)) return p;
          }
          return null;
        }
        function __yFindInputByPlaceholder(needle) {
          var inputs = document.querySelectorAll('input:not([type="hidden"]), textarea');
          var n = (needle || '').trim().toLowerCase();
          for (var i = 0; i < inputs.length; i++) {
            var el = inputs[i];
            if (!__yVisible(el)) continue;
            var ph = (el.getAttribute('placeholder') || '').toLowerCase();
            var nm = (el.name || '').toLowerCase();
            var id = (el.id || '').toLowerCase();
            var acc = (__yAccessibleName(el) || '').toLowerCase();
            if ((ph && ph.indexOf(n) !== -1) || nm.indexOf(n) !== -1 || id.indexOf(n) !== -1 || acc.indexOf(n) !== -1) {
              return el;
            }
          }
          if (n.indexOf('feed') !== -1) {
            for (var j = 0; j < inputs.length; j++) {
              if (__yVisible(inputs[j]) && inputs[j].type !== 'hidden') return inputs[j];
            }
          }
          return null;
        }
        function __yFindButton(name) {
          var els = document.querySelectorAll('button, [role="button"], input[type="submit"], input[type="button"]');
          var n = (name || '').trim().toLowerCase();
          for (var i = 0; i < els.length; i++) {
            var el = els[i];
            if (!__yVisible(el)) continue;
            var text = el.tagName === 'INPUT' ? (el.value || '') : (el.innerText || __yAccessibleName(el));
            if ((text || '').toLowerCase().indexOf(n) !== -1) return el;
          }
          return null;
        }
        function __yTypeHuman(el, text) {
          if (!el) return 'missing';
          if ((el.value || '') === text) return 'already';

          var proto = (typeof HTMLTextAreaElement !== 'undefined' && el instanceof HTMLTextAreaElement)
              ? window.HTMLTextAreaElement.prototype
              : window.HTMLInputElement.prototype;
          var setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;

          el.focus();
          if (setter) { setter.call(el, ''); } else { el.value = ''; }
          if (el._valueTracker) { el._valueTracker.setValue('__init__'); }
          el.dispatchEvent(new Event('input', { bubbles: true }));

          var idx = 0;
          function typeNext() {
            if (idx >= text.length) {
              el.dispatchEvent(new Event('change', { bubbles: true }));
              try { el.blur(); } catch (e) {}
              return;
            }
            var char = text[idx++];
            var current = (el.value || '') + char;

            var kd = new KeyboardEvent('keydown', { key: char, code: 'Key' + char.toUpperCase(), bubbles: true, cancelable: true });
            var kp = new KeyboardEvent('keypress', { key: char, charCode: char.charCodeAt(0), bubbles: true, cancelable: true });
            el.dispatchEvent(kd);
            el.dispatchEvent(kp);

            if (setter) { setter.call(el, current); } else { el.value = current; }
            if (el._valueTracker) { el._valueTracker.setValue(''); }

            var inp = new InputEvent('input', { data: char, inputType: 'insertText', bubbles: true });
            el.dispatchEvent(inp);

            var ku = new KeyboardEvent('keyup', { key: char, code: 'Key' + char.toUpperCase(), bubbles: true, cancelable: true });
            el.dispatchEvent(ku);

            var delay = 35 + Math.floor(Math.random() * 25);
            setTimeout(typeNext, delay);
          }
          typeNext();
          return 'typing';
        }
        """;

    /// <summary>
    /// Types the value smoothly character-by-character with realistic cadence into the input
    /// with the given accessible name. Returns 'already' when the field already holds the value,
    /// 'missing' if absent, or 'typing' when typing starts.
    /// </summary>
    public static string FillByName(string accessibleName, string value)
    {
        var nameJson = JsonSerializer.Serialize(accessibleName);
        var valueJson = JsonSerializer.Serialize(value);
        return Helpers +
            "(function() {\n" +
            "  var el = __yFindInputByName(" + nameJson + ");\n" +
            "  return __yTypeHuman(el, " + valueJson + ");\n" +
            "})();";
    }

    /// <summary>
    /// Types the value smoothly character-by-character into the input whose placeholder
    /// contains the given text (e.g. "Feed").
    /// </summary>
    public static string FillByPlaceholder(string placeholderNeedle, string value)
    {
        var needleJson = JsonSerializer.Serialize(placeholderNeedle);
        var valueJson = JsonSerializer.Serialize(value);
        return Helpers +
            "(function() {\n" +
            "  var el = __yFindInputByPlaceholder(" + needleJson + ");\n" +
            "  return __yTypeHuman(el, " + valueJson + ");\n" +
            "})();";
    }

    /// <summary>
    /// Clicks the button with the given accessible name if it exists, enabled and visible,
    /// dispatching mousedown, mouseup, click, and form.requestSubmit().
    /// </summary>
    public static string ClickByName(string buttonName)
    {
        var nameJson = JsonSerializer.Serialize(buttonName);
        return Helpers +
            "(function() {\n" +
            "  var btn = __yFindButton(" + nameJson + ");\n" +
            "  if (!btn) return 'missing';\n" +
            "  if (!__yVisible(btn)) return 'hidden';\n" +
            "  if (btn.disabled) { btn.removeAttribute('disabled'); btn.disabled = false; }\n" +
            "  try { btn.focus(); } catch (e) {}\n" +
            "  btn.dispatchEvent(new MouseEvent('mousedown', { bubbles: true, cancelable: true, view: window }));\n" +
            "  btn.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true, view: window }));\n" +
            "  btn.click();\n" +
            "  try {\n" +
            "    var form = btn.form || (btn.closest && btn.closest('form')) || document.querySelector('form.login-form');\n" +
            "    if (form && typeof form.requestSubmit === 'function') {\n" +
            "      form.requestSubmit(btn);\n" +
            "    }\n" +
            "  } catch (e) {}\n" +
            "  return 'clicked';\n" +
            "})();";
    }

    private static string LoadProbe()
    {
        var assembly = typeof(VmsScripts).Assembly;
        using var stream = assembly.GetManifestResourceStream(ProbeResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{ProbeResourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
