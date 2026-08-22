using System.Net;

namespace ElasticMachines.Utilities;

public class RetryingHandler : DelegatingHandler
{
    public RetryingHandler() : base(new HttpClientHandler())
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var maxTries = 3;

        for (var i = 1; ; i++)
        {
            HttpResponseMessage? response = null;

            try
            {
                response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

                if (i >= maxTries || !IsRetryable(response))
                    return response;
            }
            catch (Exception exception)
            {
                if (i >= maxTries || !IsRetryable(exception))
                    throw;
            }

            await Task.Delay(response?.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, i)), cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool IsRetryable(HttpResponseMessage response)
    {
        return response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError;
    }

    private static bool IsRetryable(Exception exception)
    {
        return exception switch
        {
            HttpRequestException _ => true,
            TaskCanceledException e when !e.CancellationToken.IsCancellationRequested => true,
            _ => false
        };
    }
}
