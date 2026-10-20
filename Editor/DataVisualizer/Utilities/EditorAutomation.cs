namespace WallstopStudios.DataVisualizer.Editor.Utilities
{
    using System;
    using System.IO;
    using UnityEditor;
    using UnityEngine;
    using Stopwatch = System.Diagnostics.Stopwatch;

    public static class EditorAutomation
    {
        public static void RunFromCommandLine()
        {
            int exitCode;
            try
            {
                string[] args = Environment.GetCommandLineArgs();
                exitCode = RunFiles(ArgumentOrNull(args, "-dxRequest"), ArgumentOrNull(args, "-dxResponse"));
            }
            catch (Exception error)
            {
                Debug.LogError("DxVisualizer automation failed: " + error.Message);
                exitCode = 1;
            }
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        /// <summary>Writes one response page; returns 0 for completed work, 1 for rejection, or 2 for partial work.</summary>
        public static int RunFiles(string requestPath, string responsePath, int timeoutMilliseconds = 30000)
        {
            if (string.IsNullOrWhiteSpace(requestPath) || string.IsNullOrWhiteSpace(responsePath) || timeoutMilliseconds < 0)
            {
                throw new ArgumentException("Specify request/response paths and a nonnegative timeout.");
            }
            string input = Path.GetFullPath(requestPath);
            string output = Path.GetFullPath(responsePath);
            const StringComparison comparison = StringComparison.OrdinalIgnoreCase;
            if (string.Equals(input, output, comparison) || !string.Equals(Path.GetExtension(output), ".json", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Response must be a separate .json file.");
            }
            string project = Directory.GetParent(Application.dataPath).FullName;
            foreach (string directory in new[] { "Assets", "Packages", "ProjectSettings" })
            {
                string root = Path.Combine(project, directory);
                if (string.Equals(output, root, comparison) || output.StartsWith(root + Path.DirectorySeparatorChar, comparison))
                {
                    throw new ArgumentException("Write responses outside Assets, Packages, and ProjectSettings.");
                }
            }
            string request;
            try
            {
                request = File.ReadAllText(input);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                File.WriteAllText(
                    output,
                    JsonUtility.ToJson(
                        new EditorAutomationResponse
                        {
                            status = "rejected",
                            code = "request_read_failed",
                            detail = error.Message,
                            items = Array.Empty<EditorAutomationItem>(),
                        },
                        true
                    )
                );
                return 1;
            }
            using EditorAutomationSession session = EditorAutomationSession.Start(request, requireSinglePage: true);
            Stopwatch clock = Stopwatch.StartNew();
            while (!session.IsFinished && !session.IsWaitingForEditor && clock.ElapsedMilliseconds < timeoutMilliseconds)
            {
                session.Advance();
            }
            if (!session.IsFinished && !session.IsWaitingForEditor)
            {
                session.Cancel("time_limit");
            }
            string json = session.GetResponseJson();
            File.WriteAllText(output, json);
            EditorAutomationResponse response = JsonUtility.FromJson<EditorAutomationResponse>(json);
            return response.status == "rejected" || response.status == "failed" ? 1
                : response.complete ? 0
                : 2;
        }

        private static string ArgumentOrNull(string[] args, string name)
        {
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal))
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }
}
