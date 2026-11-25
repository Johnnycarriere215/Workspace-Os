using WorkspaceOS.Core.Config;

namespace WorkspaceOS.Core.Taskbar
{
    /// <summary>
    /// Every Windhawk mod WorkspaceOS themes, staged and applied as one set.
    ///
    /// <see cref="Sync"/> is what a theme change calls: it writes both preset
    /// files, and if either mod holds an older theme it applies them together in
    /// one elevated run. Applying both at once matters — each settings change
    /// makes the File Explorer styler re-initialise inside every open Explorer
    /// window, so two separate runs would do that work twice.
    /// </summary>
    public static class WindhawkGlass
    {
        internal static readonly WindhawkModPreset[] Presets =
        {
            WindhawkPreset.Descriptor,
            ExplorerGlass.Descriptor
        };

        /// <summary>
        /// Stages both presets and applies them when Windhawk holds an older
        /// theme. A preset whose mod is not installed is only staged, and says so
        /// once per session.
        /// </summary>
        public static void Sync(AppearanceConfig a)
        {
            bool stale = false;

            foreach (var preset in Presets)
            {
                WindhawkPresetStore.Write(preset, a);

                if (!preset.IsAvailable)
                {
                    ReportMissing(preset);
                    continue;
                }

                if (!WindhawkPresetStore.IsCurrent(preset, a)) stale = true;
            }

            if (stale) WindhawkPresetStore.ApplyAll(Presets, a);
        }

        private static void ReportMissing(WindhawkModPreset preset)
        {
            if (preset.ReportedMissing || preset.UnavailableLog == null) return;
            preset.ReportedMissing = true;
            ConfigService.Log(preset.UnavailableLog);
        }
    }
}
