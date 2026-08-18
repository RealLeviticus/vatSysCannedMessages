using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using vatsys;

namespace vatSysCannedMessages
{
    /// <summary>
    /// Finds and fills {placeholder} tokens in a message template.
    /// </summary>
    internal static class Placeholders
    {
        private static readonly Regex Token = new Regex(@"\{([A-Za-z0-9_\-]+)\}", RegexOptions.Compiled);

        /// <summary>
        /// Placeholders vatSys can answer by itself, so the window does not
        /// bother asking the controller for them.
        /// </summary>
        public static readonly string[] Automatic = { "callsign", "recipient", "time", "date" };

        /// <summary>Placeholder keys used by the template, in order of first appearance.</summary>
        public static List<string> Find(string text)
        {
            var found = new List<string>();
            if (string.IsNullOrEmpty(text)) return found;

            foreach (Match match in Token.Matches(text))
            {
                var key = match.Groups[1].Value;
                if (!found.Contains(key, StringComparer.OrdinalIgnoreCase)) found.Add(key);
            }

            return found;
        }

        public static bool IsAutomatic(string key)
        {
            foreach (var automatic in Automatic)
                if (string.Equals(automatic, key, StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        public static string ResolveAutomatic(string key, string recipient)
        {
            switch (key.ToLowerInvariant())
            {
                case "callsign":
                    var callsign = SafeCallsign();
                    return string.IsNullOrEmpty(callsign) ? "{callsign}" : callsign;
                case "recipient":
                    return string.IsNullOrEmpty(recipient) ? "{recipient}" : recipient.Trim().ToUpperInvariant();
                case "time":
                    return DateTime.UtcNow.ToString("HHmm");
                case "date":
                    return DateTime.UtcNow.ToString("ddMMM").ToUpperInvariant();
                default:
                    return null;
            }
        }

        /// <summary>
        /// Substitutes every token. Values supplied in <paramref name="values"/>
        /// win; automatic tokens fill themselves in; anything still unknown is
        /// left as {token} so the controller can see what is missing.
        /// </summary>
        public static string Fill(string text, IDictionary<string, string> values, string recipient)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            return Token.Replace(text, match =>
            {
                var key = match.Groups[1].Value;

                string value;
                if (values != null && values.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value))
                    return value.Trim();

                var automatic = ResolveAutomatic(key, recipient);
                return automatic ?? match.Value;
            });
        }

        /// <summary>True once every token has something to fill it with.</summary>
        public static bool IsComplete(string filled)
        {
            return !Token.IsMatch(filled ?? string.Empty);
        }

        private static string SafeCallsign()
        {
            try
            {
                return Network.IsConnected ? Network.Callsign : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
