using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEditor;

namespace LML
{
    public static class NetworkingUtils
    {

        public static async Task<string> FetchSignedURLAsync(string fileName)
        {
            string endpoint = $"https://d77z45wnv0.execute-api.us-east-1.amazonaws.com/default/materials/{fileName}";
            //Debug.Log($"Fetching signed URL for file: {fileName} from endpoint: {endpoint}");

            try
            {
                using (UnityWebRequest request = UnityWebRequest.Get(endpoint))
                {
                    await SendUnityWebRequestAsync(request);

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogError($"Failed to fetch signed URL for file: {fileName}");
                        Debug.LogError($"Error: {request.error}");
                        Debug.LogError($"Response Code: {request.responseCode}");
                        if (request.downloadHandler != null)
                        {
                            Debug.LogError($"Response Text: {request.downloadHandler.text}");
                        }
                        return null;
                    }

                    string responseText = request.downloadHandler.text;
                    //Debug.Log($"Signed URL response for file '{fileName}': {responseText}");
                    var responseJson = JsonUtility.FromJson<LambdaResponse>(responseText);

                    return responseJson.url;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Exception while fetching signed URL for file: {fileName}, Error: {ex.Message}");
                return null;
            }
        }

        public static async Task<Texture2D> TryFetchTextureAsync(string url)
        {
            //Debug.Log($"Attempting to fetch texture from URL: {url}");
            try
            {
                using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(url))
                {
                    await SendUnityWebRequestAsync(request);

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogWarning($"Failed to fetch texture from URL: {url}. Error: {request.error}");
                        return null;
                    }

                    //Debug.Log($"Successfully fetched texture from URL: {url}");
                    return DownloadHandlerTexture.GetContent(request);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error fetching texture from URL: {url}. Exception: {ex.Message}");
                return null;
            }
        }

        public static async Task<Material> DownloadMaterialAsync(string materialUrl)
        {
            try
            {
                using (UnityWebRequest request = UnityWebRequest.Get(materialUrl))
                {
                    await SendUnityWebRequestAsync(request);

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogError($"Failed to download material: {request.error}");
                        return null;
                    }

                    byte[] materialData = request.downloadHandler.data;

                    // Define the path relative to the Unity project
                    string relativeFolderPath = "Assets/DownloadedMaterials";
                    string absoluteFolderPath = Path.Combine(Application.dataPath, "DownloadedMaterials");
                    string fileName = $"{Guid.NewGuid()}.mat";
                    string absolutePath = Path.Combine(absoluteFolderPath, fileName);

                    // Ensure the folder exists
                    if (!Directory.Exists(absoluteFolderPath))
                    {
                        Directory.CreateDirectory(absoluteFolderPath);
                        //Debug.Log($"Created folder: {absoluteFolderPath}");
                    }

                    // Write the material file
                    File.WriteAllBytes(absolutePath, materialData);
                    //Debug.Log($"Saved material to: {absolutePath}");

                    // Refresh AssetDatabase to ensure the file is registered
                    AssetDatabase.Refresh();

                    // Convert absolute path to a relative path for AssetDatabase
                    string relativePath = Path.Combine(relativeFolderPath, fileName).Replace("\\", "/");

                    // Load the material
                    Material tempMaterial = AssetDatabase.LoadAssetAtPath<Material>(relativePath);
                    if (tempMaterial == null)
                    {
                        Debug.LogError($"Failed to load material from path: {relativePath}");
                        return null;
                    }

                    //Debug.Log($"Successfully loaded material: {tempMaterial.name}");
                    return UnityEngine.Object.Instantiate(tempMaterial); // Return a clone to avoid modifying the original
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error downloading material: {ex.Message}");
                return null;
            }
        }

        public static async Task DownloadFileAsync(string url, string localPath, bool refresh = false)
        {
            try
            {
                using (UnityWebRequest request = UnityWebRequest.Get(url))
                {
                    await NetworkingUtils.SendUnityWebRequestAsync(request);
                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogError($"Failed to download file: {request.error}");
                        Debug.LogError($"Response Code: {request.responseCode}");

                        if (refresh)
                        {
                            string refreshedUrl = await RefreshPresignedUrl(url);
                            if (!string.IsNullOrEmpty(refreshedUrl))
                            {
                                await DownloadFileAsync(refreshedUrl, localPath, false);
                                return;
                            }
                        }

                        if (request.downloadHandler != null)
                        {
                            Debug.LogError($"Response Text: {request.downloadHandler.text}");
                        }
                        return;
                    }

                    string directory = Path.GetDirectoryName(localPath);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    File.WriteAllBytes(localPath, request.downloadHandler.data);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error downloading file from URL: {url}. Exception: {ex.Message}");
                Debug.Log("Gonna try refresh where you didn't want to");
                if (refresh)
                {
                    // Call Lambda to refresh URL
                    string refreshedUrl = await RefreshPresignedUrl(url);
                    if (!string.IsNullOrEmpty(refreshedUrl))
                    {
                        // Retry download with new URL
                        await DownloadFileAsync(refreshedUrl, localPath, false);
                        return;
                    }
                }
            }
        }

        [Serializable]
        public class RefreshUrlRequest
        {
            public string expired_url;
        }

        private static async Task<string> RefreshPresignedUrl(string expiredUrl)
        {
            try
            {
                var requestBody = new RefreshUrlRequest { expired_url = expiredUrl };
                string jsonString = JsonUtility.ToJson(requestBody);

                using (UnityWebRequest request = new UnityWebRequest("https://bcxt5ktslh.execute-api.us-east-1.amazonaws.com/default/refresh-url", "POST"))
                {
                    byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonString);
                    request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");

                    await SendUnityWebRequestAsync(request);

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        // Try parsing directly since Lambda response might be a string
                        var responseText = request.downloadHandler.text;
                        try
                        {
                            // Remove quotes if present
                            responseText = responseText.Trim('"');
                            return responseText;
                        }
                        catch
                        {
                            return null;
                        }
                    }
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error in RefreshPresignedUrl: {ex.Message}");
                return null;
            }
        }

        [System.Serializable]
        private class LambdaResponse2
        {
            public int statusCode;
            public string body;
        }

        private static Task<UnityWebRequest> SendUnityWebRequestAsync(UnityWebRequest request)
        {
            var tcs = new TaskCompletionSource<UnityWebRequest>();
            request.SendWebRequest().completed += operation => {
                tcs.SetResult(request);
            };
            return tcs.Task;
        }

        [Serializable]
        private class LambdaResponse
        {
            public string url;
        }
    }
}
