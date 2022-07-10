using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using UniqueRegionNamesPatcher.Extensions;
using UniqueRegionNamesPatcher.Utility;

namespace UniqueRegionNamesPatcher
{

    public static class ModContextExtensions
    {
        public static void Deconstruct<TMod, TModGetter, TTarget, TTargetGetter>(this IModContext<TMod, TModGetter, TTarget, TTargetGetter> modContext, out TTargetGetter record, out ModKey modKey, out IModContext? parent) where TMod : TModGetter, IMod where TModGetter : IModGetter where TTarget : TTargetGetter where TTargetGetter : notnull
        {
            record = modContext.Record;
            modKey = modContext.ModKey;
            parent = modContext.Parent;
        }
        public static void Deconstruct(this IModContext modContext, out object? record, out ModKey modKey, out IModContext? parent)
        {
            record = modContext.Record;
            modKey = modContext.ModKey;
            parent = modContext.Parent;
        }
    }

    public class Program
    {
        private static Lazy<Settings> _lazySettings = null!;
        private static Settings Settings => _lazySettings.Value;

        private static readonly List<WorldspaceRecordHandler> Handlers = new();

        public static async Task<int> Main(string[] args)
            => await SynthesisPipeline.Instance
                .AddPatch<ISkyrimMod, ISkyrimModGetter>(RunPatch)
                .SetTypicalOpen(GameRelease.SkyrimSE, "UniqueRegionNamesPatcher.esp")
                .SetAutogeneratedSettings("Settings", "settings.json", out _lazySettings, false)
                .Run(args);

        private static void InitializeHandlers(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            Handlers.Clear();

            /* v This section should be put in a loop in the future v */

            // Add a handler for Tamriel:
            var handler = new WorldspaceRecordHandler(Settings.TamrielSettings.GetUrnRegionMap(ref state));
            Handlers.Add(handler);
            Console.WriteLine($"Added {nameof(WorldspaceRecordHandler)} for {nameof(Worldspace)} '{handler.RegionMap.WorldspaceFormKey}'");

            if (Settings.verbose)
            {
                var regions = handler.RegionMap.Regions;

                Console.WriteLine($"Parsed {regions.Count} region{(regions.Count.Equals(1) ? "" : "s")} containing {handler.RegionMap.Map.Count} cell{(handler.RegionMap.Map.Count.Equals(1) ? "" : "s")}:");

                int longestEdID = 0, longestName = 0;
                regions.ForEach(delegate (RegionWrapper rw)
                {
                    if (rw.EditorID.Length > longestEdID)
                        longestEdID = rw.EditorID.Length;
                    if (rw.Name != null && rw.Name.Length > longestName)
                        longestName = rw.Name.Length;
                });

                Console.WriteLine('{');
                foreach (var region in regions)
                    Console.WriteLine($"    {{ EditorID: '{region.EditorID}':{new string(' ', longestEdID + 4 - region.EditorID.Length)}Displayname: '{region.Name}'{new string(' ', longestName - region.Name?.Length ?? 0)} }},");
                Console.WriteLine('}');
            }

            /* ^ This section should be put in a loop in the future ^ */
        }

        private static WorldspaceRecordHandler? GetHandlerForWorldspace(IWorldspaceGetter wrld) => Handlers.FirstOrDefault(h => h.AppliesTo(wrld));

        public static void RunPatch(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            Console.WriteLine("===================");

            InitializeHandlers(state);

            foreach (var wrldGetter in state.LoadOrder.PriorityOrder.Worldspace().WinningOverrides(false))
            {
                if (GetHandlerForWorldspace(wrldGetter) is WorldspaceRecordHandler handler)
                {
                    try
                    {
                        if (handler.ProcessWorldspace(wrldGetter) is Worldspace wrldCopy)
                        {
                            Console.WriteLine($"Finished processing {nameof(Worldspace)} {wrldCopy.EditorID}");
                            state.PatchMod.Worldspaces.Set(wrldCopy);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"An unhandled exception occurred while processing {nameof(Worldspace)} {wrldGetter?.EditorID}: {ex.FormatExceptionMessage()}");
#                       if DEBUG
                        throw; //< rethrow exceptions in Debug configuration
#                       endif
                    }
                }
            }
        }
    }
}
