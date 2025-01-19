using System.Collections.Generic;
using System.IO;
using UnityEngine;
using GLTFast; // Assuming you're using glTFast for .glb handling
using System;
using UnityEditor;
using System.Threading.Tasks;
using UnityEngine.Networking;
using NUnit.Framework.Constraints;
//using GLTFast.Schema;

namespace LML
{
    public static class LMLSceneBuilder
    {
        private static Dictionary<string, GameObject> sharedWalls;

        public static GameObject BuildScene(LMLScene scene, string assetsPath)
        {
            // Create a parent GameObject to hold the imported scene
            GameObject sceneParent = new GameObject("ImportedLMLScene");

            sharedWalls = new Dictionary<string, GameObject>();

            // Build rooms
            foreach (LMLRoom room in scene.rooms)
            {
                BuildRoom(room, scene, sceneParent, assetsPath);
            }

            foreach(LMLDoor door in scene.doors)
            {
                BuildDoor(door, sceneParent);
            }

            return sceneParent;
        }

        private static void BuildDoor(LMLDoor door, GameObject parent)
        {
            Debug.Log($"This door's assetId is {door.assetId}");
            foreach (var doorPoly in door.holePolygon)
            {
                Debug.Log($"poly {doorPoly}");
            }

            // Define the path to the specific door prefab
            string scriptPath = Path.GetDirectoryName(AssetDatabase.GetAssetPath(MonoScript.FromMonoBehaviour(this)));
            string prefabPath = Path.Combine(scriptPath, "..", "Prefabs", "Doorways", "Prefabs", $"{door.assetId}.prefab");

            // Load the specific door prefab by assetId
            string assetPath = prefabPath;
            GameObject doorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

            if (doorPrefab == null)
            {
                Debug.LogError($"Failed to load door prefab at path: {assetPath}");
                return;
            }

            // Instantiate the door prefab
            GameObject doorInstance = GameObject.Instantiate(doorPrefab);
            doorInstance.name = $"Door_{door.assetId}";

            doorInstance.transform.parent = parent.transform;

            float yRot;

            //Calculate door rotation
            if (door.doorSegment[0][1] == door.doorSegment[1][1])
            {
                //rotation is either 0 or 180
                if (door.doorSegment[0][0] > door.doorSegment[1][0])
                {
                    yRot = 0;
                    doorInstance.transform.position = new Vector3(door.doorSegment[1][0], 0, door.doorSegment[1][1]);
                }
                else
                {
                    yRot = 180;
                    doorInstance.transform.position = new Vector3(door.doorSegment[0][0], 0, door.doorSegment[0][1]);
                }
            }
            else
            {
                if (door.doorSegment[0][1] > door.doorSegment[1][1])
                {
                    yRot = 270;
                    doorInstance.transform.position = new Vector3(door.doorSegment[0][0], 0, door.doorSegment[0][1]);
                }
                else
                {
                    yRot = 90;
                    doorInstance.transform.position = new Vector3(door.doorSegment[1][0], 0, door.doorSegment[1][1]);
                }
            }
            Debug.Log($"This YRot: {yRot}");
            
            doorInstance.transform.rotation = Quaternion.Euler(0, yRot, 0);

            // Add a kinematic Rigidbody if it doesn't already have one
            Rigidbody rb = doorInstance.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = doorInstance.AddComponent<Rigidbody>();
            }
            rb.isKinematic = true;

            // open the door a little bit
            if (!doorInstance.name.Contains("Double"))
            {
                foreach (Transform child in doorInstance.transform)
                {
                    if (child.name.StartsWith("Doorway_Door"))
                    {
                        child.gameObject.transform.rotation *= Quaternion.Euler(0, -45, 0);
                    }
                }
            }

            Debug.Log($"Created door instance at origin for assetId: {door.assetId}");
        }

        private static async void BuildRoom(LMLRoom room, LMLScene scene, GameObject parent, string assetsPath)
        {
            GameObject roomGO = new GameObject($"Room_{room.room_id}");
            roomGO.transform.parent = parent.transform;

            // Define the offset distance to push the wall inward
            float wallOffset = 0.0125f;
            float wallHeight = 3.5f;
            float wallThickness = 0.025f;

            // Load wall material
            Material wallMaterial = null;
            if (room.materials != null && room.materials.ContainsKey("wall"))
            {
                wallMaterial = await FetchAndBuildMaterialAsync(room.materials["wall"]);
            }

            // Create walls using vertices
            for (int i = 0; i < room.vertices.Count; i++)
            {
                Vector3 start = room.vertices[i];
                Vector3 end = room.vertices[(i + 1) % room.vertices.Count];

                // Calculate wall direction and normal for this segment
                Vector3 wallDirection = (end - start).normalized;
                Vector3 inwardNormal = new Vector3(-wallDirection.z, 0, wallDirection.x);

                // Find any doors that intersect this wall segment
                List<LMLDoor> intersectingDoors = FindIntersectingDoors(start, end, scene.doors);

                if (intersectingDoors.Count == 0)
                {
                    // No doors - create a single wall segment
                    CreateWallSegment(start, end, wallOffset, wallHeight, wallThickness, roomGO.transform, wallMaterial);
                }
                else
                {
                    Debug.Log($"Intersation at room {room.room_id}");
                    // Sort doors by their position along the wall
                    intersectingDoors.Sort((a, b) =>
                    {
                        // Get both possible points for each door
                        Vector3 a1 = new Vector3(a.doorSegment[0][0], 0, a.doorSegment[0][1]);
                        Vector3 a2 = new Vector3(a.doorSegment[1][0], 0, a.doorSegment[1][1]);
                        Vector3 b1 = new Vector3(b.doorSegment[0][0], 0, b.doorSegment[0][1]);
                        Vector3 b2 = new Vector3(b.doorSegment[1][0], 0, b.doorSegment[1][1]);

                        // Use the closest point of each door to the wall start for sorting
                        float distA = Mathf.Min(Vector3.Distance(a1, start), Vector3.Distance(a2, start));
                        float distB = Mathf.Min(Vector3.Distance(b1, start), Vector3.Distance(b2, start));
                        return distA.CompareTo(distB);
                    });


                    Vector3 currentStart = start;

                    foreach (var door in intersectingDoors)
                    {
                        // Get both door endpoints
                        Vector3 doorPoint1 = new Vector3(door.doorSegment[0][0], 0, door.doorSegment[0][1]);
                        Vector3 doorPoint2 = new Vector3(door.doorSegment[1][0], 0, door.doorSegment[1][1]);

                        // Determine which point is closer to the current wall segment start
                        Vector3 doorStart, doorEnd;
                        if (Vector3.Distance(doorPoint1, currentStart) < Vector3.Distance(doorPoint2, currentStart))
                        {
                            doorStart = doorPoint1;
                            doorEnd = doorPoint2;
                        }
                        else
                        {
                            doorStart = doorPoint2;
                            doorEnd = doorPoint1;
                        }

                        // Create wall segment before the door
                        if (Vector3.Distance(currentStart, doorStart) > wallThickness)
                        {
                            GameObject firstWall = CreateWallSegment(currentStart, doorStart, wallOffset, wallHeight, wallThickness, roomGO.transform, wallMaterial);
                            firstWall.name = "1W" + firstWall.name;
                        }

                        // Skip the door segment and set up for next wall piece
                        currentStart = doorEnd;

                        // Create wall segment above the door with proper offset
                        float topWallHeight = Math.Max(door.holePolygon[0].y, door.holePolygon[1].y);

                        // Apply offset to door points for top wall
                        Vector3 offsetDoorPoint1 = doorPoint1 - (inwardNormal * wallOffset);
                        Vector3 offsetDoorPoint2 = doorPoint2 - (inwardNormal * wallOffset);

                        GameObject topWall = CreateWallSegmentWithoutOffset(offsetDoorPoint1, offsetDoorPoint2, wallHeight - topWallHeight, wallThickness, roomGO.transform, wallMaterial);
                        topWall.transform.position += new Vector3(0, topWallHeight, 0);
                        topWall.name = "TW" + topWall.name;

                    }

                    // Create final wall segment after the last door
                    if (Vector3.Distance(currentStart, end) > wallThickness)
                    {
                        GameObject lastWall = CreateWallSegment(currentStart, end, wallOffset, wallHeight, wallThickness, roomGO.transform, wallMaterial);
                        lastWall.name = "LW" + lastWall.name;
                    }
                }
            }



            // Create floor
            GameObject floor = new GameObject("Floor");
            floor.transform.parent = roomGO.transform;

            // Create a mesh for the floor
            MeshFilter meshFilter = floor.AddComponent<MeshFilter>();
            MeshRenderer meshRenderer = floor.AddComponent<MeshRenderer>();
            Mesh floorMesh = new Mesh();
            meshFilter.mesh = floorMesh;

            // Create vertices, triangles, and UVs for the polygonal floor
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Vector2> uvs = new List<Vector2>();

            // Use room.vertices as the vertices for the floor
            for (int i = 0; i < room.vertices.Count; i++)
            {
                Vector3 vertex = new Vector3(room.vertices[i].x, 0, room.vertices[i].z); // Flatten Y to 0
                vertices.Add(vertex);

                // Map UVs to vertices based on their x/z position
                uvs.Add(new Vector2(room.vertices[i].x, room.vertices[i].z));
            }

            // Generate triangles using a simple triangulation algorithm
            for (int i = 1; i < room.vertices.Count - 1; i++)
            {
                triangles.Add(0);
                triangles.Add(i);
                triangles.Add(i + 1);
            }

            // Assign the vertices, triangles, and UVs to the mesh
            floorMesh.vertices = vertices.ToArray();
            floorMesh.triangles = triangles.ToArray();
            floorMesh.uv = uvs.ToArray();
            floorMesh.RecalculateNormals();

            // Load floor material
            Material floorMaterial = null;
            if (room.materials != null && room.materials.ContainsKey("floor"))
            {
                floorMaterial = await FetchAndBuildMaterialAsync(room.materials["floor"]);
            }

            // Apply floor material
            if (floorMaterial != null)
            {
                meshRenderer.material = floorMaterial;
            }

            // Create instances
            foreach (var instance in room.instances)
            {
                if (!scene.objects.TryGetValue(instance.unique_id, out LMLObject obj))
                {
                    Debug.LogWarning($"Object with ID {instance.unique_id} not found for instance {instance.instance_id}");
                    continue;
                }

                BuildObjectInstance(instance, obj, assetsPath, roomGO);
            }
        }

        private static List<LMLDoor> FindIntersectingDoors(Vector3 wallStart, Vector3 wallEnd, List<LMLDoor> doors)
        {
            List<LMLDoor> intersecting = new List<LMLDoor>();

            foreach (var door in doors)
            {
                Vector3 doorStart = new Vector3(door.doorSegment[0][0], 0, door.doorSegment[0][1]);
                Vector3 doorEnd = new Vector3(door.doorSegment[1][0], 0, door.doorSegment[1][1]);

                if (DoLineSegmentsIntersect(wallStart, wallEnd, doorStart, doorEnd))
                {
                    intersecting.Add(door);
                }
            }

            return intersecting;
        }

        private static GameObject CreateWallSegment(Vector3 start, Vector3 end, float offset, float height, float thickness, Transform parent, Material mat)
        {
            float length = Vector3.Distance(start, end);
            Vector3 center = (start + end) / 2;
            Vector3 direction = (end - start).normalized;

            // Calculate inward normal and apply offset
            Vector3 inwardNormal = new Vector3(-direction.z, 0, direction.x);
            center -= inwardNormal * offset;

            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = $"WallSegment_{start}_{end}";
            wall.transform.position = new Vector3(center.x, height / 2, center.z);
            wall.transform.localScale = new Vector3(thickness, height, length);
            wall.transform.rotation = Quaternion.LookRotation(direction);
            wall.transform.parent = parent;

            if (mat != null)
            {
                wall.GetComponent<Renderer>().material = mat;
            }

            return wall;
        }

        private static GameObject CreateWallSegmentWithoutOffset(Vector3 start, Vector3 end, float height, float thickness, Transform parent, Material material)
        {
            float length = Vector3.Distance(start, end);
            Vector3 center = (start + end) / 2;
            Vector3 direction = (end - start).normalized;

            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = $"WallSegment_{start}_{end}";
            wall.transform.position = new Vector3(center.x, height / 2, center.z);
            wall.transform.localScale = new Vector3(thickness, height, length);
            wall.transform.rotation = Quaternion.LookRotation(direction);
            wall.transform.parent = parent;

            if (material != null)
            {
                wall.GetComponent<Renderer>().material = material;
            }

            return wall;
        }

        private static bool DoLineSegmentsIntersect(Vector3 p1, Vector3 p2, Vector3 p3, Vector3 p4)
        {
            // Convert to 2D points (ignoring Y coordinate)
            Vector2 a = new Vector2(p1.x, p1.z);
            Vector2 b = new Vector2(p2.x, p2.z);
            Vector2 c = new Vector2(p3.x, p3.z);
            Vector2 d = new Vector2(p4.x, p4.z);

            // Calculate the orientation of three points
            float orientation(Vector2 p, Vector2 q, Vector2 r)
            {
                return (q.y - p.y) * (r.x - q.x) - (q.x - p.x) * (r.y - q.y);
            }

            // Check if point p lies on segment qr
            bool onSegment(Vector2 p, Vector2 q, Vector2 r)
            {
                return p.x <= Mathf.Max(q.x, r.x) && p.x >= Mathf.Min(q.x, r.x) &&
                       p.y <= Mathf.Max(q.y, r.y) && p.y >= Mathf.Min(q.y, r.y);
            }

            float o1 = orientation(a, b, c);
            float o2 = orientation(a, b, d);
            float o3 = orientation(c, d, a);
            float o4 = orientation(c, d, b);

            // General case
            if (o1 * o2 < 0 && o3 * o4 < 0)
                return true;

            // Special cases
            if (o1 == 0 && onSegment(c, a, b)) return true;
            if (o2 == 0 && onSegment(d, a, b)) return true;
            if (o3 == 0 && onSegment(a, c, d)) return true;
            if (o4 == 0 && onSegment(b, c, d)) return true;

            return false;
        }


        private static string GetUnityAssetPath(string fullPath)
        {
            // Normalize the paths to use forward slashes
            fullPath = Path.GetFullPath(fullPath).Replace("\\", "/");
            string dataPath = Application.dataPath.Replace("\\", "/");

            // Ensure the path is in the Assets folder
            if (!fullPath.StartsWith(dataPath))
            {
                Debug.LogError($"Path is not inside the Assets folder: {fullPath}");
                //Debug.Log($"Datapath actually is {dataPath}");
                return null;
            }

            // Convert absolute path to relative path (relative to the Unity project root)
            string relativePath = "Assets" + fullPath.Substring(dataPath.Length);

            //Debug.Log($"Converted full path to Unity asset path: {relativePath}");
            return relativePath;
        }


        private static void BuildObjectInstance(LMLInstance instance, LMLObject obj, string assetsPath, GameObject parent)
        {
            if (obj.assets == null || obj.assets.Count == 0)
            {
                Debug.LogWarning($"Object {obj.name} has no assets.");
                return;
            }

            string meshPath = obj.assets[0].mesh_path;

            // Remove leading "objathor/" if present
            if (meshPath.StartsWith("objathor/"))
            {
                meshPath = meshPath.Substring("objathor/".Length);
            }

            string assetPath = GetUnityAssetPath(Path.Combine(assetsPath, meshPath, "mesh.glb"));

            // Load the prefab from the Assets folder
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

            if (prefab == null)
            {
                Debug.LogWarning($"Prefab not found for asset: {assetPath}");
                return;
            }

            // Instantiate the prefab
            GameObject instanceGO = GameObject.Instantiate(prefab, parent.transform);
            instanceGO.name = instance.instance_id;

            // Set the transform based on instance data
            instanceGO.transform.localPosition = new Vector3(
                instance.transform.position.x,
                instance.transform.position.y,
                instance.transform.position.z
            );

            instanceGO.transform.localRotation *= Quaternion.Euler(
                instance.transform.rotation.x,
                instance.transform.rotation.y,
                instance.transform.rotation.z
            );

            instanceGO.transform.localScale = new Vector3(
                instance.transform.scaling.x,
                instance.transform.scaling.y,
                instance.transform.scaling.z
            );

            // Add a kinematic Rigidbody
            Rigidbody rb = instanceGO.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = instanceGO.AddComponent<Rigidbody>();
            }
            rb.isKinematic = true; // Set Rigidbody to kinematic

            //Debug.Log($"Created instance {instance.instance_id} for object {obj.name}");
        }

       private static async Task<Material> FetchAndBuildMaterialAsync(string materialName)
       {
            //Debug.Log($"Building material for: {materialName}");

            Material material = null;

            try
            {
                // Step 1: Fetch the .mat file
                string materialFileName = $"{materialName}.mat";
                //Debug.Log($"Fetching material file: {materialFileName}");
                string materialFileUrl = await NetworkingUtils.FetchSignedURLAsync(materialFileName);
                if (string.IsNullOrEmpty(materialFileUrl))
                {
                    Debug.LogError($"Failed to fetch material file: {materialFileName}");
                    return null;
                }

                // Step 2: Download and create the material
                //Debug.Log($"Downloading material file from URL: {materialFileUrl}");
                material = await NetworkingUtils.DownloadMaterialAsync(materialFileUrl);
                if (material == null)
                {
                    Debug.LogError($"Failed to load material from file: {materialFileName}");
                    return null;
                }

                // Step 3: Fetch and assign albedo texture
                string albedoFileName = $"{materialName}_albedo.jpg";
                //Debug.Log($"Fetching albedo texture: {albedoFileName}");
                string albedoFileUrl = await NetworkingUtils.FetchSignedURLAsync(albedoFileName);
                if (!string.IsNullOrEmpty(albedoFileUrl))
                {
                    //Debug.Log($"Attempting to download albedo texture from URL: {albedoFileUrl}");
                    var albedoTexture = await NetworkingUtils.TryFetchTextureAsync(albedoFileUrl);
                    if (albedoTexture != null)
                    {
                        //Debug.Log($"Albedo texture applied for material: {materialName}");
                        material.mainTexture = albedoTexture;
                    }
                    else
                    {
                        string albedoFileNameCaps = $"{materialName}_albedo.JPG";
                        albedoFileUrl = await NetworkingUtils.FetchSignedURLAsync(albedoFileNameCaps);
                        if (!string.IsNullOrEmpty(albedoFileUrl))
                        {
                            Debug.LogWarning($"Albedo .jpg not found. Trying .JPG for: {materialName}");
                            albedoTexture = await NetworkingUtils.TryFetchTextureAsync(albedoFileUrl);
                            if (albedoTexture != null)
                            {
                                //Debug.Log($"Albedo texture applied for material: {materialName}");
                                material.mainTexture = albedoTexture;
                            }
                            else
                            {
                                //Debug.LogWarning($"Albedo texture not available for: {materialName}");
                            }
                        }
                        
                    }
                }

                // Step 4: Fetch and assign normal map (try .png first, then .tga)
                string normalFileNamePng = $"{materialName}_normal.png";
                string normalFileNameTga = $"{materialName}_normal.tga";

                //Debug.Log($"Attempting to fetch normal map: {normalFileNamePng}");
                string normalFileUrl = await NetworkingUtils.FetchSignedURLAsync(normalFileNamePng);
                Texture2D normalMap = await NetworkingUtils.TryFetchTextureAsync(normalFileUrl);

                if (normalMap == null)
                {
                    Debug.LogWarning($"Normal map .png not found. Trying .tga for: {materialName}");
                    normalFileUrl = await NetworkingUtils.FetchSignedURLAsync(normalFileNameTga);
                    normalMap = await NetworkingUtils.TryFetchTextureAsync(normalFileUrl);
                }

                if (normalMap != null)
                {
                    //Debug.Log($"Normal map applied for material: {materialName}");
                    material.SetTexture("_BumpMap", normalMap);
                    material.EnableKeyword("_NORMALMAP");
                }
                else
                {
                    Debug.LogWarning($"No normal map found for: {materialName}");
                }

                material.name = materialName;
                return material;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error building material '{materialName}': {ex.Message}");
                return null;
            }
        }

    }
}
