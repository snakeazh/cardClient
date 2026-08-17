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
        public const string ConfirmDialog = "UI/ConfirmDialog";
        public const string GameUI = "UI/GameUI";
        public const string GameHud = "Game/GameHud";
        public const string CardIcon = "Game/CardIcon";
        public const string PlayerItem = "UI/Icon/PlayerItem";
        /// <summary>SpriteAtlas under Assets/Res/Altas/Card.spriteatlasv2.</summary>
        public const string CardAtlas = "Altas/Card";

        public static string RoleAttack(int index) => $"Textures/role/role{index}_attack";

        public static string EnemyAttack(int index) => $"Textures/enemy/enemy{index}_attack";
    }
}
