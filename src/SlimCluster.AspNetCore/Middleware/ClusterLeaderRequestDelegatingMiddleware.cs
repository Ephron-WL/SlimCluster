namespace SlimCluster.AspNetCore;

using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SlimCluster.Consensus.Raft;

public class ClusterLeaderRequestDelegatingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ICluster _cluster;
    private readonly RequestDelegatingClient _requestDelegatingClient;
    private readonly ClusterAspNetOptions _options;

    public ClusterLeaderRequestDelegatingMiddleware(RequestDelegate next, ICluster cluster, RequestDelegatingClient requestDelegatingClient, IOptions<ClusterAspNetOptions> options)
    {
        _next = next;
        _cluster = cluster;
        _requestDelegatingClient = requestDelegatingClient;
        _options = options.Value;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            // Check if request should be routed to leader
            if (_options.DelegateRequestToLeader != null && _options.DelegateRequestToLeader(context.Request))
            {
                var leaderAddress = _cluster.LeaderNode?.Address;
                if (leaderAddress == null)
                {
                    throw new ClusterException("Cluster membership in transition. Leader is not known at this time. Retry request.");
                }

                if (!_cluster.SelfNode.Equals(_cluster.LeaderNode))
                {
                    // This is a follower, so need to delegate the call to the leader
                    await _requestDelegatingClient.Delegate(context.Request, context.Response, leaderAddress, localPort: context.Connection.LocalPort);
                    return;
                }
            }
            // Not subject for routing, or this is the leader node (safe to pass the request processing by self)
            await _next(context);
           
        }
        catch (Exception ex)
        {
            if(ex is ClusterException)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.ContentType = "application/json";
                string payload = JsonSerializer.Serialize(new { ex.Message, ExceptionType = ex.GetType().Name });
                context.Response.ContentLength = payload.Length;
                await context.Response.WriteAsync(payload);
            }
            throw;
        }
    }
}
