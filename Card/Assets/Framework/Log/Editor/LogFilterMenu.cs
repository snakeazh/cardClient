using UnityEditor;

namespace Framework.Log.Editor
{
    public static class LogFilterMenu
    {
        private const string MinDebug = "Log/Min Level/Debug";
        private const string MinInfo = "Log/Min Level/Info";
        private const string MinWarning = "Log/Min Level/Warning";
        private const string MinError = "Log/Min Level/Error";
        private const string EnableAll = "Log/Channel/Enable All";
        private const string DisableAll = "Log/Channel/Disable All";
        private const string ChBootstrap = "Log/Channel/Bootstrap";
        private const string ChConfig = "Log/Channel/Config";
        private const string ChGame = "Log/Channel/Game";
        private const string ChUi = "Log/Channel/UI";
        private const string ChLevel = "Log/Channel/Level";
        private const string ChBag = "Log/Channel/Bag";
        private const string ChScore = "Log/Channel/Score";
        private const string ChAtlas = "Log/Channel/Atlas";
        private const string ChAssets = "Log/Channel/Assets";
        private const string ChSave = "Log/Channel/Save";
        private const string ChTalent = "Log/Channel/Talent";

        [MenuItem(MinDebug, false, 100)]
        private static void SetMinDebug() => LogFilter.MinLevel = LogLevel.Debug;

        [MenuItem(MinDebug, true)]
        private static bool SetMinDebugValidate()
        {
            Menu.SetChecked(MinDebug, LogFilter.MinLevel == LogLevel.Debug);
            return true;
        }

        [MenuItem(MinInfo, false, 101)]
        private static void SetMinInfo() => LogFilter.MinLevel = LogLevel.Info;

        [MenuItem(MinInfo, true)]
        private static bool SetMinInfoValidate()
        {
            Menu.SetChecked(MinInfo, LogFilter.MinLevel == LogLevel.Info);
            return true;
        }

        [MenuItem(MinWarning, false, 102)]
        private static void SetMinWarning() => LogFilter.MinLevel = LogLevel.Warning;

        [MenuItem(MinWarning, true)]
        private static bool SetMinWarningValidate()
        {
            Menu.SetChecked(MinWarning, LogFilter.MinLevel == LogLevel.Warning);
            return true;
        }

        [MenuItem(MinError, false, 103)]
        private static void SetMinError() => LogFilter.MinLevel = LogLevel.Error;

        [MenuItem(MinError, true)]
        private static bool SetMinErrorValidate()
        {
            Menu.SetChecked(MinError, LogFilter.MinLevel == LogLevel.Error);
            return true;
        }

        [MenuItem(EnableAll, false, 200)]
        private static void EnableAllChannels() => LogFilter.EnableAllChannels();

        [MenuItem(DisableAll, false, 201)]
        private static void DisableAllChannels() => LogFilter.DisableAllChannels();

        [MenuItem(ChBootstrap, false, 210)]
        private static void ToggleBootstrap() => Toggle(LogChannel.Bootstrap);

        [MenuItem(ChBootstrap, true)]
        private static bool ToggleBootstrapValidate() => Validate(ChBootstrap, LogChannel.Bootstrap);

        [MenuItem(ChConfig, false, 211)]
        private static void ToggleConfig() => Toggle(LogChannel.Config);

        [MenuItem(ChConfig, true)]
        private static bool ToggleConfigValidate() => Validate(ChConfig, LogChannel.Config);

        [MenuItem(ChGame, false, 212)]
        private static void ToggleGame() => Toggle(LogChannel.Game);

        [MenuItem(ChGame, true)]
        private static bool ToggleGameValidate() => Validate(ChGame, LogChannel.Game);

        [MenuItem(ChUi, false, 213)]
        private static void ToggleUi() => Toggle(LogChannel.UI);

        [MenuItem(ChUi, true)]
        private static bool ToggleUiValidate() => Validate(ChUi, LogChannel.UI);

        [MenuItem(ChLevel, false, 214)]
        private static void ToggleLevel() => Toggle(LogChannel.Level);

        [MenuItem(ChLevel, true)]
        private static bool ToggleLevelValidate() => Validate(ChLevel, LogChannel.Level);

        [MenuItem(ChBag, false, 215)]
        private static void ToggleBag() => Toggle(LogChannel.Bag);

        [MenuItem(ChBag, true)]
        private static bool ToggleBagValidate() => Validate(ChBag, LogChannel.Bag);

        [MenuItem(ChScore, false, 216)]
        private static void ToggleScore() => Toggle(LogChannel.Score);

        [MenuItem(ChScore, true)]
        private static bool ToggleScoreValidate() => Validate(ChScore, LogChannel.Score);

        [MenuItem(ChAtlas, false, 217)]
        private static void ToggleAtlas() => Toggle(LogChannel.Atlas);

        [MenuItem(ChAtlas, true)]
        private static bool ToggleAtlasValidate() => Validate(ChAtlas, LogChannel.Atlas);

        [MenuItem(ChAssets, false, 218)]
        private static void ToggleAssets() => Toggle(LogChannel.Assets);

        [MenuItem(ChAssets, true)]
        private static bool ToggleAssetsValidate() => Validate(ChAssets, LogChannel.Assets);

        [MenuItem(ChSave, false, 219)]
        private static void ToggleSave() => Toggle(LogChannel.Save);

        [MenuItem(ChSave, true)]
        private static bool ToggleSaveValidate() => Validate(ChSave, LogChannel.Save);

        [MenuItem(ChTalent, false, 220)]
        private static void ToggleTalent() => Toggle(LogChannel.Talent);

        [MenuItem(ChTalent, true)]
        private static bool ToggleTalentValidate() => Validate(ChTalent, LogChannel.Talent);

        private static void Toggle(LogChannel channel)
        {
            LogFilter.SetChannelEnabled(channel, !LogFilter.IsChannelEnabled(channel));
        }

        private static bool Validate(string menu, LogChannel channel)
        {
            Menu.SetChecked(menu, LogFilter.IsChannelEnabled(channel));
            return true;
        }
    }
}
