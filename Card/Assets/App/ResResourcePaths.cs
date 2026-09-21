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
        public const string MainResource = "UI/Bottom/MainResource";
        public const string GameResource = "UI/Bottom/GameResource";
        public const string HealthAdvisory = "UI/HealthAdvisory";
        public const string LevelUI = "UI/LevelUI";
        public const string ConfirmDialog = "UI/ConfirmDialog";
        public const string GameUI = "UI/GameUI";
        public const string BattleFailPopup = "UI/Popup/BattleFailPopup";
        public const string BattleResultPopup = "UI/Popup/BattleResultPopup";
        public const string BattleShopPop = "UI/Popup/BattleShopPop";
        public const string BattleSettleUpPop = "UI/Popup/BattleSettleUpPop";
        public const string IllustratedBookPop = "UI/Popup/IllustratedBookPop";
        public const string TalentPopup = "UI/Popup/TalentPopup";
        public const string TalentDetail = "UI/Popup/TalentDetail";
        public const string GetEquipDetail = "UI/Popup/GetEquipDetail";
        public const string TalentRulesPop = "UI/Popup/TalentRulesPop";
        public const string ShopDetail = "UI/Top/ShopDetail";
        public const string CommonTop = "UI/Top/CommonTop";
        public const string RemainListPop = "UI/Popup/RemainListPop";
        public const string GamePopupInfo = "UI/Popup/GamePopupInfo";
        public const string EnergyPopup = "UI/Popup/EnergyPopup";
        public const string StaminaPurchasePop = "UI/Popup/StaminaPurchasePop";
        public const string ToastPanel = "UI/Popup/ToastPanel";
        public const string GuideOverlay = "UI/Guide/GuideOverlay";
        public const string ItemTip = "UI/Top/ItemTip";
        public const string WinTip = "UI/Top/WinTip";
        public const string GameHud = "Game/GameHud";
        /// <summary>对局桌面背景 Assets/Res/Textures/BG/BigBackgroundBaseFrame.png。</summary>
        public const string GameHudBg = "Textures/BG/BigBackgroundBaseFrame";
        /// <summary>Boss 关桌面背景 Assets/Res/Textures/BG/RedBigBackgroundBaseFrame.png。</summary>
        public const string GameHudBossBg = "Textures/BG/RedBigBackgroundBaseFrame";
        public const string CardIcon = "Game/CardIcon";
        public const string CardShadow = "UI/Icon/CardShadow";
        public const string PlayerItem = "UI/Icon/PlayerItem";
        public const string CardPointEffect01 = "Effect/UI/Card/CardPointEffect01";
        public const string CardPointEffect02 = "Effect/UI/Card/CardPointEffect02";
        public const string CardPointEffect03 = "Effect/UI/Card/CardPointEffect03";
        public const string ShopItem = "UI/Icon/ShopItem";
        public const string Item = "UI/Icon/Item";
        public const string CoinItem = "UI/Icon/coinitem";
        /// <summary>主界面 BGM Assets/Res/Audio/BGM/BGM.mp3。</summary>
        public const string Bgm = "Audio/BGM/BGM";
        /// <summary>攻击演出参数 Assets/Res/SO/AttackTuning.asset（<see cref="App.UI.AttackTuningConfig"/>）。</summary>
        public const string AttackTuning = "SO/AttackTuning";
        /// <summary>SpriteAtlas under Assets/Res/Altas/Card.spriteatlasv2.</summary>
        public const string CardAtlas = "Altas/Card";
        /// <summary>SpriteAtlas under Assets/Res/Altas/CardType.spriteatlasv2.</summary>
        public const string CardTypeAtlas = "Altas/CardType";
        /// <summary>SpriteAtlas under Assets/Res/Altas/cardTypeValue.spriteatlasv2.</summary>
        public const string CardTypeValueAtlas = "Altas/cardTypeValue";
        /// <summary>SpriteAtlas under Assets/Res/Altas/Relic.spriteatlasv2.</summary>
        public const string RelicAtlas = "Altas/Relic";
        /// <summary>SpriteAtlas under Assets/Res/Altas/ItemBg.spriteatlasv2（源图 Assets/Sprites/ItemBg）。</summary>
        public const string ItemBgAtlas = "Altas/ItemBg";
        /// <summary>SpriteAtlas under Assets/Res/Altas/Talent.spriteatlasv2（源图 Assets/Sprites/Talent，sprite 名=TalentConfig.Icon）。</summary>
        public const string TalentAtlas = "Altas/Talent";
        /// <summary>SpriteAtlas under Assets/Res/Altas/ShopNum.spriteatlasv2（源图 Assets/Sprites/shopNum，0-9 与 Slash）。</summary>
        public const string ShopNumAtlas = "Altas/ShopNum";

        public static string RoleAttack(int index) => $"Textures/role/role{index}_attack";

        public const string PortraitAttack = "attack";
        public const string PortraitDamage = "damage";
        public const string PortraitDead = "dead";

        /// <summary>
        /// 局内头像分档：Hp ≤ 0 或 MaxHp ≤ 0 为 dead；严格低于 50% 为 damage；其余（含恰好 50%）为 attack。
        /// </summary>
        public static string PortraitSuffix(int hp, int maxHp)
        {
            if (hp <= 0 || maxHp <= 0)
            {
                return PortraitDead;
            }

            if (hp * 2 < maxHp)
            {
                return PortraitDamage;
            }

            return PortraitAttack;
        }

        /// <summary>
        /// 配置表 Icon 无后缀，给图鉴收藏品等独立文件名用（如 Adventurer1）。
        /// </summary>
        public static string RoleIcon(string icon)
        {
            return ComposeIcon("Textures/role", icon, null);
        }

        /// <summary>
        /// <see cref="CardShare.Contracts.Config.HeroConfig.Icon"/> + _attack / _damage / _dead。
        /// </summary>
        public static string RolePortrait(string icon, string suffix)
        {
            return ComposeIcon("Textures/role", icon, suffix);
        }

        /// <summary>
        /// <see cref="CardShare.Contracts.Config.MonsterConfig.Icon"/> + _attack / _damage / _dead。
        /// </summary>
        public static string EnemyPortrait(string icon, string suffix)
        {
            return ComposeIcon("Textures/enemy", icon, suffix);
        }

        /// <summary>
        /// <see cref="CardShare.Contracts.Config.RelicConfig.Icon"/> 对应 Assets/Res/Textures/Relic 下的文件名（无扩展名）。
        /// </summary>
        public static string RelicIcon(string icon)
        {
            return ComposeIcon("Textures/Relic", icon, null);
        }

        public static string EnemyAttack(int index) => $"Textures/enemy/enemy{index}_attack";

        /// <summary>
        /// <see cref="CardShare.Contracts.Config.GameConst.GoldIcon"/> 等对应 Assets/Res/Textures/Common 下的文件名（无扩展名）。
        /// </summary>
        public static string CommonIcon(string icon)
        {
            return ComposeIcon("Textures/Common", icon, null);
        }

        private static string ComposeIcon(string folder, string icon, string suffix)
        {
            if (string.IsNullOrWhiteSpace(icon))
            {
                return null;
            }

            var name = icon.Trim();
            if (string.IsNullOrWhiteSpace(suffix))
            {
                return $"{folder}/{name}";
            }

            return $"{folder}/{name}_{suffix.Trim()}";
        }
    }
}
