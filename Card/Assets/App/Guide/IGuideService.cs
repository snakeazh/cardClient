using System;
using App.Config;

namespace App.Guide
{
    public interface IGuideWaitHandler
    {
        string Id { get; }

        void Start(GuideStepConfig step, Action onComplete);

        void Stop();
    }

    public interface IGuideService
    {
        bool IsRunning { get; }

        int CurrentGroupId { get; }

        GuideStepConfig CurrentStep { get; }

        void TryStart(GuideTriggerType type, string param);

        void StartGroup(int groupId);

        void Advance();

        /// <summary>Click 步骤点蒙版洞：有绑定按钮则触发其 onClick（含业务命令），否则仅推进。</summary>
        void InvokeClickTarget();

        void Skip();

        void Abort();
    }
}
