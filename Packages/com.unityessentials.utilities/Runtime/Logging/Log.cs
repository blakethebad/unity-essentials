using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace UnityEssentials.Utilities
{
    public enum LogSeverity
    {
        Info,
        Warning,
        Error,
        Critical
    }

    public static class Log
    {
        private static CustomLogger _logger;

        private static CustomLogger Logger => _logger ??= new CustomLogger();

        /// <summary>Logs an informational message to the Unity console.</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Info(object message)
        {
            Logger.Info(message);
        }

        /// <summary>Logs a warning to the Unity console.</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Warning(object message)
        {
            Logger.Warning(message);
        }

        /// <summary>Logs an error to the Unity console.</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Error(object message)
        {
            Logger.Error(message);
        }

        /// <summary>Logs an error whose body is highlighted in red, for failures worth spotting at a glance.</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Critical(object message)
        {
            Logger.Critical(message);
        }

        /// <summary>Logs a message at the given severity, for callers that pick the severity at runtime.</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Message(LogSeverity severity, object message)
        {
            Logger.Message(severity, message);
        }

        /// <summary>
        /// Logs an informational message on the first call at this call site, then on every
        /// <paramref name="interval"/>-th call from it.
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Interval(
            object message, int interval,
            [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            // The caller's call site is forwarded explicitly: leaving the logger to fill its own
            // defaults would key every counter to this line of the facade instead.
            Logger.Interval(message, interval, filePath, lineNumber);
        }

        /// <summary>
        /// Logs at the given severity on the first call at this call site, then on every
        /// <paramref name="interval"/>-th call from it.
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public static void Interval(
            LogSeverity severity, object message, int interval,
            [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            Logger.Interval(severity, message, interval, filePath, lineNumber);
        }
    }
}
