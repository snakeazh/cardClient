using Framework.Save;

namespace App.Guide
{
    /// <summary>记录已完成/已跳过的引导组，存档键 guide.progress.v1。</summary>
    public interface IGuideProgressService : ISaveFlushable
    {
        bool IsGroupCompleted(int groupId);

        void MarkGroupCompleted(int groupId);

        void ResetAll();

        void Load();
    }
}
