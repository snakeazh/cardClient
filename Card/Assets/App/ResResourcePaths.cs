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
        /// <summary>选关难度卡预制体 Assets/Res/UI/Icon/IevelItem.prefab（文件名首字母是大写 I）。</summary>
        public const string LevelItem = "UI/Icon/IevelItem";
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
        public const string TTRewardPop = "UI/Popup/TTRewardPop";
        public const string ToastPanel = "UI/Popup/ToastPanel";
        public const string GuideOverlay = "UI/Guide/GuideOverlay";
        public const string ItemTip = "UI/Top/ItemTip";
        public const string WinTip = "UI/Top/WinTip";
        public const string GameHud = "Game/GameHud";
        /// <summary>对局桌面背景 Assets/Res/Textures/Background/BigBackgroundBaseFrame.png。</summary>
        public const string GameHudBg = "Textures/Background/BigBackgroundBaseFrame";
        /// <summary>Boss 关桌面背景 Assets/Res/Textures/Background/RedBigBackgroundBaseFrame.png。</summary>
        public const string GameHudBossBg = "Textures/Background/RedBigBackgroundBaseFrame";
        public const string CardIcon = "Game/CardIcon";
        public const string CardShadow = "UI/Icon/CardShadow";
        public const string PlayerItem = "UI/Icon/PlayerItem";
        public const string CardPointEffect01 = "Effect/UI/Card/CardPointEffect01";
        public const string CardPointEffect02 = "Effect/UI/Card/CardPointEffect02";
        public const string CardPointEffect03 = "Effect/UI/Card/CardPointEffect03";
        public const string ShopItem = "UI/Icon/ShopItem";
        public const string Item = "UI/Icon/Item";
        public const string CoinItem = "UI/Icon/coinitem";
        /// <summary>旧主界面 BGM Assets/Res/Audio/BGM/BGM.mp3。</summary>
        public const string Bgm = "Audio/BGM/BGM";
        /// <summary>大厅 BGM Assets/Res/Audio/BGM/BGM_Lobby_Loop.wav。</summary>
        public const string BgmLobby = "Audio/BGM/BGM_Lobby_Loop";
        /// <summary>局内战斗 BGM Assets/Res/Audio/BGM/BGM_Battle_Loop.wav。</summary>
        public const string BgmBattle = "Audio/BGM/BGM_Battle_Loop";
        /// <summary>回合发牌音效 Assets/Res/Audio/Effect/deal_5cards_01.wav。</summary>
        public const string SfxDeal5Cards = "Audio/Effect/deal_5cards_01";
        /// <summary>比牌亮牌放大音效 Assets/Res/Audio/Effect/reveal_cards_3.wav。</summary>
        public const string SfxRevealCards3 = "Audio/Effect/reveal_cards_3";
        /// <summary>攻击/受击卡牌碰撞音效 Assets/Res/Audio/Effect/hurt_big_02.wav。</summary>
        public const string SfxHurtBig02 = "Audio/Effect/hurt_big_02";
        /// <summary>搓牌技能音效 Assets/Res/Audio/Effect/rub_cards_02.wav。</summary>
        public const string SfxRubCards02 = "Audio/Effect/rub_cards_02";
        /// <summary>通用按钮点击音效 Assets/Res/Audio/Effect/ui_click_03.wav。</summary>
        public const string SfxUiClick = "Audio/Effect/ui_click_03";
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
        /// <summary>SpriteAtlas under Assets/Res/Altas/playitem.spriteatlasv2（源图 Assets/Sprites/playeritem；含原 ItemBg 的框/卡背/怪物底）。</summary>
        public const string PlayItemAtlas = "Altas/playitem";
        /// <summary>已并入 <see cref="PlayItemAtlas"/>；保留常量以免外部硬编码断裂。</summary>
        public const string ItemBgAtlas = PlayItemAtlas;
        /// <summary>SpriteAtlas under Assets/Res/Altas/enemy.spriteatlasv2（源图 Assets/Res/Textures/enemy，sprite 名={Icon}_attack/_damage/_dead）。</summary>
        public const string EnemyAtlas = "Altas/enemy";
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
        /// 怪物立绘资源键 / 图集 sprite 名：{Icon}_attack / _damage / _dead。
        /// 贴图在 Assets/Res/Textures/enemy，运行时从 <see cref="EnemyAtlas"/> 取，不再按张 Load。
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

        /// <summary>图集内 sprite 名（无路径），如 enemy1_attack。</summary>
        public static string EnemySpriteName(string icon, string suffix)
        {
            if (string.IsNullOrWhiteSpace(icon))
            {
                return null;
            }

            var name = icon.Trim();
            if (string.IsNullOrWhiteSpace(suffix))
            {
                return name;
            }

            return name + "_" + suffix.Trim();
        }

        public static string EnemyAttack(int index) => EnemySpriteName("enemy" + index, PortraitAttack);

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
