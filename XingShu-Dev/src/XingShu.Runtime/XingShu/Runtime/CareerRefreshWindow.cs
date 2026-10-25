namespace XingShu.Runtime
{
    // A settlement can reach the client before the server career has been updated.
    // Keep refreshing for a bounded window instead of consuming one fire-and-forget request.
    internal sealed class CareerRefreshWindow
    {
        private float _until = -1f, _next;
        internal void Request(float now, float delay = 0f)
        {
            _until = now + delay + 65f;
            _next = now + delay;
        }
        internal bool Due(float now) { return now >= _next && now <= _until; }
        internal void Sent(float now) { _next = now + 15f; }
        internal void Retry(float now) { _next = now + 5f; }
    }
}
