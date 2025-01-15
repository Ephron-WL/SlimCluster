namespace SlimCluster.Consensus.Raft;

using Microsoft.Extensions.DependencyInjection;

using SlimCluster.AspNetCore;

public static class ClusterConfigurationExtensions
{
    public static ClusterConfiguration AddAspNetCore(this ClusterConfiguration cfg, Action<ClusterAspNetOptions> options)
    {
        cfg.PostConfigurationActions.Add(services =>
        {
            services.AddHttpClient<RequestDelegatingClient>(client => {
                // If the leader fails then followers will attempt to contact it and it needs to fail quickly because the leader's IP may change to
                // a new node and therefore the target IP for this client call may no longer be valid.
                // TODO: Should be an should be an option.
                // ElectionTimeoutMax = 6?
                client.Timeout = TimeSpan.FromSeconds(6);
            });

            services.Configure(options);
        });
        return cfg;
    }
}
