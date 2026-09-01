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

        void TryStart(GuideTriggerType type, string param);

        void StartGroup(int groupId);

        void Advance();

        void Skip();

        void Abort();
    }
}
