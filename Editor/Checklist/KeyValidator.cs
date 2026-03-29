// KeyValidator.cs
// Validates that each SDK key/ID field contains the correct FORMAT of value.
// Catches the classic mistake: right field, completely wrong key type pasted in.
// e.g. Adjust token pasted into Metica API Key, AdMob App ID pasted into AppLovin SDK Key.

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GDChecklist
{
    // ── Key format definitions ────────────────────────────────────────────────
    public enum KeyFormat
    {
        AppLovinSDKKeyAndroid, // Android: starts with digit_  e.g. 2_MaDsrBI1f52...  80+ chars
        AppLovinSDKKeyiOS,     // iOS:     starts with letter_ e.g. d5sDfeSTdoln...   80+ chars
        AppLovinAdUnitID,      // 16-char lowercase hex  e.g. 7992e676c845b78e
        MeticaAPIKey,          // 32-char hex NO hyphens e.g. 1109e367d12249889af06939dac7261b
        AdMobAppID,            // ca-app-pub-XXXXXXXXXXXXXXXX~XXXXXXXXXX
        AdMobAdUnitID,         // ca-app-pub-XXXXXXXXXXXXXXXX/XXXXXXXXXX
        UUID,                  // xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx  (Metica, AppMetrica)
        AdjustToken,           // exactly 12 alphanumeric lowercase
        MeticaAppID,           // readable short string, no UUID/hex pattern (e.g. rampwalkFashionGame)
        Any,                   // no format check — just presence
    }

    public class KeyValidationResult
    {
        public string  FieldName     { get; set; }
        public string  SDK           { get; set; }
        public string  Platform      { get; set; }
        public string  Value         { get; set; }
        public bool    IsValid        { get; set; }
        public string  Problem        { get; set; }   // what's wrong
        public string  LooksLike      { get; set; }   // which SDK it was probably copied from
        public string  Fix            { get; set; }   // how to fix
        public int     Tab            { get; set; }
    }

    public static class KeyValidator
    {
        // ── Regex patterns ────────────────────────────────────────────────────
        private static readonly Regex RX_APPLOVIN_SDK_ANDROID = new Regex(@"^\d_[A-Za-z0-9\-_]{60,}$");   // starts with digit_
        private static readonly Regex RX_APPLOVIN_SDK_IOS     = new Regex(@"^[A-Za-z][A-Za-z0-9\-_]{60,}$");  // starts with letter
        private static readonly Regex RX_APPLOVIN_UNIT = new Regex(@"^[0-9a-f]{16}$");
        private static readonly Regex RX_ADMOB_APP     = new Regex(@"^ca-app-pub-\d{16}~\d{9,11}$");
        private static readonly Regex RX_ADMOB_UNIT    = new Regex(@"^ca-app-pub-\d{16}/\d{9,11}$");
        private static readonly Regex RX_UUID          = new Regex(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", RegexOptions.IgnoreCase); // AppMetrica — standard UUID with hyphens
        private static readonly Regex RX_METICA_KEY   = new Regex(@"^[0-9a-f]{32}$", RegexOptions.IgnoreCase); // Metica API Key — 32-char hex NO hyphens
        private static readonly Regex RX_ADJUST        = new Regex(@"^[A-Za-z0-9]{12}$"); // Adjust token — exactly 12 alphanumeric, mixed case
        // Metica App ID: readable string, NOT a UUID, NOT hex, NOT ca-app-pub
        private static readonly Regex RX_METICA_APPID  = new Regex(@"^[A-Za-z0-9_]{3,64}$");

        // ── Field definitions — what format each field SHOULD contain ─────────
        // Tab indices match AssetScanner constants
        private const int TAB_APPLOVIN   = 0;
        private const int TAB_METICA     = 1;
        private const int TAB_ADJUST     = 2;
        private const int TAB_APPMETRICA = 3;
        private const int TAB_ADUNITS    = 5;
        private const int TAB_INTEGRITY  = 9; // new tab

        private class FieldDef
        {
            public string    SDK          { get; set; }
            public string    FieldName    { get; set; }
            public string    Platform     { get; set; }
            public KeyFormat Format       { get; set; }
            public int       Tab          { get; set; }
            public string    YamlKey      { get; set; }
        }

        private static readonly List<FieldDef> FieldDefs = new List<FieldDef>
        {
            // AppLovin
            // AppLovin SDK Key — same yaml field but format differs by platform
            // The asset stores one key used for both, but we validate it's the right format
            // In practice: if Android build, key starts with digit_; if iOS, starts with letter
            // We check both patterns are NOT matched by wrong-type keys
            new FieldDef { SDK="AppLovin", FieldName="SDK Key (Android)",  Platform="Android", Format=KeyFormat.AppLovinSDKKeyAndroid, Tab=TAB_APPLOVIN, YamlKey="sdkKey" },
            new FieldDef { SDK="AppLovin", FieldName="SDK Key (iOS)",      Platform="iOS",     Format=KeyFormat.AppLovinSDKKeyiOS,     Tab=TAB_APPLOVIN, YamlKey="sdkKey" },
            new FieldDef { SDK="AdMob",    FieldName="App ID",             Platform="Android", Format=KeyFormat.AdMobAppID,       Tab=TAB_APPLOVIN,   YamlKey="adMobAndroidAppId"  },
            new FieldDef { SDK="AdMob",    FieldName="App ID",             Platform="iOS",     Format=KeyFormat.AdMobAppID,       Tab=TAB_APPLOVIN,   YamlKey="adMobIosAppId"      },

            // Metica
            new FieldDef { SDK="Metica",   FieldName="Android API Key",    Platform="Android", Format=KeyFormat.MeticaAPIKey,     Tab=TAB_METICA,     YamlKey="AndroidApiKey"      },
            new FieldDef { SDK="Metica",   FieldName="iOS API Key",        Platform="iOS",     Format=KeyFormat.MeticaAPIKey,     Tab=TAB_METICA,     YamlKey="iOSApiKey"          },
            new FieldDef { SDK="Metica",   FieldName="Android App ID",     Platform="Android", Format=KeyFormat.MeticaAppID,      Tab=TAB_METICA,     YamlKey="AndroidAppID"       },
            new FieldDef { SDK="Metica",   FieldName="iOS App ID",         Platform="iOS",     Format=KeyFormat.MeticaAppID,      Tab=TAB_METICA,     YamlKey="iOSAppID"           },

            // Adjust
            new FieldDef { SDK="Adjust",   FieldName="Android Token",      Platform="Android", Format=KeyFormat.AdjustToken,     Tab=TAB_ADJUST,     YamlKey="Android"            },
            new FieldDef { SDK="Adjust",   FieldName="iOS Token",          Platform="iOS",     Format=KeyFormat.AdjustToken,     Tab=TAB_ADJUST,     YamlKey="iOS"                },

            // AppMetrica
            new FieldDef { SDK="AppMetrica",FieldName="Android API Key",   Platform="Android", Format=KeyFormat.UUID,            Tab=TAB_APPMETRICA, YamlKey="Android"            },
            new FieldDef { SDK="AppMetrica",FieldName="iOS API Key",       Platform="iOS",     Format=KeyFormat.UUID,            Tab=TAB_APPMETRICA, YamlKey="iOS"                },

            // Ad Units — AppLovin MAX units are 16-char hex
            new FieldDef { SDK="Applovin AdUnit", FieldName="Interstitial ID", Platform="Android", Format=KeyFormat.AppLovinAdUnitID, Tab=TAB_ADUNITS, YamlKey="Applovin.AdUnit.0.Android" },
            new FieldDef { SDK="Applovin AdUnit", FieldName="Interstitial ID", Platform="iOS",     Format=KeyFormat.AppLovinAdUnitID, Tab=TAB_ADUNITS, YamlKey="Applovin.AdUnit.0.iOS"     },
            new FieldDef { SDK="Applovin AdUnit", FieldName="Rewarded ID",     Platform="Android", Format=KeyFormat.AppLovinAdUnitID, Tab=TAB_ADUNITS, YamlKey="Applovin.AdUnit.1.Android" },
            new FieldDef { SDK="Applovin AdUnit", FieldName="Rewarded ID",     Platform="iOS",     Format=KeyFormat.AppLovinAdUnitID, Tab=TAB_ADUNITS, YamlKey="Applovin.AdUnit.1.iOS"     },
            new FieldDef { SDK="Applovin AdUnit", FieldName="AppOpen ID",      Platform="Android", Format=KeyFormat.AppLovinAdUnitID, Tab=TAB_ADUNITS, YamlKey="Applovin.AdUnit.2.Android" },
            new FieldDef { SDK="Applovin AdUnit", FieldName="AppOpen ID",      Platform="iOS",     Format=KeyFormat.AppLovinAdUnitID, Tab=TAB_ADUNITS, YamlKey="Applovin.AdUnit.2.iOS"     },
            new FieldDef { SDK="Applovin AdUnit", FieldName="Banner ID",       Platform="Android", Format=KeyFormat.AppLovinAdUnitID, Tab=TAB_ADUNITS, YamlKey="Applovin.AdUnit.3.Android" },
            new FieldDef { SDK="Applovin AdUnit", FieldName="Banner ID",       Platform="iOS",     Format=KeyFormat.AppLovinAdUnitID, Tab=TAB_ADUNITS, YamlKey="Applovin.AdUnit.3.iOS"     },
            new FieldDef { SDK="Applovin AdUnit", FieldName="MRec ID",         Platform="Android", Format=KeyFormat.AppLovinAdUnitID, Tab=TAB_ADUNITS, YamlKey="Applovin.AdUnit.4.Android" },
            new FieldDef { SDK="Applovin AdUnit", FieldName="MRec ID",         Platform="iOS",     Format=KeyFormat.AppLovinAdUnitID, Tab=TAB_ADUNITS, YamlKey="Applovin.AdUnit.4.iOS"     },

            // Metica Ad Units — also use MAX as mediation so same 16-char hex format
            new FieldDef { SDK="Metica AdUnit", FieldName="Interstitial ID",   Platform="Android", Format=KeyFormat.AppLovinAdUnitID, Tab=TAB_ADUNITS, YamlKey="Metica.AdUnit.0.Android"   },
            new FieldDef { SDK="Metica AdUnit", FieldName="Interstitial ID",   Platform="iOS",     Format=KeyFormat.AppLovinAdUnitID, Tab=TAB_ADUNITS, YamlKey="Metica.AdUnit.0.iOS"       },
            new FieldDef { SDK="Metica AdUnit", FieldName="Rewarded ID",       Platform="Android", Format=KeyFormat.AppLovinAdUnitID, Tab=TAB_ADUNITS, YamlKey="Metica.AdUnit.1.Android"   },
            new FieldDef { SDK="Metica AdUnit", FieldName="Rewarded ID",       Platform="iOS",     Format=KeyFormat.AppLovinAdUnitID, Tab=TAB_ADUNITS, YamlKey="Metica.AdUnit.1.iOS"       },
            new FieldDef { SDK="Metica AdUnit", FieldName="Banner ID",         Platform="Android", Format=KeyFormat.AppLovinAdUnitID, Tab=TAB_ADUNITS, YamlKey="Metica.AdUnit.3.Android"   },
            new FieldDef { SDK="Metica AdUnit", FieldName="Banner ID",         Platform="iOS",     Format=KeyFormat.AppLovinAdUnitID, Tab=TAB_ADUNITS, YamlKey="Metica.AdUnit.3.iOS"       },

            // AdMob Ad Units (via Admob network block in AdUnitsSettings)
            new FieldDef { SDK="AdMob AdUnit", FieldName="Interstitial ID",    Platform="Android", Format=KeyFormat.AdMobAdUnitID, Tab=TAB_ADUNITS, YamlKey="Admob.AdUnit.0.Android"    },
            new FieldDef { SDK="AdMob AdUnit", FieldName="Interstitial ID",    Platform="iOS",     Format=KeyFormat.AdMobAdUnitID, Tab=TAB_ADUNITS, YamlKey="Admob.AdUnit.0.iOS"        },
            new FieldDef { SDK="AdMob AdUnit", FieldName="Rewarded ID",        Platform="Android", Format=KeyFormat.AdMobAdUnitID, Tab=TAB_ADUNITS, YamlKey="Admob.AdUnit.1.Android"    },
            new FieldDef { SDK="AdMob AdUnit", FieldName="Rewarded ID",        Platform="iOS",     Format=KeyFormat.AdMobAdUnitID, Tab=TAB_ADUNITS, YamlKey="Admob.AdUnit.1.iOS"        },
            new FieldDef { SDK="AdMob AdUnit", FieldName="Banner ID",          Platform="Android", Format=KeyFormat.AdMobAdUnitID, Tab=TAB_ADUNITS, YamlKey="Admob.AdUnit.3.Android"    },
            new FieldDef { SDK="AdMob AdUnit", FieldName="Banner ID",          Platform="iOS",     Format=KeyFormat.AdMobAdUnitID, Tab=TAB_ADUNITS, YamlKey="Admob.AdUnit.3.iOS"        },
        };

        // ════════════════════════════════════════════════════════════════════════
        //  ENTRY — validate a ScanResult's fields for format correctness
        // ════════════════════════════════════════════════════════════════════════
        public static List<KeyValidationResult> Validate(ScanResult scan)
        {
            var results = new List<KeyValidationResult>();
            if (scan == null) return results;

            foreach (var def in FieldDefs)
            {
                // Find the matching field in scan results
                var field = scan.AllFields.FirstOrDefault(f =>
                    f.Tab == def.Tab &&
                    f.FieldName == def.FieldName &&
                    (string.IsNullOrEmpty(def.Platform) || f.Platform == def.Platform));

                if (field == null) continue;

                string value = field.ProjectValue?.Trim();

                // Skip empty/missing — AssetScanner already flags those
                if (string.IsNullOrEmpty(value) ||
                    value.StartsWith("(") ||        // "(not found)", "(empty)"
                    value.StartsWith("File not"))
                    continue;

                // Strip display suffixes added by scanner e.g. "Sandbox  (raw value: 0)"
                if (value.Contains("  ("))
                    value = value.Substring(0, value.IndexOf("  (")).Trim();

                var vr = ValidateFormat(def, value);
                if (vr != null)
                    results.Add(vr);
            }

            return results;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  FORMAT VALIDATION
        // ════════════════════════════════════════════════════════════════════════
        private static KeyValidationResult ValidateFormat(FieldDef def, string value)
        {
            bool valid = false;
            string problem = null;
            string looksLike = null;
            string fix = null;

            switch (def.Format)
            {
                case KeyFormat.AppLovinSDKKeyAndroid:
                    valid = RX_APPLOVIN_SDK_ANDROID.IsMatch(value);
                    if (!valid)
                    {
                        looksLike = IdentifyKey(value);
                        problem   = $"AppLovin Android SDK key should start with a digit and underscore (e.g. 2_MaDsr...) and be 80+ chars. Found: \"{Truncate(value, 30)}\"";
                        fix       = "Get Android SDK key from AppLovin Dashboard → Account → Keys — it starts with a number followed by underscore";
                    }
                    break;

                case KeyFormat.AppLovinSDKKeyiOS:
                    valid = RX_APPLOVIN_SDK_IOS.IsMatch(value);
                    if (!valid)
                    {
                        looksLike = IdentifyKey(value);
                        problem   = $"AppLovin iOS SDK key should start with a letter (e.g. d5sDfe...) and be 80+ chars. Found: \"{Truncate(value, 30)}\"";
                        fix       = "Get iOS SDK key from AppLovin Dashboard → Account → Keys — it starts with a letter";
                    }
                    break;

                case KeyFormat.AppLovinAdUnitID:
                    valid = RX_APPLOVIN_UNIT.IsMatch(value);
                    if (!valid)
                    {
                        looksLike = IdentifyKey(value);
                        problem   = $"MAX Ad Unit ID should be 16-char lowercase hex. Found: \"{Truncate(value, 30)}\"";
                        fix       = "Get the correct Ad Unit ID from AppLovin Dashboard → Monetize → Ad Units";
                    }
                    break;

                case KeyFormat.AdMobAppID:
                    valid = RX_ADMOB_APP.IsMatch(value);
                    if (!valid)
                    {
                        looksLike = IdentifyKey(value);
                        problem   = $"AdMob App ID must start with ca-app-pub- and use ~ separator. Found: \"{Truncate(value, 30)}\"";
                        fix       = "Get App ID from AdMob Dashboard → Apps → App settings (format: ca-app-pub-XXXXXXXXXXXXXXXX~XXXXXXXXXX)";
                    }
                    break;

                case KeyFormat.AdMobAdUnitID:
                    valid = RX_ADMOB_UNIT.IsMatch(value);
                    if (!valid)
                    {
                        looksLike = IdentifyKey(value);
                        problem   = $"AdMob Ad Unit ID must start with ca-app-pub- and use / separator. Found: \"{Truncate(value, 30)}\"";
                        fix       = "Get Ad Unit ID from AdMob Dashboard → Monetize → Ad units (format: ca-app-pub-XXXXXXXXXXXXXXXX/XXXXXXXXXX)";
                    }
                    break;

                case KeyFormat.MeticaAPIKey:
                    valid = RX_METICA_KEY.IsMatch(value);
                    if (!valid)
                    {
                        looksLike = IdentifyKey(value);
                        problem   = $"Metica API Key should be 32-char hex with no hyphens (e.g. 1109e367d12249889af06939dac7261b). Found: \"{Truncate(value, 30)}\"";
                        fix       = "Get the correct API Key from Metica Dashboard — it is 32 hex characters without any hyphens";
                    }
                    break;

                case KeyFormat.UUID:
                    // AppMetrica uses standard UUID with hyphens
                    valid = RX_UUID.IsMatch(value);
                    if (!valid)
                    {
                        looksLike = IdentifyKey(value);
                        problem   = $"{def.SDK} {def.FieldName} should be UUID format (xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx). Found: \"{Truncate(value, 30)}\"";
                        fix       = $"Get the correct key from {def.SDK} dashboard — it should be a standard UUID with 4 hyphens";
                    }
                    break;

                case KeyFormat.AdjustToken:
                    valid = RX_ADJUST.IsMatch(value);
                    if (!valid)
                    {
                        looksLike = IdentifyKey(value);
                        problem   = $"Adjust token should be exactly 12 lowercase alphanumeric chars. Found: \"{Truncate(value, 30)}\"";
                        fix       = "Get the App Token from Adjust Dashboard → App Settings → General";
                    }
                    break;

                case KeyFormat.MeticaAppID:
                    // Metica App ID is readable string — fail if it looks like a UUID, hex, or ca-app-pub
                    bool looksWrong = RX_UUID.IsMatch(value) ||
                                     RX_APPLOVIN_UNIT.IsMatch(value) ||
                                     value.StartsWith("ca-app-pub") ||
                                     RX_ADJUST.IsMatch(value);
                    valid = !looksWrong && RX_METICA_APPID.IsMatch(value);
                    if (!valid)
                    {
                        looksLike = IdentifyKey(value);
                        problem   = $"Metica App ID should be a readable app identifier (e.g. myGameName), not a key or UUID. Found: \"{Truncate(value, 30)}\"";
                        fix       = "Get the App ID from Metica Dashboard — it is a short readable string, not a UUID";
                    }
                    break;

                case KeyFormat.Any:
                    valid = true;
                    break;
            }

            if (valid) return null; // no issue

            return new KeyValidationResult
            {
                SDK       = def.SDK,
                FieldName = def.FieldName,
                Platform  = def.Platform,
                Value     = Truncate(value, 50),
                IsValid   = false,
                Problem   = problem,
                LooksLike = looksLike,
                Fix       = fix,
                Tab       = def.Tab
            };
        }

        // ════════════════════════════════════════════════════════════════════════
        //  IDENTIFY — tells the developer which SDK the wrong key belongs to
        // ════════════════════════════════════════════════════════════════════════
        private static string IdentifyKey(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            string v = value.Trim();

            if (RX_ADMOB_APP.IsMatch(v))            return "AdMob App ID (ca-app-pub-...~...)";
            if (RX_ADMOB_UNIT.IsMatch(v))           return "AdMob Ad Unit ID (ca-app-pub-.../...)";
            if (RX_METICA_KEY.IsMatch(v))           return "Metica API Key (32-char hex, no hyphens)";
            if (RX_UUID.IsMatch(v))                 return "UUID key — likely AppMetrica API Key";
            if (RX_ADJUST.IsMatch(v))               return "Adjust App Token (12-char)";
            if (RX_APPLOVIN_UNIT.IsMatch(v))        return "MAX Ad Unit ID (16-char hex)";
            if (RX_APPLOVIN_SDK_ANDROID.IsMatch(v)) return "AppLovin Android SDK Key (starts with digit_)";
            if (RX_APPLOVIN_SDK_IOS.IsMatch(v))     return "AppLovin iOS SDK Key (starts with letter)";
            if (v.Length > 60)                      return "Unknown long key — check source";
            return null;
        }

        private static string Truncate(string s, int max)
            => s?.Length > max ? s.Substring(0, max) + "…" : s;
    }
}
