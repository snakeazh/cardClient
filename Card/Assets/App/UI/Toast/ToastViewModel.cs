using Framework.UI.View;

namespace App.UI
{
    /// <summary>
    /// Toast提示数据
    /// </summary>
    public class ToastData
    {
        public string Message;
        public float Duration;
        public ToastType Type;
    }

    /// <summary>
    /// Toast提示 ViewModel。Toast 的显示内容由 ToastService 事件直接驱动，
    /// 这里只作为导航系统打开 ToastPanel 所需的视图模型载体。
    /// </summary>
    public class ToastViewModel : ViewModelBase
    {
    }
}
