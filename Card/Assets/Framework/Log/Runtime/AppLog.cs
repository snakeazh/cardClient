using System;
using System.Diagnostics;
using UnityEngine;

namespace Framework.Log
{
    /// <summary>
    /// Static logger. Info/Debug calls are stripped from release player builds unless ENABLE_APP_LOG is set.
    /// Warning/Error always compile so logcat still shows failures.
    /// </summary>
    public static class AppLog
    {
        [HideInCallstack]
        [Conditional("UNITY_EDITOR")]
        [Conditional("ENABLE_APP_LOG")]
        public static void Debug(LogChannel channel, string message)
        {
            if (!LogFilter.Allows(channel, LogLevel.Debug))
            {
                return;
            }

            UnityEngine.Debug.Log(Format(channel, message));
        }

        [HideInCallstack]
        [Conditional("UNITY_EDITOR")]
        [Conditional("ENABLE_APP_LOG")]
        public static void Info(LogChannel channel, string message)
        {
            if (!LogFilter.Allows(channel, LogLevel.Info))
            {
                return;
            }

            UnityEngine.Debug.Log(Format(channel, message));
        }

        [HideInCallstack]
        public static void Warn(LogChannel channel, string message)
        {
            if (!LogFilter.Allows(channel, LogLevel.Warning))
            {
                return;
            }

            UnityEngine.Debug.LogWarning(Format(channel, message));
        }

        [HideInCallstack]
        public static void Warn(LogChannel channel, string message, UnityEngine.Object context)
        {
            if (!LogFilter.Allows(channel, LogLevel.Warning))
            {
                return;
            }

            UnityEngine.Debug.LogWarning(Format(channel, message), context);
        }

        [HideInCallstack]
        public static void Error(LogChannel channel, string message)
        {
            UnityEngine.Debug.LogError(Format(channel, message));
        }

        [HideInCallstack]
        public static void Error(LogChannel channel, string message, UnityEngine.Object context)
        {
            UnityEngine.Debug.LogError(Format(channel, message), context);
        }

        [HideInCallstack]
        public static void Exception(LogChannel channel, Exception exception)
        {
            if (exception == null)
            {
                UnityEngine.Debug.LogError(Format(channel, "Exception is null."));
                return;
            }

            UnityEngine.Debug.LogError(Format(channel, exception.GetType().Name + ": " + exception.Message));
            UnityEngine.Debug.LogException(exception);
        }

        private static string Format(LogChannel channel, string message)
        {
            return $"[{channel}] {message}";
        }
    }
}
