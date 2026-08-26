using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Framework.Log;
using Framework.UI.Navigation;

namespace App.UI
{
    /// <summary>
    /// Toast类型
    /// </summary>
    public enum ToastType
    {
        Normal,     // 普通提示
        Success,    // 成功提示
        Warning,    // 警告提示
        Error       // 错误提示
    }

    /// <summary>
    /// Toast提示服务 - 全局管理Toast提示。
    /// 新Toast立即替换旧的；ToastPanel 首次显示时懒打开，之后常驻在 TopMost 层。
    /// 依赖 IUINavigator，须在 UIFramework.Create 之后注册（见 AppBootstrap）。
    /// </summary>
    public sealed class ToastService
    {
        private readonly IUINavigator mNavigator;
        private readonly Queue<ToastData> mToastQueue = new Queue<ToastData>();
        private bool mIsShowing = false;
        private Task mPanelOpenTask;

        // Toast 动画入场/出场各 0.5s，与 ToastPanel 中的常量对应
        private const float TOAST_ANIMATION_TOTAL = 1.0f;

        // 默认显示时长
        private const float DEFAULT_DURATION = 2f;

        public ToastService(IUINavigator navigator)
        {
            mNavigator = navigator ?? throw new ArgumentNullException(nameof(navigator));
        }

        #region 事件
        /// <summary>
        /// 显示Toast事件
        /// </summary>
        public event Action<string, float, ToastType> OnShowToast;

        /// <summary>
        /// Toast关闭事件
        /// </summary>
        public event Action OnToastClosed;
        #endregion

        #region 公共方法 - 便捷接口

        /// <summary>
        /// 显示普通Toast
        /// </summary>
        public void Show(string message, float duration = DEFAULT_DURATION)
        {
            ShowToast(message, duration, ToastType.Normal);
        }

        /// <summary>
        /// 显示成功Toast
        /// </summary>
        public void ShowSuccess(string message, float duration = DEFAULT_DURATION)
        {
            ShowToast(message, duration, ToastType.Success);
        }

        /// <summary>
        /// 显示警告Toast
        /// </summary>
        public void ShowWarning(string message, float duration = DEFAULT_DURATION)
        {
            ShowToast(message, duration, ToastType.Warning);
        }

        /// <summary>
        /// 显示错误Toast
        /// </summary>
        public void ShowError(string message, float duration = DEFAULT_DURATION)
        {
            ShowToast(message, duration, ToastType.Error);
        }

        /// <summary>
        /// 显示带类型的Toast
        /// </summary>
        public void Show(string message, float duration, ToastType type)
        {
            ShowToast(message, duration, type);
        }

        #endregion

        /// <summary>
        /// 显示Toast提示（基础方法）- 新Toast替换旧的，立即显示
        /// </summary>
        public void ShowToast(string message, float duration = DEFAULT_DURATION, ToastType type = ToastType.Normal)
        {
            mToastQueue.Clear();
            mToastQueue.Enqueue(new ToastData
            {
                Message = message,
                Duration = duration,
                Type = type
            });
            AppLog.Debug(LogChannel.UI, $"[ToastService] 请求显示: {message}");

            if (!mIsShowing)
            {
                mIsShowing = true;
                _ = RunToastLoopAsync();
            }
        }

        /// <summary>
        /// 清空所有待显示的Toast并隐藏当前Toast
        /// </summary>
        public void ClearAllToasts()
        {
            mToastQueue.Clear();
            OnToastClosed?.Invoke();
            AppLog.Debug(LogChannel.UI, "[ToastService] 清空所有Toast");
        }

        /// <summary>
        /// 获取队列中的消息数量
        /// </summary>
        public int GetQueueCount()
        {
            return mToastQueue.Count;
        }

        /// <summary>
        /// 是否正在显示Toast
        /// </summary>
        public bool IsShowing()
        {
            return mIsShowing;
        }

        #region 私有方法

        private async Task RunToastLoopAsync()
        {
            while (mToastQueue.Count > 0)
            {
                var toast = mToastQueue.Dequeue();
                AppLog.Debug(LogChannel.UI, $"[ToastService] 显示Toast: {toast.Message}, 类型: {toast.Type}");

                // 确保 ToastPanel 是打开的（失败不缓存，下次重试）
                var opened = await EnsureToastPanelOpenAsync();
                if (!opened)
                {
                    continue;
                }

                // 触发显示事件 - 面板内会替换旧的Toast
                OnShowToast?.Invoke(toast.Message, toast.Duration, toast.Type);

                // 等待显示时间（动画0.5s进 + 停留 + 0.5s出），Task.Delay 不受 timeScale 影响
                await Task.Delay(TimeSpan.FromSeconds(toast.Duration + TOAST_ANIMATION_TOTAL));
            }

            mIsShowing = false;
        }

        /// <summary>
        /// 确保 ToastPanel 是打开的；用缓存 Task 防止并发重复打开
        /// </summary>
        private async Task<bool> EnsureToastPanelOpenAsync()
        {
            var task = mPanelOpenTask ??= OpenToastPanelCoreAsync();
            try
            {
                await task;
                return true;
            }
            catch (Exception ex)
            {
                // 打开失败时清空缓存，允许下次重试
                mPanelOpenTask = null;
                AppLog.Exception(LogChannel.UI, ex);
                return false;
            }
        }

        private async Task OpenToastPanelCoreAsync()
        {
            await mNavigator.Open(new ToastViewModel());
            AppLog.Debug(LogChannel.UI, "[ToastService] ToastPanel 已打开");
        }

        #endregion
    }
}
