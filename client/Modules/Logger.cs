using System;
using System.Diagnostics;
using UnityEngine;

namespace pitTeam.Modules
{
    public class Logger
    {
        public Logger()
        {
        }

        [Conditional("DEBUG")]
        public static void LogInfo(string message)
        {
            if (pitFireTeam.IsDebugBuild)
                pitFireTeam.Log.LogInfo($"[{Time.time}] " + message);

        }

        [Conditional("DEBUG")]
        public static void LogTrace(string message)
        {
            if (pitFireTeam.IsDebugBuild)
            {
                var stackTrace = new StackTrace();
                pitFireTeam.Log.LogDebug($"[{Time.time}] {message}\nStackTrace:\n{stackTrace}");
            }
        }

        public static void LogError(string message)
        {
            pitFireTeam.Log.LogError(message);
        }
        public static void LogError(Exception error)
        {
            pitFireTeam.Log.LogError(error);
        }
    }
}
