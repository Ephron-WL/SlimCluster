namespace SlimCluster.Consensus.Raft;

public class RaftFollowerState
{
    private readonly TimeSpan _leaderTimeout;
    private readonly ITime _time;
    private readonly ILogger<RaftFollowerState> _logger;

    private readonly int _term;

    public DateTimeOffset LeaderTimeout { get; protected set; }

    public INode? Leader { get; protected set; }

    public RaftFollowerState(ILogger<RaftFollowerState> logger, TimeSpan leaderTimeout, ITime time, int term, INode? leaderNode)
    {
        _logger = logger;
        _leaderTimeout = leaderTimeout;
        _time = time;
        _term = term;
        Leader = leaderNode;
        OnLeaderMessage(leaderNode);
    }

    public void OnLeaderMessage(INode? leaderNode)
    {
        LeaderTimeout = _time.Now.Add(_leaderTimeout);
        if (Leader != leaderNode)
        {
            Leader = leaderNode;
            _logger.LogInformation("New leader is {Node} for term {Term}", Leader, _term);
        }
    }
}