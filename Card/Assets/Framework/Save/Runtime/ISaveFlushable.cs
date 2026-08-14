namespace Framework.Save
{
    /// <summary>
    /// A service that keeps state in memory and flushes it to storage on demand.
    /// Hosts can auto-flush these on pause / quit.
    /// </summary>
    public interface ISaveFlushable
    {
        /// <summary>
        /// Flush to storage only when there are pending changes. Safe to call frequently.
        /// </summary>
        void Save();
    }
}
