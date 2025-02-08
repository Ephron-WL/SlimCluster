namespace SlimCluster.Consensus.Raft;

public class FifoLock
{
    private readonly Queue<(ManualResetEventSlim ResetEvent, bool IsTimeout)> _queue = new();
    private readonly object _lock = new();

    //private readonly ILogger _logger;
    private readonly TimeSpan _timeoutPeriod;

    public FifoLock(TimeSpan timeoutPeriod) //ILogger logger)
    {
        _timeoutPeriod = timeoutPeriod;
        //_logger = logger;
    }

    public bool Enter()
    {
        (ManualResetEventSlim ResetEvent, bool IsTimeout) signal = (new(false), false);
        lock (_lock)
        {
            _queue.Enqueue(signal);

            if (_queue.Count == 1)
            {
                signal.ResetEvent.Set();
            }
        }

        var index = Task.WaitAny(Task.Run(signal.ResetEvent.Wait), Task.Delay(_timeoutPeriod));
        
        signal.IsTimeout = (index == 1);

        return index == 0;
    }

    public void Exit()
    {
        lock (_lock)
        {
            var signal = _queue.Dequeue();

            if (_queue.Count > 0)
            {
                if (signal.IsTimeout)
                {
                    Exit();
                }
                _queue.Peek().ResetEvent.Set();
            }
            signal.ResetEvent.Dispose();
        }
    }
}