using System;
using System.Threading.Tasks;
using App.Resources;
using Framework.Assets;
using Framework.Log;
using UnityEngine;

namespace App.UI
{
    /// <summary>
    /// 攻击演出的时长与位移参数，资产在 Assets/Res/SO/AttackTuning.asset。
    /// 启动时经 <see cref="PreloadAsync"/> 预热，之后由 <see cref="Instance"/> 直接取；
    /// 预热失败则 <see cref="Instance"/> 退回本类的字段默认值，不影响演出。
    /// </summary>
    [CreateAssetMenu(fileName = "AttackTuning", menuName = "Card/攻击演出参数")]
    public sealed class AttackTuningConfig : ScriptableObject
    {
        private static AttackTuningConfig _instance;

        /// <summary>预热好的配置；未预热或预热失败时返回一份内置默认值。</summary>
        public static AttackTuningConfig Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = CreateInstance<AttackTuningConfig>();
                }

                return _instance;
            }
        }

        /// <summary>启动时预热攻击演出参数，常驻资源缓存。</summary>
        public static async Task PreloadAsync(IResourceService resources)
        {
            if (resources == null || _instance != null)
            {
                return;
            }

            try
            {
                _instance = await resources.LoadAsync<AttackTuningConfig>(ResResourcePaths.AttackTuning);
            }
            catch (Exception ex)
            {
                AppLog.Warn(
                    LogChannel.UI,
                    "AttackTuning not found at Res/" + ResResourcePaths.AttackTuning + ": " + ex.Message);
            }
        }

        /// <summary>一档强度的演出参数。等级由牌型映射，散牌/对子为低，顺子/金花为中，顺金/豹子为高。</summary>
        [Serializable]
        public sealed class LevelTuning
        {
            [Tooltip("起手阶段时长（秒）。起手动画、转向瞄准、后撤蓄力三者同时进行，共用这个时长。")]
            public float StartDuration = 0.43f;

            [Tooltip("从蓄力位置冲撞到目标的时长（秒）。所有敌人槽位共用，距离越远飞得越快。")]
            public float MoveDuration = 0.18f;

            [Tooltip("命中后双方定格停留的时长（秒）。")]
            public float HitHoldDuration = 0.25f;

            [Tooltip("退回原位的时长（秒）。")]
            public float BackDuration = 0.45f;

            [Tooltip("后撤蓄力的距离，朝目标反方向拉开多少 UI 像素。")]
            public float RetreatDistance = 160f;

            [Tooltip("受击方被击退的距离，朝攻击方的反方向弹开多少 UI 像素。")]
            public float HitKnockbackDistance = 40f;

            [Tooltip("受击方被击退的时长（秒）。与播 hit 片段同时开始。")]
            public float HitKnockbackDuration = 0.1f;

            [Tooltip("受击方从击退位置回到原位的时长（秒）。")]
            public float HitRecoverDuration = 0.22f;
        }

        [Header("低（散牌 / 对子）")]
        [SerializeField]
        private LevelTuning low = new LevelTuning
        {
            StartDuration = 0.43f,
            MoveDuration = 0.18f,
            HitHoldDuration = 0.25f,
            BackDuration = 0.45f,
            RetreatDistance = 160f,
            HitKnockbackDistance = 40f,
            HitKnockbackDuration = 0.1f,
            HitRecoverDuration = 0.22f
        };

        [Header("中（顺子 / 金花）")]
        [SerializeField]
        private LevelTuning middle = new LevelTuning
        {
            StartDuration = 0.90f,
            MoveDuration = 0.18f,
            HitHoldDuration = 0.25f,
            BackDuration = 0.45f,
            RetreatDistance = 224f,
            HitKnockbackDistance = 64f,
            HitKnockbackDuration = 0.12f,
            HitRecoverDuration = 0.28f
        };

        [Header("高（顺金 / 豹子）")]
        [SerializeField]
        private LevelTuning high = new LevelTuning
        {
            StartDuration = 1.25f,
            MoveDuration = 0.18f,
            HitHoldDuration = 0.33f,
            BackDuration = 0.55f,
            RetreatDistance = 288f,
            HitKnockbackDistance = 96f,
            HitKnockbackDuration = 0.16f,
            HitRecoverDuration = 0.34f
        };

        [Header("通用")]
        [Tooltip("退回原位后伤害数字继续停留的时长（秒），过完才结算扣血并进入下一个对手。")]
        [SerializeField]
        private float hpTextHoldDuration = 0.85f;

        [Tooltip("释放特效（非投射物）按自身动画时长收手，配置表里的 Time 只当上限。关掉则完全按 Time 等。")]
        [SerializeField]
        private bool clampCastTimeToClip = true;

        [Tooltip("英雄特效分档：这一刀伤害 ≥ 此值用「中」档。填 0 表示不按伤害分档，直接按牌型档位（低/中/高）。")]
        [SerializeField]
        private int heroFxMidDamage;

        [Tooltip("英雄特效分档：这一刀伤害 ≥ 此值用「强」档，要连「中」档一起填。填 0 表示按牌型档位。")]
        [SerializeField]
        private int heroFxStrongDamage;

        [Tooltip("这一击致死后，等多久才开始播溶解（秒）。致死时等溶解结束才结算下一个对手，被打死的卡会先站着不动。")]
        [SerializeField]
        private float deathDissolveDelay = 2f;

        [Tooltip("这一击把血量打到 0 时，在被打者卡片位置播放的特效。留空则不播。")]
        [SerializeField]
        private GameObject deathEffect;

        [Tooltip("致死特效的存活时长（秒），到点销毁。填 0 则一直留到下一次攻击演出开始。")]
        [SerializeField]
        private float deathEffectDuration = 0.6f;

        [Header("暗灵法师齐射")]
        [Tooltip("01 法阵持续时长（秒）。")]
        [SerializeField]
        private float volleyChannelTime = 1.5f;

        [Tooltip("02 主弹蓄力时长（秒），蓄满才起飞。")]
        [SerializeField]
        private float volleyChargeTime = 1f;

        [Tooltip("02 主弹直线飞向主目标的时长（秒）。")]
        [SerializeField]
        private float volleyMainFlightTime = 0.3f;

        [Tooltip("03 溅射弹飞向两侧的时长（秒）。")]
        [SerializeField]
        private float volleySplashFlightTime = 0.4f;

        [Tooltip("03 抛物线弧高 = 飞行距离 × 此比例。")]
        [SerializeField]
        private float volleyJumpHeightRatio = 0.45f;

        [Tooltip("03 抛物线弧高下限（UI 世界单位）。")]
        [SerializeField]
        private float volleyMinJumpPower = 0.8f;

        [Tooltip("03 贝塞尔 P1 下沉比例（0~1）：越大反向勾越往下沉。0=不沉只横甩。")]
        [SerializeField]
        private float volleyJumpReverseXAt = 0.35f;

        [Tooltip("03 贝塞尔 P2 沿「起点→怪」的位置（0~1）。越大越早拐向怪；推荐 0.55~0.7。")]
        [SerializeField]
        private float volleyJumpXZeroAt = 0.6f;

        [Tooltip("03 反向勾的横向幅度 = max(下限, |Δx| × 此比例)。越大先甩得越开。")]
        [SerializeField]
        private float volleyJumpReverseXRatio = 1.5f;

        [Tooltip("03 反向勾横向幅度下限（UI 世界单位）。勾太小就看不见反向弯。")]
        [SerializeField]
        private float volleyJumpReverseXMin = 3f;

        [Tooltip("03 溅射弹相对主弹起飞再晚多少秒生成。默认 0.1。")]
        [SerializeField]
        private float volleySpawnDelay = 0.1f;

        [Tooltip("01/02/03 相对人物卡的纵向偏移（UI 世界单位）。正数往上抬。")]
        [SerializeField]
        private float volleySpawnOffsetY = 0.25f;

        [Tooltip("齐射特效在 UI 层基准缩放之上再乘的倍率。1 = 跟萨满那套一样大。")]
        [SerializeField]
        private float volleyFxScale = 1f;

        public float HpTextHoldDuration => Mathf.Max(0f, hpTextHoldDuration);

        public bool ClampCastTimeToClip => clampCastTimeToClip;

        public int HeroFxMidDamage => Mathf.Max(0, heroFxMidDamage);

        public int HeroFxStrongDamage => Mathf.Max(0, heroFxStrongDamage);

        /// <summary>
        /// 选英雄特效档位：配了强档伤害阈值就按这一刀的伤害分（1 弱 / 2 中 / 3 强），
        /// 否则直接用牌型档位（AttackLevel 1/2/3）——它跟卡牌动作、震屏用的是同一套档。
        /// </summary>
        public int ResolveHeroFxLevel(int attackLevel, int damage)
        {
            var strong = HeroFxStrongDamage;
            if (strong <= 0)
            {
                return Mathf.Clamp(attackLevel, 1, 3);
            }

            if (damage >= strong)
            {
                return 3;
            }

            var mid = HeroFxMidDamage;
            return mid > 0 && damage >= mid ? 2 : 1;
        }

        public float DeathDissolveDelay => Mathf.Max(0f, deathDissolveDelay);

        public GameObject DeathEffect => deathEffect;

        public float DeathEffectDuration => Mathf.Max(0f, deathEffectDuration);

        public float VolleyChannelTime => Mathf.Max(0.01f, volleyChannelTime);

        public float VolleyChargeTime => Mathf.Max(0f, volleyChargeTime);

        public float VolleyMainFlightTime => Mathf.Max(0.01f, volleyMainFlightTime);

        public float VolleySplashFlightTime => Mathf.Max(0.01f, volleySplashFlightTime);

        public float VolleyJumpHeightRatio => Mathf.Max(0f, volleyJumpHeightRatio);

        public float VolleyMinJumpPower => Mathf.Max(0f, volleyMinJumpPower);

        public float VolleyJumpReverseXAt => Mathf.Clamp(volleyJumpReverseXAt, 0.01f, 0.98f);

        public float VolleyJumpXZeroAt => Mathf.Clamp(volleyJumpXZeroAt, 0.02f, 0.99f);

        public float VolleyJumpReverseXRatio => Mathf.Max(0f, volleyJumpReverseXRatio);

        public float VolleyJumpReverseXMin => Mathf.Max(0f, volleyJumpReverseXMin);

        public float VolleySpawnDelay => Mathf.Max(0f, volleySpawnDelay);

        public float VolleySpawnOffsetY => volleySpawnOffsetY;

        public float VolleyFxScale => Mathf.Max(0.01f, volleyFxScale);

        /// <summary>取一档参数，level 为 1 低 / 2 中 / 3 高，越界按低档处理。</summary>
        public LevelTuning Level(int level)
        {
            switch (level)
            {
                case 3:
                    return high ?? new LevelTuning();
                case 2:
                    return middle ?? new LevelTuning();
                default:
                    return low ?? new LevelTuning();
            }
        }
    }
}
