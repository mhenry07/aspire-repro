namespace AspireRepro.Worker;

public class ResourceClient(HttpClient httpClient)
{
    public HttpClient HttpClient { get; } = httpClient;
}
