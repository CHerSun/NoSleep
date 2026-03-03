using NoSleep.Properties;
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
            var AppsList = new List<AppEntry>(Settings.Default.WatchedApps.Count);
            foreach (var app in Settings.Default.WatchedApps)
                AppsList.Add(AppEntry.FromExePath(app));
            return AppsList;
        }

        public static void Save(List<AppEntry> apps)
        {
            // Can we avoid saving? Do we really have changes?
            var newDistinctPaths = new HashSet<string>(apps.Select(x => x.ExePath));
            var oldDistinctPaths = new HashSet<string>(Load().Select(x => x.ExePath));

            if (newDistinctPaths == oldDistinctPaths)
                return;

            // We reached here = some changes. Save actually.
            Settings.Default.WatchedApps.Clear();
            Settings.Default.WatchedApps.AddRange(apps.Select(x => x.ExePath).ToArray());
            Settings.Default.Save();
        }
    }
}
