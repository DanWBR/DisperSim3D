namespace DisperSim3D.Automation
{
    /// <summary>The last few hundred things the server did, for GET /log.</summary>
    internal sealed class AutomationLog
    {
        private const int Capacity = 500;

        private readonly object _gate = new();
        private readonly Queue<Entry> _entries = new();
        private long _next;

        internal sealed record Entry(long Seq, DateTime Time, string Text);

        public void Add(string text)
        {
            lock (_gate)
            {
                _entries.Enqueue(new Entry(++_next, DateTime.Now, text));
                while (_entries.Count > Capacity) _entries.Dequeue();
            }
            System.Diagnostics.Debug.WriteLine("[Automation] " + text);
        }

        /// <summary>The entries after <paramref name="afterSeq"/>, oldest first.</summary>
        public List<Entry> Since(long afterSeq)
        {
            lock (_gate)
            {
                return _entries.Where(e => e.Seq > afterSeq).ToList();
            }
        }
    }
}
