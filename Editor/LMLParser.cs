using System;
using System.Collections.Generic;
using System.IO;
using Unity.Plastic.Newtonsoft.Json;
using UnityEngine;

namespace LML
{
    [Serializable]
    public class LMLScene
    {
        public string LML_Version;
        public Dictionary<string, LMLObject> objects;
        public List<LMLRoom> rooms;
        public List<LMLDoor> doors;
        public List<LMLOpenWall> openWalls;
    }

    [Serializable]
    public class LMLRoom
    {
        public string room_id;
        public List<Vector3> vertices;
        public Dictionary<string, string> materials;
        public List<LMLInstance> instances;
    }

    [Serializable]
    public class LMLObject
    {
        public string name;
        public string unique_id;
        public string object_type;
        public List<LMLAsset> assets;
        public CollisionData collision_data;
    }

    [Serializable]
    public class LMLAsset
    {
        public string mesh_path;
        public string texture_path;
        public bool alpha;
        public bool emission;
    }

    [Serializable]
    public class CollisionData
    {
        public bool collisionState;
        public string attachPoint;
    }

    [Serializable]
    public class LMLInstance
    {
        public string unique_id;
        public string instance_id;
        public TransformData transform;
    }

    [Serializable]
    public class TransformData
    {
        public Vector3 position;
        public Vector3 rotation;
        public Vector3 scaling;
    }

    public class LMLDoor
    {
        public List<Vector3> holePolygon;
        public List<List<float>> doorSegment;
        public Vector3 assetPosition;
        public string assetId;
    }

    [Serializable]
    public class LMLOpenWall
    {
        [JsonProperty("segments")]
        public List<List<List<float>>> segments;  // [[[x1,y1], [x2,y2]], ...]

        [JsonProperty("openWallBoxes")]
        public List<List<List<float>>> openWallBoxes;  // [[[x1,y1], [x2,y2], [x3,y3], [x4,y4]], ...]
    }

    public static class LMLParser
    {
        public static LMLScene Parse(string lmlFilePath)
        {
            if (!File.Exists(lmlFilePath))
                throw new FileNotFoundException($"LML file not found: {lmlFilePath}");

            string lmlContent = File.ReadAllText(lmlFilePath);

            // Deserialize using Newtonsoft.Json
            LMLScene scene = JsonConvert.DeserializeObject<LMLScene>(lmlContent);

            // Post-process vertices (convert from raw JSON to Unity-compatible Vector3)
            foreach (var room in scene.rooms)
            {
                List<Vector3> processedVertices = new List<Vector3>();
                foreach (var vertex in room.vertices)
                {
                    processedVertices.Add(new Vector3(vertex.x, vertex.y, vertex.z));
                }
                room.vertices = processedVertices;
            }

            return scene;
        }
    }
}
