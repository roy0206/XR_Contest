using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>Raised by any AI service. <see cref="IsNetworkError"/> drives the offline fallback.</summary>
public sealed class AiServiceException : Exception
{
    public AiServiceException(string message, bool isNetworkError = false, Exception inner = null)
        : base(message, inner) => IsNetworkError = isNetworkError;

    public bool IsNetworkError { get; }
}

public readonly struct AiHttpResponse
{
    public AiHttpResponse(bool success, long statusCode, string text, byte[] data, string error, bool networkError)
    {
        Success = success;
        StatusCode = statusCode;
        Text = text;
        Data = data;
        Error = error;
        IsNetworkError = networkError;
    }

    public bool Success { get; }
    public long StatusCode { get; }
    public string Text { get; }
    public byte[] Data { get; }
    public string Error { get; }
    public bool IsNetworkError { get; }

    public AiServiceException ToException(string serviceName) =>
        new($"{serviceName} request failed ({StatusCode}): {Error}", IsNetworkError);
}

/// <summary>
/// Task wrapper around UnityWebRequest. Every AI call goes through here so timeout,
/// cancellation and network-vs-service error classification stay in one place.
/// </summary>
public static class AiHttp
{
    public static bool IsOnline => Application.internetReachability != NetworkReachability.NotReachable;

    public static Task<AiHttpResponse> PostJsonAsync(
        string url,
        string json,
        IReadOnlyDictionary<string, string> headers,
        float timeoutSeconds,
        CancellationToken cancellationToken) =>
        PostAsync(url, Encoding.UTF8.GetBytes(json), "application/json; charset=utf-8", headers, timeoutSeconds, cancellationToken);

    public static Task<AiHttpResponse> PostFormAsync(
        string url,
        string formBody,
        IReadOnlyDictionary<string, string> headers,
        float timeoutSeconds,
        CancellationToken cancellationToken) =>
        PostAsync(url, Encoding.UTF8.GetBytes(formBody), "application/x-www-form-urlencoded", headers, timeoutSeconds, cancellationToken);

    public static async Task<AiHttpResponse> PostAsync(
        string url,
        byte[] body,
        string contentType,
        IReadOnlyDictionary<string, string> headers,
        float timeoutSeconds,
        CancellationToken cancellationToken)
    {
        // A request that never leaves the device would still burn the whole timeout.
        if (!IsOnline)
            return new AiHttpResponse(false, 0, null, null, "The device reports no network connection.", true);

        using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
        {
            uploadHandler = new UploadHandlerRaw(body) { contentType = contentType },
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = Mathf.Max(1, Mathf.CeilToInt(timeoutSeconds))
        };

        if (headers != null)
            foreach (var header in headers)
                request.SetRequestHeader(header.Key, header.Value);

        await SendAsync(request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var success = request.result == UnityWebRequest.Result.Success;
        var networkError = request.result == UnityWebRequest.Result.ConnectionError;
        var text = request.downloadHandler?.text;
        var error = success ? null : BuildError(request, text);
        return new AiHttpResponse(success, request.responseCode, text, request.downloadHandler?.data, error, networkError);
    }

    /// <summary>Awaits the operation and aborts the request when the token is cancelled.</summary>
    public static Task SendAsync(UnityWebRequest request, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = default(CancellationTokenRegistration);
        var operation = request.SendWebRequest();

        operation.completed += _ =>
        {
            registration.Dispose();
            completion.TrySetResult(true);
        };

        if (cancellationToken.CanBeCanceled && !operation.isDone)
        {
            registration = cancellationToken.Register(() =>
            {
                // Abort raises `completed`, so the task still finishes instead of hanging.
                if (request != null && !request.isDone) request.Abort();
            });
        }

        return completion.Task;
    }

    /// <summary>Reads a text asset that ships in StreamingAssets, on every platform including Android.</summary>
    public static async Task<string> ReadStreamingTextAsync(string path, CancellationToken cancellationToken)
    {
        using var request = UnityWebRequest.Get(path);
        request.timeout = 10;
        await SendAsync(request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return request.result == UnityWebRequest.Result.Success ? request.downloadHandler.text : null;
    }

    static string BuildError(UnityWebRequest request, string text)
    {
        if (string.IsNullOrEmpty(text)) return request.error;
        // Server error bodies are the only useful diagnostic for 4xx auth/quota problems.
        var trimmed = text.Length > 400 ? text.Substring(0, 400) + "…" : text;
        return $"{request.error} | {trimmed}";
    }
}
