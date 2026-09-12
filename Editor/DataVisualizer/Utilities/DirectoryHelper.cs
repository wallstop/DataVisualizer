namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;
    using System.IO;
    using System.Runtime.CompilerServices;
    using UnityEditor;
    using UnityEngine;

    /*
        Public because the package's editor test assembly cannot see internals of the editor
        assembly (InternalsVisibleTo is not honored for that pair in Unity's compilation).
    */
    public static class DirectoryHelper
    {
        public static void EnsureDirectoryExists(string relativeDirectoryPath)
        {
            if (string.IsNullOrWhiteSpace(relativeDirectoryPath))
            {
                return;
            }

            if (!relativeDirectoryPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                if (
                    string.Equals(
                        relativeDirectoryPath,
                        "Assets",
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return;
                }

                Debug.LogError(
                    $"Attempted to create directory outside of Assets: '{relativeDirectoryPath}'"
                );
                throw new ArgumentException(
                    "Cannot create directories outside the Assets folder using AssetDatabase.",
                    nameof(relativeDirectoryPath)
                );
            }

            if (AssetDatabase.IsValidFolder(relativeDirectoryPath))
            {
                return;
            }

            string parentPath = Path.GetDirectoryName(relativeDirectoryPath).SanitizePath();
            if (
                string.IsNullOrWhiteSpace(parentPath)
                || string.Equals(parentPath, "Assets", StringComparison.OrdinalIgnoreCase)
            )
            {
                string folderNameToCreate = Path.GetFileName(relativeDirectoryPath);
                if (
                    !string.IsNullOrWhiteSpace(folderNameToCreate)
                    && !AssetDatabase.IsValidFolder(relativeDirectoryPath)
                )
                {
                    AssetDatabase.CreateFolder("Assets", folderNameToCreate);
                }
                return;
            }

            EnsureDirectoryExists(parentPath);
            string currentFolderName = Path.GetFileName(relativeDirectoryPath);
            if (
                !string.IsNullOrWhiteSpace(currentFolderName)
                && !AssetDatabase.IsValidFolder(relativeDirectoryPath)
            )
            {
                AssetDatabase.CreateFolder(parentPath, currentFolderName);
            }
        }

        public static string GetCallerScriptDirectory([CallerFilePath] string sourceFilePath = "")
        {
            return string.IsNullOrWhiteSpace(sourceFilePath)
                ? string.Empty
                : Path.GetDirectoryName(sourceFilePath);
        }

        public static string FindPackageRootPath(string startDirectory)
        {
            return FindRootPath(
                startDirectory,
                static path => File.Exists(Path.Combine(path, "package.json"))
            );
        }

        public static string FindRootPath(
            string startDirectory,
            Func<string, bool> terminalCondition
        )
        {
            string currentPath = startDirectory;
            while (!string.IsNullOrWhiteSpace(currentPath))
            {
                try
                {
                    if (terminalCondition(currentPath))
                    {
                        DirectoryInfo directoryInfo = new(currentPath);
                        if (!directoryInfo.Exists)
                        {
                            return currentPath;
                        }

                        return directoryInfo.FullName;
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    return currentPath;
                }

                try
                {
                    string parentPath = Path.GetDirectoryName(currentPath);
                    if (string.Equals(parentPath, currentPath, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    currentPath = parentPath;
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    return currentPath;
                }
            }

            return string.Empty;
        }

        public static string FindAbsolutePathToDirectory(string directory)
        {
            string scriptDirectory = GetCallerScriptDirectory();
            if (string.IsNullOrEmpty(scriptDirectory))
            {
                return string.Empty;
            }

            string packageRootAbsolute = FindPackageRootPath(scriptDirectory);
            if (string.IsNullOrEmpty(packageRootAbsolute))
            {
                return string.Empty;
            }

            string targetPathAbsolute = Path.Combine(
                packageRootAbsolute,
                directory.Replace('/', Path.DirectorySeparatorChar)
            );

            return AbsoluteToUnityRelativePath(targetPathAbsolute);
        }

        public static string AbsoluteToUnityRelativePath(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath))
            {
                return string.Empty;
            }

            absolutePath = absolutePath.SanitizePath();
            string projectRoot = Application.dataPath.SanitizePath();

            projectRoot = Path.GetDirectoryName(projectRoot)?.SanitizePath();
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                return string.Empty;
            }

            /*
                Windows drive roots keep a trailing separator after GetDirectoryName; trim it so
                the boundary check below uses one uniform indexing model.
            */
            if (projectRoot[projectRoot.Length - 1] == '/')
            {
                projectRoot = projectRoot[..^1];
            }

            if (
                !absolutePath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase)
                || absolutePath.Length <= projectRoot.Length
                || absolutePath[projectRoot.Length] != '/'
            )
            {
                return string.Empty;
            }

            return absolutePath[(projectRoot.Length + 1)..];
        }
    }
}
