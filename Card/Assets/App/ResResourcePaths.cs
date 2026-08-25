namespace App.Resources
{
    /// <summary>
    /// Asset keys for bundles built from Assets/Res (e.g. bundle "ui" + asset "Home").
    /// 配置表路径见 Generated/ResResourcePaths.Config.g.cs（自动生成）。
    /// </summary>
    public static partial class ResResourcePaths
    {
        public const string UIRoot = "UI/UIRoot";
        public const string Home = "UI/Home";
        public const string MainInterfaceBottom = "UI/Bottom/MainInterfaceBottom";
        public const string HealthAdvisory = "UI/HealthAdvisory";
        public const string LevelUI = "UI/LevelUI";
        public const string ConfirmDialog = "UI/ConfirmDialog";
        public const string GameUI = "UI/GameUI";
        public const string BattleFailPopup = "UI/Popup/BattleFailPopup";
        public const string BattleShopPop = "UI/Popup/BattleShopPop";
        public const string BattleSettleUpPop = "UI/Popup/BattleSettleUpPop";
        public const string IllustratedBookPop = "UI/Popup/IllustratedBookPop";
        public const string ItemTip = "UI/Top/ItemTip";
        public const string GameHud = "Game/GameHud";
        public const string CardIcon = "Game/CardIcon";
        public const string CardShadow = "UI/Icon/CardShadow";
        public const string PlayerItem = "UI/Icon/PlayerItem";
        public const string ShopItem = "UI/Icon/ShopItem";
        public const string Item = "UI/Icon/Item";
        /// <summary>SpriteAtlas under Assets/Res/Altas/Card.spriteatlasv2.</summary>
        public const string CardAtlas = "Altas/Card";
        /// <summary>SpriteAtlas under Assets/Res/Altas/CardType.spriteatlasv2.</summary>
        public const string CardTypeAtlas = "Altas/CardType";
        /// <summary>SpriteAtlas under Assets/Res/Altas/Relic.spriteatlasv2.</summary>
        public const string RelicAtlas = "Altas/Relic";

        public static string RoleAttack(int index) => $"Textures/role/role{index}_attack";

        /// <summary>
        /// <see cref="App.Config.HeroConfig.Icon"/> 对应 Assets/Res/Textures/role 下的文件名（无扩展名）。
        /// </summary>
        public static string RoleIcon(string icon)
        {
            if (string.IsNullOrWhiteSpace(icon))
            {
                return null;
            }

            return $"Textures/role/{icon.Trim()}";
        }

        /// <summary>
        /// <see cref="App.Config.RelicConfig.Icon"/> 对应 Assets/Res/Textures/Relic 下的文件名（无扩展名）。
        /// </summary>
        public static string RelicIcon(string icon)
        {
            if (string.IsNullOrWhiteSpace(icon))
            {
                return null;
            }

            return $"Textures/Relic/{icon.Trim()}";
        }

        public static string EnemyAttack(int index) => $"Textures/enemy/enemy{index}_attack";
    }
}
