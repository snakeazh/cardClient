using System;
using App.Game;
using App.Item;
using App.Resources;
using Framework.UI.Navigation;
using Framework.UI.View;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI.Popup
{
    /// <summary>
    /// 天赋详情弹窗。注册在 TopMost 层：叠加在 Popup 层的天赋列表之上，弹出时不隐藏列表。
    /// Item 卡显示天赋名、图标与等级角标，Detail 显示当前等级描述；LeftBtn/RightBtn 切换已解锁天赋，
    /// 不足两个时隐藏；UpgradeBtn 看广告升级，满级隐藏；Tip 为预制体固定文案；点 Mask 关闭。
    /// 图鉴展示模式：遗物/收藏用 Item 卡；怪物条目切换到 MonsterItem（PlayerItem 敌人形态，显示
    /// 配表 BaseMap 底图），与 Item 卡互斥；AcquireMethod 显示获得方式，CongratulationsImg 抽卡入口开。
    /// </summary>
    [AutoScreen(AppScreenIds.TalentDetail, UILayer.TopMost, ResResourcePaths.TalentDetail)]
    public sealed class TalentDetailView : ViewBase<TalentDetailViewModel>
    {
        private const string MonsterItemName = "MonsterItem";

        private ItemCard _card;
        private PlayerItem _monsterCard;

        protected override void OnBind()
        {
            _card = UI.GetGameObject("Item").GetComponent<ItemCard>();
            if (_card != null)
            {
                _card.SetShadowVisible(false);
                if (ViewModel.ShowCongratulations.Value)
                {
                    // 抽卡入口：播翻卡动画并按品质点亮内嵌天赋特效（红=传说/紫=史诗/蓝=稀有）
                    _card.PlayRewardReveal(ViewModel.Quality.Value);
                }
                else
                {
                    _card.SetAnimationEnabled(false);
                }
                // 不清 card_icon：无配置 Icon 时保留预制体默认图
                Binding.Add(ViewModel.NameText.Subscribe(ApplyName));
                // 未拥有观感同天赋列表：Mask 激活 + card_Name ？？？ + card_icon 黑色剪影
                Binding.Add(ViewModel.CardOwned.Subscribe(ApplyCardOwned));
                Binding.Add(ViewModel.LevelText.Subscribe(_card.SetLevel));
                // 品质边框（Altas/ItemBg），切换/升级换行时随快照刷新
                Binding.Add(ViewModel.Quality.Subscribe(_card.ApplyQuality));
                Binding.Add(ViewModel.IconKey.Subscribe(LoadDetailIcon));
                // 展示模式图标（图鉴传入的 Sprite，优先于 IconKey 图集逻辑）
                Binding.Add(ViewModel.IconOverride.Subscribe(ApplyDetailIcon));
            }

            BindMonsterItem();
            var left = UI.GetGameObject("LeftBtn");
            var right = UI.GetGameObject("RightBtn");
            Binding.BindCommand(left.GetComponent<Button>(), ViewModel.PrevCommand);
            Binding.BindCommand(right.GetComponent<Button>(), ViewModel.NextCommand);
            Binding.BindActive(left, ViewModel.ShowSwitch);
            Binding.BindActive(right, ViewModel.ShowSwitch);

            // UpgradeBtn 键指向已改名的 BuyBtn 节点：看广告获得（未拥有）/升级（已拥有未满级），
            // 主文案按拥有态切换（未拥有="立即获得"），满级整体隐藏
            var upgrade = UI.GetGameObject("UpgradeBtn");
            if (upgrade != null)
            {
                var button = upgrade.GetComponent<Button>();
                if (button == null)
                {
                    button = upgrade.AddComponent<Button>();
                    button.transition = Selectable.Transition.None;
                }

                Binding.BindCommand(button, ViewModel.UpgradeCommand);
                // Tip 节点按拥有态切换文案（未拥有="立即获得"，已拥有="立即升级"）；
                // 主文案 Text (TMP) 保持预制体静态文案
                var tip = upgrade.transform.Find("Tip");
                var tipTmp = tip != null ? tip.GetComponent<TMP_Text>() : null;
                if (tipTmp != null)
                {
                    Binding.BindText(tipTmp, ViewModel.ActionText);
                }
            }

            Binding.BindActive(upgrade, ViewModel.ShowUpgrade);

            // 恭喜获得图（抽卡入口开）；获得方式文本（图鉴遗物页开，文案=UnlockConditionConfig.Desc）
            Binding.BindActive(UI.GetGameObject("CongratulationsImg"), ViewModel.ShowCongratulations);
            var acquire = UI.GetGameObject("AcquireMethod");
            Binding.BindActive(acquire, ViewModel.ShowAcquireMethod);
            Binding.BindText(acquire.GetComponent<TMP_Text>(), ViewModel.AcquireMethodText);

            Binding.BindText(UI.GetGameObject("Detail").GetComponent<TMP_Text>(), ViewModel.DescText);
            BindMaskClose();
        }

        /// <summary>怪物条目用 PlayerItem 敌人形态卡：优先取 UIReference 注册的 PlayerItem 节点
        ///（编辑器拖入），缺键时按名兜底找 MonsterItem；都没有则安全退化——仍用 Item 卡显示。
        /// 初始化细节同图鉴 FillMonsterSection。</summary>
        private void BindMonsterItem()
        {
            Component node = null;
            if (UI.TryGet<Component>("PlayerItem", out var registered) && registered != null)
            {
                node = registered;
            }
            else
            {
                node = FindDeep(transform, MonsterItemName);
            }

            _monsterCard = node != null ? node.GetComponent<PlayerItem>() : null;
            if (_monsterCard != null)
            {
                Binding.Add(ViewModel.CurrentMonsterId.Subscribe(ApplyMonsterCard));
                Binding.Add(ViewModel.ShowMonsterCard.Subscribe(ApplyCardSwitch));
            }
        }

        /// <summary>拥有态切换：Mask 激活/关闭、图标剪影染色，并联动刷新名字口径。</summary>
        private void ApplyCardOwned(bool owned)
        {
            if (_card != null)
            {
                _card.SetUnlocked(owned);
                _card.SetIconColor(owned ? Color.white : Color.black);
                ApplyName(ViewModel.NameText.Value);
            }
        }

        private void ApplyName(string name)
        {
            if (_card != null)
            {
                // 未拥有 card_Name 显示 ？？？（null 触发占位，同天赋列表口径）
                _card.SetName(ViewModel.CardOwned.Value ? name : null);
            }

            if (_monsterCard != null && ViewModel.ShowMonsterCard.Value)
            {
                _monsterCard.SetName(string.IsNullOrEmpty(name) ? "？？？" : name);
            }
        }

        /// <summary>怪物卡随条目切换刷新：敌人形态底图 + 隐藏 cardMask/攻血块 + 名字/头像。</summary>
        private void ApplyMonsterCard(int monsterId)
        {
            if (_monsterCard == null || monsterId <= 0)
            {
                return;
            }

            _monsterCard.ApplyEnemyTheme(monsterId);
            var cardMask = FindDeep(_monsterCard.transform, "cardMask");
            if (cardMask != null)
            {
                cardMask.gameObject.SetActive(false);
            }

            _monsterCard.SetAttack(0);
            _monsterCard.SetHp(0);
            _monsterCard.SetName(ViewModel.NameText.Value);
            var icon = ViewModel.IconOverride.Value;
            if (icon != null)
            {
                _monsterCard.SetPortrait(icon, locked: false);
            }
        }

        /// <summary>Item 卡与 MonsterItem 互斥显隐。</summary>
        private void ApplyCardSwitch(bool monster)
        {
            if (_monsterCard != null)
            {
                _monsterCard.gameObject.SetActive(monster);
            }

            if (_card != null)
            {
                _card.gameObject.SetActive(!monster);
            }
        }


        /// <summary>图标在 Altas/Talent 图集（sprite 名=TalentConfig.Icon）；缺图保留预制体默认图。</summary>
        private void LoadDetailIcon(string key)
        {
            if (_card == null || string.IsNullOrEmpty(key) || ViewModel.Atlas == null)
            {
                return;
            }

            if (ViewModel.Atlas.TryGetSprite(ResResourcePaths.TalentAtlas, key, out var sprite) && sprite != null)
            {
                _card.SetIcon(sprite);
            }
        }

        /// <summary>展示模式图标；null 保留 IconKey 图集逻辑或预制体默认图，不主动清空。
        /// 怪物条目把头像画到 PlayerItem 卡上，其余画到 Item 卡。</summary>
        private void ApplyDetailIcon(Sprite icon)
        {
            if (icon == null)
            {
                return;
            }

            if (ViewModel.ShowMonsterCard.Value)
            {
                if (_monsterCard != null)
                {
                    _monsterCard.SetPortrait(icon, locked: false);
                }
            }
            else if (_card != null)
            {
                _card.SetIcon(icon);
            }
        }

        private static Transform FindDeep(Transform root, string nodeName)
        {
            if (root == null)
            {
                return null;
            }

            if (root.name == nodeName)
            {
                return root;
            }

            for (var i = 0; i < root.childCount; i++)
            {
                var found = FindDeep(root.GetChild(i), nodeName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private void BindMaskClose()
        {
            // 预制体优先经 UIReference 挂 Mask 的 Button；缺引用时退回按名查找并运行时补 Button。
            if (!UI.TryGet<Button>("Mask", out var overlay))
            {
                var mask = transform.Find("Mask");
                if (mask == null)
                {
                    return;
                }

                overlay = mask.GetComponent<Button>();
                if (overlay == null)
                {
                    overlay = mask.gameObject.AddComponent<Button>();
                    overlay.transition = Selectable.Transition.None;
                }
            }

            Binding.BindCommand(overlay, ViewModel.CloseCommand);
        }
    }
}
