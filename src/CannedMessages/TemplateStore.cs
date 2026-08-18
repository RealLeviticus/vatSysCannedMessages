using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using vatsys;

namespace vatSysCannedMessages
{
    /// <summary>
    /// Owns the canned message and name lists.
    ///
    /// Three layers, lowest priority first:
    ///   1. templates/ shipped next to the plugin DLL (works offline, first run)
    ///   2. the copy pulled from the GitHub repository, cached on disk
    ///   3. local-messages.json / local-names.json in the vatSys Files folder
    ///
    /// A local template whose id matches a repository template replaces it,
    /// which is how a controller keeps a private tweak of a shared message.
    /// </summary>
    internal static class TemplateStore
    {
        public const string DefaultRawBaseUrl =
            "https://raw.githubusercontent.com/RealLeviticus/vatSysCannedMessages/main/templates/";

        private const string MessagesFileName = "messages.json";
        private const string NamesFileName = "names.json";

        private static readonly object Sync = new object();

        private static MessageFile messages = new MessageFile();
        private static NamesFile names = new NamesFile();

        /// <summary>Raised on a background thread after a successful refresh.</summary>
        public static event EventHandler Updated;

        public static string LastSyncStatus { get; private set; }

        public static PluginConfig Config { get; private set; }

        #region Paths

        /// <summary>Documents\vatSys Files\CannedMessages\</summary>
        public static string DataFolder
        {
            get { return Path.Combine(Helpers.GetFilesFolder(), "CannedMessages"); }
        }

        public static string CacheFolder
        {
            get { return Path.Combine(DataFolder, "cache"); }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(DataFolder, "config.json"); }
        }

        public static string LocalMessagesPath
        {
            get { return Path.Combine(DataFolder, "local-messages.json"); }
        }

        public static string LocalNamesPath
        {
            get { return Path.Combine(DataFolder, "local-names.json"); }
        }

        /// <summary>templates\ folder shipped alongside the plugin DLL.</summary>
        public static string BundledFolder
        {
            get
            {
                var dll = Assembly.GetExecutingAssembly().Location;
                var dir = string.IsNullOrEmpty(dll) ? null : Path.GetDirectoryName(dll);
                return dir == null ? null : Path.Combine(dir, "templates");
            }
        }

        #endregion

        #region Public accessors

        public static List<MessageCategory> Categories
        {
            get { lock (Sync) return messages.SafeCategories.ToList(); }
        }

        public static List<string> Names
        {
            get { lock (Sync) return names.SafeNames.ToList(); }
        }

        #endregion

        /// <summary>
        /// Loads whatever is already on disk. Cheap and synchronous - safe to
        /// call from the plugin constructor during vatSys startup.
        /// </summary>
        public static void LoadFromDisk()
        {
            EnsureConfig();

            var mergedMessages = new MessageFile();
            var mergedNames = new NamesFile();

            var bundled = BundledFolder;
            if (bundled != null)
            {
                MergeMessages(mergedMessages, TryRead<MessageFile>(Path.Combine(bundled, MessagesFileName)));
                MergeNames(mergedNames, TryRead<NamesFile>(Path.Combine(bundled, NamesFileName)));
            }

            MergeMessages(mergedMessages, TryRead<MessageFile>(Path.Combine(CacheFolder, MessagesFileName)));
            MergeNames(mergedNames, TryRead<NamesFile>(Path.Combine(CacheFolder, NamesFileName)));

            MergeMessages(mergedMessages, TryRead<MessageFile>(LocalMessagesPath));
            MergeNames(mergedNames, TryRead<NamesFile>(LocalNamesPath));

            lock (Sync)
            {
                messages = mergedMessages;
                names = mergedNames;
            }
        }

        /// <summary>
        /// Pulls messages.json and names.json from the repository, writes them
        /// to the cache and reloads. Blocking - call it off the GUI thread.
        /// </summary>
        /// <returns>True if at least one file was refreshed.</returns>
        public static bool Refresh()
        {
            EnsureConfig();

            var baseUrl = Config.RawBaseUrl;
            if (string.IsNullOrEmpty(baseUrl)) baseUrl = DefaultRawBaseUrl;
            if (!baseUrl.EndsWith("/")) baseUrl += "/";

            var timeout = Config.TimeoutSeconds.HasValue && Config.TimeoutSeconds.Value > 0
                ? Config.TimeoutSeconds.Value
                : 10;

            var downloaded = 0;
            var problems = new List<string>();

            foreach (var file in new[] { MessagesFileName, NamesFileName })
            {
                try
                {
                    var body = Download(baseUrl + file, timeout);

                    // Parse before caching so a malformed push cannot poison the
                    // cache and take the plugin down on the next start.
                    if (file == MessagesFileName) Json.Deserialize<MessageFile>(body);
                    else Json.Deserialize<NamesFile>(body);

                    Directory.CreateDirectory(CacheFolder);
                    File.WriteAllText(Path.Combine(CacheFolder, file), body, new UTF8Encoding(false));
                    downloaded++;
                }
                catch (Exception ex)
                {
                    problems.Add(file + ": " + ex.Message);
                }
            }

            LastSyncStatus = problems.Count == 0
                ? "Synced " + DateTime.UtcNow.ToString("HH:mm") + "Z from " + baseUrl
                : "Sync failed - " + string.Join("; ", problems.ToArray());

            LoadFromDisk();

            var handler = Updated;
            if (handler != null) handler(null, EventArgs.Empty);

            return downloaded > 0;
        }

        private static string Download(string url, int timeoutSeconds)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            // Cache buster - raw.githubusercontent.com sits behind a CDN that
            // otherwise serves a stale copy for a few minutes after a push.
            var separator = url.Contains("?") ? "&" : "?";
            var request = (HttpWebRequest)WebRequest.Create(url + separator + "cb=" + DateTime.UtcNow.Ticks);
            request.Timeout = timeoutSeconds * 1000;
            request.ReadWriteTimeout = timeoutSeconds * 1000;
            request.UserAgent = "vatSys-CannedMessages/" + Assembly.GetExecutingAssembly().GetName().Version;
            request.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            {
                if (stream == null) throw new IOException("Empty response.");
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        #region Merging

        private static void MergeMessages(MessageFile target, MessageFile source)
        {
            if (source == null) return;

            foreach (var category in source.SafeCategories)
            {
                if (category == null) continue;

                var name = string.IsNullOrEmpty(category.Name) ? "Uncategorised" : category.Name;
                var existing = target.SafeCategories
                    .FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

                if (existing == null)
                {
                    existing = new MessageCategory { Name = name, Messages = new List<MessageTemplate>() };
                    target.SafeCategories.Add(existing);
                }

                foreach (var message in category.SafeMessages)
                {
                    if (message == null || string.IsNullOrEmpty(message.Text)) continue;

                    var replaced = false;
                    if (!string.IsNullOrEmpty(message.Id))
                    {
                        for (var i = 0; i < existing.SafeMessages.Count; i++)
                        {
                            if (!string.Equals(existing.SafeMessages[i].Id, message.Id, StringComparison.OrdinalIgnoreCase))
                                continue;

                            existing.SafeMessages[i] = message;
                            replaced = true;
                            break;
                        }
                    }

                    if (!replaced) existing.SafeMessages.Add(message);
                }
            }
        }

        private static void MergeNames(NamesFile target, NamesFile source)
        {
            if (source == null) return;

            foreach (var name in source.SafeNames)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (target.SafeNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase))) continue;
                target.SafeNames.Add(name.Trim());
            }
        }

        #endregion

        private static T TryRead<T>(string path) where T : class
        {
            try
            {
                return Json.ReadFile<T>(path);
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not read " + path + ": " + ex.Message, ex), Plugin.PluginName);
                return null;
            }
        }

        private static void EnsureConfig()
        {
            if (Config != null) return;

            PluginConfig loaded = null;
            try
            {
                loaded = Json.ReadFile<PluginConfig>(ConfigPath);
            }
            catch (Exception ex)
            {
                Errors.Add(new Exception("Could not read config.json: " + ex.Message, ex), Plugin.PluginName);
            }

            if (loaded == null)
            {
                loaded = new PluginConfig
                {
                    RawBaseUrl = DefaultRawBaseUrl,
                    RefreshOnStartup = true,
                    TimeoutSeconds = 10,
                    DefaultName = string.Empty,
                    MaxMessageLength = 200
                };

                try
                {
                    Json.WriteFile(ConfigPath, loaded);
                }
                catch (Exception ex)
                {
                    Errors.Add(new Exception("Could not write config.json: " + ex.Message, ex), Plugin.PluginName);
                }
            }

            Config = loaded;
        }
    }
}
