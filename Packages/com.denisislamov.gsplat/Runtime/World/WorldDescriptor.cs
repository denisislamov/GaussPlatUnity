using System;
using System.Collections.Generic;
using UnityEngine;

namespace GSplat
{
    public enum WorldDescriptorError
    {
        None = 0,
        EmptyPayload,
        MalformedJson,
        NoLevels,
        InvalidLevel,
        InvalidCoordinateSystem
    }

    public sealed class WorldDescriptorException : Exception
    {
        public WorldDescriptorError Code { get; }

        public WorldDescriptorException(WorldDescriptorError code, string message) : base(message)
        {
            Code = code;
        }
    }

    /// <summary>One quality level of a world: a splat file and what it costs.</summary>
    [Serializable]
    public sealed class WorldLevel
    {
        public string url;
        public int splatCount;
        public long bytes;
        public int shDegree;
    }

    [Serializable]
    public sealed class WorldSpawn
    {
        public float[] position = { 0f, 0f, 0f };
        public float[] rotationEuler = { 0f, 0f, 0f };

        public Vector3 Position => position != null && position.Length == 3 ? new Vector3(position[0], position[1], position[2]) : Vector3.zero;
        public Quaternion Rotation => rotationEuler != null && rotationEuler.Length == 3 ? Quaternion.Euler(rotationEuler[0], rotationEuler[1], rotationEuler[2]) : Quaternion.identity;
    }

    /// <summary>
    /// What the viewer needs to know about a world (TZ E8-T1): the quality levels to choose from, an optional
    /// collider, where the camera starts and which axis convention the files use. JSON via JsonUtility, so the
    /// fields are public and lower-case like the file:
    /// { "name": "...", "coordinateSystem": "Rub", "levels": [{ "url": "...", "splatCount": 150000, "bytes": 2400000, "shDegree": 0 }],
    ///   "colliderUrl": "...", "spawn": { "position": [0,1.6,0], "rotationEuler": [0,0,0] } }
    /// </summary>
    [Serializable]
    public sealed class WorldDescriptor
    {
        public string name;
        public string coordinateSystem = "Rub";
        public List<WorldLevel> levels = new List<WorldLevel>();
        public string colliderUrl;
        public WorldSpawn spawn = new WorldSpawn();

        public bool HasCollider => !string.IsNullOrEmpty(colliderUrl);

        public SplatCoordinateSystem CoordinateSystem
        {
            get
            {
                if (Enum.TryParse(coordinateSystem, true, out SplatCoordinateSystem parsed)) return parsed;
                throw new WorldDescriptorException(WorldDescriptorError.InvalidCoordinateSystem, $"Unknown coordinate system '{coordinateSystem}'; expected Ruf, Rub, Rdf or Luf.");
            }
        }

        public static WorldDescriptor Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new WorldDescriptorException(WorldDescriptorError.EmptyPayload, "The world descriptor is empty.");

            WorldDescriptor descriptor;
            try
            {
                descriptor = JsonUtility.FromJson<WorldDescriptor>(json);
            }
            catch (ArgumentException e)
            {
                throw new WorldDescriptorException(WorldDescriptorError.MalformedJson, "The world descriptor is not valid JSON: " + e.Message);
            }

            if (descriptor == null || descriptor.levels == null || descriptor.levels.Count == 0)
            {
                throw new WorldDescriptorException(WorldDescriptorError.NoLevels, "The world descriptor lists no levels.");
            }

            for (int levelIndex = 0; levelIndex < descriptor.levels.Count; levelIndex++)
            {
                WorldLevel level = descriptor.levels[levelIndex];
                if (level == null || string.IsNullOrEmpty(level.url) || level.splatCount <= 0)
                {
                    throw new WorldDescriptorException(WorldDescriptorError.InvalidLevel, $"Level {levelIndex} needs a url and a positive splatCount.");
                }
            }

            // Validate the coordinate system now so a typo fails at parse time, not mid-load.
            _ = descriptor.CoordinateSystem;
            descriptor.levels.Sort((a, b) => a.splatCount.CompareTo(b.splatCount));
            return descriptor;
        }

        /// <summary>A descriptor for a single file URL, when there is no JSON: one level, count unknown (0 means "whatever it holds").</summary>
        public static WorldDescriptor ForSingleFile(string url, SplatCoordinateSystem coordinateSystem)
        {
            return new WorldDescriptor
            {
                name = url,
                coordinateSystem = coordinateSystem.ToString(),
                levels = new List<WorldLevel> { new WorldLevel { url = url, splatCount = int.MaxValue } }
            };
        }

        /// <summary>The level shown first: the smallest one, as long as it is within the profile's first-level cap (else still the smallest).</summary>
        public WorldLevel FirstLevel(SplatQualityProfile profile)
        {
            return levels[0];
        }

        /// <summary>The level to end on: the largest one within the profile's budget, or the smallest when none fits.</summary>
        public WorldLevel FinalLevel(SplatQualityProfile profile)
        {
            WorldLevel best = levels[0];
            for (int levelIndex = 1; levelIndex < levels.Count; levelIndex++)
            {
                if (profile.MaxSplatCount > 0 && levels[levelIndex].splatCount > profile.MaxSplatCount) break;
                best = levels[levelIndex];
            }

            return best;
        }
    }
}
