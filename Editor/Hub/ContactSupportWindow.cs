// Editor/Hub/ContactSupportWindow.cs
// Standalone Contact Support window — opens as its own OS-level EditorWindow.

using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;

namespace GDCodeShield
{
    public class ContactSupportWindow : EditorWindow
    {
        // ── Palette ───────────────────────────────────────────────────────────
        private static readonly Color C_BG     = new Color32(12,  12,  12,  255);
        private static readonly Color C_SURF   = new Color32(22,  22,  22,  255);
        private static readonly Color C_BORDER = new Color32(55,  55,  55,  255);
        private static readonly Color C_ACCENT = new Color32(255, 211, 0,   255);
        private static readonly Color C_TEXT   = new Color32(240, 240, 240, 255);
        private static readonly Color C_MUTED  = new Color32(140, 140, 140, 255);
        private static readonly Color C_GREEN  = new Color32(80,  200, 100, 255);
        private static readonly Color C_RED    = new Color32(220,  70,  70, 255);

        // ── State ─────────────────────────────────────────────────────────────
        private string _subject = "";
        private string _body    = "";
        private string _status  = "";
        private bool   _sending = false;

        private const string SUPPORT_EMAIL = "ghulammohyuddin@gamedistrict.co";
        private const int    MAX_CHARS     = 1000;

        // ── Open ──────────────────────────────────────────────────────────────
        public static void Open()
        {
            var w = GetWindow<ContactSupportWindow>(true, "Contact Support", true);
            w.minSize = new Vector2(520, 480);
            w.maxSize = new Vector2(520, 480);
            w.ShowUtility(); // floating utility window — always on top, own input
        }

        // ── OnGUI ─────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            // Background
            EditorGUI.DrawRect(new Rect(0, 0, position.width, position.height), C_BG);

            float x = 24f;
            float w = position.width - 48f;
            float y = 20f;

            // Top accent bar
            EditorGUI.DrawRect(new Rect(0, 0, position.width, 3), C_ACCENT);

            // ── Title ─────────────────────────────────────────────────────────
            GUI.Label(new Rect(x, y, w, 28),
                "Contact Support",
                new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 17,
                    normal   = { textColor = C_TEXT }
                });
            y += 30;

            GUI.Label(new Rect(x, y, w, 16),
                $"Send a message to the GD CodeShield team",
                new GUIStyle(EditorStyles.label)
                {
                    fontSize = 9,
                    normal   = { textColor = new Color(C_MUTED.r, C_MUTED.g, C_MUTED.b, 0.65f) }
                });
            y += 26;

            // Divider
            EditorGUI.DrawRect(new Rect(x, y, w, 1),
                new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.25f));
            y += 16;

            // ── Subject ───────────────────────────────────────────────────────
            GUI.Label(new Rect(x, y, w, 14),
                "SUBJECT",
                new GUIStyle(EditorStyles.label)
                {
                    fontSize  = 8,
                    fontStyle = FontStyle.Bold,
                    normal    = { textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.85f) }
                });
            y += 16;

            EditorGUI.DrawRect(new Rect(x, y, w, 30), new Color(1, 1, 1, 0.05f));
            EditorGUI.DrawRect(new Rect(x, y + 29, w, 1),
                new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.45f));

            GUI.SetNextControlName("Subject");
            _subject = EditorGUI.TextField(
                new Rect(x + 4, y + 6, w - 8, 18),
                _subject,
                new GUIStyle(EditorStyles.textField)
                {
                    fontSize = 11,
                    normal   = { textColor = C_TEXT,
                                 background = MakeTex(1, 1, Color.clear) },
                    focused  = { textColor = C_TEXT,
                                 background = MakeTex(1, 1, Color.clear) },
                    active   = { textColor = C_TEXT,
                                 background = MakeTex(1, 1, Color.clear) }
                });
            y += 40;

            // ── Message ───────────────────────────────────────────────────────
            GUI.Label(new Rect(x, y, w, 14),
                "MESSAGE",
                new GUIStyle(EditorStyles.label)
                {
                    fontSize  = 8,
                    fontStyle = FontStyle.Bold,
                    normal    = { textColor = new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.85f) }
                });
            y += 16;

            float bodyH = 220f;
            EditorGUI.DrawRect(new Rect(x, y, w, bodyH), new Color(1, 1, 1, 0.05f));
            EditorGUI.DrawRect(new Rect(x, y + bodyH - 1, w, 1),
                new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.45f));

            GUI.SetNextControlName("Body");
            string newBody = EditorGUI.TextArea(
                new Rect(x + 4, y + 6, w - 8, bodyH - 26),
                _body,
                new GUIStyle(EditorStyles.textArea)
                {
                    fontSize = 11,
                    wordWrap = true,
                    normal   = { textColor = C_TEXT,
                                 background = MakeTex(1, 1, Color.clear) },
                    focused  = { textColor = C_TEXT,
                                 background = MakeTex(1, 1, Color.clear) },
                    active   = { textColor = C_TEXT,
                                 background = MakeTex(1, 1, Color.clear) }
                });
            _body = newBody.Length > MAX_CHARS
                ? newBody.Substring(0, MAX_CHARS)
                : newBody;

            // Character counter
            int  chars    = _body.Length;
            Color charCol = chars > 950 ? C_RED
                          : chars > 800 ? new Color(1f, 0.65f, 0.15f)
                          : new Color(C_MUTED.r, C_MUTED.g, C_MUTED.b, 0.5f);
            GUI.Label(new Rect(x, y + bodyH - 18, w - 4, 14),
                $"{chars} / {MAX_CHARS}",
                new GUIStyle(EditorStyles.label)
                {
                    fontSize  = 8,
                    alignment = TextAnchor.MiddleRight,
                    normal    = { textColor = charCol }
                });
            y += bodyH + 14;

            // ── Status ────────────────────────────────────────────────────────
            if (!string.IsNullOrEmpty(_status))
            {
                bool isErr = _status.StartsWith("✗");
                EditorGUI.DrawRect(new Rect(x, y, w, 28),
                    new Color(isErr ? C_RED.r : C_GREEN.r,
                              isErr ? C_RED.g : C_GREEN.g,
                              isErr ? C_RED.b : C_GREEN.b, 0.1f));
                EditorGUI.DrawRect(new Rect(x, y, 3, 28),
                    isErr ? C_RED : C_GREEN);
                GUI.Label(new Rect(x + 10, y + 6, w - 14, 16), _status,
                    new GUIStyle(EditorStyles.label)
                    {
                        fontSize = 10,
                        normal   = { textColor = isErr ? C_RED : C_GREEN }
                    });
                y += 36;
            }

            // ── Buttons ───────────────────────────────────────────────────────
            bool canSend = !_sending
                && !string.IsNullOrWhiteSpace(_subject)
                && !string.IsNullOrWhiteSpace(_body);

            // Cancel
            var cancelBtn = new Rect(x, y, 90, 34);
            bool ch = cancelBtn.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(cancelBtn, new Color(1, 1, 1, ch ? 0.08f : 0.03f));
            EditorGUI.DrawRect(new Rect(cancelBtn.x, cancelBtn.y, cancelBtn.width, 1),
                new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.6f));
            EditorGUI.DrawRect(new Rect(cancelBtn.x, cancelBtn.yMax - 1, cancelBtn.width, 1),
                new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.6f));
            EditorGUI.DrawRect(new Rect(cancelBtn.x, cancelBtn.y, 1, cancelBtn.height),
                new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.6f));
            EditorGUI.DrawRect(new Rect(cancelBtn.xMax - 1, cancelBtn.y, 1, cancelBtn.height),
                new Color(C_BORDER.r, C_BORDER.g, C_BORDER.b, 0.6f));
            GUI.Label(cancelBtn, "Cancel",
                new GUIStyle(EditorStyles.label)
                {
                    fontSize  = 11,
                    alignment = TextAnchor.MiddleCenter,
                    normal    = { textColor = ch ? C_TEXT : C_MUTED }
                });
            if (Event.current.type == EventType.MouseDown
                && cancelBtn.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                Close();
                return;
            }

            // Send
            var sendBtn = new Rect(position.width - x - 120, y, 120, 34);
            bool sh = sendBtn.Contains(Event.current.mousePosition);
            EditorGUI.DrawRect(sendBtn, canSend
                ? (sh ? new Color(1f, 0.88f, 0.05f) : C_ACCENT)
                : new Color(C_ACCENT.r, C_ACCENT.g, C_ACCENT.b, 0.18f));
            GUI.Label(sendBtn,
                _sending ? "Sending…" : "✉  Send Message",
                new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize  = 11,
                    alignment = TextAnchor.MiddleCenter,
                    normal    = { textColor = canSend
                        ? new Color32(10, 10, 10, 255)
                        : new Color(0.35f, 0.35f, 0.35f, 0.5f) }
                });
            if (canSend
                && Event.current.type == EventType.MouseDown
                && sendBtn.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                DoSend();
            }

            // ESC = cancel
            if (Event.current.type == EventType.KeyDown
                && Event.current.keyCode == KeyCode.Escape)
            {
                Event.current.Use();
                Close();
            }
        }

        // ── Send ──────────────────────────────────────────────────────────────
        // Sends directly via HTTP POST (form-encoded) to a Google Apps Script webhook.
        // No email client required on the user's machine.

        // Google Apps Script code — update your existing deployment with this:
        //
        //   function doPost(e) {
        //     var subject = e.parameter.subject || "(no subject)";
        //     var body    = e.parameter.body    || "(no body)";
        //     var meta    = e.parameter.meta    || "";
        //     MailApp.sendEmail("ghulammohyuddin@gamedistrict.co",
        //       "[GD CodeShield] " + subject,
        //       body + "\n\n---\n" + meta);
        //     return ContentService.createTextOutput("ok")
        //       .setMimeType(ContentService.MimeType.TEXT);
        //   }
        //
        private const string WEBHOOK_URL =
            "https://script.google.com/macros/s/AKfycbxpjMwXnytvZGWAXBEwHIvXmYCQ1faPMfofJHC7nM0_JBO-Le53mYkEWojCYGoEOBcy/exec";

        private UnityWebRequest _request;

        private void DoSend()
        {
            _sending = true;
            _status  = "";
            Repaint();

            string meta = $"Unity: {Application.unityVersion} | "
                        + $"Project: {Application.productName} | "
                        + $"Platform: {Application.platform}";

            // Google Apps Script requires form-encoded POST to avoid redirect issues
            string formData = $"subject={UnityWebRequest.EscapeURL(_subject)}"
                            + $"&body={UnityWebRequest.EscapeURL(_body)}"
                            + $"&meta={UnityWebRequest.EscapeURL(meta)}";

            byte[] raw = System.Text.Encoding.UTF8.GetBytes(formData);

            _request = new UnityWebRequest(WEBHOOK_URL, "POST");
            _request.uploadHandler   = new UploadHandlerRaw(raw);
            _request.downloadHandler = new DownloadHandlerBuffer();
            _request.SetRequestHeader("Content-Type", "application/x-www-form-urlencoded");
            _request.SendWebRequest();

            EditorApplication.update += PollRequest;
        }

        private void PollRequest()
        {
            if (_request == null) { EditorApplication.update -= PollRequest; return; }
            if (!_request.isDone) return;

            EditorApplication.update -= PollRequest;

#if UNITY_2020_1_OR_NEWER
            bool isError = _request.result != UnityWebRequest.Result.Success;
#else
            bool isError = _request.isNetworkError || _request.isHttpError;
#endif

            if (isError)
            {
                _status  = $"✗ Network error: {_request.error}";
                _sending = false;
            }
            else
            {
                // Read the actual response — script returns "ok" on success or "error: ..." on failure
                string response = _request.downloadHandler?.text?.Trim() ?? "";
                long responseCode = _request.responseCode;

                if (string.IsNullOrEmpty(response))
                {
                    _status  = $"✗ Empty response (HTTP {responseCode}) — check script logs";
                    _sending = false;
                }
                else if (response.StartsWith("error", System.StringComparison.OrdinalIgnoreCase))
                {
                    _status  = $"✗ {response}";
                    _sending = false;
                }
                else if (response == "ok")
                {
                    _status  = "✓ Message sent successfully!";
                    _sending = false;
                    double closeAt = EditorApplication.timeSinceStartup + 2.0;
                    EditorApplication.update += CheckClose;
                    void CheckClose()
                    {
                        if (EditorApplication.timeSinceStartup >= closeAt)
                        {
                            EditorApplication.update -= CheckClose;
                            if (this != null) Close();
                        }
                    }
                }
                else
                {
                    // Show whatever the script returned so we can debug
                    string preview = response.Length > 120
                        ? response.Substring(0, 120) + "…"
                        : response;
                    _status  = $"✗ Unexpected response: {preview}";
                    _sending = false;
                }
            }

            _request.Dispose();
            _request = null;
            Repaint();
        }

        private static string EscapeJson(string s)
            => s.Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");

        // ── Utility ───────────────────────────────────────────────────────────
        private static Texture2D MakeTex(int w, int h, Color c)
        {
            var t = new Texture2D(w, h);
            var p = new Color[w * h];
            for (int i = 0; i < p.Length; i++) p[i] = c;
            t.SetPixels(p); t.Apply(); return t;
        }
    }
}
