namespace SlimCluster.Consensus.Raft;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

using Microsoft.Extensions.DependencyInjection;


using SlimCluster.Consensus.Raft.Logs;
using SlimCluster.Host.Common;
using SlimCluster.Membership;
using SlimCluster.Persistence;
using SlimCluster.Serialization;

public record FollowerReplicatonState
{
    /// <summary>
    /// For each server, index of the next log entry to send to that server (initialized to leader last log index + 1)
    /// </summary>
    public int NextIndex { get; set; }

    /// <summary>
    /// For each server, index of highest log entry known to be replicated on server (initialized to 0, increases monotonically)
    /// </summary>
    public int MatchIndex { get; set; }

    /// <summary>
    /// Last point in time when the <see cref="AppendEntriesRequest"/> was sent to the follower.
    /// </summary>
    public DateTimeOffset? LastAppendRequest { get; set; }
}

public delegate Task OnNewerTermDiscovered(int term, INode node);

public class RaftLeaderState : TaskLoop, IRaftClientRequestHandler, IDurableComponent
{
    private readonly ILogger<RaftLeaderState> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly RaftConsensusOptions _options;
    private readonly IClusterMembership _clusterMembership;
    private readonly ILogRepository _logRepository;
    private readonly IStateMachine _stateMachine;
    private readonly IMessageSender _messageSender;
    private readonly ISerializer _logSerializer;
    private readonly IClusterPersistenceService _clusterPersistenceService;
    private readonly ITime _time;
    private readonly OnNewerTermDiscovered _onNewerTermDiscovered;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<object?>> _pendingCommandResults;

    public int Term { get; protected set; }
    public IDictionary<string, FollowerReplicatonState> ReplicationStateByNode { get; protected set; }

    public RaftLeaderState(
        ILogger<RaftLeaderState> logger,
        IServiceProvider serviceProvider,
        int term,
        RaftConsensusOptions options,
        ClusterOptions clusterOptions,
        //IClusterPersistenceService clusterPersistenceService,
        IClusterMembership clusterMembership,
        IMessageSender messageSender,
        ILogRepository logRepository,
        IStateMachine stateMachine,
        ISerializer logSerializer,
        ITime time,
        OnNewerTermDiscovered onNewerTermDiscovered)
        : base(logger, clusterOptions)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _options = options;
        _clusterMembership = clusterMembership;
        _logSerializer = logSerializer;
        _logRepository = logRepository;
        _stateMachine = stateMachine;
        _messageSender = messageSender;
       // _clusterPersistenceService = clusterPersistenceService;
        _time = time;
        _onNewerTermDiscovered = onNewerTermDiscovered;
        _pendingCommandResults = new ConcurrentDictionary<int, TaskCompletionSource<object?>>();

        _service = new Lazy<IClusterPersistenceService>(() => _serviceProvider.GetService<IClusterPersistenceService>());

        Term = term;
        ReplicationStateByNode = new Dictionary<string, FollowerReplicatonState>();
    }

    protected override async Task<bool> OnLoopRun(CancellationToken token)
    {
        var tasks = new List<Task>();

        var lastIndex = _logRepository.LastIndex;
        var now = _time.Now;

        // for each cluster member - replicate logs 
        foreach (var member in _clusterMembership.OtherMembers)
        {
            var followerReplicationState = EnsureReplicationState(member.Node.Id);

            // check if there are new logs to replicate
            var sendNewLogEntries = lastIndex.Index >= followerReplicationState.NextIndex;
            // check if first ping after election
            var sendFirstPing = followerReplicationState.LastAppendRequest == null; // never sent an AppendEntriesRequest yet
            // check if ping sent in longer while
            var sendPing = followerReplicationState.LastAppendRequest == null
                || now >= followerReplicationState.LastAppendRequest.Value.Add(_options.HeartbeatInterval); // time to send ping to not get overthrown by another node

            if (sendNewLogEntries || sendPing)
            {
                var task = ReplicateLogWithFollower(lastIndex, followerReplicationState, member.Node, skipEntries: sendFirstPing, token);
                tasks.Add(task);
            }
        }

        if (tasks.Count > 0)
        {
            // idle run if all were idle runs
            await Task.WhenAll(tasks);
        }

        // ToDo: Remove lost nodes from ReplicationStateByNode (after some idle time)

        // ToDo: Perhaps run in a separate task loop
        var idleRun = await TryApplyLogs().ConfigureAwait(false);

        // idle run
        return idleRun;
    }

    private async Task<bool> TryApplyLogs()
    {
        var idleRun = true;

        // check if log replicated to majorty, if so then
        //   1. apply to state machione
        //   2. notify that commit index changed

        var commitedIndex = _logRepository.CommitedIndex;
        var highestReplicatedIndex = FindMajorityReplicatedIndex();
        if (highestReplicatedIndex > commitedIndex)
        {
            await ApplyLogs(commitedIndex, highestReplicatedIndex).ConfigureAwait(false);
            idleRun = false;
        }

        return idleRun;
    }

    private async Task ApplyLogs(int commitedIndex, int highestReplicatedIndex)
    {
        _logger.LogDebug("HighestReplicatedIndex: {HighestReplicatedIndex}, CommitedIndex: {ComittedIndex}", highestReplicatedIndex, commitedIndex);

        var logIndexStart = commitedIndex + 1;
        var logCount = highestReplicatedIndex - commitedIndex;
        var logs = await _logRepository.GetLogsAtIndex(logIndexStart, logCount).ConfigureAwait(false);

        for (var i = 0; i < logCount; i++)
        {
            var index = logIndexStart + i;

            var commandResult = await _stateMachine.Apply(_logRepository, logEntry: logs[i].Entry, logIndex: index, _logger, _logSerializer);
            await _service.Value.Persist(default);

            // Signal that this changed and pass result
            if (_pendingCommandResults.TryGetValue(index, out var tcs))
            {
                _logger.LogDebug("Set result to {Result}", commandResult);
                _logger.LogInformation("Set result to {Result}", commandResult);
                tcs.TrySetResult(commandResult);
            }
        }
    }

    private Lazy<IClusterPersistenceService> _service;

    private int GetMajorityCountMinusOne()
    {
        var majorityCount = _options.NodeCount / 2;

        return majorityCount;
    }

    private int FindMajorityReplicatedIndex()
    {
        var majorityCount = GetMajorityCountMinusOne();

        // ToDo: Do not take into acount inactive members
        var orderedMatchIndexes = ReplicationStateByNode.Values.Select(x => x.MatchIndex).OrderByDescending(x => x).ToList();

        var majorityMatchIndex = orderedMatchIndexes.FirstOrDefault(matchIndex =>
        {
            // This considers members that have left.
            return orderedMatchIndexes.Count(x => x >= matchIndex) + _options.NodeCount - _clusterMembership.Members.Count > majorityCount;
        });

        return Math.Max(majorityMatchIndex, _logRepository.CommitedIndex);
    }

    protected internal async Task ReplicateLogWithFollower(LogIndex lastIndex, FollowerReplicatonState followerReplicationState, INode followerNode, bool skipEntries, CancellationToken token)
    {
        var prevLogIndex = followerReplicationState.NextIndex - 1;

        var req = new AppendEntriesRequest
        {
            Term = Term,
            LeaderId = _clusterMembership.SelfMember.Node.Id,
            LeaderCommitIndex = _logRepository.CommitedIndex,
            PrevLogIndex = prevLogIndex,
            PrevLogTerm = prevLogIndex == 0 ? 0 : _logRepository.GetTermAtIndex(prevLogIndex)
        };

        if (!skipEntries && lastIndex.Index > prevLogIndex)
        {
            // ToDo: Intro option for max logs send
            var logsCount = Math.Min(lastIndex.Index - prevLogIndex, 1);
            var logs = logsCount > 0
                ? await _logRepository.GetLogsAtIndex(prevLogIndex + 1, logsCount)
                : null;

            req.Entries = logs;
        }

        try
        {
            _logger.LogDebug("{Node}: Sending {MessageName} with PrevLogIndex = {PrevLogIndex}, PrevLogTerm = {PrevLogTerm}, LogCount = {LogCount}", followerNode, nameof(AppendEntriesRequest), req.PrevLogIndex, req.PrevLogTerm, req.Entries?.Count ?? 0);
            // Note: setting the request timeout to match the leader timeout - now point in waiting longer
            var resp = await _messageSender.SendRequest(req, followerNode.Address, timeout: _options.HeartbeatInterval);

            // when the response arrives, update the timestamp of when the request was sent
            followerReplicationState.LastAppendRequest = _time.Now;

            if (resp.Success)
            {
                var count = req.Entries?.Count ?? 0;
                followerReplicationState.NextIndex += count;
                var newMatchIndex = prevLogIndex + count;
                if (newMatchIndex != followerReplicationState.MatchIndex)
                {
                    followerReplicationState.MatchIndex = newMatchIndex;
                    _logger.LogInformation("{Node}: Follower has log match until MatchIndex = {MatchIndex}", followerNode, followerReplicationState.MatchIndex);
                }
            }
            else
            {
                // if higher term discoverd
                if (resp.Term > Term)
                {
                    await _onNewerTermDiscovered(resp.Term, followerNode);
                    return;
                }

                _logger.LogInformation("{Node}: Follower log does not match at MatchIndex = {MatchIndex}", followerNode, followerReplicationState.MatchIndex);
                // logs dont match for the specified index, will retry on next loop run with prev index
                followerReplicationState.NextIndex--;
            }
        }
        catch (OperationCanceledException opCancelledEx)
        {
            // Will retry next time
            _logger.LogWarning("{Node}: Did not receive {MessageName}, will retry... ({CancelReason})", followerNode, nameof(AppendEntriesResponse), opCancelledEx.Message);

            // The response did not arrive, account for the wait time we already lost to not keep on calling the possibly failed follower
            followerReplicationState.LastAppendRequest = _time.Now;
        }
    }

    private FollowerReplicatonState EnsureReplicationState(string nodeId)
    {
        if (!ReplicationStateByNode.TryGetValue(nodeId, out var replicatonState))
        {
            replicatonState = new FollowerReplicatonState
            {
                NextIndex = _logRepository.LastIndex.Index + 1,
                MatchIndex = 0
            };
            ReplicationStateByNode.Add(nodeId, replicatonState);
        }
        return replicatonState;
    }

    private readonly Dictionary<LockRequestCommand, FifoLock> locks = new();
    private readonly object _lock = new();

    public async Task<object?> OnClientRequest(object command, CancellationToken token)
    {
        using var _ = _logger.BeginScope("Command {Command} processing", command.GetType().Name);

        var majorityCount = GetMajorityCountMinusOne();
        if (_clusterMembership.Members.Count <= majorityCount)
        {
            throw new ClusterException("This node is not a member of a cluster having a quorum. While trying again may result in routing to a node having a quorum, there is no guarantee this will occur. To resolve, increase the number of nodes running in the cluster or address a possible network fragmentation.");
        }

        switch (command)
        {
            case LockRequestCommand lockRequest:

                FifoLock fifoLock;
                LockRequestCommand existingRequest;
                KeyValuePair<LockRequestCommand, FifoLock> pair;
                lock (_lock)
                {
                    pair = locks.SingleOrDefault(lock_ => lock_.Key.Name == lockRequest.Name);
                    if (pair.Value is null)
                    {
                        fifoLock = new(_options.FifoLockTimeout);
                        locks.Add(lockRequest, fifoLock);
                        existingRequest = lockRequest;
                    }
                    else
                    {
                        if(pair.Key.Owner == lockRequest.Owner)
                        {
                            lockRequest.Result = true;
                        }

                        existingRequest = pair.Key;
                        fifoLock = pair.Value;
                    }
                }

                //try
                //{
                    if(!lockRequest.Result)
                    {
                        var lockResult = fifoLock.Enter();
                        lockRequest.Result = lockResult;
                    }
                //}
                //catch(OperationCanceledException ex)
                //{
                //    _logger.LogInformation($"Lock request timeout: lockRequest.Name: {lockRequest.Name} assigned to owner: {lockRequest.Owner}");
                //    throw new ClusterException("The lock request timed out. Retry request.");
                //}

                if(lockRequest.Result)
                {
                    existingRequest.Owner = lockRequest.Owner;
                }

                _logger.LogInformation($"lockRequest.Name: {lockRequest.Name} assigned to owner: {lockRequest.Owner}");
                break;

            case LockReleaseCommand lockRelease:

                lock (_lock)
                {
                    pair = locks.SingleOrDefault(lock_ => lock_.Key.Name.Equals(lockRelease.Name, StringComparison.CurrentCultureIgnoreCase) && lock_.Key.Owner.Equals(lockRelease.Owner, StringComparison.CurrentCultureIgnoreCase));
                    if (pair.Value is not null)
                    {
                        _logger.LogInformation($"The lock was found. lock count: {locks.Count}. lockRequest.Name: {lockRelease.Name} assigned to host: {lockRelease.Owner}");

                        fifoLock = pair.Value;
                        fifoLock.Exit();
                        locks.Remove(pair.Key);
                        lockRelease.Result = true;

                        _logger.LogInformation($"The lock was released.  lock count: {locks.Count}. lockRequest.Name: {lockRelease.Name} assigned to host: {lockRelease.Owner}");
                    }
                }

                break;
        }

        // Append log to local node (must be thread-safe)
        var log = _logSerializer.Serialize(command);
        var commandIndex = await _logRepository.Append(Term, log);
        _logger.LogTrace("Appended command at index {Index} in term {Term}", commandIndex, Term);

        var requestTimer = Stopwatch.StartNew();

        var tcs = new TaskCompletionSource<object?>();
        _pendingCommandResults.TryAdd(commandIndex, tcs);
        try
        {
            // Wait until index committed (replicated to majority of nodes) and applied to state machine
            while (requestTimer.Elapsed < _options.RequestTimeout)
            {
                token.ThrowIfCancellationRequested();

                var t = await Task.WhenAny(tcs.Task, Task.Delay(50, token));

                if (t == tcs.Task)
                {
                    var result = tcs.Task.Result;
                    _logger.LogInformation("Command {Command} result is {Result}", command, result);
                    return result;
                }
            }
            throw new ClusterException($"The state machine application process took too long. It must take less than {_options.RequestTimeout.TotalSeconds} seconds.  Despite this exceptions, state machine changes may still be successfully applied.");
        }
        catch (Exception ex)
        {
            _logger.LogInformation("OnClientRequest exception: {Exception}", ex.ToString());
            throw;
        }
        finally
        {
            // Attempt to cancel if still was pending
            tcs.TrySetCanceled();

            // Clean up pending commands
            _pendingCommandResults.TryRemove(commandIndex, out var _);
        }
    }

    #region IDurableComponent

    public void OnStateRestore(IStateReader state)
    {
        _logger.LogInformation("Restoring state");

        Term = state.Get<int>("leaderTerm");
        ReplicationStateByNode = state.Get<IDictionary<string, FollowerReplicatonState>>("leaderReplicationState") ?? new Dictionary<string, FollowerReplicatonState>();
    }

    public void OnStatePersist(IStateWriter state)
    {
        _logger.LogInformation("Persisting state");

        state.Set("leaderTerm", Term);
        state.Set("leaderReplicationState", ReplicationStateByNode);
    }

    #endregion
}
