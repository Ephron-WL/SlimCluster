namespace SlimCluster.Consensus.Raft;

public record LockReleaseCommand() //: AbstractCommand
{
    public string Name { get; set; }
    public string Owner { get; set; }
    public bool Result { get; set; }
}