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

        [Tooltip("这一击致死后，等多久才开始播溶解（秒）。致死时等溶解结束才结算下一个对手，被打死的卡会先站着不动。")]
        [SerializeField]
        private float deathDissolveDelay = 2f;

        [Tooltip("这一击把血量打到 0 时，在被打者卡片位置播放的特效。留空则不播。")]
        [SerializeField]
        private GameObject deathEffect;

        [Tooltip("致死特效的存活时长（秒），到点销毁。填 0 则一直留到下一次攻击演出开始。")]
        [SerializeField]
        private float deathEffectDuration = 0.6f;

        public float HpTextHoldDuration => Mathf.Max(0f, hpTextHoldDuration);

        public float DeathDissolveDelay => Mathf.Max(0f, deathDissolveDelay);

        public GameObject DeathEffect => deathEffect;

        public float DeathEffectDuration => Mathf.Max(0f, deathEffectDuration);

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
