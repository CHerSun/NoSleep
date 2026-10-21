using NoSleep.Properties;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NoSleep
{
    public class AppEntry
    {
        /// <summary> Program displayed name </summary>
        public string Name { get; set; }
        /// <summary> Path to the executable file </summary>
        public string ExePath { get; set; }

        /// <summary> Create a new AppEntry from executable path. Name is taken as file name without extension. </summary>
        public static AppEntry FromExePath(string exePath) 
            => new AppEntry() { Name = Path.GetFileNameWithoutExtension(exePath), ExePath = exePath };
    }

    internal static class AppsConfig
    {
        public static List<AppEntry> Load()
        {
            // WatchedApps may deserialize as null (e.g. corrupted user.config) - treat it as an empty list
            var watchedApps = Settings.Default.WatchedApps;
            var AppsList = new List<AppEntry>(watchedApps?.Count ?? 0);
            if (watchedApps != null)
                foreach (var app in watchedApps)
                    AppsList.Add(AppEntry.FromExePath(app));
            return AppsList;
        }

        public static void Save(List<AppEntry> apps)
        {
            // Do we really have changes? Names must be part of the comparison:
            // the grid allows renaming an entry without touching its path.
            var newPaths = new HashSet<string>(apps.Select(x => x.ExePath), StringComparer.OrdinalIgnoreCase);
            var newNames = new HashSet<string>(apps.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
            var oldApps = Load();
            var oldPaths = new HashSet<string>(oldApps.Select(x => x.ExePath), StringComparer.OrdinalIgnoreCase);
            var oldNames = new HashSet<string>(oldApps.Select(x => x.Name), StringComparer.OrdinalIgnoreCase);
            if (newPaths.SetEquals(oldPaths) && newNames.SetEquals(oldNames))
                return;

            // We reached here = some changes. Save actually.
            Settings.Default.WatchedApps.Clear();
            Settings.Default.WatchedApps.AddRange(apps.Select(x => x.ExePath).ToArray());
            Settings.Default.Save();
        }
    }
}
