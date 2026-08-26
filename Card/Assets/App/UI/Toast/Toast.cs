using App.Bootstrap;

namespace App.UI
{
    /// <summary>
    /// Toast 提示工具类 - 全局访问接口。
    /// 用法：<code>Toast.Show("金币不足");</code> 或 Toast.Success / Toast.Warning / Toast.Error。
    /// 启动完成前（AppServices 未就绪或服务未注册时）静默忽略。
    /// </summary>
    public static class Toast
    {
        /// <summary>
        /// 获取 ToastService（未注册时返回 null，不抛异常）
        /// </summary>
        private static ToastService Service
        {
            get
            {
                if (!AppServices.IsReady)
                {
                    return null;
                }

                return AppServices.Container.GetService(typeof(ToastService)) as ToastService;
            }
        }

        /// <summary>
        /// 显示普通提示
        /// </summary>
        public static void Show(string message, float duration = 2f)
        {
            Service?.Show(message, duration);
        }

        /// <summary>
        /// 显示成功提示
        /// </summary>
        public static void Success(string message, float duration = 2f)
        {
            Service?.ShowSuccess(message, duration);
        }

        /// <summary>
        /// 显示警告提示
        /// </summary>
        public static void Warning(string message, float duration = 2f)
        {
            Service?.ShowWarning(message, duration);
        }

        /// <summary>
        /// 显示错误提示
        /// </summary>
        public static void Error(string message, float duration = 2f)
        {
            Service?.ShowError(message, duration);
        }

        /// <summary>
        /// 清空所有提示
        /// </summary>
        public static void Clear()
        {
            Service?.ClearAllToasts();
        }
    }
}
