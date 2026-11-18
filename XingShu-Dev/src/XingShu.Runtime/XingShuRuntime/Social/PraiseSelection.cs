using System.Collections.Generic;

namespace XingShuRuntime.Social
{
    // Choices belong to one match. Reserve before calling native UI: never retry
    // an asynchronous or throwing callback that may already have sent a request.
    internal sealed class PraiseSelection
    {
        private string _session;
        private long _self;
        private readonly HashSet<long> _initialized = new HashSet<long>();
        private readonly HashSet<long> _selected = new HashSet<long>();
        private readonly HashSet<long> _submitted = new HashSet<long>();
        private readonly HashSet<long> _confirmed = new HashSet<long>();
        internal string Session { get { return _session; } }
        internal int SubmittedCount { get { return _submitted.Count; } }
        internal int PendingCount { get { int count = 0; foreach (long id in _selected) if (!_submitted.Contains(id)) count++; return count; } }
        internal bool Begin(string session, long self)
        {
            if (string.IsNullOrEmpty(session) || self <= 0) { session = null; self = 0; }
            if (_session == session && _self == self) return false;
            _session = session; _self = self; _initialized.Clear(); _selected.Clear(); _submitted.Clear(); _confirmed.Clear();
            return true;
        }
        internal bool EnsureDefaultSelected(long id, bool eligible, bool defaultOn)
        {
            if (_session == null || id <= 0 || id == _self || !eligible || !defaultOn || _submitted.Contains(id)) return false;
            if (!_initialized.Add(id)) return false;
            return _selected.Add(id);
        }
        internal bool Toggle(long id, bool eligible)
        {
            if (_session == null || id <= 0 || id == _self || !eligible || _submitted.Contains(id)) return false;
            _initialized.Add(id);
            if (!_selected.Remove(id)) _selected.Add(id);
            return true;
        }
        internal bool IsSelected(long id) { return _selected.Contains(id) || _submitted.Contains(id); }
        internal bool IsSubmitted(long id) { return _submitted.Contains(id); }
        internal bool IsConfirmed(long id) { return _confirmed.Contains(id); }
        internal void ClearChoices() { _initialized.Clear(); _selected.Clear(); }
        internal bool Reserve(long id, bool eligible)
        {
            if (_session == null || id <= 0 || id == _self || !eligible || !_selected.Contains(id) || _submitted.Contains(id)) return false;
            return _submitted.Add(id);
        }
        internal void ObserveNative(long id, bool eligible)
        {
            if (_session == null || id <= 0 || id == _self || !eligible) return;
            _submitted.Add(id); _confirmed.Add(id);
        }
    }
}
