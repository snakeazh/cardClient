using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using App.Bootstrap;
using App.Config;
using App.Game;
using App.Guide;
using App.Item;
using App.Net;
using App.Resources;
using CardShare.Battle;
using CardShare.Contracts;
using CardShare.Contracts.Config;
using Framework.Log;
using Framework.UI.Binding;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.UI
{
    public sealed partial class GameUIView
    {
        private struct PvpRoleEntry
        {
            public string UserId;
            public string Icon;
            public bool Monster;
            public int Hp;
            public int MaxHp;
            public bool Alive;
            public int[] RelicIds;
        }

        private bool _pvpSideBound;
        private bool _pvpSideAlive = true;
        private int _pvpSideSerial;
        private int _pvpSideSig = int.MinValue;
        private Transform _pvpRolesRoot;
        private readonly List<Transform> _pvpRoleSlots = new List<Transform>(4);
        private readonly List<PvpRoleEntry> _pvpRoleEntries = new List<PvpRoleEntry>(4);
        private readonly List<PvpFighterDto> _pvpRoleRest = new List<PvpFighterDto>(4);
        private Transform _bondsRoot;
        private GameObject _bondIconTemplate;
        private readonly List<GameObject> _bondIcons = new List<GameObject>(8);
        private readonly List<PvpRelicBindings.BindingHold> _bondHolds = new List<PvpRelicBindings.BindingHold>(8);
        private readonly Dictionary<string, Sprite> _battleSprites = new Dictionary<string, Sprite>(16);
        private readonly List<string> _battleSpriteKeys = new List<string>(16);
        private readonly Dictionary<string, Task> _battleSpriteTasks = new Dictionary<string, Task>(StringComparer.Ordinal);
        private GameObject _bondsTip;
        private Task _ensuringBondsTip;
        private Image _bondsTipIcon;
        private TMP_Text _bondsTipTitle;
        private TMP_Text _bondsTipBody;
        private Transform _bondsTipGrid;
        private GameObject _bondsTipItemTemplate;
        private readonly List<ItemCard> _bondsTipItems = new List<ItemCard>(8);
        private int _shownBondId;
        private GameObject _streakTip;
        private TMP_Text _streakText;
        private GameObject _hasTip;
        private Task _ensuringHasTip;
        private TMP_Text _hasTipTitle;
        private Transform _hasTipGrid;
        private GameObject _hasTipItemTemplate;
        private readonly List<ItemCard> _hasTipItems = new List<ItemCard>(8);
        private readonly List<int> _hasTipRelicIds = new List<int>(8);
        private string _shownHasUserId;

        private static readonly Color BondMissingTint = new Color(0.45f, 0.45f, 0.45f, 1f);
        private static readonly Color RoleHpAlive = new Color(0.31764707f, 1f, 0f, 1f);
        private static readonly Color RoleHpDead = new Color(0.55f, 0.55f, 0.55f, 1f);

        private void BindPvpSide()
        {
            _pvpRolesRoot = ResolveSlot("PvpRoles") ?? transform.Find("PvpRoles");
            _pvpRoleSlots.Clear();
            if (_pvpRolesRoot != null)
            {
                for (var i = 0; i < _pvpRolesRoot.childCount; i++)
                {
                    _pvpRoleSlots.Add(_pvpRolesRoot.GetChild(i));
                }
            }

            _bondsRoot = ResolveSlot("bonds") ?? transform.Find("bonds");
            if (_bondsRoot != null && _bondsRoot.childCount > 0)
            {
                _bondIconTemplate = _bondsRoot.GetChild(0).gameObject;
                _bondIconTemplate.SetActive(false);
            }

            _pvpSideBound = _pvpRolesRoot != null || _bondsRoot != null;
            for (var i = 0; i < _pvpRoleSlots.Count; i++)
            {
                HookRoleClick(_pvpRoleSlots[i], i);
            }

            _streakTip = ResolveSlot("winTip") != null ? ResolveSlot("winTip").gameObject : null;
            if (_streakTip == null)
            {
                var streak = transform.Find("winTip") ?? FindDeep(transform, "winTip");
                _streakTip = streak != null ? streak.gameObject : null;
            }

            var streakText = ResolveSlot("winTipText");
            _streakText = streakText != null ? streakText.GetComponent<TMP_Text>() : null;
            if (_streakText == null && _streakTip != null)
            {
                var textNode = FindDeep(_streakTip.transform, "winTipText");
                _streakText = textNode != null ? textNode.GetComponent<TMP_Text>() : null;
            }

            if (_streakTip != null)
            {
                _streakTip.SetActive(false);
            }
        }

        private void RefreshPvpSide()
        {
            if (!_pvpSideBound || !_pvpSideAlive || ViewModel == null)
            {
                return;
            }

            RebuildPvpRoles();
            RebuildBonds();
            ApplyStreakTip();
            var sig = PvpSideSignature();
            if (sig == _pvpSideSig)
            {
                return;
            }

            _pvpSideSig = sig;
            ApplyPvpRoles();
            ApplyBonds();
            RefreshOpenHasTip();
            if (_shownBondId > 0 && !BondStillHeld(_shownBondId))
            {
                CloseBondsTipOnly();
            }
            else if (_shownBondId > 0)
            {
                var open = FindHold(_shownBondId);
                if (open.Binding != null)
                {
                    FillBondsTip(open);
                }
            }

            var serial = ++_pvpSideSerial;
            _ = LoadPvpSideArt(serial);
        }

        private void RebuildPvpRoles()
        {
            _pvpRoleEntries.Clear();
            var session = ViewModel.Session;
            var state = ViewModel.PvpState;
            if (session == null || !session.IsPvp || state == null || state.Players == null)
            {
                return;
            }

            var selfId = ViewModel.PvpUserId;
            var foeId = CurrentFoeUserId(state, selfId);
            var monsterRound = IsMonsterRound(state);
            if (monsterRound)
            {
                AddMonsterRole(session, state);
            }

            PvpFighterDto foe = null;
            var rest = _pvpRoleRest;
            rest.Clear();
            for (var i = 0; i < state.Players.Length; i++)
            {
                var player = state.Players[i];
                if (player == null || PvpMatchSession.SameUser(player.UserId, selfId))
                {
                    continue;
                }

                if (!monsterRound && PvpMatchSession.SameUser(player.UserId, foeId))
                {
                    foe = player;
                    continue;
                }

                rest.Add(player);
            }

            if (foe != null)
            {
                AddPlayerRole(foe);
            }

            for (var pass = 0; pass < 2; pass++)
            {
                var wantAlive = pass == 0;
                for (var i = 0; i < rest.Count; i++)
                {
                    var player = rest[i];
                    if (player.Alive == wantAlive)
                    {
                        AddPlayerRole(player);
                    }
                }
            }
        }

        private void AddMonsterRole(GameSession session, PvpMatchStateDto state)
        {
            var enemy = session.Enemies != null && session.Enemies.Length > 0 ? session.Enemies[0] : null;
            if (enemy != null && enemy.ActiveInStage && !enemy.DisplayAsHero && !string.IsNullOrEmpty(enemy.Icon))
            {
            _pvpRoleEntries.Add(new PvpRoleEntry
            {
                Icon = enemy.Icon,
                Monster = true,
                Hp = enemy.Hp,
                MaxHp = enemy.MaxHp,
                Alive = enemy.Alive && enemy.Hp > 0,
                RelicIds = Array.Empty<int>()
            });
                return;
            }

            var tables = UnityGameConfigLoader.Current;
            if (tables == null)
            {
                return;
            }

            var groupId = PvpSchedule.MonsterGroup(tables, state.Round);
            if (!tables.TryGetMonsterGroup(groupId, out var group) ||
                !tables.TryGetMonster(group.MonsterId, group.MonsterLevel, out var monster) ||
                monster == null)
            {
                return;
            }

            var hp = Math.Max(1, monster.MonsterHp);
            _pvpRoleEntries.Add(new PvpRoleEntry
            {
                Icon = monster.Icon,
                Monster = true,
                Hp = hp,
                MaxHp = hp,
                Alive = true,
                RelicIds = Array.Empty<int>()
            });
        }

        private void AddPlayerRole(PvpFighterDto player)
        {
            var hp = player.Hp;
            var maxHp = player.MaxHp;
            var alive = player.Alive && player.Hp > 0;
            if (!player.Alive && TryMirrorSeatHp(player.UserId, out var mirrorHp, out var mirrorMaxHp))
            {
                hp = mirrorHp;
                maxHp = mirrorMaxHp;
                alive = mirrorHp > 0;
            }

            if (TryDeferredFoeHp(player.UserId, out var deferredHp, out var deferredAlive))
            {
                hp = deferredHp;
                alive = deferredAlive;
            }

            _pvpRoleEntries.Add(new PvpRoleEntry
            {
                UserId = player.UserId,
                Icon = HeroIconOf(player.HeroId),
                Monster = false,
                Hp = hp,
                MaxHp = maxHp,
                Alive = alive,
                RelicIds = player.RelicIds ?? Array.Empty<int>()
            });
        }

        /// <summary>当前对手是死亡玩家的镜像时，头像血量用这一桌的座位，不用名册上的 0。</summary>
        private bool TryMirrorSeatHp(string userId, out int hp, out int maxHp)
        {
            hp = 0;
            maxHp = 0;
            var state = ViewModel?.PvpState;
            var duel = state != null ? state.Duel : null;
            var seats = duel != null ? duel.Seats : null;
            if (seats == null)
            {
                return false;
            }

            for (var i = 0; i < seats.Count; i++)
            {
                var seat = seats[i];
                if (seat == null || seat.MaxHp <= 0 || !PvpMatchSession.SameUser(seat.UserId, userId))
                {
                    continue;
                }

                hp = seat.Hp;
                maxHp = seat.MaxHp;
                return true;
            }

            return false;
        }

        /// <summary>这场比牌的伤害还没落到座位上时，对手头像沿用座位旧血。</summary>
        private bool TryDeferredFoeHp(string userId, out int hp, out bool alive)
        {
            hp = 0;
            alive = true;
            var session = ViewModel?.Session;
            var state = ViewModel?.PvpState;
            if (session == null || !session.PvpHpDeferred || state == null)
            {
                return false;
            }

            var foeId = CurrentFoeUserId(state, ViewModel.PvpUserId);
            if (string.IsNullOrEmpty(foeId) || !PvpMatchSession.SameUser(userId, foeId))
            {
                return false;
            }

            var enemy = session.Enemies != null && session.Enemies.Length > 0 ? session.Enemies[0] : null;
            if (enemy == null)
            {
                return false;
            }

            hp = enemy.Hp;
            alive = enemy.Alive && enemy.Hp > 0;
            return true;
        }

        private void RebuildBonds()
        {
            var tables = UnityGameConfigLoader.Current;
            var owned = ViewModel.Session != null && ViewModel.Session.Run != null
                ? ViewModel.Session.Run.RelicConfigIds
                : null;
            PvpRelicBindings.CollectHeld(tables, owned, _bondHolds);
        }

        private void ApplyPvpRoles()
        {
            var show = ViewModel.Session != null && ViewModel.Session.IsPvp && _pvpRoleEntries.Count > 0;
            if (_pvpRolesRoot != null)
            {
                _pvpRolesRoot.gameObject.SetActive(show);
            }

            for (var i = 0; i < _pvpRoleSlots.Count; i++)
            {
                var slot = _pvpRoleSlots[i];
                if (slot == null)
                {
                    continue;
                }

                var visible = show && i < _pvpRoleEntries.Count;
                slot.gameObject.SetActive(visible);
                if (!visible)
                {
                    continue;
                }

                var entry = _pvpRoleEntries[i];
                var portrait = PortraitLoader.GetPvpHead(entry.Monster, entry.Icon);
                var icon = FindDeep(slot, "icon");
                var image = icon != null ? icon.GetComponent<Image>() : null;
                if (image != null)
                {
                    image.sprite = portrait;
                    image.enabled = portrait != null;
                    image.color = Color.white;
                }

                var num = FindDeep(slot, "valueNum");
                var text = num != null ? num.GetComponent<TMP_Text>() : null;
                if (text != null)
                {
                    text.text = Math.Max(0, entry.Hp).ToString();
                    text.color = entry.Alive ? RoleHpAlive : RoleHpDead;
                }
            }
        }

        private void ApplyBonds()
        {
            if (_bondsRoot != null)
            {
                _bondsRoot.gameObject.SetActive(_bondHolds.Count > 0);
            }

            if (_bondIconTemplate == null)
            {
                RegisterBondsGuideTarget();
                return;
            }

            while (_bondIcons.Count < _bondHolds.Count)
            {
                var clone = Instantiate(_bondIconTemplate, _bondsRoot, false);
                clone.name = "bondIcon" + (_bondIcons.Count + 1);
                HookBondClick(clone, _bondIcons.Count);
                _bondIcons.Add(clone);
            }

            for (var i = 0; i < _bondIcons.Count; i++)
            {
                var go = _bondIcons[i];
                if (go == null)
                {
                    continue;
                }

                var visible = i < _bondHolds.Count;
                go.SetActive(visible);
                if (!visible)
                {
                    continue;
                }

                var hold = _bondHolds[i];
                var image = go.GetComponent<Image>();
                if (image == null)
                {
                    continue;
                }

                var sprite = BondSprite(hold, out var tint);
                image.sprite = sprite;
                image.enabled = sprite != null;
                image.color = tint;
            }

            RegisterBondsGuideTarget();
        }

        /// <summary>登记首个可见羁绊图标，供 FirstBond 挖孔。</summary>
        private void RegisterBondsGuideTarget()
        {
            if (!AppServices.IsReady)
            {
                return;
            }

            _guideTargets ??= AppServices.Resolve<GuideTargetRegistry>();
            if (_guideTargets == null)
            {
                return;
            }

            RectTransform rect = null;
            for (var i = 0; i < _bondIcons.Count; i++)
            {
                var go = _bondIcons[i];
                if (go != null && go.activeInHierarchy)
                {
                    rect = go.transform as RectTransform;
                    break;
                }
            }

            if (rect == null &&
                _bondsRoot != null &&
                _bondsRoot.gameObject.activeInHierarchy &&
                _bondHolds.Count > 0)
            {
                rect = _bondsRoot as RectTransform;
            }

            if (rect == null)
            {
                if (_guideTargetIds.Contains(GuideTargetIds.Bonds))
                {
                    _guideTargets.Unregister(GuideTargetIds.Bonds);
                    _guideTargetIds.Remove(GuideTargetIds.Bonds);
                }

                return;
            }

            _guideTargets.RegisterUi(GuideTargetIds.Bonds, rect);
            if (!_guideTargetIds.Contains(GuideTargetIds.Bonds))
            {
                _guideTargetIds.Add(GuideTargetIds.Bonds);
            }
        }

        private void HookBondClick(GameObject go, int index)
        {
            var button = go.GetComponent<Button>();
            if (button == null)
            {
                button = go.AddComponent<Button>();
            }

            var image = go.GetComponent<Image>();
            if (image != null)
            {
                button.targetGraphic = image;
            }

            button.transition = Selectable.Transition.None;
            button.onClick.RemoveAllListeners();
            var captured = index;
            button.onClick.AddListener(() => OnBondClicked(captured));
        }

        private void OnBondClicked(int index)
        {
            if (index < 0 || index >= _bondHolds.Count || _bondHolds[index].Binding == null)
            {
                return;
            }

            var id = _bondHolds[index].Binding.Id;
            if (_shownBondId == id)
            {
                HideEquipTip();
                return;
            }

            _ = ShowBondsTip(index);
        }

        private async Task ShowBondsTip(int index)
        {
            if (index < 0 || index >= _bondHolds.Count)
            {
                return;
            }

            var bindingId = _bondHolds[index].Binding != null ? _bondHolds[index].Binding.Id : 0;
            if (bindingId <= 0)
            {
                return;
            }

            await EnsureBondsTip();
            if (_bondsTip == null || !_pvpSideAlive)
            {
                return;
            }

            var hold = FindHold(bindingId);
            if (hold.Binding == null)
            {
                return;
            }

            index = IndexOfHold(bindingId);
            var anchor = index >= 0 && index < _bondIcons.Count ? _bondIcons[index].transform : _bondsRoot;
            HideEquipTip();
            _shownBondId = hold.Binding.Id;
            FillBondsTip(hold);
            EnsureEquipTipCatcher();
            if (_equipTipCatcher != null)
            {
                _equipTipCatcher.SetActive(true);
                _equipTipCatcher.transform.SetAsLastSibling();
            }

            _bondsTip.SetActive(true);
            if (_bondsTipBody != null)
            {
                _bondsTipBody.ForceMeshUpdate();
            }

            Canvas.ForceUpdateCanvases();
            var tipRt = _bondsTip.GetComponent<RectTransform>();
            if (tipRt != null)
            {
                // 嵌套 VLG + Grid，单次 rebuild 时高度常偏小，定位后再长高会顶出屏幕。
                LayoutRebuilder.ForceRebuildLayoutImmediate(tipRt);
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(tipRt);
            }

            _bondsTip.transform.SetAsLastSibling();
            if (anchor != null)
            {
                PositionBondsTip(anchor);
            }
        }

        /// <summary>
        /// 羁绊图标在右上，弹窗较高；竖直跟图标居中会对齐到屏幕外。X 贴图标左侧，Y 相对 GameUI 居中后再夹边。
        /// </summary>
        private void PositionBondsTip(Transform slot)
        {
            var tipRt = _bondsTip != null ? _bondsTip.GetComponent<RectTransform>() : null;
            var itemRt = slot as RectTransform;
            var parent = transform as RectTransform;
            if (tipRt == null || itemRt == null || parent == null)
            {
                return;
            }

            if (_hudCanvas == null)
            {
                _hudCanvas = GetComponentInParent<Canvas>();
            }

            var cam = _hudCanvas != null ? _hudCanvas.worldCamera : null;
            itemRt.GetWorldCorners(_equipTipCorners);
            var edge = (_equipTipCorners[0] + _equipTipCorners[1]) * 0.5f;
            var screen = RectTransformUtility.WorldToScreenPoint(cam, edge);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screen, cam, out var local))
            {
                return;
            }

            var tipWidth = tipRt.rect.width;
            var tipHeight = tipRt.rect.height;
            tipRt.anchoredPosition = new Vector2(
                local.x - 8f - (1f - tipRt.pivot.x) * tipWidth,
                (0.5f - tipRt.pivot.y) * tipHeight);
            ItemTipPlacement.ClampToParent(tipRt, parent);
        }

        private Task EnsureBondsTip()
        {
            if (_bondsTip != null || ViewModel == null || ViewModel.Resources == null)
            {
                return Task.CompletedTask;
            }

            if (_ensuringBondsTip == null)
            {
                _ensuringBondsTip = LoadBondsTip();
            }

            return _ensuringBondsTip;
        }

        private async Task LoadBondsTip()
        {
            try
            {
                var prefab = await ViewModel.Resources.LoadAsync<GameObject>(ResResourcePaths.BondsTip);
                if (prefab == null || !_pvpSideAlive || _bondsTip != null)
                {
                    if (prefab != null && _bondsTip == null)
                    {
                        ViewModel?.Resources?.Release(ResResourcePaths.BondsTip);
                    }

                    return;
                }

                _bondsTip = Instantiate(prefab, transform, false);
                _bondsTip.name = "BondsTip";
                _bondsTip.SetActive(false);
                BindBondsTipNodes(_bondsTip);
                if (_hudCanvas == null)
                {
                    _hudCanvas = GetComponentInParent<Canvas>();
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.UI, "BondsTip load failed: " + ex.Message);
            }
        }

        private void BindBondsTipNodes(GameObject tip)
        {
            _bondsTipTitle = null;
            _bondsTipBody = null;
            _bondsTipIcon = null;
            _bondsTipGrid = null;
            _bondsTipItemTemplate = null;
            var ui = tip != null ? tip.GetComponent<UIReference>() : null;
            if (ui != null && ui.TryGet<Component>("title", out var title) && title != null)
            {
                _bondsTipTitle = title.GetComponent<TMP_Text>() ?? title.GetComponentInChildren<TMP_Text>(true);
            }

            if (ui != null && ui.TryGet<Component>("tipContext", out var context) && context != null)
            {
                _bondsTipBody = context.GetComponent<TMP_Text>() ?? context.GetComponentInChildren<TMP_Text>(true);
            }

            var icon = tip != null ? FindDeep(tip.transform, "bondsicon") : null;
            _bondsTipIcon = icon != null ? icon.GetComponent<Image>() : null;
            _bondsTipGrid = tip != null ? FindDeep(tip.transform, "vecLayout") : null;
            if (_bondsTipGrid != null && _bondsTipGrid.childCount > 0)
            {
                _bondsTipItemTemplate = _bondsTipGrid.GetChild(0).gameObject;
                _bondsTipItemTemplate.SetActive(false);
            }
        }

        private void FillBondsTip(PvpRelicBindings.BindingHold hold)
        {
            if (_bondsTip == null || hold.Binding == null)
            {
                return;
            }

            if (_bondsTipTitle != null)
            {
                _bondsTipTitle.text = hold.Binding.Name + "（" + hold.Owned + "/" + hold.Required + "）";
            }

            if (_bondsTipBody != null)
            {
                _bondsTipBody.text = hold.Binding.Desc ?? string.Empty;
            }

            if (_bondsTipIcon != null)
            {
                var sprite = BondSprite(hold, out var tint);
                _bondsTipIcon.sprite = sprite;
                _bondsTipIcon.enabled = sprite != null;
                _bondsTipIcon.color = tint;
            }

            FillBondMembers(hold);
            var tipRt = _bondsTip.GetComponent<RectTransform>();
            if (tipRt != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(tipRt);
            }
        }

        private void FillBondMembers(PvpRelicBindings.BindingHold hold)
        {
            if (_bondsTipGrid == null || _bondsTipItemTemplate == null || hold.Binding == null)
            {
                return;
            }

            var members = GameSession.CollectBindingMemberRelicIds(hold.Binding.Id, pvp: true);
            var owned = ViewModel.Session != null && ViewModel.Session.Run != null
                ? ViewModel.Session.Run.RelicConfigIds
                : null;
            while (_bondsTipItems.Count < members.Count)
            {
                var clone = Instantiate(_bondsTipItemTemplate, _bondsTipGrid, false);
                clone.name = "Item" + (_bondsTipItems.Count + 1);
                var card = clone.GetComponent<ItemCard>();
                if (card == null)
                {
                    card = clone.AddComponent<ItemCard>();
                }

                _bondsTipItems.Add(card);
            }

            var atlas = ViewModel.Atlas;
            for (var i = 0; i < _bondsTipItems.Count; i++)
            {
                var card = _bondsTipItems[i];
                if (card == null)
                {
                    continue;
                }

                var visible = i < members.Count;
                card.gameObject.SetActive(visible);
                if (!visible)
                {
                    continue;
                }

                var relic = LookupPvpRelic(members[i]);
                Sprite sprite = null;
                if (relic != null && atlas != null && !string.IsNullOrWhiteSpace(relic.Icon))
                {
                    atlas.TryGetSprite(ResResourcePaths.RelicAtlas, relic.Icon.Trim(), out sprite);
                }

                var has = OwnsRelic(owned, members[i]);
                card.Bind(relic != null ? relic.Name : string.Empty, sprite, true);
                card.SetLevel(null);
                card.SetIconColor(Color.white);
                SetMemberGray(card, !has);
            }
        }

        private static void SetMemberGray(ItemCard card, bool gray)
        {
            if (card == null)
            {
                return;
            }

            var mark = card.GetComponent<UIGrayscale>();
            if (!gray)
            {
                if (mark != null)
                {
                    mark.enabled = false;
                }

                return;
            }

            if (mark == null)
            {
                mark = card.gameObject.AddComponent<UIGrayscale>();
            }

            mark.enabled = true;
        }

        private async Task LoadPvpSideArt(int serial)
        {
            // PVP 侧栏头像在 pvphead 图集，启动已预载；这里只刷 UI 并补羁绊 Battle 图标。
            ApplyPvpRoles();
            for (var i = 0; i < _bondHolds.Count; i++)
            {
                var stem = ResResourcePaths.BondArtName(_bondHolds[i].Binding != null ? _bondHolds[i].Binding.Type : 0);
                if (string.IsNullOrEmpty(stem))
                {
                    continue;
                }

                await LoadBattleSprite(ResResourcePaths.BattleIcon(stem));
                await LoadBattleSprite(ResResourcePaths.BattleIcon(stem + "Gray"));
                if (!_pvpSideAlive || serial != _pvpSideSerial)
                {
                    return;
                }
            }

            if (!_pvpSideAlive || serial != _pvpSideSerial)
            {
                return;
            }

            ApplyBonds();
            if (_shownBondId > 0)
            {
                var open = FindHold(_shownBondId);
                if (open.Binding != null)
                {
                    FillBondsTip(open);
                }
            }
        }

        private Task LoadBattleSprite(string key)
        {
            if (string.IsNullOrEmpty(key) || _battleSprites.ContainsKey(key))
            {
                return Task.CompletedTask;
            }

            if (_battleSpriteTasks.TryGetValue(key, out var running))
            {
                return running;
            }

            var task = LoadBattleSpriteCore(key);
            _battleSpriteTasks[key] = task;
            return task;
        }

        private async Task LoadBattleSpriteCore(string key)
        {
            Sprite sprite = null;
            try
            {
                if (ViewModel != null && ViewModel.Resources != null)
                {
                    sprite = await ViewModel.Resources.LoadAsync<Sprite>(key);
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.UI, "Bond icon load failed " + key + ": " + ex.Message);
            }

            _battleSpriteTasks.Remove(key);
            if (!_pvpSideAlive)
            {
                if (sprite != null)
                {
                    ViewModel?.Resources?.Release(key);
                }

                return;
            }

            _battleSprites[key] = sprite;
            if (sprite != null)
            {
                _battleSpriteKeys.Add(key);
            }
        }

        private Sprite BondSprite(PvpRelicBindings.BindingHold hold, out Color tint)
        {
            tint = Color.white;
            var stem = ResResourcePaths.BondArtName(hold.Binding != null ? hold.Binding.Type : 0);
            if (string.IsNullOrEmpty(stem))
            {
                return null;
            }

            var colorKey = ResResourcePaths.BattleIcon(stem);
            var grayKey = ResResourcePaths.BattleIcon(stem + "Gray");
            if (hold.Complete)
            {
                return SpriteOf(colorKey);
            }

            var gray = SpriteOf(grayKey);
            if (gray != null)
            {
                return gray;
            }

            tint = BondMissingTint;
            return SpriteOf(colorKey);
        }

        private Sprite SpriteOf(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }

            return _battleSprites.TryGetValue(key, out var sprite) ? sprite : null;
        }

        private void HookRoleClick(Transform slot, int index)
        {
            if (slot == null)
            {
                return;
            }

            var graphic = slot.GetComponent<Graphic>();
            if (graphic == null)
            {
                var image = slot.gameObject.AddComponent<Image>();
                image.color = new Color(1f, 1f, 1f, 0.01f);
                graphic = image;
            }

            graphic.raycastTarget = true;
            var button = slot.GetComponent<Button>();
            if (button == null)
            {
                button = slot.gameObject.AddComponent<Button>();
            }

            button.targetGraphic = graphic;
            button.transition = Selectable.Transition.None;
            button.onClick.RemoveAllListeners();
            var captured = index;
            button.onClick.AddListener(() => OnRoleClicked(captured));
        }

        private void OnRoleClicked(int index)
        {
            if (index < 0 || index >= _pvpRoleEntries.Count)
            {
                return;
            }

            var entry = _pvpRoleEntries[index];
            if (entry.Monster || string.IsNullOrEmpty(entry.UserId))
            {
                return;
            }

            if (!string.IsNullOrEmpty(_shownHasUserId) &&
                PvpMatchSession.SameUser(_shownHasUserId, entry.UserId) &&
                _hasTip != null &&
                _hasTip.activeSelf)
            {
                HideEquipTip();
                return;
            }

            _ = ShowHasTip(index);
        }

        private void ApplyStreakTip()
        {
            if (_streakTip == null)
            {
                return;
            }

            var session = ViewModel != null ? ViewModel.Session : null;
            var state = ViewModel != null ? ViewModel.PvpState : null;
            PvpFighterDto self = null;
            if (session != null && session.IsPvp && state != null && state.Players != null)
            {
                var selfId = ViewModel.PvpUserId;
                for (var i = 0; i < state.Players.Length; i++)
                {
                    var player = state.Players[i];
                    if (player != null && PvpMatchSession.SameUser(player.UserId, selfId))
                    {
                        self = player;
                        break;
                    }
                }
            }

            var win = self != null ? self.WinStreak : 0;
            var lose = self != null ? self.LoseStreak : 0;
            if (win <= 0 && lose <= 0)
            {
                _streakTip.SetActive(false);
                return;
            }

            if (_streakText != null)
            {
                _streakText.text = win > 0
                    ? ChineseCount(win) + "连胜"
                    : ChineseCount(lose) + "连败";
            }

            _streakTip.SetActive(true);
        }

        private static string ChineseCount(int value)
        {
            if (value <= 0)
            {
                return "零";
            }

            if (value >= 100)
            {
                return value.ToString();
            }

            var digits = new[] { "零", "一", "二", "三", "四", "五", "六", "七", "八", "九" };
            if (value < 10)
            {
                return digits[value];
            }

            if (value == 10)
            {
                return "十";
            }

            var tens = value / 10;
            var ones = value % 10;
            var tenText = tens == 1 ? "十" : digits[tens] + "十";
            return ones == 0 ? tenText : tenText + digits[ones];
        }

        private async Task ShowHasTip(int index)
        {
            if (index < 0 || index >= _pvpRoleEntries.Count)
            {
                return;
            }

            var entry = _pvpRoleEntries[index];
            if (entry.Monster || string.IsNullOrEmpty(entry.UserId))
            {
                return;
            }

            await EnsureHasTip();
            if (_hasTip == null || !_pvpSideAlive)
            {
                return;
            }

            index = IndexOfRole(entry.UserId);
            if (index < 0)
            {
                return;
            }

            entry = _pvpRoleEntries[index];
            var anchor = index < _pvpRoleSlots.Count ? _pvpRoleSlots[index] : _pvpRolesRoot;
            HideEquipTip();
            _shownHasUserId = entry.UserId;
            FillHasTip(entry);
            EnsureEquipTipCatcher();
            if (_equipTipCatcher != null)
            {
                _equipTipCatcher.SetActive(true);
                _equipTipCatcher.transform.SetAsLastSibling();
            }

            _hasTip.SetActive(true);
            Canvas.ForceUpdateCanvases();
            var tipRt = _hasTip.GetComponent<RectTransform>();
            if (tipRt != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(tipRt);
            }

            _hasTip.transform.SetAsLastSibling();
            if (anchor != null)
            {
                PositionTip(_hasTip, anchor, placeRight: true);
            }
        }

        private Task EnsureHasTip()
        {
            if (_hasTip != null || ViewModel == null || ViewModel.Resources == null)
            {
                return Task.CompletedTask;
            }

            if (_ensuringHasTip == null)
            {
                _ensuringHasTip = LoadHasTip();
            }

            return _ensuringHasTip;
        }

        private async Task LoadHasTip()
        {
            try
            {
                var prefab = await ViewModel.Resources.LoadAsync<GameObject>(ResResourcePaths.HasTip);
                if (prefab == null || !_pvpSideAlive || _hasTip != null)
                {
                    if (prefab != null && _hasTip == null)
                    {
                        ViewModel?.Resources?.Release(ResResourcePaths.HasTip);
                    }

                    return;
                }

                _hasTip = Instantiate(prefab, transform, false);
                _hasTip.name = "HasTip";
                _hasTip.SetActive(false);
                _hasTipTitle = null;
                var title = FindDeep(_hasTip.transform, "title");
                _hasTipTitle = title != null ? title.GetComponent<TMP_Text>() : null;
                if (_hasTipTitle != null)
                {
                    _hasTipTitle.text = "圣物列表";
                }

                _hasTipGrid = FindDeep(_hasTip.transform, "vecLayout");
                if (_hasTipGrid != null && _hasTipGrid.childCount > 0)
                {
                    _hasTipItemTemplate = _hasTipGrid.GetChild(0).gameObject;
                    _hasTipItemTemplate.SetActive(false);
                }

                if (_hudCanvas == null)
                {
                    _hudCanvas = GetComponentInParent<Canvas>();
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn(LogChannel.UI, "HasTip load failed: " + ex.Message);
            }
        }

        private void RefreshOpenHasTip()
        {
            if (string.IsNullOrEmpty(_shownHasUserId) || _hasTip == null || !_hasTip.activeSelf)
            {
                return;
            }

            var index = IndexOfRole(_shownHasUserId);
            if (index < 0)
            {
                CloseHasTipOnly();
                return;
            }

            FillHasTip(_pvpRoleEntries[index]);
        }

        private void FillHasTip(PvpRoleEntry entry)
        {
            if (_hasTip == null || _hasTipGrid == null || _hasTipItemTemplate == null)
            {
                return;
            }

            var relics = entry.RelicIds ?? Array.Empty<int>();
            _hasTipRelicIds.Clear();
            for (var n = 0; n < relics.Length; n++)
            {
                _hasTipRelicIds.Add(relics[n]);
            }
            while (_hasTipItems.Count < relics.Length)
            {
                var clone = Instantiate(_hasTipItemTemplate, _hasTipGrid, false);
                clone.name = "Item" + (_hasTipItems.Count + 1);
                var card = clone.GetComponent<ItemCard>();
                if (card == null)
                {
                    card = clone.AddComponent<ItemCard>();
                }

                card.SetAnimationEnabled(false);
                card.Clicked -= OnHasRelicClicked;
                card.Clicked += OnHasRelicClicked;
                _hasTipItems.Add(card);
            }

            var atlas = ViewModel != null ? ViewModel.Atlas : null;
            for (var i = 0; i < _hasTipItems.Count; i++)
            {
                var card = _hasTipItems[i];
                if (card == null)
                {
                    continue;
                }

                var visible = i < relics.Length;
                if (visible)
                {
                    card.SetAnimationEnabled(false);
                }

                card.gameObject.SetActive(visible);
                if (!visible)
                {
                    continue;
                }

                var relic = LookupPvpRelic(relics[i]);
                Sprite sprite = null;
                if (relic != null && atlas != null && !string.IsNullOrWhiteSpace(relic.Icon))
                {
                    atlas.TryGetSprite(ResResourcePaths.RelicAtlas, relic.Icon.Trim(), out sprite);
                }

                card.Bind(relic != null ? relic.Name : string.Empty, sprite, true);
                card.SetLevel(null);
                if (relic != null)
                {
                    card.ApplyQuality(relic.Type);
                }

                HookHasRelicHit(card);
            }

            var tipRt = _hasTip.GetComponent<RectTransform>();
            if (tipRt != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(tipRt);
            }
        }

        private int IndexOfRole(string userId)
        {
            for (var i = 0; i < _pvpRoleEntries.Count; i++)
            {
                if (!_pvpRoleEntries[i].Monster && PvpMatchSession.SameUser(_pvpRoleEntries[i].UserId, userId))
                {
                    return i;
                }
            }

            return -1;
        }

        private int _hasRelicClickFrame = -1;

        private void HookHasRelicHit(ItemCard card)
        {
            if (card == null)
            {
                return;
            }

            var host = FindDeep(card.transform, "cardFrame") ?? card.transform;
            var hit = host.Find("HasHit");
            if (hit == null)
            {
                var go = new GameObject(
                    "HasHit",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(Button));
                go.layer = card.gameObject.layer;
                go.SetActive(false);
                hit = go.transform;
                hit.SetParent(host, false);
                var rt = (RectTransform)hit;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                rt.localScale = Vector3.one;
                var image = go.GetComponent<Image>();
                image.color = new Color(1f, 1f, 1f, 0f);
                image.raycastTarget = true;
                var created = go.GetComponent<Button>();
                created.targetGraphic = image;
                created.transition = Selectable.Transition.None;
                go.SetActive(true);
            }

            hit.SetAsLastSibling();
            var button = hit.GetComponent<Button>();
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveAllListeners();
            var captured = card;
            button.onClick.AddListener(() => OnHasRelicClicked(captured));
        }

        private void OnHasRelicClicked(ItemCard card)
        {
            var index = card != null ? _hasTipItems.IndexOf(card) : -1;
            if (index < 0 || index >= _hasTipRelicIds.Count)
            {
                return;
            }

            var relic = LookupPvpRelic(_hasTipRelicIds[index]);
            if (relic == null)
            {
                return;
            }

            if (Time.frameCount == _hasRelicClickFrame)
            {
                return;
            }

            _hasRelicClickFrame = Time.frameCount;
            if (_shownEquipRelicId == relic.Id && _equipTip != null && _equipTip.activeSelf)
            {
                HideHasRelicDetail();
                return;
            }

            var anchor = FindDeep(card.transform, "cardFrame") ?? card.transform;
            _ = ShowHasRelicDetail(anchor, relic);
        }

        private async Task ShowHasRelicDetail(Transform slot, RelicConfig relic)
        {
            _shownEquipRelicId = relic.Id;
            _shownRoundBuffId = 0;
            _shownPlayerTip = false;
            _shownEnemySlot = -1;
            await PresentItemTip(
                slot,
                relic.Name,
                RelicMechanics.FormatDisplayDesc(ViewModel?.Session?.Run, relic),
                showUse: false,
                placeRight: false);
            if (!_pvpSideAlive || _hasTip == null || !_hasTip.activeSelf || _equipTip == null)
            {
                return;
            }

            if (_equipTipCatcher != null)
            {
                _equipTipCatcher.transform.SetAsLastSibling();
            }

            _hasTip.transform.SetAsLastSibling();
            _equipTip.transform.SetAsLastSibling();
            PositionTipAbove(_equipTip, slot);
        }

        private void HideHasRelicDetail()
        {
            _shownEquipRelicId = 0;
            _equipTipAnchor = null;
            if (_equipTip != null)
            {
                _equipTip.SetActive(false);
            }
        }

        private void HideHasTip()
        {
            _shownHasUserId = null;
            if (_hasTip != null)
            {
                _hasTip.SetActive(false);
            }
        }

        private void CloseHasTipOnly()
        {
            HideHasTip();
            if ((_equipTip == null || !_equipTip.activeSelf) &&
                (_bondsTip == null || !_bondsTip.activeSelf) &&
                (_winTip == null || !_winTip.activeSelf) &&
                _equipTipCatcher != null)
            {
                _equipTipCatcher.SetActive(false);
            }
        }

        private void HideBondsTip()
        {
            _shownBondId = 0;
            if (_bondsTip != null)
            {
                _bondsTip.SetActive(false);
            }
        }

        private void CloseBondsTipOnly()
        {
            HideBondsTip();
            if ((_equipTip == null || !_equipTip.activeSelf) && _equipTipCatcher != null)
            {
                _equipTipCatcher.SetActive(false);
            }
        }

        private void ReleasePvpSide()
        {
            _pvpSideAlive = false;
            _pvpSideSerial++;
            HideBondsTip();
            HideHasTip();
            if (_bondsTip != null)
            {
                Destroy(_bondsTip);
                _bondsTip = null;
            }

            if (_hasTip != null)
            {
                Destroy(_hasTip);
                _hasTip = null;
            }

            var resources = ViewModel != null ? ViewModel.Resources : null;
            resources?.Release(ResResourcePaths.BondsTip);
            resources?.Release(ResResourcePaths.HasTip);
            for (var i = 0; i < _battleSpriteKeys.Count; i++)
            {
                resources?.Release(_battleSpriteKeys[i]);
            }

            _battleSpriteKeys.Clear();
            _battleSprites.Clear();
            _battleSpriteTasks.Clear();
        }

        private int PvpSideSignature()
        {
            unchecked
            {
                var hash = 17;
                var pvp = ViewModel.Session != null && ViewModel.Session.IsPvp;
                hash = (hash * 31) + (pvp ? 1 : 0);
                for (var i = 0; i < _pvpRoleEntries.Count; i++)
                {
                    var entry = _pvpRoleEntries[i];
                    hash = (hash * 31) + entry.Hp;
                    hash = (hash * 31) + entry.MaxHp;
                    hash = (hash * 31) + (entry.Alive ? 1 : 0);
                    hash = (hash * 31) + (entry.Monster ? 1 : 0);
                    hash = (hash * 31) + (entry.Icon != null ? entry.Icon.GetHashCode() : 0);
                    hash = (hash * 31) + (entry.UserId != null ? entry.UserId.GetHashCode() : 0);
                    var relics = entry.RelicIds;
                    if (relics != null)
                    {
                        for (var r = 0; r < relics.Length; r++)
                        {
                            hash = (hash * 31) + relics[r];
                        }
                    }
                }

                for (var i = 0; i < _bondHolds.Count; i++)
                {
                    var hold = _bondHolds[i];
                    hash = (hash * 31) + (hold.Binding != null ? hold.Binding.Id : 0);
                    hash = (hash * 31) + hold.Owned;
                    hash = (hash * 31) + hold.Required;
                }

                return hash;
            }
        }

        private bool BondStillHeld(int bindingId)
        {
            return FindHold(bindingId).Binding != null;
        }

        private PvpRelicBindings.BindingHold FindHold(int bindingId)
        {
            var index = IndexOfHold(bindingId);
            return index >= 0 ? _bondHolds[index] : default;
        }

        private int IndexOfHold(int bindingId)
        {
            for (var i = 0; i < _bondHolds.Count; i++)
            {
                if (_bondHolds[i].Binding != null && _bondHolds[i].Binding.Id == bindingId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static bool IsMonsterRound(PvpMatchStateDto state)
        {
            if (state == null || !string.Equals(state.FightKind, "monster", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.Equals(state.Phase, PvpPhases.Fight, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(state.Phase, PvpPhases.Settle, StringComparison.OrdinalIgnoreCase);
        }

        private static string CurrentFoeUserId(PvpMatchStateDto state, string selfId)
        {
            var seats = state.Duel != null ? state.Duel.Seats : null;
            if (seats == null)
            {
                return null;
            }

            for (var i = 0; i < seats.Count; i++)
            {
                var seat = seats[i];
                if (seat == null || string.IsNullOrEmpty(seat.UserId))
                {
                    continue;
                }

                if (!PvpMatchSession.SameUser(seat.UserId, selfId))
                {
                    return seat.UserId;
                }
            }

            return null;
        }

        private static string HeroIconOf(int heroId)
        {
            var hero = heroId > 0 ? HeroConfig.Get(heroId) : null;
            if (hero == null && GameConst.IsLoaded)
            {
                hero = HeroConfig.Get(GameConst.Instance.DefaultHeroId);
            }

            return hero != null ? hero.Icon : null;
        }

        private static bool OwnsRelic(IReadOnlyList<int> owned, int relicId)
        {
            if (owned == null)
            {
                return false;
            }

            for (var i = 0; i < owned.Count; i++)
            {
                if (owned[i] == relicId)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>PVP UI 只读 PvpRelic 表，不回落 RelicConfig。</summary>
        private static RelicConfig LookupPvpRelic(int relicId)
        {
            var tables = UnityGameConfigLoader.Current;
            return tables != null && tables.TryGetPvpRelic(relicId, out var relic) ? relic : null;
        }

    }
}
