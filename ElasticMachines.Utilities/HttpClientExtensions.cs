namespace ElasticMachines.Utilities;

public static class HttpClientExtensions
{
    public static async Task<HttpResponseMessage> SendSuccessfullyAsync(this HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
            return response;

        var statusCode = (int)response.StatusCode;
        var reasonPhrase = response.ReasonPhrase;
        var content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        response.Dispose();

        throw new HttpRequestException($"Response status code does not indicate success: {statusCode} ({reasonPhrase}):\n{content}");
    }
}
