using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.WPF.Reflection.Attributes;
using Noggog;
using System;
using System.IO;
using System.Text;

namespace UniqueRegionNamesPatcher
{
    public class TamrielSettings
    {
        public TamrielSettings()
        {
        }

        public static FormLink<IWorldspaceGetter> Worldspace => Mutagen.Bethesda.FormKeys.SkyrimSE.Skyrim.Worldspace.Tamriel;

        [SettingName("Override Region Map Path")]
        public string OverrideMapPath = string.Empty;
        [SettingName("Override Region Data Path")]
        public string OverrideRegionPath = string.Empty;

        public Utility.UrnRegionMap GetUrnRegionMap(ref IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            Stream mapStream;
            if (OverrideMapPath.Trim().Length > 0)
            {
                if (File.Exists(OverrideMapPath))
                {
                    Console.WriteLine($"Using custom map path '{OverrideMapPath}'");
                    using StreamReader sr = new(File.Open(OverrideMapPath, FileMode.Open, FileAccess.Read));
                    using StreamWriter sw = new(mapStream = new MemoryStream());
                    sw.Write(sr.ReadToEnd());
                    sr.Close();
                    sw.Flush();
                    sw.Close();

                    mapStream.Seek(0, SeekOrigin.Begin);
                }
                else
                {
                    throw new Exception($"The custom map path '{OverrideMapPath}' doesn't exist!");
                }
            }
            else
            {
                mapStream = new MemoryStream(Encoding.ASCII.GetBytes(Properties.Resources.tamriel_map));
            }

            Stream regionStream;
            if (OverrideRegionPath.Trim().Length > 0)
            {
                if (File.Exists(OverrideMapPath))
                {
                    Console.WriteLine($"Using custom map path '{OverrideMapPath}'");
                    using StreamReader sr = new(File.Open(OverrideMapPath, FileMode.Open, FileAccess.Read));
                    using StreamWriter sw = new(regionStream = new MemoryStream());
                    sw.Write(sr.ReadToEnd());
                    sr.Close();
                    sw.Flush();
                    sw.Close();

                    regionStream.Seek(0, SeekOrigin.Begin);
                }
                else
                {
                    throw new Exception($"The custom map path '{OverrideMapPath}' doesn't exist!");
                }
            }
            else
            {
                regionStream = new MemoryStream(Encoding.ASCII.GetBytes(Properties.Resources.tamriel_region));
            }

            return new(mapStream, regionStream, Worldspace.FormKey, ref state);
        }
    }
    public class Settings
    {
        public Settings() {}

        [SettingName("Verbose Log")]
        public bool verbose = true;

        public TamrielSettings TamrielSettings = new();
    }
}
