﻿namespace SlimCluster.AspNetCore;

using System.Net.Http.Headers;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using SlimCluster.Transport.Ip;

public class RequestDelegatingClient
{
    private readonly HttpClient _client;
    private readonly ILogger _logger;

    private static readonly ISet<string> _headerBlacklist = new HashSet<string>
    {
        Microsoft.Net.Http.Headers.HeaderNames.ContentLength,
        Microsoft.Net.Http.Headers.HeaderNames.ContentType
    };

    public RequestDelegatingClient(HttpClient client, ILogger<RequestDelegatingClient> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task Delegate(HttpRequest request, HttpResponse response, IAddress leaderAddress, int localPort)
    {
        var leaderHost = leaderAddress.ToIPEndPoint().Address.ToString();

        var uriBuilder = new UriBuilder
        {
            Host = leaderHost,
            Port = localPort,
            Scheme = request.Scheme,
            Path = request.Path,
            Query = request.QueryString.Value,
        };
        var uri = uriBuilder.Uri;

        using var delegatedRequest = new HttpRequestMessage(GetMethod(request.Method), uri);

        // Pass headers
        foreach (var header in request.Headers)
        {
            if (!_headerBlacklist.Contains(header.Key))
            {
                _logger.LogTrace("Passing request header {HeaderName}", header.Key);
                delegatedRequest.Headers.Add(header.Key, header.Value.AsEnumerable());
            }
        }

        // Pass request content
        if (request.Body != null)
        {
            _logger.LogTrace("Passing request body");

            var ms = new MemoryStream((int?)request.ContentLength ?? 1024);
            await request.Body.CopyToAsync(ms);
            ms.Position=0;
            delegatedRequest.Content = new StreamContent(ms);
            if (request.ContentType != null)
            {
                delegatedRequest.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(request.ContentType);
            }
            if (request.ContentLength != null)
            {
                delegatedRequest.Content.Headers.ContentLength = request.ContentLength;
            }
        }

        _logger.LogInformation("Delegating the request {RequestUri} to the leader node", uri);
        try
        {


            using var delegatedResponse = await _client.SendAsync(delegatedRequest);

            if(delegatedResponse.StatusCode == System.Net.HttpStatusCode.NoContent)
            {
                _logger.LogInformation("StatusCode: No Content.", uri);
                throw new ClusterException();
            }

            response.StatusCode = (int)delegatedResponse.StatusCode;

            foreach (var header in delegatedResponse.Headers)
            {
                _logger.LogTrace("Passing response header {HeaderName}", header.Key);
                ///context.Response.Headers.Add(header.Key, new HeaderStringValues(){ header.Value);
            }

            response.ContentLength = delegatedResponse.Content.Headers.ContentLength;
            response.ContentType = delegatedResponse.Content.Headers.ContentType?.ToString();

            if (delegatedResponse.Content != null)
            {
                await delegatedResponse.Content.CopyToAsync(response.Body);
            }
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            throw new ClusterException($"An attempt by a follower node to redirect the command to the leader node failed because the leader did not respond within the timeout period. Try again. Message: ({ex.Message})", ex);
        }
        catch (HttpRequestException ex) when (ex.Message.StartsWith("No route to host"))
        {
            throw new ClusterException($"An attempt by a follower node to redirect the command to the leader node failed because the leader appears offline. Try again. Message: ({ex.Message})", ex);
        }
        catch (ClusterException ex)
        {
            throw new ClusterException($"Cluster membership in transition. The leader could not be contacted. Retry request. Message: ({ex.Message})", ex);
        }
        catch
        {
            throw;
        }
    }

    private static HttpMethod GetMethod(string method)
    {
        if (HttpMethods.IsDelete(method)) return HttpMethod.Delete;
        if (HttpMethods.IsGet(method)) return HttpMethod.Get;
        if (HttpMethods.IsHead(method)) return HttpMethod.Head;
        if (HttpMethods.IsOptions(method)) return HttpMethod.Options;
        if (HttpMethods.IsPost(method)) return HttpMethod.Post;
        if (HttpMethods.IsPut(method)) return HttpMethod.Put;
        if (HttpMethods.IsTrace(method)) return HttpMethod.Trace;
        return new HttpMethod(method);
    }
}
