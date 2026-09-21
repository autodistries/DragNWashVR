using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;

namespace WalkNWash.VRCompanion
{
    internal sealed class UserSettings
    {
        private readonly ConfigFile file, versioned;
        private readonly List<ConfigEntryBase> entries = new List<ConfigEntryBase>();
        internal static ConfigEntry<bool> DialogueFollowsView { get; private set; }

        internal UserSettings(ConfigFile versioned = null, string directory = null)
        {
            directory = directory ?? Paths.ConfigPath;
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, Plugin.Id + ".user.cfg");
            this.versioned = versioned ?? Plugin.Current?.Config
                ?? new ConfigFile(Path.Combine(directory, Plugin.Id + ".cfg"), false);

            string existing = File.Exists(path) ? File.ReadAllText(path) : null;
            // Early development builds put a schema marker and many internal
            // values in the user file. Defaults are fine for those builds, so
            // discard that old format once instead of carrying migration code.
            if (existing != null && existing.IndexOf("Settings Schema", System.StringComparison.Ordinal) >= 0)
            {
                File.Delete(path);
            }

            file = new ConfigFile(path, false) { SaveOnConfigSet = true };

            RemoveObsoletePreferences(file);
            RemoveObsoletePreferences(this.versioned);

            // Bind the persistent entries up front to define their display order.
            entries.Add(file.Bind("Camera", "Eye Height Offset", 0f,
                "Additional eye height in game world units after recentering. Use this instead of UnityVRMod's eye offset."));
            entries.Add(file.Bind("Hands", "Contact Haptics", true,
                "Brief feedback on contact and while rubbing."));
            entries.Add(file.Bind("Hands", "Trigger Extend", true,
                "Let the matching use trigger add the assisted forward reach after physical contact misses. Disable to keep hands and the sponge strictly physical; contact works either way."));
            var toolHand = file.Bind("Hands", "Tool Hand", ToolController.Right,
                "Controller holding equipped tools. The other controller is the free/slap hand. Menus always use the right controller.");
            toolHand.SettingChanged += (_, __) => ToolHandRuntime.Apply(this, toolHand.Value);
            entries.Add(toolHand);
            entries.Add(file.Bind("Input", "Smooth Turn Speed", 135f,
                new ConfigDescription("Degrees per second at full stick deflection.", new AcceptableValueRange<float>(0f, 360f))));
            entries.Add(file.Bind("Input", "Smooth Turning", true,
                "Use continuous right-stick turning. Disable to use Snap Turn Degrees instead."));
            entries.Add(file.Bind("Input", "Snap Turn Degrees", 30f,
                new ConfigDescription("One turn per right-stick deflection; release stick to turn again.", new AcceptableValueRange<float>(0f, 90f))));
            entries.Add(file.Bind("Hands", "Hand Reach", 1.5f,
                new ConfigDescription("Maximum assisted forward reach while Trigger Extend is enabled and the matching use trigger is held. Physical contact works regardless of this value.", new AcceptableValueRange<float>(0f, 2f))));
            DialogueFollowsView = file.Bind("UI", "Dialogue Follows View", false,
                "Keep game dialogue fixed relative to the headset view instead of leaving each conversation anchored in world space.");
            entries.Add(DialogueFollowsView);
            file.Save();
        }

        private static readonly string[][] ObsoletePreferences =
        {
            new[] { "Hands", "Contact-Driven Use" },
            // Cutscene freecam is driven by the left Y binding, so its entry moved
            // back to the versioned config and the user file keeps preferences only.
            new[] { "Camera", "Cutscene Freecam" },
        };

        private static void RemoveObsoletePreferences(ConfigFile config)
        {
            if (!File.Exists(config.ConfigFilePath)) return;
            string existing = File.ReadAllText(config.ConfigFilePath);
            bool saveOnSet = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try
            {
                bool removed = false;
                foreach (string[] obsolete in ObsoletePreferences)
                {
                    if (existing.IndexOf(obsolete[1] + " =", System.StringComparison.Ordinal) < 0) continue;
                    // Bind consumes BepInEx's orphaned entry; Remove alone would retain it on disk.
                    var entry = config.Bind(obsolete[0], obsolete[1], false, "Obsolete draft preference.");
                    config.Remove(entry.Definition);
                    removed = true;
                }
                if (removed) config.Save();
            }
            finally { config.SaveOnConfigSet = saveOnSet; }
        }

        internal IEnumerable<ConfigEntryBase> Entries => entries;

        private static bool Persistent(string section, string key)
        {
            if (section == "Camera") return key == "Eye Height Offset";
            if (section == "Hands") return key == "Contact Haptics" || key == "Trigger Extend" || key == "Tool Hand" || key == "Hand Reach";
            if (section == "UI") return key == "Dialogue Follows View";
            return section == "Input" && (key == "Smooth Turn Speed" || key == "Smooth Turning" || key == "Snap Turn Degrees");
        }

        internal ConfigEntry<T> Bind<T>(string section, string key, T value, string description)
            => Bind(section, key, value, new ConfigDescription(description));

        internal ConfigEntry<T> Bind<T>(string section, string key, T value, ConfigDescription description)
        {
            if (!Persistent(section, key))
                return versioned.Bind(section, key, value, description);
            return file.Bind(section, key, value, description);
        }

        internal void Save()
        {
            file.Save();
            versioned.Save();
        }
    }
}
