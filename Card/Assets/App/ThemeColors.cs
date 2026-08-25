using App.Config;
using UnityEngine;

namespace App
{
    /// <summary>
    /// 全局 UI 色板：装备品质色、空槽底色、人物/敌人主色。界面从这里取色，不要再写 hex。
    /// </summary>
    public static class ThemeColors
    {
        public static readonly Color Ordinary = ParseHex("FBF5DF");
        public static readonly Color Rare = ParseHex("76A1F1");
        public static readonly Color Epic = ParseHex("B950FF");
        public static readonly Color Legend = ParseHex("FF9F3F");

        public static readonly Color Player = ParseHex("FBF5DF");
        public static readonly Color Enemy = ParseHex("F6393C");
        public static readonly Color PlayerCircle = ParseHex("F8AB67");
        public static readonly Color EnemyCircle = ParseHex("B20003");
        public static readonly Color EquipEmpty = Color.black;

        public static Color ForQuality(QualityType type)
        {
            switch (type)
            {
                case QualityType.Rare:
                    return Rare;
                case QualityType.Epic:
                    return Epic;
                case QualityType.Legend:
                    return Legend;
                default:
                    return Ordinary;
            }
        }

        public static Color EquipSlot(bool occupied, QualityType type)
        {
            return occupied ? ForQuality(type) : EquipEmpty;
        }

        public static Color ParseHex(string hex)
        {
            ColorUtility.TryParseHtmlString("#" + hex, out var color);
            color.a = 1f;
            return color;
        }
    }
}
