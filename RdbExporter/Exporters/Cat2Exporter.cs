using RdbExporter.Entities;
using RdbExporter.Parsers;
using RdbExporter.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace RdbExporter.Exporters
{
    /// <summary>
    /// CAT2 skeletons (Arguments[0] "Skeleton", 1010002) and animations ("Animation", 1010003) as JSON. Version 3
    /// animation tracks only store the bone name's hash, so their names are looked up in every skeleton's bones.
    /// Animations are written without indentation, as they are large.
    /// </summary>
    public class Cat2Exporter : JsonExporter
    {
        private const int SkeletonType = 1010002;
        private static readonly JsonSerializerOptions CompactOptions = new(JsonOptions) { WriteIndented = false };
        private static readonly object BoneNamesLock = new();
        private static Dictionary<uint, string> _boneNames;

        private static bool IsAnimation(ExportParameters parameters) => parameters.Arguments.FirstOrDefault() == "Animation";

        protected override JsonSerializerOptions GetOptions(ExportParameters parameters) => IsAnimation(parameters) ? CompactOptions : JsonOptions;

        protected override object Parse(ExportParameters parameters, IDBRIndexEntrty entry, byte[] data)
        {
            if (!IsAnimation(parameters)) return Cat2Parser.ParseSkeleton(data);

            var animation = Cat2Parser.ParseAnimation(data);
            var boneNames = GetBoneNames(parameters.SwlInstallDir);
            foreach (var track in animation.Tracks)
            {
                if (track.Name == null && boneNames.TryGetValue(track.Hash, out var name)) track.Name = name;
            }
            return animation;
        }

        /// <summary>Bone name by FNV-1 hash, from every skeleton.</summary>
        public static Dictionary<uint, string> GetBoneNames(string installDir)
        {
            lock (BoneNamesLock)
            {
                if (_boneNames != null) return _boneNames;
                _boneNames = new Dictionary<uint, string>();
                foreach (var entry in Helpers.GetRdbIndex(installDir).Where(e => e.Type == SkeletonType))
                {
                    try
                    {
                        foreach (var bone in Cat2Parser.ParseSkeleton(entry.ReadAllBytes(installDir)).Bones) _boneNames.TryAdd(bone.Hash, bone.Name);
                    }
                    catch (InvalidDataException)
                    {
                    }
                }
                return _boneNames;
            }
        }
    }
}
