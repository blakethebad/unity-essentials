using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UnityEssentials.Utilities
{
    /// <summary>
    /// A logger for one aspect of a project: it prefixes every message with a coloured header and
    /// owns the palette, hashing, message composition and console dispatch behind that. Built
    /// without a header (as <see cref="Log"/> builds it) it logs messages bare. Stripped from
    /// release builds exactly like <see cref="Log"/>.
    /// </summary>
    public sealed class CustomLogger
    {
        internal const string NullMessage = "Null";

        internal const string CriticalColorHex = "E04B4B";

        private const uint OffsetBasis = 2166136261u;

        private const uint Prime = 16777619u;

        internal static readonly Color32[] Palette =
        {
            new Color32(210, 95, 95, 255),
            new Color32(208, 133, 64, 255),
            new Color32(184, 145, 42, 255),
            new Color32(138, 161, 60, 255),
            new Color32(79, 160, 97, 255),
            new Color32(63, 163, 155, 255),
            new Color32(65, 147, 196, 255),
            new Color32(90, 127, 208, 255),
            new Color32(123, 111, 214, 255),
            new Color32(164, 100, 200, 255),
            new Color32(199, 95, 168, 255),
            new Color32(201, 96, 125, 255)
        };

        private static readonly Dictionary<CallSite, int> _intervalCounts =
            new Dictionary<CallSite, int>();

        private readonly string _prefix;

        /// <summary>Creates a logger with neither header nor colour; messages are logged bare.</summary>
        public CustomLogger()
        {
        }

        /// <summary>Creates a logger whose colour is derived from <paramref name="header"/>.</summary>
        public CustomLogger(string header)
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                throw new ArgumentException(
                    "A logger header must be a non-empty, non-whitespace string.", nameof(header));
            }

            Color color = PickColor(header);
            Header = header;
            Color = color;
            _prefix = BuildPrefix(header, color);
        }

        /// <summary>Creates a logger with an explicit header colour.</summary>
        public CustomLogger(string header, Color color)
        {
            if (string.IsNullOrWhiteSpace(header))
            {
                throw new ArgumentException(
                    "A logger header must be a non-empty, non-whitespace string.", nameof(header));
            }

            Header = header;
            Color = color;
            _prefix = BuildPrefix(header, color);
        }

        public string Header { get; }

        public Color Color { get; }

        internal string Prefix => _prefix;

        /// <summary>Logs an informational message under this logger's header.</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public void Info(object message)
        {
            Dispatch(LogSeverity.Info, message);
        }

        /// <summary>Logs a warning under this logger's header.</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public void Warning(object message)
        {
            Dispatch(LogSeverity.Warning, message);
        }

        /// <summary>Logs an error under this logger's header.</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public void Error(object message)
        {
            Dispatch(LogSeverity.Error, message);
        }

        /// <summary>Logs an error whose body is highlighted in red, for failures worth spotting at a glance.</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public void Critical(object message)
        {
            Dispatch(LogSeverity.Critical, message);
        }

        /// <summary>Logs a message at the given severity, for callers that pick the severity at runtime.</summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public void Message(LogSeverity severity, object message)
        {
            Dispatch(severity, message);
        }

        /// <summary>
        /// Logs an informational message on the first call at this call site, then on every
        /// <paramref name="interval"/>-th call from it.
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public void Interval(
            object message, int interval,
            [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            DispatchInterval(LogSeverity.Info, message, interval, filePath, lineNumber);
        }

        /// <summary>
        /// Logs at the given severity on the first call at this call site, then on every
        /// <paramref name="interval"/>-th call from it.
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        [Conditional("DEVELOPMENT_BUILD")]
        public void Interval(
            LogSeverity severity, object message, int interval,
            [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0)
        {
            DispatchInterval(severity, message, interval, filePath, lineNumber);
        }

        // CustomLogger is non-generic, so RuntimeInitializeOnLoadMethod fires on it directly and the
        // counters need no StaticResetRegistry entry.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        internal static void ResetIntervalCounts()
        {
            _intervalCounts.Clear();
        }

        internal static uint Hash(string header)
        {
            unchecked
            {
                var hash = OffsetBasis;
                for (var i = 0; i < header.Length; i++)
                {
                    hash ^= header[i];
                    hash *= Prime;
                }

                return hash;
            }
        }

        internal static Color32 PickColor(string header)
        {
            return Palette[Hash(header) % (uint)Palette.Length];
        }

        internal static string ToHex(Color color)
        {
            // Round rather than truncate: Unity's Color to Color32 conversion floors, which turns an
            // exact palette entry such as 210 into 209 after the round trip through Color.
            var r = (byte)Mathf.Clamp(Mathf.RoundToInt(color.r * 255f), 0, 255);
            var g = (byte)Mathf.Clamp(Mathf.RoundToInt(color.g * 255f), 0, 255);
            var b = (byte)Mathf.Clamp(Mathf.RoundToInt(color.b * 255f), 0, 255);
            return r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
        }

        internal static string BuildPrefix(string header, Color color)
        {
            return "<color=#" + ToHex(color) + ">[" + header + "]</color> ";
        }

        internal static string FormatMessage(string prefix, LogSeverity severity, object message)
        {
            var body = message?.ToString() ?? NullMessage;
            if (severity == LogSeverity.Critical)
            {
                body = "<color=#" + CriticalColorHex + ">" + body + "</color>";
            }

            // A null prefix concatenates as empty and String.Concat hands back the body itself, so
            // a header-less logger pays no allocation for the join.
            return prefix + body;
        }

        private void Dispatch(LogSeverity severity, object message)
        {
            var text = FormatMessage(_prefix, severity, message);
            switch (severity)
            {
                case LogSeverity.Info:
                    Debug.Log(text);
                    break;
                case LogSeverity.Warning:
                    Debug.LogWarning(text);
                    break;
                case LogSeverity.Error:
                case LogSeverity.Critical:
                    Debug.LogError(text);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(severity), severity, "Unknown log severity.");
            }
        }

        // Counts are keyed by call site alone and shared by every logger instance, so one log
        // statement owns one counter. Storing (count + 1) % interval keeps the first call at a site
        // logging, every interval-th call after it logging, and the stored value bounded.
        private void DispatchInterval(
            LogSeverity severity, object message, int interval, string filePath, int lineNumber)
        {
            StaticResetRegistry.AssertMainThread();

            if (interval <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(interval), interval, "A logging interval must be positive.");
            }

            var site = new CallSite(filePath, lineNumber);
            _intervalCounts.TryGetValue(site, out var count);
            if (count == 0)
            {
                Dispatch(severity, message);
            }

            _intervalCounts[site] = (count + 1) % interval;
        }

        private readonly struct CallSite : IEquatable<CallSite>
        {
            private readonly string _filePath;

            private readonly int _lineNumber;

            internal CallSite(string filePath, int lineNumber)
            {
                _filePath = filePath;
                _lineNumber = lineNumber;
            }

            public bool Equals(CallSite other)
            {
                return _lineNumber == other._lineNumber &&
                       string.Equals(_filePath, other._filePath, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is CallSite other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((_filePath?.GetHashCode() ?? 0) * 397) ^ _lineNumber;
                }
            }
        }
    }
}
