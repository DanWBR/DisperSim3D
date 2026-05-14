using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using DisperSim3D.Models;

namespace DisperSim3D.Core
{
    /// <summary>
    /// A self-contained DisperSim 3D project file (.dsproj). Internally a ZIP archive containing
    /// project.xml, manifest.json, embedded geometry assets and (optionally) embedded OpenFOAM
    /// case files. On <see cref="Open"/> the bundle is extracted to a session temp directory and
    /// every <c>bundle://</c> URI inside the XML is rewritten to its extracted absolute path, so
    /// existing engines and renderers that expect real files keep working unchanged.
    /// </summary>
    public sealed class ProjectBundle : IDisposable
    {
        public const string BundleExtension = ".dsproj";
        public const string ManifestEntryName = "manifest.json";
        public const string ProjectXmlEntryName = "project.xml";
        public const int CurrentBundleVersion = 1;

        public string BundleRoot { get; private set; }
        public XDocument ProjectXml { get; private set; }
        public bool OwnsTempDir { get; private set; }

        private ProjectBundle() { }

        /// <summary>
        /// Opens a .dsproj bundle, extracts it to a session temp directory and returns the
        /// path-resolved project XML. The caller may then run their existing XML deserializer
        /// over <see cref="ProjectXml"/>.
        /// </summary>
        public static ProjectBundle Open(string filePath, Action<string, double> progress = null)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Bundle file not found", filePath);

            string tempRoot = CreateSessionTempDir();
            try
            {
                progress?.Invoke("Extracting bundle archive...", 0.30);
                ZipFile.ExtractToDirectory(filePath, tempRoot);
            }
            catch
            {
                TryDelete(tempRoot);
                throw;
            }

            string projectXmlPath = Path.Combine(tempRoot, ProjectXmlEntryName);
            if (!File.Exists(projectXmlPath))
            {
                TryDelete(tempRoot);
                throw new InvalidDataException("Bundle is missing " + ProjectXmlEntryName);
            }

            progress?.Invoke("Reading project XML from bundle...", 0.80);
            var doc = XDocument.Load(projectXmlPath);
            RewriteBundlePathsToAbsolute(doc, tempRoot);
            progress?.Invoke("Bundle ready.", 1.0);

            return new ProjectBundle
            {
                BundleRoot = tempRoot,
                ProjectXml = doc,
                OwnsTempDir = true
            };
        }

        /// <summary>
        /// Saves a project to a .dsproj bundle. Walks <paramref name="doc"/> and copies every
        /// referenced decoration asset and OpenFOAM case folder into the bundle, rewriting the
        /// attributes to <c>bundle://</c> URIs inside the saved project.xml. The in-memory
        /// <paramref name="doc"/> and <paramref name="scene"/> are NOT mutated.
        /// </summary>
        public static void Save(string outPath, Scene3D scene, XDocument doc,
            Action<string, double> progress = null)
        {
            if (string.IsNullOrEmpty(outPath)) throw new ArgumentNullException(nameof(outPath));
            if (scene == null) throw new ArgumentNullException(nameof(scene));
            if (doc == null) throw new ArgumentNullException(nameof(doc));

            string stagingRoot = CreateSessionTempDir("staging-");
            try
            {
                // Clone the doc so we can safely rewrite without touching the caller's instance.
                var stagedDoc = new XDocument(doc);

                progress?.Invoke("Copying decoration assets...", 0.05);
                CopyDecorationAssets(stagedDoc, scene, stagingRoot);
                progress?.Invoke("Copying wind field cases...", 0.20);
                CopyWindFieldCases(stagedDoc, scene, stagingRoot);
                progress?.Invoke("Copying simulation cases...", 0.40);
                CopySimulationCases(stagedDoc, scene, stagingRoot);

                // manifest.json
                progress?.Invoke("Writing manifest...", 0.65);
                File.WriteAllText(Path.Combine(stagingRoot, ManifestEntryName), BuildManifestJson(),
                    new UTF8Encoding(false));

                // project.xml (with bundle:// rewrites)
                progress?.Invoke("Writing project XML...", 0.70);
                stagedDoc.Save(Path.Combine(stagingRoot, ProjectXmlEntryName));

                // Pack into zip with Fastest compression. Bundle content is dominated by
                // .bin field snapshots (typed double[,,] arrays — entropy is already high,
                // so deflate gives only ~5 % size reduction with Optimal vs. Fastest, while
                // taking 5–10× longer on large grids). Fastest keeps Save responsive even
                // for 1000-snapshot dispersion runs.
                progress?.Invoke("Packing bundle archive...", 0.80);
                string tmpZip = outPath + ".tmp";
                if (File.Exists(tmpZip)) File.Delete(tmpZip);
                ZipFile.CreateFromDirectory(stagingRoot, tmpZip, CompressionLevel.Fastest,
                    includeBaseDirectory: false);

                progress?.Invoke("Replacing target file...", 0.98);
                if (File.Exists(outPath)) File.Delete(outPath);
                File.Move(tmpZip, outPath);
                progress?.Invoke("Bundle written.", 1.0);
            }
            finally
            {
                TryDelete(stagingRoot);
            }
        }

        /// <summary>Returns true if the file extension matches a .dsproj bundle.</summary>
        public static bool IsBundleFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            return string.Equals(Path.GetExtension(filePath), BundleExtension,
                StringComparison.OrdinalIgnoreCase);
        }

        public void Dispose()
        {
            if (OwnsTempDir && !string.IsNullOrEmpty(BundleRoot))
                TryDelete(BundleRoot);
            BundleRoot = null;
            ProjectXml = null;
        }

        // ─── path rewriting (load) ──────────────────────────────────────────

        private static void RewriteBundlePathsToAbsolute(XDocument doc, string bundleRoot)
        {
            foreach (var el in doc.Descendants("Decoration"))
                RewriteAttr(el, "FilePath", BundlePathResolver.ToAbsolute, bundleRoot);

            foreach (var el in doc.Descendants("WindFieldScenario"))
                RewriteAttr(el, "CasePath", BundlePathResolver.ToAbsolute, bundleRoot);

            foreach (var el in doc.Descendants("Simulation"))
                RewriteAttr(el, "CasePath", BundlePathResolver.ToAbsolute, bundleRoot);
        }

        private static void RewriteAttr(XElement el, string attrName,
            Func<string, string, string> rewrite, string bundleRoot)
        {
            var a = el.Attribute(attrName);
            if (a == null || string.IsNullOrEmpty(a.Value)) return;
            a.Value = rewrite(a.Value, bundleRoot);
        }

        // ─── path rewriting + copy (save) ───────────────────────────────────

        private static void CopyDecorationAssets(XDocument doc, Scene3D scene, string bundleRoot)
        {
            foreach (var el in doc.Descendants("Decoration"))
            {
                var idAttr = el.Attribute("Id");
                var filePathAttr = el.Attribute("FilePath");
                if (filePathAttr == null) continue;
                string original = filePathAttr.Value;
                if (string.IsNullOrEmpty(original)) continue;
                if (BundlePathResolver.IsBundlePath(original)) continue;
                if (!File.Exists(original)) continue;

                string id = idAttr != null ? idAttr.Value : Guid.NewGuid().ToString();
                string fileName = Path.GetFileName(original);
                string relDir = "assets/geometry/" + id;
                string destDir = Path.Combine(bundleRoot, "assets", "geometry", id);
                Directory.CreateDirectory(destDir);
                string destPath = Path.Combine(destDir, fileName);
                File.Copy(original, destPath, overwrite: true);

                filePathAttr.Value = BundlePathResolver.Scheme + relDir + "/" + fileName;
            }
        }

        private static void CopyWindFieldCases(XDocument doc, Scene3D scene, string bundleRoot)
        {
            var byId = new Dictionary<string, WindFieldScenario>(StringComparer.OrdinalIgnoreCase);
            if (scene.WindFieldScenarios != null)
                foreach (var w in scene.WindFieldScenarios)
                    if (!string.IsNullOrEmpty(w.Id) && !byId.ContainsKey(w.Id))
                        byId[w.Id] = w;

            foreach (var el in doc.Descendants("WindFieldScenario"))
            {
                CopyCaseFolder(el, byId, bundleRoot, "cases/windfields",
                    w => w != null ? w.EmbedMode : BundleEmbedMode.ResultsOnly);
            }
        }

        private static void CopySimulationCases(XDocument doc, Scene3D scene, string bundleRoot)
        {
            var byId = new Dictionary<string, Simulation>(StringComparer.OrdinalIgnoreCase);
            if (scene.Simulations != null)
                foreach (var s in scene.Simulations)
                    if (!string.IsNullOrEmpty(s.Id) && !byId.ContainsKey(s.Id))
                        byId[s.Id] = s;

            foreach (var el in doc.Descendants("Simulation"))
            {
                CopyCaseFolder(el, byId, bundleRoot, "cases/simulations",
                    s => s != null ? s.EmbedMode : BundleEmbedMode.ResultsOnly);
            }
        }

        private static void CopyCaseFolder<T>(XElement el, Dictionary<string, T> byId,
            string bundleRoot, string subRoot, Func<T, BundleEmbedMode> embedSelector) where T : class
        {
            var idAttr = el.Attribute("Id");
            var caseAttr = el.Attribute("CasePath");
            if (caseAttr == null) return;
            string original = caseAttr.Value;
            if (string.IsNullOrEmpty(original)) return;
            if (BundlePathResolver.IsBundlePath(original)) return;
            if (!Directory.Exists(original)) return;

            string id = idAttr != null && !string.IsNullOrEmpty(idAttr.Value)
                ? idAttr.Value : Guid.NewGuid().ToString();

            T model = null;
            if (byId.TryGetValue(id, out var lookup)) model = lookup;
            var mode = embedSelector(model);

            string relDir = subRoot + "/" + id + "/case";
            string destRoot = Path.Combine(bundleRoot,
                subRoot.Replace('/', Path.DirectorySeparatorChar),
                id, "case");
            Directory.CreateDirectory(destRoot);

            foreach (var src in CaseBundler.EnumerateFilesToBundle(original, mode))
            {
                string rel = src.Substring(original.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                string dst = Path.Combine(destRoot, rel);
                string dstDir = Path.GetDirectoryName(dst);
                if (!string.IsNullOrEmpty(dstDir)) Directory.CreateDirectory(dstDir);
                try
                {
                    File.Copy(src, dst, overwrite: true);
                }
                catch (IOException) { /* skip locked files */ }
                catch (UnauthorizedAccessException) { }
            }

            caseAttr.Value = BundlePathResolver.Scheme + relDir;
        }

        // ─── plumbing ───────────────────────────────────────────────────────

        private static string BuildManifestJson()
        {
            var sb = new StringBuilder();
            sb.Append("{");
            sb.Append("\"format\":\"dsproj\",");
            sb.Append("\"version\":").Append(CurrentBundleVersion).Append(",");
            sb.Append("\"created\":\"").Append(DateTime.UtcNow.ToString("o")).Append("\",");
            sb.Append("\"app\":\"DisperSim3D\"");
            sb.Append("}");
            return sb.ToString();
        }

        private static string CreateSessionTempDir(string prefix = null)
        {
            string baseDir = Path.Combine(TempManager.GetWorkDir(), "DisperSim3D");
            Directory.CreateDirectory(baseDir);
            PurgeStaleSessions(baseDir, TimeSpan.FromDays(7));
            string name = (prefix ?? "") + Guid.NewGuid().ToString("N");
            string full = Path.Combine(baseDir, name);
            Directory.CreateDirectory(full);
            return full;
        }

        private static void PurgeStaleSessions(string baseDir, TimeSpan maxAge)
        {
            try
            {
                var cutoff = DateTime.UtcNow - maxAge;
                foreach (var d in Directory.EnumerateDirectories(baseDir))
                {
                    try
                    {
                        var info = new DirectoryInfo(d);
                        if (info.LastWriteTimeUtc < cutoff)
                            Directory.Delete(d, recursive: true);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void TryDelete(string dir)
        {
            try { if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
            catch { }
        }
    }
}
