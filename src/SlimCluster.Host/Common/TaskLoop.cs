namespace SlimCluster.Host.Common;

using Microsoft.Extensions.Options;

public abstract class TaskLoop
{
    private readonly ILogger _logger;
    private readonly ClusterOptions _options;

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private bool _isStarted = false;
    private bool _isStarting = false;
    private bool _isStopping = false;

    public bool IsStarted => _isStarted;

    protected TaskLoop(ILogger logger, ClusterOptions options)
    {
        _logger = logger;
        _options = options;
    }

    public async Task Start()

    {
        if (!_isStarted && !_isStarting)
        {
            _isStarting = true;
            try
            {
                await OnStarting();

                _loopCts = new CancellationTokenSource();
                _loopTask = Task.Run(Loop);

                _isStarted = true;

                await OnStarted();
            }
            finally
            {
                _isStarting = false;
            }
        }
    }

    public async Task Stop()
    {
        if (_isStarted && !_isStopping)
        {
            _isStopping = true;
            try
            {
                if (_loopCts != null)
                {
                    _loopCts.Cancel();
                }

                await OnStopping();

                if (_loopTask != null)
                {
                    await _loopTask;
                    _loopTask = null;
                }

                if (_loopCts != null)
                {
                    _loopCts.Dispose();
                    _loopCts = null;
                }

                _isStarted = false;

                await OnStopped();
            }
            finally
            {
                _isStopping = false;
            }
        }
    }

    private async Task Loop()
    {
        try
        {
            while (_loopCts != null && !_loopCts.IsCancellationRequested)
            {
                try
                {
                    var idleRun = await OnLoopRun(_loopCts.Token).ConfigureAwait(false);
                    if (idleRun)
                    {
                        await Task.Delay(_options.IdleLoopDelay).ConfigureAwait(false);
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Loop error, will retry");
                }
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Loop crashed");
        }
    }

    protected internal virtual Task OnStarted() => Task.CompletedTask;
    protected internal virtual Task OnStarting() => Task.CompletedTask;
    protected internal virtual Task OnStopped() => Task.CompletedTask;
    protected internal virtual Task OnStopping() => Task.CompletedTask;
    protected internal abstract Task<bool> OnLoopRun(CancellationToken token);
}
