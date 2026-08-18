using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using vatsys;

namespace vatSysCannedMessages
{
    /// <summary>
    /// Sends a private message to a callsign.
    ///
    /// vatSys exposes Network.SendRadioMessage(string) publicly but keeps the
    /// private-message path internal (Network.Instance.SendTextMessage), so this
    /// goes through reflection. Everything is resolved once and cached, and any
    /// failure is reported rather than swallowed - the window falls back to
    /// putting the message on the clipboard.
    /// </summary>
    internal static class Sender
    {
        private static readonly object Sync = new object();

        private static bool resolved;
        private static FieldInfo instanceField;
        private static MethodInfo sendTextMessage;
        private static string resolveError;

        /// <summary>Why sending is unavailable, or null if it is available.</summary>
        public static string Unavailable
        {
            get
            {
                Resolve();
                return resolveError;
            }
        }

        public static bool CanSend
        {
            get { return Unavailable == null && IsConnected; }
        }

        public static bool IsConnected
        {
            get
            {
                try
                {
                    return Network.IsConnected;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Sends <paramref name="message"/> to <paramref name="recipient"/>,
        /// one private message per line, wrapping lines longer than
        /// <paramref name="maxLength"/> on word boundaries.
        /// </summary>
        public static void SendPrivateMessage(string recipient, string message, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(recipient))
                throw new InvalidOperationException("No recipient callsign.");

            if (string.IsNullOrWhiteSpace(message))
                throw new InvalidOperationException("Message is empty.");

            Resolve();
            if (resolveError != null) throw new InvalidOperationException(resolveError);

            if (!IsConnected)
                throw new InvalidOperationException("Not connected to the network.");

            var target = recipient.Trim().ToUpperInvariant();
            var instance = instanceField.GetValue(null);
            if (instance == null) throw new InvalidOperationException("vatSys network session is not available.");

            foreach (var line in Split(message, maxLength))
            {
                try
                {
                    sendTextMessage.Invoke(instance, new object[] { target, line });
                }
                catch (TargetInvocationException ex)
                {
                    throw ex.InnerException ?? ex;
                }
            }
        }

        /// <summary>Splits on explicit line breaks, then wraps over-long lines.</summary>
        public static List<string> Split(string message, int maxLength)
        {
            var result = new List<string>();
            var lines = (message ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;

                if (maxLength <= 0 || line.Length <= maxLength)
                {
                    result.Add(line);
                    continue;
                }

                var current = new StringBuilder();
                foreach (var word in line.Split(' '))
                {
                    if (word.Length == 0) continue;

                    // A single word longer than the limit gets hard-split.
                    if (word.Length > maxLength)
                    {
                        if (current.Length > 0)
                        {
                            result.Add(current.ToString());
                            current.Length = 0;
                        }

                        for (var i = 0; i < word.Length; i += maxLength)
                            result.Add(word.Substring(i, Math.Min(maxLength, word.Length - i)));

                        continue;
                    }

                    if (current.Length > 0 && current.Length + 1 + word.Length > maxLength)
                    {
                        result.Add(current.ToString());
                        current.Length = 0;
                    }

                    if (current.Length > 0) current.Append(' ');
                    current.Append(word);
                }

                if (current.Length > 0) result.Add(current.ToString());
            }

            return result;
        }

        private static void Resolve()
        {
            lock (Sync)
            {
                if (resolved) return;
                resolved = true;

                try
                {
                    var networkType = typeof(Network);

                    instanceField = networkType.GetField("Instance",
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

                    if (instanceField == null)
                    {
                        resolveError = "This vatSys build does not expose Network.Instance - private messaging is unavailable.";
                        return;
                    }

                    sendTextMessage = networkType.GetMethod("SendTextMessage",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
                        null,
                        new[] { typeof(string), typeof(string) },
                        null);

                    if (sendTextMessage == null)
                    {
                        resolveError = "This vatSys build does not expose Network.SendTextMessage - private messaging is unavailable.";
                        return;
                    }
                }
                catch (Exception ex)
                {
                    resolveError = "Could not hook the vatSys network layer: " + ex.Message;
                }
            }
        }
    }
}
