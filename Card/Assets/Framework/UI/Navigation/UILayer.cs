namespace Framework.UI.Navigation
{
    public enum UILayer
    {
        Hud = 0,
        Page = 1,
        Navigation = 2,
        Popup = 3,
        Resource = 4,
        Loading = 5,
        TopMost = 6,
        Guide = 7,
        /// <summary>常驻 Toast，不参与关屏栈；画在 TopMost 之上、Guide 之下。</summary>
        Toast = 8
    }
}
