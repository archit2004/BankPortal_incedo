using System.Net.Http.Json;
using Xunit.Sdk;

namespace BankPortal.Api.IntegrationTests;

public static class HttpExtensions
{
    // Reads the body as T, or fails the test with the server's response when the call was not successful.
    public static async Task<T> ReadOkAsync<T>(this HttpResponseMessage res)
    {
        if (!res.IsSuccessStatusCode)
            throw new XunitException(
                $"Expected a success status but got {(int)res.StatusCode} {res.StatusCode}: {await res.Content.ReadAsStringAsync()}");
        return (await res.Content.ReadFromJsonAsync<T>())!;
    }
}