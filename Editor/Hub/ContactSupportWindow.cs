// Editor/Hub/ContactSupportWindow.cs
// Standalone Contact Support window — editorial redesign (v1.3.0)
// Preserved behaviour: POSTs to Apps Script webhook, ESC to cancel, 1000-char limit.

using UnityEngine;
using UnityEditor;
using UnityEngine.Networking;
using GDCodeShield.Brand;

namespace GDCodeShield
{
    public class ContactSupportWindow : EditorWindow
    {
        // ── State ─────────────────────────────────────────────────────────────
        private string _subject = "";
        private string _body    = "";
        private string _status  = "";
        private bool   _sending = false;

        private const int MAX_CHARS = 1000;

        // ── Cached editorial styles ───────────────────────────────────────────
        private GUIStyle _sBrand, _sH1, _sLede, _sEyebrow, _sBody, _sMuted, _sFootnote, _sBtn;
        private bool _stylesReady;
        private static Texture2D _clearBg;

        // ── Open ──────────────────────────────────────────────────────────────
        public static void Open()
        {
            var w = GetWindow<ContactSupportWindow>(true, "Contact Support", true);
            w.minSize = new Vector2(560, 540);
            w.maxSize = new Vector2(560, 540);
            w.ShowUtility(); // floating utility window — always on top, own input
        }

        private void EnsureStyles()
        {
            if (_stylesReady) return;
            _stylesReady = true;

            var fraunces = BrandTokens.Fraunces;
            var italics  = BrandTokens.FrauncesItalic ?? fraunces;
            var inter    = BrandTokens.Inter;

            _sBrand    = BrandTokens.MakeStyle(fraunces, 14, BrandTokens.Navy, FontStyle.Bold);
            _sH1       = BrandTokens.MakeStyle(fraunces, 30, BrandTokens.Navy, FontStyle.Bold);
            _sLede     = BrandTokens.MakeWrappedStyle(italics, 15, BrandTokens.Ink, FontStyle.Italic);
            _sEyebrow  = BrandTokens.MakeStyle(inter, BrandTokens.SizeEyebrow, BrandTokens.Navy, FontStyle.Bold);
            _sBody     = BrandTokens.MakeStyle(inter, BrandTokens.SizeBody, BrandTokens.Ink);
            _sMuted    = BrandTokens.MakeStyle(inter, BrandTokens.SizeBody, BrandTokens.WarmGray);
            _sFootnote = BrandTokens.MakeStyle(inter, BrandTokens.SizeFootnote, BrandTokens.WarmGray);
            _sBtn      = BrandTokens.MakeStyle(inter, BrandTokens.SizeBody, BrandTokens.Navy, FontStyle.Bold, TextAnchor.MiddleCenter);

            if (_clearBg == null)
            {
                _clearBg = new Texture2D(1, 1);
                _clearBg.SetPixel(0, 0, Color.clear);
                _clearBg.Apply();
                _clearBg.hideFlags = HideFlags.HideAndDontSave;
            }
        }

        // ── OnGUI ─────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            EnsureStyles();
            float W = position.width, H = position.height;

            // Cream page + gold left-bar
            EditorGUI.DrawRect(new Rect(0, 0, W, H), BrandTokens.Cream);
            EditorGUI.DrawRect(new Rect(0, 0, BrandTokens.GoldBarWidth, H), BrandTokens.Gold);

            float padL = BrandTokens.GoldBarWidth + 30;
            float padR = 36;
            float contentW = W - padL - padR;

            // ── Eyebrow + title + lede ────────────────────────────────────────
            float y = 32f;
            DrawEyebrow(padL, y, "SUPPORT");
            y += 28;

            GUI.Label(new Rect(padL, y, contentW, 40), "Contact support.", _sH1);
            y += 44;

            GUI.Label(new Rect(padL, y, contentW, 40),
                "Tell us what's broken, what's unclear, or what would help. We read every message.",
                _sLede);
            y += 50;

            BrandTokens.HairlineH(padL, y, contentW, BrandTokens.Taupe);
            y += 20;

            // ── Subject ───────────────────────────────────────────────────────
            DrawEyebrow(padL, y, "SUBJECT");
            y += 22;

            var subjRect = new Rect(padL, y, contentW, 32);
            DrawInputField(subjRect);

            GUI.SetNextControlName("Subject");
            _subject = EditorGUI.TextField(
                new Rect(padL + 8, y + 6, contentW - 16, 22),
                _subject,
                MakeTextStyle());
            y += 44;

            // ── Message ───────────────────────────────────────────────────────
            DrawEyebrow(padL, y, "MESSAGE");
            y += 22;

            float bodyH = 220f;
            var bodyRect = new Rect(padL, y, contentW, bodyH);
            DrawInputField(bodyRect);

            GUI.SetNextControlName("Body");
            string newBody = EditorGUI.TextArea(
                new Rect(padL + 8, y + 6, contentW - 16, bodyH - 24),
                _body,
                MakeTextAreaStyle());
            _body = newBody.Length > MAX_CHARS ? newBody.Substring(0, MAX_CHARS) : newBody;

            // Character counter — bottom-right inside the field
            int chars = _body.Length;
            Color charCol = chars > 950 ? BrandTokens.Overdue
                          : chars > 800 ? BrandTokens.Gold
                          : BrandTokens.WarmGray;
            GUI.Label(new Rect(padL, y + bodyH - 18, contentW - 8, 14),
                $"{chars} / {MAX_CHARS}",
                BrandTokens.MakeStyle(BrandTokens.Inter, 10, charCol, FontStyle.Normal, TextAnchor.MiddleRight));
            y += bodyH + 16;

            // ── Status ────────────────────────────────────────────────────────
            if (!string.IsNullOrEmpty(_status))
            {
                bool isErr = _status.StartsWith("✗");
                Color barColor = isErr ? BrandTokens.Overdue : BrandTokens.Shipped;
                EditorGUI.DrawRect(new Rect(padL, y, 3, 28), barColor);
                GUI.Label(new Rect(padL + 12, y + 6, contentW - 16, 16), _status,
                    BrandTokens.MakeStyle(BrandTokens.Inter, BrandTokens.SizeBody, barColor, FontStyle.Bold));
                y += 36;
            }

            // ── Buttons ───────────────────────────────────────────────────────
            bool canSend = !_sending
                && !string.IsNullOrWhiteSpace(_subject)
                && !string.IsNullOrWhiteSpace(_body);

            // Cancel — outlined ghost button on left
            var cancelBtn = new Rect(padL, y, 96, 36);
            bool ch = cancelBtn.Contains(Event.current.mousePosition);
            if (ch) EditorGUI.DrawRect(cancelBtn, BrandTokens.GoldTint);
            DrawRectOutline(cancelBtn, ch ? BrandTokens.Navy : BrandTokens.Taupe);
            GUI.Label(cancelBtn, "Cancel", _sBtn);
            EditorGUIUtility.AddCursorRect(cancelBtn, MouseCursor.Link);

            if (Event.current.type == EventType.MouseDown
                && cancelBtn.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                Close();
                return;
            }

            // Send — gold primary button on right
            var sendBtn = new Rect(W - padR - 160, y, 160, 36);
            bool sh = sendBtn.Contains(Event.current.mousePosition);
            Color sendBg = canSend
                ? (sh ? new Color(244f/255f, 196f/255f, 48f/255f, 0.85f) : BrandTokens.Gold)
                : BrandTokens.Taupe;
            EditorGUI.DrawRect(sendBtn, sendBg);
            GUI.Label(sendBtn,
                _sending ? "Sending…" : "Send message  →",
                BrandTokens.MakeStyle(BrandTokens.Inter, 12,
                    canSend ? BrandTokens.Navy : BrandTokens.WarmGray,
                    FontStyle.Bold, TextAnchor.MiddleCenter));
            if (canSend) EditorGUIUtility.AddCursorRect(sendBtn, MouseCursor.Link);

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

        // ── Editorial primitives ──────────────────────────────────────────────
        private void DrawEyebrow(float x, float y, string label)
        {
            EditorGUI.DrawRect(new Rect(x, y + 4, BrandTokens.EyebrowSquare, BrandTokens.EyebrowSquare), BrandTokens.Gold);
            string spaced = "";
            for (int i = 0; i < label.Length; i++)
            {
                spaced += label[i];
                if (i < label.Length - 1) spaced += "\u2009";
            }
            GUI.Label(new Rect(x + BrandTokens.EyebrowSquare + 10, y, 400, 18), spaced, _sEyebrow);
        }

        private void DrawInputField(Rect r)
        {
            // Subtle off-cream fill so the field is visible against the page
            EditorGUI.DrawRect(r, new Color(0.96f, 0.95f, 0.91f, 1f));
            DrawRectOutline(r, BrandTokens.Taupe);
            // Gold bottom-edge accent for focus signal
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), BrandTokens.Gold);
        }

        private void DrawRectOutline(Rect r, Color c)
        {
            EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y + r.height - 1, r.width, 1), c);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
            EditorGUI.DrawRect(new Rect(r.x + r.width - 1, r.y, 1, r.height), c);
        }

        // TextField/TextArea styles with transparent backgrounds so our cream fill shows through
        private GUIStyle MakeTextStyle()
        {
            var s = new GUIStyle(EditorStyles.textField)
            {
                fontSize = 13,
                normal   = { textColor = BrandTokens.Navy, background = _clearBg },
                focused  = { textColor = BrandTokens.Navy, background = _clearBg },
                active   = { textColor = BrandTokens.Navy, background = _clearBg },
                hover    = { textColor = BrandTokens.Navy, background = _clearBg }
            };
            if (BrandTokens.Inter != null) s.font = BrandTokens.Inter;
            return s;
        }

        private GUIStyle MakeTextAreaStyle()
        {
            var s = new GUIStyle(EditorStyles.textArea)
            {
                fontSize = 13,
                wordWrap = true,
                normal   = { textColor = BrandTokens.Navy, background = _clearBg },
                focused  = { textColor = BrandTokens.Navy, background = _clearBg },
                active   = { textColor = BrandTokens.Navy, background = _clearBg },
                hover    = { textColor = BrandTokens.Navy, background = _clearBg }
            };
            if (BrandTokens.Inter != null) s.font = BrandTokens.Inter;
            return s;
        }

        // ═══════════════════════════════════════════════════════════════════════
        //  SEND — preserved exactly from prior version
        // ═══════════════════════════════════════════════════════════════════════
        private const string WEBHOOK_URL =
            "https://script.google.com/macros/s/AKfycbzn7mlBIZByoS39ICiYvA7RLvnCvE2uc9LQd-VnM6Ytprn5jOqYq0niTg9IjsIHRYCM/exec";

        private UnityWebRequest _request;

        private void DoSend()
        {
            _sending = true;
            _status  = "";
            Repaint();

            string meta = $"Unity: {Application.unityVersion} | "
                        + $"Project: {Application.productName} | "
                        + $"Platform: {Application.platform}";

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
    }
}