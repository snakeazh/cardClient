using Framework.Save;

namespace App.Guide
{
    /// <summary>记录已完成/已跳过的引导组，存档键 guide.progress.v1。</summary>
    public interface IGuideProgressService : ISaveFlushable
    {
        bool IsGroupCompleted(int groupId);

        void MarkGroupCompleted(int groupId);

        /// <summary>用服务器主档覆盖已完成引导组。仅 <c>ApplyServerProfile</c> 调用。</summary>
        void ReplaceFromServer(int[] completedGroupIds);

        void ResetAll();

        void Load();
    }
}
