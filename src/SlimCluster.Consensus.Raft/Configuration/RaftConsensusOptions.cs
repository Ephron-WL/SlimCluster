﻿namespace SlimCluster.Consensus.Raft;

using SlimCluster.Serialization;

public class RaftConsensusOptions
{
    /// <summary>
    /// Minimum election timeout min
    /// </summary>
    public TimeSpan ElectionTimeoutMin { get; set; } = TimeSpan.FromSeconds(3);
    /// <summary>
    /// Minimum election timeout max
    /// </summary>
    public TimeSpan ElectionTimeoutMax { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Timeout after which a leader is considered dead (unless ApppendEntriesRequest arrives).
    /// </summary>
    // Not needed
    //public TimeSpan LeaderTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Time after which an empty AppendEntriesRequest must be sent to prevent Leader timout from happening.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Defines the target cluster size. It is used to calculate majority of nodes.
    /// </summary>
    public int NodeCount { get; set; }

    /// <summary>
    /// The service type that should be resolved from MSDI for Log Entry serialization.
    /// </summary>
    public Type LogSerializerType { get; set; } = typeof(ISerializer);

    /// <summary>
    /// The timeout for a leader to process the request.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);

    public TimeSpan FifoLockTimeout { get; set; } = TimeSpan.FromSeconds(30);
}