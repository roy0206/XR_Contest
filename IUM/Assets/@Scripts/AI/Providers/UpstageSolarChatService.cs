using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Upstage Solar chat completions. The endpoint follows the OpenAI schema, so switching to
/// another compatible provider is an endpoint and model change in ai_config.json.
/// </summary>
public sealed class UpstageSolarChatService : IAiChatService
{
    readonly AiLlmConfig _config;
    readonly AiSecrets _secrets;
    readonly bool _verbose;

    public UpstageSolarChatService(AiConfig config, AiSecrets secrets)
    {
        _config = config?.Llm ?? new AiLlmConfig();
        _secrets = secrets ?? new AiSecrets();
        _verbose = config?.VerboseLogging ?? false;
    }

    public bool IsMock => false;

    public async Task<AiChatResult> CompleteAsync(AiChatRequest request, CancellationToken cancellationToken)
    {
        if (request == null || request.Messages.Count == 0)
            return new AiChatResult(null);

        var messages = new JArray();
        for (var i = 0; i < request.Messages.Count; i++)
        {
            var message = request.Messages[i];
            if (string.IsNullOrWhiteSpace(message?.Content)) continue;
            messages.Add(new JObject
            {
                ["role"] = message.RoleName,
                ["content"] = message.Content
            });
        }

        var payload = new JObject
        {
            ["model"] = _config.Model,
            ["messages"] = messages,
            ["temperature"] = request.Temperature,
            ["max_tokens"] = request.MaxTokens,
            ["stream"] = false
        };

        // Omitted unless configured: the parameter is model-specific and reasoning is not
        // wanted for short hint answers.
        if (!string.IsNullOrWhiteSpace(_config.ReasoningEffort))
            payload["reasoning_effort"] = _config.ReasoningEffort;

        var body = payload.ToString(Newtonsoft.Json.Formatting.None);

        var headers = new Dictionary<string, string>
        {
            ["Authorization"] = $"Bearer {_secrets.UpstageApiKey}"
        };

        var response = await AiHttp.PostJsonAsync(
            _config.Endpoint, body, headers, _config.TimeoutSeconds, cancellationToken);

        if (!response.Success)
            throw response.ToException("Upstage Solar");

        var content = ParseContent(response.Text);
        if (_verbose)
            Debug.Log($"[AI] Solar({_config.Model}) -> {content}");

        return new AiChatResult(content);
    }

    static string ParseContent(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (Exception exception)
        {
            throw new AiServiceException($"Upstage Solar returned unreadable JSON: {exception.Message}");
        }

        if (root["error"] is JObject error)
            throw new AiServiceException($"Upstage Solar error: {error.Value<string>("message")}");

        if (root["choices"] is not JArray choices || choices.Count == 0) return null;

        var message = choices[0]["message"];
        return message?.Value<string>("content");
    }
}
