namespace SlimCluster.Samples.Service.Controllers;

using Microsoft.AspNetCore.Mvc;

using SlimCluster.Consensus.Raft;
using SlimCluster.Samples.ConsoleApp.State.Logs;
using SlimCluster.Samples.ConsoleApp.State.StateMachine;

[ApiController]
[Route("[controller]")]
public class CounterController : ControllerBase
{
    private readonly ICounterState _counterState;
    private readonly IRaftClientRequestHandler _clientRequestHandler;

    public CounterController(ICounterState counterState, IRaftClientRequestHandler clientRequestHandler)
    {
        _counterState = counterState;
        _clientRequestHandler = clientRequestHandler;
    }

    [HttpGet()]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    public int Get() => _counterState.Counter;

    [HttpPost("[action]")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    public async Task<int?> Increment(CancellationToken cancellationToken)
    {
        int? result = null;
        try
        {
            result = (int?)await _clientRequestHandler.OnClientRequest(new IncrementCounterCommand(), cancellationToken);
            result ??= -1;
        }
        catch (Exception ex) 
        {
            throw;
        }

        return result;
    }

    [HttpPost("[action]")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    public async Task<int?> Decrement(CancellationToken cancellationToken)
    {
        var result = await _clientRequestHandler.OnClientRequest(new DecrementCounterCommand(), cancellationToken);
        return (int?)result;
    }

    [HttpPost("[action]")]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    public async Task<int?> Reset(CancellationToken cancellationToken)
    {
        var result = await _clientRequestHandler.OnClientRequest(new ResetCounterCommand(), cancellationToken);
        return (int?)result;
    }

    [HttpPost("[action]")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public async Task<bool?> LockRequest([FromBody] LockRequestCommand command, CancellationToken cancellationToken)
    {
        var result = await _clientRequestHandler.OnClientRequest(command, cancellationToken);
        return (bool?)result;
    }

    [HttpPost("[action]")]
    [ProducesResponseType(typeof(bool), StatusCodes.Status200OK)]
    public async Task<bool?> LockRelease([FromBody] LockReleaseCommand command, CancellationToken cancellationToken)
    {
        var result = await _clientRequestHandler.OnClientRequest(command, cancellationToken);
        return (bool?)result;
    }
}