using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System;
using UnityEditor;
using System.Threading.Tasks;
using UnityEngine.Networking;
using UnityEditor.Experimental.GraphView;
using UnityEditor.Callbacks;
using System.Net;
using UnityEngine.Rendering;

//using GLTFast.Schema;

namespace LML
{
    public static class LMLSceneBuilder
    {
        private static Dictionary<string, GameObject> sharedWalls;

        private static bool isURPProject;

        public static bool IsURPProject()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null)
            {
                // Check if the render pipeline's type name contains "Universal"
                // This avoids direct URP type references that would cause compilation errors
                isURPProject = pipeline.GetType().FullName.Contains("Universal");
                return isURPProject;
            }
            return false;
        }

        public static Material ConvertToURP(Material standardMaterial)
        {
            // Find the URP Lit shader
            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
            if (urpShader == null)
            {
                Debug.LogError("URP Lit shader not found! Make sure URP is properly installed.");
                return standardMaterial;
            }

            // Create new material with URP shader
            Material urpMaterial = new Material(urpShader);
            urpMaterial.name = standardMaterial.name;

            // Main Texture and Color
            if (standardMaterial.HasProperty("_MainTex"))
            {
                urpMaterial.SetTexture("_BaseMap", standardMaterial.GetTexture("_MainTex"));
            }
            if (standardMaterial.HasProperty("_Color"))
            {
                urpMaterial.SetColor("_BaseColor", standardMaterial.GetColor("_Color"));
            }

            // Normal Map
            if (standardMaterial.HasProperty("_BumpMap"))
            {
                var normalMap = standardMaterial.GetTexture("_BumpMap");
                if (normalMap != null)
                {
                    urpMaterial.SetTexture("_BumpMap", normalMap);
                    urpMaterial.EnableKeyword("_NORMALMAP");

                    // Copy normal map intensity if it exists
                    if (standardMaterial.HasProperty("_BumpScale"))
                    {
                        urpMaterial.SetFloat("_BumpScale", standardMaterial.GetFloat("_BumpScale"));
                    }
                }
            }

            // Metallic Setup
            if (standardMaterial.HasProperty("_MetallicGlossMap"))
            {
                var metallicMap = standardMaterial.GetTexture("_MetallicGlossMap");
                if (metallicMap != null)
                {
                    urpMaterial.SetTexture("_MetallicGlossMap", metallicMap);
                    urpMaterial.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
            }
            if (standardMaterial.HasProperty("_Metallic"))
            {
                urpMaterial.SetFloat("_Metallic", standardMaterial.GetFloat("_Metallic"));
            }

            // Smoothness
            if (standardMaterial.HasProperty("_Glossiness"))
            {
                urpMaterial.SetFloat("_Smoothness", standardMaterial.GetFloat("_Glossiness"));
            }
            else if (standardMaterial.HasProperty("_Glossiness"))
            {
                urpMaterial.SetFloat("_Smoothness", standardMaterial.GetFloat("_Glossiness"));
            }

            // Emission
            if (standardMaterial.HasProperty("_EmissionColor"))
            {
                var emissionColor = standardMaterial.GetColor("_EmissionColor");
                if (emissionColor != Color.black)
                {
                    urpMaterial.SetColor("_EmissionColor", emissionColor);
                    urpMaterial.EnableKeyword("_EMISSION");
                }
            }
            if (standardMaterial.HasProperty("_EmissionMap"))
            {
                var emissionMap = standardMaterial.GetTexture("_EmissionMap");
                if (emissionMap != null)
                {
                    urpMaterial.SetTexture("_EmissionMap", emissionMap);
                    urpMaterial.EnableKeyword("_EMISSION");
                }
            }

            // Occlusion
            if (standardMaterial.HasProperty("_OcclusionMap"))
            {
                var occlusionMap = standardMaterial.GetTexture("_OcclusionMap");
                if (occlusionMap != null)
                {
                    urpMaterial.SetTexture("_OcclusionMap", occlusionMap);

                    if (standardMaterial.HasProperty("_OcclusionStrength"))
                    {
                        urpMaterial.SetFloat("_OcclusionStrength",
                            standardMaterial.GetFloat("_OcclusionStrength"));
                    }
                }
            }

            // Surface Settings
            urpMaterial.SetFloat("_Surface", 0); // 0 = Opaque, 1 = Transparent
            urpMaterial.SetFloat("_WorkflowMode", 1); // 1 = Metallic workflow

            // Copy rendering settings
            urpMaterial.renderQueue = standardMaterial.renderQueue;
            urpMaterial.enableInstancing = standardMaterial.enableInstancing;
            urpMaterial.doubleSidedGI = standardMaterial.doubleSidedGI;

            return urpMaterial;
        }

        public static GameObject BuildScene(LMLScene scene, string assetsPath)
        {
            Debug.Log($"IS UNIVERSAL: {IsURPProject()}");
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

            // Define the path to the specific door prefab
            // This will work whether the code is in Packages or Assets
            string packagePath = "Packages/com.limitlabs.lml2unity";
            // Check if the package directory exists
            if (!Directory.Exists(packagePath))
            {
                Debug.Log($"Package path {packagePath} not found, using local development path");
                packagePath = "Assets/lml2unity/";
            }

            string prefabPath = Path.Combine(packagePath, "Prefabs", "Doorways", "Prefabs", $"{door.assetId}.prefab");
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

                if (CheckOpenWallIntersection(start, end, scene))
                {
                    continue; //this wall should be open
                }

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
                    //Debug.Log($"Intersation at room {room.room_id}");
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

        private static bool CheckOpenWallIntersection(Vector3 wallStart, Vector3 wallEnd, LMLScene scene)
        {
            foreach (LMLOpenWall openWall in scene.openWalls)
            {
                foreach (List<List<float>> segment in openWall.segments)
                {
                    if (
                         (segment[0][0] == wallStart.x && segment[0][1] == wallStart.z && segment[1][0] == wallEnd.x && segment[1][1] == wallEnd.z) ||
                         (segment[0][0] == wallEnd.x && segment[0][1] == wallEnd.z && segment[1][0] == wallStart.x && segment[1][1] == wallStart.z)
                        )
                    {
                        return true;
                    }
                }
            }
            return false;
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

            //JSON METADATA
            ModelData modelData;
            try
            {
                string jsonPath = GetUnityAssetPath(Path.Combine(assetsPath, meshPath, "metadata.json"));
                string jsonContent = File.ReadAllText(jsonPath);
                modelData = JsonUtility.FromJson<ModelData>(jsonContent);
            }
            catch
            {
                modelData = defaultModelData;
            }

            //PrintModelData(modelData);

            string assetPath = GetUnityAssetPath(Path.Combine(assetsPath, meshPath, "mesh_rescaled.glb"));
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);

            if (prefab == null)
            {
                assetPath = GetUnityAssetPath(Path.Combine(assetsPath, meshPath, "mesh.glb"));
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            }

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
                CalcOffset(modelData.bbox.min.x, modelData.bbox.max.x, instance.transform.position.x),
                instance.transform.position.y, //CalcOffset(modelData.bbox.min.y, modelData.bbox.max.y, instance.transform.position.y) - modelData.bbox.min.y,
                CalcOffset(modelData.bbox.min.z, modelData.bbox.max.z, instance.transform.position.z)
            );

            

            Vector3 rotSource = instanceGO.transform.rotation.eulerAngles;
            Vector3 target = instance.transform.rotation;

            Vector3 newAngle = rotSource + target;
            newAngle.y -= modelData.pose_z_rot_angle * 180f / 3.1415192653589793284f;


            /*
            instanceGO.transform.localRotation *= Quaternion.Euler(
                instance.transform.rotation.x,
                instance.transform.rotation.y,
                instance.transform.rotation.z
            );
            */
            instanceGO.transform.localRotation = Quaternion.Euler(newAngle);

            instanceGO.transform.localScale = new Vector3(
                instanceGO.transform.localScale.x * instance.transform.scaling.x,
                instanceGO.transform.localScale.y * instance.transform.scaling.y,
                instanceGO.transform.localScale.z * instance.transform.scaling.z
            );

            //instanceGO.transform.localScale /= modelData.lml_scale_factor;

            LMLAssetLicense thisLicense = instanceGO.AddComponent<LMLAssetLicense>();
            thisLicense.attribution = modelData.license_info.ToString();

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
                    Debug.LogError($"Failed to load material from file: {materialFileName} URL: {materialFileUrl}");
                    material = IsURPProject() ? new Material(Shader.Find("Universal Render Pipeline/Lit")) : new Material(Shader.Find("Standard"));
                }
                else if (IsURPProject())
                {
                    material = ConvertToURP(material);
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
                                Debug.Log("TRYING TO USE PNG ALBEDO");
                                //Debug.LogWarning($"Albedo texture not available for: {materialName}");
                                albedoFileName = $"{materialName}_albedo.png";
                                //Debug.Log($"Fetching albedo texture: {albedoFileName}");
                                albedoFileUrl = await NetworkingUtils.FetchSignedURLAsync(albedoFileName);
                                albedoTexture = await NetworkingUtils.TryFetchTextureAsync(albedoFileUrl);
                                Debug.Log($"Fetched the albedo maybe? Would be {materialName}, {(albedoTexture != null ? "valid":"invalid")}");
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
                    normalFileNamePng = $"{materialName}_Normal.png";
                    normalFileUrl = await NetworkingUtils.FetchSignedURLAsync(normalFileNamePng);
                    normalMap = await NetworkingUtils.TryFetchTextureAsync(normalFileUrl);
                    if (normalMap == null)
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

        private static float CalcOffset(float min, float max, float current)
        {
            float size = Math.Abs(max - min);
            float relativeCenter = min + size / 2;
            float offset  = -relativeCenter;
            return current - offset;
        }

        public static void PrintModelData(ModelData data)
        {
            if (data == null)
            {
                Debug.LogError("ModelData is null!");
                return;
            }

            // Bounding Box
            Debug.Log("=== Bounding Box ===");
            if (data.bbox != null)
            {
                Debug.Log($"Min: ({data.bbox.min.x}, {data.bbox.min.y}, {data.bbox.min.z})");
                Debug.Log($"Max: ({data.bbox.max.x}, {data.bbox.max.y}, {data.bbox.max.z})");
            }
            else
            {
                Debug.Log("bbox is null");
            }

            // Unscaled Bounding Box
            Debug.Log("=== Unscaled Bounding Box ===");
            if (data.bbox_unscaled != null)
            {
                Debug.Log($"Min: ({data.bbox_unscaled.min.x}, {data.bbox_unscaled.min.y}, {data.bbox_unscaled.min.z})");
                Debug.Log($"Max: ({data.bbox_unscaled.max.x}, {data.bbox_unscaled.max.y}, {data.bbox_unscaled.max.z})");
            }
            else
            {
                Debug.Log("bbox_unscaled is null");
            }

            // Scale values
            Debug.Log("=== Scale Values ===");
            Debug.Log($"Scale: {data.scale}");
            Debug.Log($"LML Scale Factor: {data.lml_scale_factor}");
            Debug.Log($"Pose Z Rotation Angle: {data.pose_z_rot_angle}");

            // License Info
            Debug.Log("=== License Info ===");
            if (data.license_info != null)
            {
                Debug.Log($"License: {data.license_info.license}");
                Debug.Log($"URI: {data.license_info.uri}");
                Debug.Log($"Creator Username: {data.license_info.creator_username}");
                Debug.Log($"Creator Display Name: {data.license_info.creator_display_name}");
                Debug.Log($"Creator Profile URL: {data.license_info.creator_profile_url}");
            }
            else
            {
                Debug.Log("license_info is null");
            }
        }

        static ModelData defaultModelData = new ModelData
        {
            bbox = new BoundingBox
            {
                min = new Vector3Data { x = 0, y = 0, z = 0 },
                max = new Vector3Data { x = 0, y = 0, z = 0 }
            },
            bbox_unscaled = new BoundingBox
            {
                min = new Vector3Data { x = 0, y = 0, z = 0 },
                max = new Vector3Data { x = 0, y = 0, z = 0 }
            },
            scale = 1f,
            lml_scale_factor = 1f,
            pose_z_rot_angle = 0f,
            license_info = new LicenseInfo()
        };

    }

    [System.Serializable]
    public class Vector3Data
    {
        public float x;
        public float y;
        public float z;
    }

    [System.Serializable]
    public class BoundingBox
    {
        public Vector3Data min;
        public Vector3Data max;
    }

    [System.Serializable]
    public class LicenseInfo
    {
        public string license;
        public string uri;
        public string creator_username;
        public string creator_display_name;
        public string creator_profile_url;

        public override string ToString()
        {
            return $"License: {license}\n" +
                   $"URI: {uri}\n" +
                   $"Creator: {creator_display_name} (@{creator_username})\n" +
                   $"Profile: {creator_profile_url}";
        }
    }

    [System.Serializable]
    public class ModelData
    {
        public BoundingBox bbox;
        public BoundingBox bbox_unscaled;
        public float scale;
        public float lml_scale_factor;
        public float pose_z_rot_angle;
        public LicenseInfo license_info;
    }

}
