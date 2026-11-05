using System;
using App.Bootstrap;
using Framework.Log;
using Framework.Save;
using UnityEngine;

namespace App.ShareReward
{
    /// <summary>
    /// 分享奖励存档服务：`sharereward.v1` 里记「是否领取」与「永久加成总量」。
    /// 只改内存，<see cref="Save"/> 才刷盘；由 AppServicesHost 在切后台/退出时自动落盘。
    /// </summary>
    public sealed class ShareRewardService : IShareRewardService, ISaveFlushable
    {
        public const string SaveKey = "sharereward.v1";

        private readonly ISaveService _save;
        private bool _claimed;
        private int _rubBonusCharges;
        private bool _dirty;

        public ShareRewardService(ISaveService save)
        {
            _save = save ?? throw new ArgumentNullException(nameof(save));
        }

        public bool HasClaimed => _claimed;

        public int RubBonusCharges => _rubBonusCharges;

        public event Action Changed;

        public bool TryClaim()
        {
            if (_claimed)
            {
                return false;
            }

            _claimed = true;
            _rubBonusCharges += Math.Max(0, ShareRewardBalance.RubBonusCharges);
            _dirty = true;
            Save();
            AppLog.Info(LogChannel.UI, $"[ShareReward] 领取分享奖励：搓牌永久 +{ShareRewardBalance.RubBonusCharges}");
            Changed?.Invoke();
            return true;
        }

        public void Load()
        {
            _claimed = false;
            _rubBonusCharges = 0;
            _dirty = false;

            if (_save.HasKey(SaveKey))
            {
                var json = _save.GetString(SaveKey, string.Empty);
                if (!string.IsNullOrWhiteSpace(json))
                {
                    var data = JsonUtility.FromJson<ShareRewardSaveData>(json);
                    if (data != null)
                    {
                        _claimed = data.Claimed;
                        _rubBonusCharges = Math.Max(0, data.RubBonusCharges);
                    }
                    else
                    {
                        AppLog.Warn(LogChannel.UI, "Failed to parse share reward save data.");
                    }
                }
            }

            // 老存档只有领取标记、没有加成数值时按当前配置补齐。
            if (_claimed && _rubBonusCharges <= 0)
            {
                _rubBonusCharges = Math.Max(0, ShareRewardBalance.RubBonusCharges);
                _dirty = true;
                Save();
            }
        }

        public void Save()
        {
            if (!_dirty)
            {
                return;
            }

            var data = new ShareRewardSaveData
            {
                Claimed = _claimed,
                RubBonusCharges = _rubBonusCharges
            };
            _save.SetString(SaveKey, JsonUtility.ToJson(data));
            _save.Save();
            _dirty = false;
        }

#if UNITY_EDITOR
        /// <summary>编辑器调试：清空领取记录和加成，方便反复走分享流程。</summary>
        public void DebugReset()
        {
            _claimed = false;
            _rubBonusCharges = 0;
            _dirty = true;
            Save();
            AppLog.Info(LogChannel.UI, "[ShareReward] 调试重置分享奖励状态");
            Changed?.Invoke();
        }
#endif
    }

    /// <summary>
    /// 账号级永久技能加成查询：供 SkillRubUses 等纯逻辑/静态计算读取。
    /// 服务未注册（如脱离启动流程的单元测试）时返回 0。
    /// </summary>
    public static class ShareRewardBonus
    {
        public static int RubUses
        {
            get
            {
                if (!AppServices.IsReady)
                {
                    return 0;
                }

                var service = AppServices.Container.GetService(typeof(IShareRewardService)) as IShareRewardService;
                return service?.RubBonusCharges ?? 0;
            }
        }
    }
}
