using UnityEditor;
using UnityEngine;
using System.IO;
using System;
using LML; // Use the LML namespace

namespace LML
{
    public class LMLImporterWindow : EditorWindow
    {
        private string lmlFilePath = ""; // Path to the selected .lml file
        private string assetsFolderPath = ""; // Path to the assets folder
        private string tarGzUrl = ""; // URL for the .tar.gz file

        [MenuItem("Tools/LML Importer")]
        public static void ShowWindow()
        {
            // Show existing window or create a new one
            GetWindow<LMLImporterWindow>("LML Importer");
        }

        private void OnGUI()
        {
            GUILayout.Label("LML Importer", EditorStyles.boldLabel);

            // File Picker for output.lml
            if (GUILayout.Button("Select LML File"))
            {
                lmlFilePath = EditorUtility.OpenFilePanel("Select output.lml File", "", "lml");
                if (!string.IsNullOrEmpty(lmlFilePath))
                {
                    assetsFolderPath = Path.Combine(Path.GetDirectoryName(lmlFilePath), "assets");
                }
            }

            // Display the selected LML file path
            if (!string.IsNullOrEmpty(lmlFilePath))
            {
                GUILayout.Label($"Selected LML File: {lmlFilePath}", EditorStyles.wordWrappedLabel);
                GUILayout.Label($"Expected Assets Folder: {assetsFolderPath}", EditorStyles.wordWrappedLabel);
            }

            // Parse and process the LML file
            if (!string.IsNullOrEmpty(lmlFilePath) && GUILayout.Button("Parse and Import LML"))
            {
                ParseAndImportLML(lmlFilePath, assetsFolderPath);
            }

            GUILayout.Space(20);

            // Input for .tar.gz presigned URL
            GUILayout.Label("Download and Import from Import Code", EditorStyles.boldLabel);
            tarGzUrl = EditorGUILayout.TextField("Import Code", tarGzUrl);

            if (!string.IsNullOrEmpty(tarGzUrl) && GUILayout.Button("Download and Import from Import Code"))
            {
                DownloadAndProcessTarGz(tarGzUrl);
            }
        }

        private void ParseAndImportLML(string lmlPath, string assetsPath)
        {
            try
            {
                // Parse the LML file
                LMLScene scene = LMLParser.Parse(lmlPath);
                Debug.Log($"Successfully parsed LML file: {lmlPath}");

                // Build the scene
                GameObject sceneGO = LMLSceneBuilder.BuildScene(scene, assetsPath);
                Debug.Log("Scene successfully built.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error processing LML file: {ex.Message}");
            }
        }

        private async void DownloadAndProcessTarGz(string url)
        {
            try
            {
                string downloadFolder = Path.Combine(Application.dataPath, "DownloadedLML");
                if (!Directory.Exists(downloadFolder))
                {
                    Directory.CreateDirectory(downloadFolder);
                }

                string tarGzFilePath = Path.Combine(downloadFolder, "scene.tar.gz");
                Debug.Log($"Downloading .tar.gz file from: {url} to {tarGzFilePath}");

                // Download the file
                await NetworkingUtils.DownloadFileAsync(url, tarGzFilePath, true);
                Debug.Log("Download completed.");

                // Extract the .tar.gz file
                string extractedFolder = Path.Combine(downloadFolder, "ExtractedScene");
                if (Directory.Exists(extractedFolder))
                {
                    Directory.Delete(extractedFolder, true);
                }
                Directory.CreateDirectory(extractedFolder);

                TargzExtractor.ExtractTarGz(tarGzFilePath, extractedFolder);
                Debug.Log($"Extraction completed. Files extracted to: {extractedFolder}");

                // Find the .lml file
                string[] lmlFiles = Directory.GetFiles(extractedFolder, "*.lml", SearchOption.AllDirectories);
                if (lmlFiles.Length == 0)
                {
                    Debug.LogError("No .lml file found in the extracted archive.");
                    return;
                }

                string extractedLmlPath = lmlFiles[0];
                string extractedAssetsPath = Path.Combine(Path.GetDirectoryName(extractedLmlPath), "assets");

                Debug.Log($"Found .lml file: {extractedLmlPath}");

                // Process the .lml file
                ParseAndImportLML(extractedLmlPath, extractedAssetsPath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error downloading or processing .tar.gz file: {ex.Message}");
            }
        }
    }
}
