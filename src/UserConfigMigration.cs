using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WalkNWash.VRCompanion
{
    // File-only migration: safe to run before BepInEx constructs the plugin.
    internal static class UserConfigMigration
    {
        internal const int Schema = 1;
        internal sealed class Entry
        {
            internal string Section, Key, Value, Default;
        }
        internal static List<Entry> Read(string text)
        {
            var entries = new List<Entry>(); string section = "", defaultValue = null;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                { section = line.Substring(1, line.Length - 2); defaultValue = null; continue; }
                const string prefix = "# Default value:";
                if (line.StartsWith(prefix, StringComparison.Ordinal)) { defaultValue = line.Substring(prefix.Length).Trim(); continue; }
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                int split = line.IndexOf('='); if (split < 1) continue;
                entries.Add(new Entry { Section = section, Key = line.Substring(0, split).Trim(), Value = line.Substring(split + 1).Trim(), Default = defaultValue });
                defaultValue = null;
            }
            return entries;
        }
        internal static bool IsPreference(string section, string key)
        {
            if (section == "Input" || section == "UI" || section == "Camera") return true;
            if (section == "General") return key == "Enabled" || key == "Start VR Automatically";
            return section == "Hands" && new[] { "Tool Hand", "Left Wrist Position Offset", "Right Wrist Position Offset", "Hand Pullback", "Left Hand Left Shift", "Hand Reach", "Contact Haptics", "Left Hand Rotation Offset", "Left Palm Reach Tilt" }.Contains(key);
        }
        private static string Normalize(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
        internal static string ConvertLegacy(string text)
        {
            var retained = Read(text).Where(e => IsPreference(e.Section, e.Key)
                || ((e.Section == "Tool Grips" || e.Section == "Tool Grips Left")
                    && (e.Default == null || Normalize(e.Value) != Normalize(e.Default))));
            var result = new StringBuilder("# Persistent preferences; independent of plugin version.\n[General]\nSettings Schema = 1\n");
            foreach (var group in retained.GroupBy(e => e.Section))
            {
                result.Append("\n[").Append(group.Key).Append("]\n");
                foreach (var entry in group) result.Append(entry.Key).Append(" = ").Append(entry.Value).Append('\n');
            }
            return result.ToString();
        }
        internal static void Ensure(string legacyPath, string userPath)
        {
            if (File.Exists(userPath))
            {
                var schema = Read(File.ReadAllText(userPath)).LastOrDefault(e => e.Section == "General" && e.Key == "Settings Schema");
                if (schema != null && (!int.TryParse(schema.Value, out int version) || version != Schema))
                    throw new InvalidDataException("Unsupported user settings schema; file left unchanged: " + schema.Value);
                return; // Existing preferences always win; never reimport stale legacy values.
            }
            Directory.CreateDirectory(Path.GetDirectoryName(userPath));
            string temp = userPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, ConvertLegacy(File.Exists(legacyPath) ? File.ReadAllText(legacyPath) : ""));
                File.Move(temp, userPath); // Publish completely before legacy config may be reset.
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
