// SDKVersionDetector.cs
// Detects the installed version of each SDK that CodeShield validates.
// Strategy per SDK:
//   1. Check UPM Packages/manifest.json (handles UPM, file:, .tgz)
//   2. Search the SDK's folder under Assets/ for a known version constant pattern
//   3. Return null if nothing matches

using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace GDChecklist
{
    public class SDKVersion
    {
        public string Name;
        public string Version;
        public string Source;

        // For multi-component SDKs (Firebase modules, AppLovin android/ios, etc.)
        // Each entry: ("Analytics", "12.10.1") or ("Android", "13.3.1")
        public List<(string label, string version)> Modules = new List<(string, string)>();
    }

    public static class SDKVersionDetector
    {
        private static readonly Dictionary<int, System.Func<SDKVersion>> _byTab =
            new Dictionary<int, System.Func<SDKVersion>>
        {
            { 0, DetectAppLovin },
            { 1, DetectMetica },
            { 2, DetectAdjust },
            { 3, DetectAppMetrica },
            { 4, DetectFirebase },
        };

        private static readonly Dictionary<int, SDKVersion> _cache = new Dictionary<int, SDKVersion>();

        public static SDKVersion GetVersionForTab(int tabIndex)
        {
            if (_cache.TryGetValue(tabIndex, out var cached)) return cached;
            if (!_byTab.TryGetValue(tabIndex, out var detector)) return null;
            try
            {
                var v = detector();
                _cache[tabIndex] = v;
                return v;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[SDKVersionDetector] " + ex);
                return null;
            }
        }

        public static void ClearCache() => _cache.Clear();

        // ════════════════════════════════════════════════════════════════════════
        //  PER-SDK DETECTORS
        // ════════════════════════════════════════════════════════════════════════

        // AppLovin MAX — Assets/MaxSdk/AppLovin/Editor/Dependencies.xml has separate Android + iOS versions
        private static SDKVersion DetectAppLovin()
        {
            var v = new SDKVersion { Name = "AppLovin MAX" };

            string upmVer = ReadUpmVersion("com.applovin.mediation.ads");
            if (!string.IsNullOrEmpty(upmVer))
            {
                v.Version = upmVer;
                v.Source = "UPM (com.applovin.mediation.ads)";
                return v;
            }

            // Dependencies.xml has separate android + ios specs
            string androidVer = ReadXmlVersion("Assets/MaxSdk/AppLovin/Editor/Dependencies.xml", new[]
            {
                @"applovin-sdk:([\d\.]+)",
            });
            string iosVer = ReadXmlVersion("Assets/MaxSdk/AppLovin/Editor/Dependencies.xml", new[]
            {
                @"<iosPod\s+name=""AppLovinSDK""\s+version=""([\d\.]+)""",
            });

            if (!string.IsNullOrEmpty(androidVer)) v.Modules.Add(("Android SDK", androidVer));
            if (!string.IsNullOrEmpty(iosVer))     v.Modules.Add(("iOS SDK", iosVer));

            // Also scan for mediation adapters under Assets/MaxSdk/Mediation/<network>/Editor/Dependencies.xml
            // Each network has Android + iOS versions — show both with the "Network · Platform" label
            string mediationFolder = "Assets/MaxSdk/Mediation";
            if (Directory.Exists(mediationFolder))
            {
                try
                {
                    var sortedDirs = Directory.GetDirectories(mediationFolder);
                    System.Array.Sort(sortedDirs);
                    foreach (var networkDir in sortedDirs)
                    {
                        string netName = Path.GetFileName(networkDir);
                        string depFile = Path.Combine(networkDir, "Editor", "Dependencies.xml");
                        if (!File.Exists(depFile)) continue;

                        // Android: <androidPackage spec="...:VERSION" />
                        string androidNet = ReadXmlVersion(depFile, new[]
                        {
                            @"<androidPackage\s+spec=""[^""]*?:([\d\.]+)""",
                        });
                        // iOS: <iosPod name="..." version="VERSION" />
                        string iosNet = ReadXmlVersion(depFile, new[]
                        {
                            @"<iosPod\s+name=""[^""]+""\s+version=""([\d\.]+)""",
                        });

                        if (!string.IsNullOrEmpty(androidNet))
                            v.Modules.Add(($"{netName}  ·  Android", androidNet));
                        if (!string.IsNullOrEmpty(iosNet))
                            v.Modules.Add(($"{netName}  ·  iOS", iosNet));
                    }
                }
                catch { }
            }

            if (v.Modules.Count > 0)
            {
                v.Source = "AppLovin/Editor/Dependencies.xml";
                // Pick first as the "headline" version
                v.Version = v.Modules[0].version;
                return v;
            }

            // Fallback: scan any .cs file under MaxSdk
            string found = SearchFolder("Assets/MaxSdk", new[]
            {
                @"public\s+const\s+string\s+Version\s*=\s*""([\d\.]+)""",
                @"PluginVersion\s*=\s*""([\d\.]+)""",
                @"SDK_VERSION\s*=\s*""([\d\.]+)""",
            }, out string sourceFile);
            if (!string.IsNullOrEmpty(found))
            {
                v.Version = found;
                v.Source = sourceFile;
            }
            return v;
        }

        // Metica — prefer Assets/MeticaSdk/MeticaSdk.cs `Version { get => "X.Y.Z"; }` over the abstractions .tgz package
        // (abstractions is a thin interface package; MeticaSdk is the real SDK)
        private static SDKVersion DetectMetica()
        {
            var v = new SDKVersion { Name = "Metica" };

            // Try the SDK folder FIRST — this is the authoritative version
            string found = SearchFolder("Assets/MeticaSdk", new[]
            {
                @"public\s+static\s+string\s+Version\s*\{\s*get\s*=>\s*""([\d\.]+)""\s*;?\s*\}",
                @"public\s+const\s+string\s+(?:Version|VERSION|SdkVersion|SDK_VERSION)\s*=\s*""([\d\.]+)""",
                @"(?:Version|VERSION)\s*=\s*""([\d\.]+)""",
            }, out string sourceFile);
            if (!string.IsNullOrEmpty(found)) return Set(v, found, sourceFile);

            // Fallback to UPM packages (abstractions, etc.)
            foreach (var pkg in new[] {
                "com.metica.analytics.abstractions",
                "com.metica.analytics",
                "com.metica.sdk",
                "com.metica.unitysdk",
                "com.gamedistrict.metica"
            })
            {
                string upmVer = ReadUpmVersion(pkg);
                if (!string.IsNullOrEmpty(upmVer))
                    return Set(v, upmVer, $"UPM ({pkg})");
            }

            return v;
        }

        // Adjust — Assets/Adjust/* has AdjustDependencies.xml with android + ios native SDK versions,
        // plus AdjustConfig.cs sdkPrefix = "unityX.Y.Z" for the Unity plugin version
        private static SDKVersion DetectAdjust()
        {
            var v = new SDKVersion { Name = "Adjust" };

            string upmVer = ReadUpmVersion("com.adjust.sdk");
            if (!string.IsNullOrEmpty(upmVer))
                return Set(v, upmVer, "UPM (com.adjust.sdk)");

            // Unity plugin version from sdkPrefix or SdkVersion constant
            string unityVer = SearchFolder("Assets/Adjust", new[]
            {
                @"sdkPrefix\s*=\s*""unity([\d\.]+)""",
                @"public\s+const\s+string\s+(?:Version|SdkVersion|SDK_VERSION)\s*=\s*""([\d\.]+)""",
            }, out string unitySource);
            if (!string.IsNullOrEmpty(unityVer)) v.Modules.Add(("Unity Plugin", unityVer));

            // Native Android + iOS versions from Dependencies.xml (any file named *Dependencies.xml under Assets/Adjust)
            string depFile = FindFirstFile("Assets/Adjust", "*Dependencies.xml");
            if (!string.IsNullOrEmpty(depFile))
            {
                string androidVer = ReadXmlVersion(depFile, new[]
                {
                    @"com\.adjust\.sdk:adjust-android(?:-signature|-imei|-oaid)?:([\d\.]+)",
                    @"<androidPackage\s+spec=""com\.adjust[^""]*?:([\d\.]+)""",
                });
                string iosVer = ReadXmlVersion(depFile, new[]
                {
                    @"<iosPod\s+name=""Adjust""\s+version=""([\d\.]+)""",
                    @"<iosPod\s+name=""AdjustSdk""\s+version=""([\d\.]+)""",
                });
                if (!string.IsNullOrEmpty(androidVer)) v.Modules.Add(("Android Native", androidVer));
                if (!string.IsNullOrEmpty(iosVer))     v.Modules.Add(("iOS Native", iosVer));
            }

            if (v.Modules.Count > 0)
            {
                v.Source = string.IsNullOrEmpty(unitySource) ? "Assets/Adjust" : unitySource;
                v.Version = v.Modules[0].version;
                return v;
            }
            return v;
        }

        // AppMetrica — Assets/AppMetrica/ — version constant in one of the .cs files
        private static SDKVersion DetectAppMetrica()
        {
            var v = new SDKVersion { Name = "AppMetrica" };

            foreach (var pkg in new[] {
                "io.appmetrica.analytics",
                "com.yandex.appmetrica.sdk",
                "com.appmetrica.analytics"
            })
            {
                string upmVer = ReadUpmVersion(pkg);
                if (!string.IsNullOrEmpty(upmVer))
                    return Set(v, upmVer, $"UPM ({pkg})");
            }

            string found = SearchFolder("Assets/AppMetrica", new[]
            {
                @"public\s+const\s+string\s+(?:Version|VERSION|SdkVersion)\s*=\s*""([\d\.]+)""",
                @"LIBRARY_VERSION\s*=\s*""([\d\.]+)""",
                @"(?:Version|VERSION)\s*=\s*""([\d\.]+)""",
            }, out string sourceFile);
            if (!string.IsNullOrEmpty(found)) return Set(v, found, sourceFile);

            return v;
        }

        // Firebase — Assets/Firebase/Editor/Firebase{Module}_version-X.Y.Z_manifest.txt — one per module.
        // Lists Analytics, RemoteConfig, Messaging, Auth, Firestore, Database, Storage, etc. separately.
        private static SDKVersion DetectFirebase()
        {
            var v = new SDKVersion { Name = "Firebase" };

            foreach (var pkg in new[] { "com.google.firebase.app", "com.google.firebase.core" })
            {
                string upmVer = ReadUpmVersion(pkg);
                if (!string.IsNullOrEmpty(upmVer))
                    return Set(v, upmVer, $"UPM ({pkg})");
            }

            // Primary: enumerate every Firebase{Module}_version-*.txt file and pair with its Dependencies.xml
            string folder = "Assets/Firebase/Editor";
            if (Directory.Exists(folder))
            {
                try
                {
                    var files = Directory.GetFiles(folder, "Firebase*_version-*_manifest.txt", SearchOption.TopDirectoryOnly);
                    System.Array.Sort(files);
                    var rx = new Regex(@"Firebase([A-Za-z]+)_version-([\d\.]+)_manifest\.txt");
                    foreach (var f in files)
                    {
                        var m = rx.Match(Path.GetFileName(f));
                        if (!m.Success) continue;
                        string moduleName  = m.Groups[1].Value;
                        string unityVer    = m.Groups[2].Value;

                        // The Unity wrapper version (e.g. firebase-app-unity:12.10.1)
                        v.Modules.Add(($"{moduleName}  ·  Unity", unityVer));

                        // Native Android + iOS versions from {Module}Dependencies.xml
                        string depPath = Path.Combine(folder, moduleName + "Dependencies.xml");
                        if (File.Exists(depPath))
                        {
                            string androidNative = ReadXmlVersion(depPath, new[]
                            {
                                // First non-unity android package (e.g. firebase-analytics:22.4.0)
                                @"<androidPackage\s+spec=""com\.google\.firebase:firebase-(?!.*-unity)[\w-]+:([\d\.]+)""",
                            });
                            string iosNative = ReadXmlVersion(depPath, new[]
                            {
                                @"<iosPod\s+name=""Firebase/[^""]+""\s+version=""([\d\.]+)""",
                            });

                            if (!string.IsNullOrEmpty(androidNative))
                                v.Modules.Add(($"{moduleName}  ·  Android", androidNative));
                            if (!string.IsNullOrEmpty(iosNative))
                                v.Modules.Add(($"{moduleName}  ·  iOS", iosNative));
                        }
                    }
                }
                catch { }
            }

            if (v.Modules.Count > 0)
            {
                v.Source = "Firebase/Editor/*_manifest.txt + *Dependencies.xml";
                v.Version = v.Modules[0].version;
                return v;
            }

            // Fallback: AppDependencies.xml firebase-app-unity spec
            string depVer = ReadXmlVersion("Assets/Firebase/Editor/AppDependencies.xml", new[]
            {
                @"firebase-app-unity:([\d\.]+)",
            });
            if (!string.IsNullOrEmpty(depVer))
            {
                v.Version = depVer;
                v.Source = "Firebase/Editor/AppDependencies.xml";
                v.Modules.Add(("App", depVer));
                return v;
            }

            // Last resort: scan for SdkVersion constant
            string found = SearchFolder("Assets/Firebase", new[]
            {
                @"SdkVersion\s*=\s*""([\d\.]+)""",
                @"public\s+const\s+string\s+SdkVersion\s*=\s*""([\d\.]+)""",
                @"DefaultVersion\s*=\s*""([\d\.]+)""",
            }, out string sourceFile);
            if (!string.IsNullOrEmpty(found)) return Set(v, found, sourceFile);

            return v;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  CORE SEARCH HELPERS
        // ════════════════════════════════════════════════════════════════════════

        /// Searches the given folder (recursively) for a .cs file containing one of the regex patterns.
        /// First checks well-known filenames, then falls back to all .cs (capped at 500 files).
        private static string SearchFolder(string folder, string[] regexes, out string sourceFile)
        {
            sourceFile = null;
            if (!Directory.Exists(folder)) return null;

            string[] preferred = {
                "MaxSdk.cs", "MaxSdkUtils.cs",
                "AdjustConfig.cs", "Adjust.cs", "AdjustUtils.cs",
                "VersionInfo.cs", "FirebaseApp.cs",
                "AppMetrica.cs", "AppMetricaConfig.cs", "AppMetricaUtils.cs", "Constants.cs",
                "MeticaConstants.cs", "MeticaSdk.cs", "Metica.cs", "MeticaConfiguration.cs"
            };

            // Pass 1: preferred filenames
            foreach (var name in preferred)
            {
                foreach (var path in Enumerate(folder, name))
                {
                    string ver = TryRegexes(path, regexes);
                    if (!string.IsNullOrEmpty(ver))
                    {
                        sourceFile = Path.GetFileName(path);
                        return ver;
                    }
                }
            }

            // Pass 2: every .cs (capped)
            int count = 0;
            foreach (var path in Enumerate(folder, "*.cs"))
            {
                if (++count > 500) break;
                string ver = TryRegexes(path, regexes);
                if (!string.IsNullOrEmpty(ver))
                {
                    sourceFile = Path.GetFileName(path);
                    return ver;
                }
            }

            return null;
        }

        private static IEnumerable<string> Enumerate(string folder, string pattern)
        {
            string[] files;
            try { files = Directory.GetFiles(folder, pattern, SearchOption.AllDirectories); }
            catch { yield break; }
            foreach (var f in files) yield return f.Replace('\\', '/');
        }

        private static string FindFirstFile(string folder, string pattern)
        {
            if (!Directory.Exists(folder)) return null;
            try
            {
                var files = Directory.GetFiles(folder, pattern, SearchOption.AllDirectories);
                return files.Length > 0 ? files[0].Replace('\\', '/') : null;
            }
            catch { return null; }
        }

        /// Reads a single XML/text file and returns the first matching capture group from any of the supplied regexes.
        private static string ReadXmlVersion(string path, string[] regexes)
        {
            if (!File.Exists(path)) return null;
            string text;
            try { text = File.ReadAllText(path); }
            catch { return null; }
            foreach (var rx in regexes)
            {
                var m = Regex.Match(text, rx);
                if (m.Success && m.Groups.Count > 1 && !string.IsNullOrEmpty(m.Groups[1].Value))
                    return m.Groups[1].Value;
            }
            return null;
        }

        private static string TryRegexes(string path, string[] regexes)
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch { return null; }

            foreach (var rx in regexes)
            {
                var m = Regex.Match(text, rx);
                if (m.Success && m.Groups.Count > 1 && !string.IsNullOrEmpty(m.Groups[1].Value))
                {
                    string ver = m.Groups[1].Value;
                    if (Regex.IsMatch(ver, @"^\d+\.\d+")) return ver;
                }
            }
            return null;
        }

        private static SDKVersion Set(SDKVersion v, string version, string source)
        {
            v.Version = version;
            v.Source = source;
            return v;
        }

        // ════════════════════════════════════════════════════════════════════════
        //  UPM MANIFEST READING
        // ════════════════════════════════════════════════════════════════════════

        private static string _manifestText;
        private static string ManifestText
        {
            get
            {
                if (_manifestText != null) return _manifestText;
                string p = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Packages", "manifest.json");
                _manifestText = File.Exists(p) ? File.ReadAllText(p) : "";
                return _manifestText;
            }
        }

        private static string ReadUpmVersion(string packageName)
        {
            string text = ManifestText;
            if (string.IsNullOrEmpty(text)) return null;

            var rx = new Regex(@"""" + Regex.Escape(packageName) + @"""\s*:\s*""([^""]+)""");
            var m = rx.Match(text);
            if (!m.Success) return null;

            string val = m.Groups[1].Value;

            if (Regex.IsMatch(val, @"^\d+\.\d+(\.\d+)?")) return val;

            if (val.StartsWith("file:"))
            {
                string relPath = val.Substring("file:".Length);

                // .tgz: version is in the filename
                if (relPath.EndsWith(".tgz") || relPath.EndsWith(".tar.gz"))
                {
                    string fileName = Path.GetFileNameWithoutExtension(relPath);
                    var vm = Regex.Match(fileName, @"-(\d+\.\d+\.\d+(?:[-+][\w\.]+)?)$");
                    if (vm.Success) return vm.Groups[1].Value;
                }

                // Directory: read embedded package.json
                string projectRoot = Directory.GetParent(Application.dataPath).FullName;
                string fullPath = Path.Combine(projectRoot, "Packages", relPath, "package.json");
                if (!File.Exists(fullPath))
                    fullPath = Path.Combine(projectRoot, relPath, "package.json");

                if (File.Exists(fullPath))
                {
                    string pkgJson = File.ReadAllText(fullPath);
                    var vm = Regex.Match(pkgJson, @"""version""\s*:\s*""([^""]+)""");
                    if (vm.Success) return vm.Groups[1].Value;
                }
            }

            return null;
        }
    }
}