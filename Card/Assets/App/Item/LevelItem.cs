using System;
using App.Level;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace App.Item
{
    /// <summary>
    /// 关卡列表单卡。未解锁显示 ???，选中时显示 levelonselect。
    /// </summary>
    public sealed class LevelItem : MonoBehaviour
    {
        private const string LockedText = "???";

        [SerializeField] private GameObject selectFrame;
        [SerializeField] private TMP_Text levelInfo;
        [SerializeField] private Button button;

        public LevelSnapshot Data { get; private set; }

        public event Action<LevelItem> Clicked;

        public void Bind(LevelSnapshot snapshot, bool unlocked, bool selected)
        {
            Data = snapshot;
            EnsureRefs();
            SetSelected(selected);
            if (levelInfo == null)
            {
                return;
            }

            if (!unlocked || snapshot == null)
            {
                levelInfo.text = LockedText;
                return;
            }

            levelInfo.text = snapshot.HasBoss
                ? $"第{snapshot.Level}关\nBOSS"
                : $"第{snapshot.Level}关";
        }

        public void SetSelected(bool selected)
        {
            EnsureRefs();
            if (selectFrame != null)
            {
                selectFrame.SetActive(selected);
            }
        }

        public void BindClick(Action<LevelItem> onClick)
        {
            Clicked = null;
            if (onClick != null)
            {
                Clicked += onClick;
            }
        }

        private void Awake()
        {
            EnsureRefs();
            HookClick();
        }

        private void OnDestroy()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(HandleClick);
            }

            Clicked = null;
        }

        private void HookClick()
        {
            if (button == null)
            {
                return;
            }

            button.onClick.RemoveListener(HandleClick);
            button.onClick.AddListener(HandleClick);
        }

        private void HandleClick()
        {
            Clicked?.Invoke(this);
        }

        private void EnsureRefs()
        {
            if (selectFrame == null)
            {
                var node = transform.Find("levelonselect");
                if (node != null)
                {
                    selectFrame = node.gameObject;
                }
            }

            if (levelInfo == null)
            {
                var node = transform.Find("levelBG/levelInfo") ?? transform.Find("levelInfo");
                if (node != null)
                {
                    levelInfo = node.GetComponent<TMP_Text>();
                }
            }

            if (button == null)
            {
                button = GetComponent<Button>();
            }
        }
    }
}
