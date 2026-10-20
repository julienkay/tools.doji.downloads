using System;
using System.IO;
using Newtonsoft.Json;

namespace Doji.Downloads
{
    /// <summary>
    /// Reads and writes durable state as JSON files.
    /// </summary>
    public static class JsonStateStore
    {
        /// <summary>
        /// Reads previously saved state. Invalid or missing files return null so callers can start fresh.
        /// </summary>
        public static TState Load<TState>(string path)
            where TState : class
        {
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<TState>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Saves state through temporary file replacement so interrupted writes do not corrupt active state.
        /// </summary>
        public static void Save<TState>(string path, TState state)
        {
            var directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InvalidOperationException("Missing persistent state directory.");
            }

            Directory.CreateDirectory(directory);
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(state, Formatting.Indented));

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            File.Move(temporaryPath, path);
        }

        /// <summary>
        /// Removes previously saved state file if present.
        /// </summary>
        public static void Delete(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
