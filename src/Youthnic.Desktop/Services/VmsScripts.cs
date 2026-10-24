using System.IO;
using System.Text.Json;

namespace Youthnic.Desktop.Services;

/// <summary>
/// Builds the small JS snippets the automation service evaluates at runtime. Every
/// element is located by accessible name / placeholder text at execution time —
/// no CSS classes, no XPath. Fill uses the native HTMLInputElement value setter
/// plus React valueTracker reset and bubbling input/change events so Ant-Design/React
/// frameworks pick the value up exactly as if the user typed it.
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
          var inputs = document.querySelectorAll('input:not([type="hidden"]), textarea');
          var n = (name || '').trim().toLowerCase();
          for (var i = 0; i < inputs.length; i++) {
            if (__yEq(__yAccessibleName(inputs[i]), name)) return inputs[i];
          }
          for (var i = 0; i < inputs.length; i++) {
            var el = inputs[i];
            var acc = (__yAccessibleName(el) || '').toLowerCase();
            var nm = (el.name || '').toLowerCase();
            var id = (el.id || '').toLowerCase();
            var ph = (el.getAttribute('placeholder') || '').toLowerCase();
            if (acc.indexOf(n) !== -1 || nm.indexOf(n) !== -1 || id.indexOf(n) !== -1 || ph.indexOf(n) !== -1) {
              return el;
            }
          }
          if (n.indexOf('pass') !== -1) {
            var p = document.querySelector('input[type="password"]');
            if (p) return p;
          }
          return null;
        }
        function __yFindInputByPlaceholder(needle) {
          var inputs = document.querySelectorAll('input, textarea');
          var n = (needle || '').trim().toLowerCase();
          for (var i = 0; i < inputs.length; i++) {
            var ph = (inputs[i].getAttribute('placeholder') || '').toLowerCase();
            var nm = (inputs[i].name || '').toLowerCase();
            var id = (inputs[i].id || '').toLowerCase();
            if ((ph && ph.indexOf(n) !== -1) || nm.indexOf(n) !== -1 || id.indexOf(n) !== -1) return inputs[i];
          }
          return null;
        }
        function __yFindButton(name) {
          var els = document.querySelectorAll('button, [role="button"], input[type="submit"], input[type="button"]');
          var n = (name || '').trim().toLowerCase();
          for (var i = 0; i < els.length; i++) {
            var el = els[i];
            var text = el.tagName === 'INPUT' ? (el.value || '') : (el.innerText || __yAccessibleName(el));
            if ((text || '').toLowerCase().indexOf(n) !== -1) return el;
          }
          return null;
        }
        """;

    /// <summary>
    /// Fills the input with the given accessible name. Returns 'already' when the
    /// field already holds the desired value (operator did it manually — never redo).
    /// </summary>
    public static string FillByName(string accessibleName, string value)
    {
        var nameJson = JsonSerializer.Serialize(accessibleName);
        var valueJson = JsonSerializer.Serialize(value);
        return Helpers +
            "(function() {\n" +
            "  var el = __yFindInputByName(" + nameJson + ");\n" +
            "  if (!el) return 'missing';\n" +
            "  if ((el.value || '') === " + valueJson + ") return 'already';\n" +
            "  var proto = (typeof HTMLTextAreaElement !== 'undefined' && el instanceof HTMLTextAreaElement) ? window.HTMLTextAreaElement.prototype : window.HTMLInputElement.prototype;\n" +
            "  var setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;\n" +
            "  el.focus();\n" +
            "  if (setter) { setter.call(el, " + valueJson + "); } else { el.value = " + valueJson + "; }\n" +
            "  if (el._valueTracker) { el._valueTracker.setValue(''); }\n" +
            "  el.dispatchEvent(new Event('input', { bubbles: true }));\n" +
            "  el.dispatchEvent(new Event('change', { bubbles: true }));\n" +
            "  return 'filled';\n" +
            "})();";
    }

    /// <summary>
    /// Fills the input whose placeholder contains the given text (e.g. "Feed Id").
    /// Returns the same status codes as <see cref="FillByName"/>.
    /// </summary>
    public static string FillByPlaceholder(string placeholderNeedle, string value)
    {
        var needleJson = JsonSerializer.Serialize(placeholderNeedle);
        var valueJson = JsonSerializer.Serialize(value);
        return Helpers +
            "(function() {\n" +
            "  var el = __yFindInputByPlaceholder(" + needleJson + ");\n" +
            "  if (!el) return 'missing';\n" +
            "  if ((el.value || '') === " + valueJson + ") return 'already';\n" +
            "  var proto = (typeof HTMLTextAreaElement !== 'undefined' && el instanceof HTMLTextAreaElement) ? window.HTMLTextAreaElement.prototype : window.HTMLInputElement.prototype;\n" +
            "  var setter = Object.getOwnPropertyDescriptor(proto, 'value')?.set;\n" +
            "  el.focus();\n" +
            "  if (setter) { setter.call(el, " + valueJson + "); } else { el.value = " + valueJson + "; }\n" +
            "  if (el._valueTracker) { el._valueTracker.setValue(''); }\n" +
            "  el.dispatchEvent(new Event('input', { bubbles: true }));\n" +
            "  el.dispatchEvent(new Event('change', { bubbles: true }));\n" +
            "  return 'filled';\n" +
            "})();";
    }

    /// <summary>Clicks the button with the given accessible name if it exists, enabled and visible.</summary>
    public static string ClickByName(string buttonName)
    {
        var nameJson = JsonSerializer.Serialize(buttonName);
        return Helpers +
            "(function() {\n" +
            "  var btn = __yFindButton(" + nameJson + ");\n" +
            "  if (!btn) return 'missing';\n" +
            "  if (!__yVisible(btn)) return 'hidden';\n" +
            "  if (btn.disabled) { btn.removeAttribute('disabled'); btn.disabled = false; }\n" +
            "  btn.click();\n" +
            "  if (btn.form && typeof btn.form.requestSubmit === 'function') {\n" +
            "    try { btn.form.requestSubmit(); } catch (e) {}\n" +
            "  }\n" +
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
