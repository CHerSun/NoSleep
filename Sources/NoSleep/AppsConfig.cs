using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace NoSleep
{
    public class AppEntry
    {
        public string Name { get; set; }
        public string ExePath { get; set; }
    }

    internal static class AppsConfig
    {
        private static string ConfigDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NoSleep");
        private static string ConfigFile => Path.Combine(ConfigDir, "apps.xml");

        public static List<AppEntry> Load()
        {
            try
            {
                if (!Directory.Exists(ConfigDir)) Directory.CreateDirectory(ConfigDir);
                if (!File.Exists(ConfigFile))
                    return new List<AppEntry>();

                using (var stream = File.OpenRead(ConfigFile))
                {
                  var ser = new XmlSerializer(typeof(List<AppEntry>));
                  return (List<AppEntry>)ser.Deserialize(stream) ?? new List<AppEntry>();
                }
            }
            catch
            {
                return new List<AppEntry>();
            }
        }

        public static void Save(List<AppEntry> apps)
        {
            try
            {
                if (!Directory.Exists(ConfigDir))
                    Directory.CreateDirectory(ConfigDir);

                using (var stream = File.Create(ConfigFile))
                {
                  var ser = new XmlSerializer(typeof(List<AppEntry>));
                  ser.Serialize(stream, apps ?? new List<AppEntry>());
                }
            }
            catch (Exception)
            {
                // swallow - non-critical
            }
        }
    }
}
