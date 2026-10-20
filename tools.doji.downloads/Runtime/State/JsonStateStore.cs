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
        /// Reads saved state. A missing file or invalid JSON returns null, so the caller starts fresh.
        /// </summary>
        /// <exception cref="IOException">The file exists but cannot be read, for example because another process holds it.</exception>
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
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>
        /// Saves state through temporary file replacement so interrupted writes do not corrupt active state.
        /// </summary>
        public static void Save<TState>(string path, TState state)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("State path is required.", nameof(path));
            }

            // A bare file name has no directory part; resolve it against the current directory.
            path = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(state, Formatting.Indented));

            if (File.Exists(path))
            {
                File.Replace(temporaryPath, path, null);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
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
