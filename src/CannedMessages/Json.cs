using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Text;

namespace vatSysCannedMessages
{
    /// <summary>
    /// Thin wrapper over DataContractJsonSerializer so the plugin has no
    /// third-party dependencies and cannot be broken by a Newtonsoft version
    /// change inside vatSys.
    /// </summary>
    internal static class Json
    {
        public static T Deserialize<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json)) return null;

            // Strip a UTF-8 BOM if a contributor's editor left one behind.
            if (json[0] == 0xFEFF) json = json.Substring(1);

            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (T)serializer.ReadObject(stream);
            }
        }

        public static string Serialize<T>(T value)
        {
            var serializer = new DataContractJsonSerializer(typeof(T));
            using (var stream = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true, "  "))
                {
                    serializer.WriteObject(writer, value);
                    writer.Flush();
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static T ReadFile<T>(string path) where T : class
        {
            if (!File.Exists(path)) return null;
            return Deserialize<T>(File.ReadAllText(path, Encoding.UTF8));
        }

        public static void WriteFile<T>(string path, T value)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            // UTF8Encoding(false) so we never write a BOM of our own.
            File.WriteAllText(path, Serialize(value), new UTF8Encoding(false));
        }
    }
}
