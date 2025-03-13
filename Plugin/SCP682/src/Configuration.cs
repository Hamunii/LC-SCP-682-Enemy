using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;

namespace SCP682.Configuration
{
    public class PluginConfig
    {
        // For more info on custom configs, see https://lethal.wiki/dev/intermediate/custom-configs
        public readonly ConfigEntry<bool> doSpeaking;

        public PluginConfig(ConfigFile cfg)
        {
            doSpeaking = cfg.Bind(
                "Voice Lines",
                "Enable",
                true,
                "Should SCP-682 have the ability to speak in certain situations?"
            );

            ClearUnusedEntries(cfg);
        }

        private void ClearUnusedEntries(ConfigFile cfg)
        {
            // Normally, old unused config entries don't get removed, so we do it with this piece of code. Credit to Kittenji.
            PropertyInfo orphanedEntriesProp = cfg.GetType()
                .GetProperty("OrphanedEntries", BindingFlags.NonPublic | BindingFlags.Instance);
            var orphanedEntries =
                (Dictionary<ConfigDefinition, string>)orphanedEntriesProp.GetValue(cfg, null);
            orphanedEntries.Clear(); // Clear orphaned entries (Unbinded/Abandoned entries)
            cfg.Save(); // Save the config file to save these changes
        }
    }
}
