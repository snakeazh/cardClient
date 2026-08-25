using System;
using UnityEngine;

namespace Framework.Log
{
    /// <summary>
    /// Runtime gate for <see cref="AppLog"/>. Editor stores in EditorPrefs; player builds use PlayerPrefs.
    /// Defaults: all channels on, min level Info.
    /// </summary>
    public static class LogFilter
    {
        public const string MinLevelKey = "Framework.Log.MinLevel";
        public const string ChannelsKey = "Framework.Log.Channels";

        private static readonly int AllChannelsMask;

        static LogFilter()
        {
            AllChannelsMask = (1 << Enum.GetValues(typeof(LogChannel)).Length) - 1;
        }

        public static LogLevel MinLevel
        {
            get => (LogLevel)GetInt(MinLevelKey, (int)LogLevel.Info);
            set => SetInt(MinLevelKey, (int)value);
        }

        public static int ChannelMask
        {
            get => GetInt(ChannelsKey, AllChannelsMask);
            set => SetInt(ChannelsKey, value & AllChannelsMask);
        }

        public static bool Allows(LogChannel channel, LogLevel level)
        {
            if (level < MinLevel)
            {
                return false;
            }

            return IsChannelEnabled(channel);
        }

        public static bool IsChannelEnabled(LogChannel channel)
        {
            return (ChannelMask & ChannelBit(channel)) != 0;
        }

        public static void SetChannelEnabled(LogChannel channel, bool enabled)
        {
            var bit = ChannelBit(channel);
            ChannelMask = enabled ? ChannelMask | bit : ChannelMask & ~bit;
        }

        public static void EnableAllChannels()
        {
            ChannelMask = AllChannelsMask;
        }

        public static void DisableAllChannels()
        {
            ChannelMask = 0;
        }

        private static int ChannelBit(LogChannel channel)
        {
            return 1 << (int)channel;
        }

        private static int GetInt(string key, int fallback)
        {
#if UNITY_EDITOR
            return UnityEditor.EditorPrefs.GetInt(key, fallback);
#else
            return PlayerPrefs.GetInt(key, fallback);
#endif
        }

        private static void SetInt(string key, int value)
        {
#if UNITY_EDITOR
            UnityEditor.EditorPrefs.SetInt(key, value);
#else
            PlayerPrefs.SetInt(key, value);
            PlayerPrefs.Save();
#endif
        }
    }
}
