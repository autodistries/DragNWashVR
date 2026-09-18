using System;
using System.IO;
using System.Reflection;
using BepInEx;

namespace System.Runtime.CompilerServices
{
    // netstandard2.1 does not expose this attribute, but the C# compiler
    // recognizes it by full name and emits a module initializer.
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute { }
}

namespace WalkNWash.VRCompanion
{
    /// <summary>
    /// Resets the plugin config before BaseUnityPlugin constructs its ConfigFile
    /// when the installed plugin version differs from the version that last ran.
    /// Running before Plugin.Awake avoids BepInEx retaining old values as orphaned
    /// config entries in memory.
    /// </summary>
    internal static class ConfigVersionReset
    {
        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Initialize()
        {
            try
            {
                string configDirectory = Paths.ConfigPath;
                if (string.IsNullOrEmpty(configDirectory)) return;

                string currentVersion = GetPluginVersion();
                if (string.IsNullOrEmpty(currentVersion)) return;

                string markerPath = Path.Combine(configDirectory, Plugin.Id + ".version");
                string previousVersion = File.Exists(markerPath)
                    ? File.ReadAllText(markerPath).Trim()
                    : null;

                if (string.Equals(previousVersion, currentVersion, StringComparison.Ordinal))
                    return;

                Directory.CreateDirectory(configDirectory);
                string configPath = Path.Combine(configDirectory, Plugin.Id + ".cfg");

                if (File.Exists(configPath))
                {
                    try
                    {
                        File.Copy(configPath, configPath + ".bak", true);
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine("Walk N Wash VR Companion config backup failed: " + e.Message);
                    }

                    // BaseUnityPlugin has not constructed its ConfigFile yet, so
                    // truncating here guarantees later Config.Bind calls see no
                    // old values and therefore apply their declared defaults.
                    File.WriteAllText(configPath, string.Empty);
                }

                // Write the marker only after the reset succeeds. If this write
                // fails, the next launch safely attempts the migration again.
                File.WriteAllText(markerPath, currentVersion);

                Console.WriteLine(
                    "Walk N Wash VR Companion config reset for version change " +
                    (string.IsNullOrEmpty(previousVersion) ? "<unknown>" : previousVersion) +
                    " -> " + currentVersion + ".");
            }
            catch (Exception e)
            {
                // A config migration failure must never prevent the plugin assembly
                // from loading. Leave the marker unchanged so a later run can retry.
                Console.Error.WriteLine("Walk N Wash VR Companion config reset failed: " + e.Message);
            }
        }

        private static string GetPluginVersion()
        {
            // Read the third BepInPlugin constructor argument directly from
            // attribute metadata. Accessing BepInPlugin.Version here would make
            // this project require a compile-time reference to SemanticVersioning.
            foreach (CustomAttributeData attribute in typeof(Plugin).GetCustomAttributesData())
            {
                if (attribute.AttributeType != typeof(BepInPlugin)) continue;
                if (attribute.ConstructorArguments.Count < 3) return null;
                return attribute.ConstructorArguments[2].Value as string;
            }

            return null;
        }
    }
}
