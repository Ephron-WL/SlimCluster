namespace SlimCluster.Samples.ConsoleApp.State.StateMachine;

using SlimCluster.Consensus.Raft;
using SlimCluster.Persistence;
using SlimCluster.Samples.ConsoleApp.State.Logs;

using static SlimCluster.Samples.ConsoleApp.State.StateMachine.CounterStateMachine;

/// <summary>
/// Counter state machine that processes counter commands. Everything is stored in memory.
/// </summary>
public class CounterStateMachine : IStateMachine, ICounterState, IDurableComponent
{
    private int _index = 0;
    private int _counter = 0;

    public int CurrentIndex => _index;

    /// <summary>
    /// The counter value
    /// </summary>
    public int Counter => _counter;

    public List<LockRequestCommand> _locks = new();

    //public CounterStateMachine(LoggerFactory loggerFactory)
    //{
    //    _logger=loggerFactory.CreateLogger<CounterStateMachine>();
    //}
    //private ILogger<CounterStateMachine> _logger;  

    public async Task<object?> Apply(object command, int index)
    {
        // Note: This is thread safe - there is ever going to be only one task at a time calling Apply

        if (_index + 1 != index)
        {
            throw new InvalidOperationException($"The State Machine can only apply next command at index ${_index + 1}");
        }

        //await Task.Delay(20000);

        object? result = null;

        switch (command)
        {
            case LockRequestCommand lockRequest:

                result = lockRequest.Result;

                if(lockRequest.Result)
                {
                    var existing = _locks.SingleOrDefault(lock_ => lock_.Name.Equals(lockRequest.Name, StringComparison.CurrentCultureIgnoreCase)); 
                    if (existing is null)
                    {
                        _locks.Add(lockRequest);
                    }
                    // Change owner
                    else if(!existing.Owner.Equals(lockRequest.Owner, StringComparison.CurrentCultureIgnoreCase))
                    {
                        existing.Owner = lockRequest.Owner;
                    }
                }
                break;
            case LockReleaseCommand lockRelease:

                result = lockRelease.Result;
                if (lockRelease.Result)
                {
                    var existing = _locks.SingleOrDefault(lock_ => lock_.Name.Equals(lockRelease.Name, StringComparison.CurrentCultureIgnoreCase) && lock_.Owner.Equals(lockRelease.Owner, StringComparison.CurrentCultureIgnoreCase));
                    if (existing is not null)
                    {
                        _locks.Remove(existing);
                    }
                }
                break;
            default:

                result = command switch
                {
                    IncrementCounterCommand => ++_counter,
                    DecrementCounterCommand => --_counter,
                    ResetCounterCommand => _counter = 0,
                    _ => throw new NotImplementedException($"The command type ${command?.GetType().Name} is not supported")
                };
                break;
        }

        _index = index;

        return result;
    }

    // For now we don't support snapshotting
    public Task Restore() => throw new NotImplementedException();

    // For now we don't support snapshotting
    public Task Snapshot() => throw new NotImplementedException();

    public void OnStatePersist(IStateWriter state)
    {
        state.Set("counter", _counter);
        state.Set("locks", _locks);
    }

    public void OnStateRestore(IStateReader state) => throw new NotImplementedException();
}
