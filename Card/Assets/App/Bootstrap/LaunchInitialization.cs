using System;
using System.Threading.Tasks;

namespace App.Bootstrap
{
    /// <summary>
    /// 冷启动重型初始化（图集/配置表/头像预载/各业务服务注册/列表项预热）的完成信号。
    /// 健康游戏忠告展示期间并发执行，忠告页倒计时结束后等它完成再进首页；
    /// 不展示忠告时由 AppBootstrap 直接等待。用 TaskCompletionSource 而非任务属性赋值，
    /// 避免忠告页计时协程先于启动流程读取到未完成状态。
    /// </summary>
    public sealed class LaunchInitialization
    {
        private readonly TaskCompletionSource<bool> _completion = new TaskCompletionSource<bool>();

        public Task Completion => _completion.Task;

        public void Complete()
        {
            _completion.TrySetResult(true);
        }

        public void Fail(Exception exception)
        {
            _completion.TrySetException(exception);
        }
    }
}
