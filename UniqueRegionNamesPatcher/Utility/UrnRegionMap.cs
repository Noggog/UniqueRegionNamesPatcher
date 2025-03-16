using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;
using Noggog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UniqueRegionNamesPatcher.Utility
{
    /// <summary>
    /// This is a custom object used to parse and interact with the region/cell map generated by the ParseImage project on the dev branch.
    /// </summary>
    public class UrnRegionMap
    {
        #region Constructors

        /// <summary>
        /// Constructor that accepts a pre-defined <see cref="Stream"/> object.
        /// </summary>
        /// <param name="stream">A pre-defined <see cref="Stream"/> derived object.</param>
        /// <param name="state">Reference of the <see cref="IPatcherState"/> object passed to the <see cref="Program.RunPatch(IPatcherState{ISkyrimMod, ISkyrimModGetter})"/> function.</param>
        public UrnRegionMap(Stream map_stream, Stream region_stream, FormKey worldspaceFormKey, ref IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            _worldspaceFormKey = worldspaceFormKey;
            //linkCache = state.PatchMod.ToMutableLinkCache();
            var streams = SplitStream(map_stream);
            this.ParseRegionAreas(streams[FileHeader.RegionAreas], new(region_stream), ref state);
            this.ParseHoldMap(streams[FileHeader.HoldMap]);
            //UpdateRegionPriorityLevels(ref state);
        }

        #endregion Constructors

        #region Enum

        /// <summary>
        /// Defines the recognized header names in the INI-formatted map file.
        /// </summary>
        internal enum FileHeader : byte
        {
            /// <summary>
            /// This doesn't correspond to any actual headers, and is used to indicate the absense of one.
            /// </summary>
            Null = 0,
            /// <summary>
            /// This corresponds to the [Regions] header.<br/>
            /// It is used to define the points that make up the border of a region.
            /// These points differ from the keys in the [HoldMap] header because they are purposefully larger than the actual region.
            /// </summary>
            RegionAreas = 1,
            /// <summary>
            /// This corresponds to the [HoldMap] header.<br/>
            /// It is used to define which regions belong to a given cell coordinate.
            /// </summary>
            HoldMap = 2,
        }

        #endregion Enum

        #region Members

        /// <summary>
        /// This is the <see cref="IPatcherState.PatchMod"/>'s <see cref="ILinkCache"/>, for use when resolving records.
        /// </summary>
        //private readonly ILinkCache linkCache;
        /// <summary>
        /// List of all custom regions added by the patcher.<br/>
        /// This uses the <see cref="RegionWrapper"/> object to prevent unnecessary LinkCache lookups for trivial information.
        /// </summary>
        public List<RegionWrapper> Regions = new();
        /// <summary>
        /// Coordinate:FormLink map used to look up the coordinates of cells.
        /// </summary>
        public Dictionary<P2Int, List<FormLink<IRegionGetter>>> Map = new();
        /// <summary>
        /// The formkey of the worldspace that this RegionMap instance belongs to.
        /// </summary>
        private readonly FormKey _worldspaceFormKey;

        #endregion Members

        #region Methods

        #region Methods_Parsing

        /// <summary>
        /// Splits the given stream by partially parsing it to find all recognized INI headers, as defined by <see cref="FileHeader"/>.
        /// </summary>
        /// <param name="stream">Stream containing an entire INI file's contents.</param>
        /// <returns>A <see cref="Dictionary{FileHeader, {Stream, int}}"/> of file headers and tuples where <b>Item1</b> is the portion of the stream that contains the members of the associated file header, and <b>Item2</b> is the line number that the header appears on.</returns>
        private static Dictionary<FileHeader, (Stream, int)> SplitStream(Stream stream)
        {
            using StreamReader sr = new(stream);

            Dictionary<FileHeader, (Stream, int)> streams = new();

            FileHeader currentHeader = FileHeader.Null;

            int ln = 0;
            for (string? line = sr.ReadLine(); !sr.EndOfStream; line = sr.ReadLine(), ++ln)
            {
                if (line == null)
                    continue;

                // strip all comments & whitespace
                line = line.TrimComments().RemoveIf(char.IsWhiteSpace);

                if (line.Length == 0)
                    continue;

                if (!line.EndsWith('\n'))
                    line += '\n';

                // check for an INI header:
                int open = line.IndexOf('['), close = line.IndexOf(']');

                if (!line.Contains('=') && open != -1 && close != -1)
                {
                    string header = line[(open + 1)..close];
                    if (Enum.TryParse(typeof(FileHeader), header, true, out object? result) && result is FileHeader head)
                    {
                        currentHeader = head;
                        streams.Add(currentHeader, (new MemoryStream(), ln));
                    }
                    else currentHeader = FileHeader.Null;
                }
                else if (currentHeader != FileHeader.Null)
                {
                    streams[currentHeader].Item1.Write(Encoding.ASCII.GetBytes(line));
                }
            }

            // append a newline to each stream, then set each stream's read pos to the beginning before returning
            streams.ForEach(s => s.Value.Item1.WriteByte((byte)'\n'));
            streams.ForEach(s => s.Value.Item1.Seek(0, SeekOrigin.Begin));

            return streams;
        }

        /// <summary>
        /// Parses the <see cref="FileHeader.RegionAreas"/> header, and creates new <see cref="Region"/> records to be used during the main patcher process.
        /// </summary>
        /// <param name="streamSection">A pair where <b>Item1</b> is the portion of the input stream that contains the <see cref="FileHeader.Regions"/> header, and <b>Item2</b> is the line number that the header appears on.</param>
        /// <param name="state">Reference of the current patcher state to use when adding new regions.</param>
        /// <exception cref="FormatException">Thrown when the input stream contains invalid data.</exception>
        private void ParseRegionAreas((Stream, int) streamSection, UrnRegionFile regionData, ref IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            var (stream, startIndex) = streamSection;

            // create a stream reader
            using StreamReader sr = new(stream);

            int ln = startIndex + 1;
            for (string? line = sr.ReadLine(); !sr.EndOfStream; line = sr.ReadLine(), ++ln)
            {
                if (line == null || line.Length == 0)
                    continue;

                int eq = line.IndexOf('=');

                if (eq == -1)
                    continue;

                string // get the key & value from this line
                    editorID = line[..eq].Trim(),
                    value = line[(eq + 1)..].Trim('[', ']', ' ', '\n');

                // parse the value's point list
                ExtendedList<P2Float> pointList = new();

                foreach (string point in Regex.Matches(value, "\\([\\-0-9]+,[\\-0-9]+\\)").Cast<Match>().Select(m => m.Value))
                {
                    if (point.ParsePoint() is P2Int p)
                    {
                        pointList.Add(new P2Float(p.X * 4096, p.Y * 4096));
                    }
                    else throw new FormatException($"Invalid point '{point}' at line {ln}! (Key '{editorID}')");
                }

                var data = regionData.Regions.FirstOrDefault(r => r.EditorID.Equals(editorID, StringComparison.OrdinalIgnoreCase));

                // check for an already existing region (added by this patcher only) with the given editor ID (name)
                RegionWrapper? existing = Regions.FirstOrDefault(r => r != null && editorID.Equals(r.EditorID, StringComparison.Ordinal), null);

                if (existing == null)
                {
                    var region = new Region(state.PatchMod.GetNextFormKey(), state.GameRelease.ToSkyrimRelease())
                    {
                        EditorID = editorID,
                        MapColor = data.Color,
                        // Region Data
                        Map = new RegionMap()
                        {
                            Name = data.MapName,
                            Priority = data.Priority,
                            Flags = RegionData.RegionDataFlag.Override,
                        },
                        // Region Areas
                        RegionAreas = new()
                        {
                            new() // Region Area #0
                            {
                                EdgeFallOff = 1024,
                                RegionPointListData = pointList
                            }
                        }
                    };
                    // set the primary 
                    region.Worldspace.SetTo(_worldspaceFormKey);

                    state.PatchMod.Regions.Add(region);

                    Regions.Add(new(editorID, region.FormKey, data.MapName));
                }
            }

            // dispose of stream
            sr.Dispose();
            stream.Dispose();
        }

        /// <summary>
        /// Parses the given stream, and populates the <see cref="Map"/> and <see cref="Regions"/> members.
        /// </summary>
        /// <remarks><b>THIS MUST BE CALLED AFTER <see cref="ParseRegions(Stream, int, ref IPatcherState{ISkyrimMod, ISkyrimModGetter})"/>!</b></remarks>
        /// <param name="stream">A <see cref="Stream"/> object with the contents of the region map file.</param>
        /// <param name="startIndex">The line number that this stream section begins at.</param>
        /// <param name="state">Reference of the <see cref="IPatcherState"/> object passed to the <see cref="Program.RunPatch(IPatcherState{ISkyrimMod, ISkyrimModGetter})"/> function.</param>
        private void ParseHoldMap((Stream, int) streamSection)
        {
            var (stream, startIndex) = streamSection;

            using StreamReader sr = new(stream);

            if (Regions.Count == 0)
                throw new Exception("Invalid [Regions] map doesn't contain any data!");

            int ln = startIndex + 1;
            for (string? line = sr.ReadLine(); !sr.EndOfStream; line = sr.ReadLine(), ++ln)
            {
                if (line == null || line.Length == 0)
                    continue;

                // check for an INI header:
                int eq = line.IndexOf('=');

                if (eq == -1)
                    continue;

                // parse the key (coordinate)
                if (line[..eq].RemoveAll('(', ')', ' ').ParsePoint() is not P2Int coord)
                {
                    Console.WriteLine($"[WARNING]\tLine {ln} contains an invalid coordinate string! ('{line}')");
                    continue;
                }

                // parse the value (region name list)
                string value = line[(eq + 1)..].Trim();

                List<string> regionNames = new();
                foreach (string elem in value.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    string e = elem.Trim('"');
                    if (e.Length > 0)
                        regionNames.Add(e);
                }

                List<FormLink<IRegionGetter>> links = new();

                foreach (string editorID in regionNames)
                {
                    // check for an already existing region (added by this patcher only) with the given editor ID (name)
                    RegionWrapper? existing = Regions.FirstOrDefault(r => r != null && editorID.Equals(r.EditorID, StringComparison.Ordinal), null);

                    if (existing == null)
                        throw new Exception($"Hold with editor ID '{editorID}' doesn't have any valid area data!");

                    links.Add(existing.FormLink);
                }

                Map.Add(coord, links);
            }
        }

        #endregion Methods_Parsing

        /// <summary>
        /// Retrieve the list of <see cref="Region"/> formlinks associated with a given cell's coordinates.
        /// </summary>
        /// <param name="coord">The coordinates of the cell to check. <b>This MUST be in cell coordinates, NOT Raw/SubBlock/Block coordinates!</b></param>
        /// <returns>List of formlinks to regions associated with this cell.<br/>If <see cref="Map"/> doesn't contain the given point, an empty list is returned.</returns>
        public List<FormLink<IRegionGetter>> GetFormLinksForPos(P2Int coord)
        {
            List<FormLink<IRegionGetter>> links = new();

            if (Map.ContainsKey(coord))
            {
                var arr = Map[coord];
                if (arr != null)
                {
                    foreach (var link in arr)
                    {
                        links.Add(link);
                    }
                }
            }

            return links;
        }

        //private void UpdateRegionPriorityLevels(ref IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        //{
        ////    foreach (var rw in Regions)
        ////    {
        ////        IRegionGetter region = rw.FormLink.Resolve(linkCache);
        ////        byte priority = region.Map?.Header?.Priority ?? 60;

        ////        byte highest = 0;
        ////        state.LoadOrder.PriorityOrder.Region().WinningOverrides().ForEach(delegate (IRegionGetter modRegion)
        ////        {
        ////            if (modRegion.Map?.Header?.Priority != null && modRegion.Map.Header.Priority > highest)
        ////                highest = modRegion.Map.Header.Priority;
        ////        });
        ////        // if our region's priority is lower than 
        ////        if (priority < highest)
        ////        {
        ////            var regionCopy = region.DeepCopy();
        ////            regionCopy.Map!.Header!.Priority += Convert.ToByte(highest - priority + 1);
        ////            state.PatchMod.Regions.Set(regionCopy);
        ////        }
        ////    }
        //}
        #endregion Methods
    }
}
