using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Networking;

namespace LLMMysterSolving
{
    public class DeepSeekClientResult
    {
        public bool IsSuccess { get; private set; }
        public string Content { get; private set; }
        public string ErrorMessage { get; private set; }

        public static DeepSeekClientResult Success(string content)
        {
            return new DeepSeekClientResult { IsSuccess = true, Content = content, ErrorMessage = string.Empty };
        }

        public static DeepSeekClientResult Failure(string message)
        {
            return new DeepSeekClientResult { IsSuccess = false, Content = string.Empty, ErrorMessage = message ?? "Request failed." };
        }
    }

    public class DeepSeekClient
    {
        private const int MaxAttempts = 2;
        private const string EmptyRetrySystemPrompt = "Your previous reply was blank. Return exactly one valid JSON object with fields stress_delta,current_stress,emotion_state,inner_thought,dialogue, and ensure dialogue is non-empty. Do not output markdown or explanations.";

        static string MaskApiKey(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                return "(empty)";
            }

            if (apiKey.Length <= 8)
            {
                return "****";
            }

            return $"{apiKey.Substring(0, 4)}****{apiKey.Substring(apiKey.Length - 4)}";
        }

        static string BuildRequestFailure(UnityWebRequest request)
        {
            long code = request.responseCode;
            string transport = string.IsNullOrWhiteSpace(request.error) ? "Unknown transport error" : request.error;
            string body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;

            if (!string.IsNullOrWhiteSpace(body))
            {
                try
                {
                    DeepSeekChatCompletionResponse parsed = JsonConvert.DeserializeObject<DeepSeekChatCompletionResponse>(body);
                    if (parsed != null && parsed.error != null && !string.IsNullOrWhiteSpace(parsed.error.message))
                    {
                        return $"HTTP {code}: {parsed.error.message}";
                    }
                }
                catch
                {
                    // Fall through and use transport/body summary.
                }

                string compactBody = body.Length > 240 ? body.Substring(0, 240) + "..." : body;
                return $"HTTP {code}: {transport}. Body: {compactBody}";
            }

            return $"HTTP {code}: {transport}";
        }

        static string ExtractChoiceContent(DeepSeekChatCompletionResponse response, string responseBody)
        {
            if (response != null && response.choices != null && response.choices.Count > 0)
            {
                DeepSeekChoice firstChoice = response.choices[0];
                if (firstChoice != null && firstChoice.message != null && !string.IsNullOrWhiteSpace(firstChoice.message.content))
                {
                    return firstChoice.message.content.Trim();
                }
            }

            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return string.Empty;
            }

            try
            {
                JToken root = JToken.Parse(responseBody);
                JToken contentToken = root.SelectToken("choices[0].message.content");
                if (contentToken == null || contentToken.Type == JTokenType.Null)
                {
                    return string.Empty;
                }

                if (contentToken.Type == JTokenType.String)
                {
                    return contentToken.ToString().Trim();
                }

                if (contentToken.Type == JTokenType.Array)
                {
                    StringBuilder builder = new StringBuilder();
                    foreach (JToken item in contentToken)
                    {
                        if (item == null || item.Type == JTokenType.Null)
                        {
                            continue;
                        }

                        if (item.Type == JTokenType.String)
                        {
                            builder.Append(item.ToString());
                            continue;
                        }

                        string text = item["text"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            builder.Append(text);
                        }
                    }

                    return builder.ToString().Trim();
                }
            }
            catch
            {
                return string.Empty;
            }

            return string.Empty;
        }

        static string GetFinishReason(DeepSeekChatCompletionResponse response)
        {
            if (response == null || response.choices == null || response.choices.Count == 0 || response.choices[0] == null)
            {
                return string.Empty;
            }

            return response.choices[0].finish_reason ?? string.Empty;
        }

        static DeepSeekChatRequest BuildAttemptPayload(string model, List<DeepSeekMessage> baseMessages, int attempt)
        {
            List<DeepSeekMessage> attemptMessages = new List<DeepSeekMessage>();
            if (attempt > 1)
            {
                attemptMessages.Add(new DeepSeekMessage
                {
                    role = "system",
                    content = EmptyRetrySystemPrompt
                });
            }

            if (baseMessages != null)
            {
                foreach (DeepSeekMessage message in baseMessages)
                {
                    if (message == null)
                    {
                        continue;
                    }

                    attemptMessages.Add(new DeepSeekMessage
                    {
                        role = message.role,
                        content = message.content
                    });
                }
            }

            return new DeepSeekChatRequest
            {
                model = model,
                messages = attemptMessages,
                response_format = new DeepSeekResponseFormat { type = "json_object" }
            };
        }

        public async Task<DeepSeekClientResult> SendChatAsync(string baseUrl, string apiKey, string model, List<DeepSeekMessage> messages)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return DeepSeekClientResult.Failure("DeepSeek base URL is empty.");
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return DeepSeekClientResult.Failure("DeepSeek API key is empty.");
            }

            for (int attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                DeepSeekChatRequest requestPayload = BuildAttemptPayload(model, messages, attempt);
                string requestJson = JsonConvert.SerializeObject(requestPayload);
                byte[] bodyRaw = Encoding.UTF8.GetBytes(requestJson);

                Debug.Log($"[DeepSeekRequest] attempt={attempt}, url={baseUrl}, model={model}, api_key={MaskApiKey(apiKey)}");
                Debug.Log($"[DeepSeekRequest] payload={requestJson}");

                using (UnityWebRequest request = new UnityWebRequest(baseUrl, UnityWebRequest.kHttpVerbPOST))
                {
                    request.timeout = 45;
                    request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.SetRequestHeader("Authorization", $"Bearer {apiKey}");

                    UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        return DeepSeekClientResult.Failure(BuildRequestFailure(request));
                    }

                    string responseBody = request.downloadHandler.text;
                    if (string.IsNullOrWhiteSpace(responseBody))
                    {
                        return DeepSeekClientResult.Failure("DeepSeek returned an empty response.");
                    }

                    try
                    {
                        DeepSeekChatCompletionResponse response = JsonConvert.DeserializeObject<DeepSeekChatCompletionResponse>(responseBody);
                        if (response == null)
                        {
                            return DeepSeekClientResult.Failure("Unable to parse DeepSeek response.");
                        }

                        if (response.error != null && !string.IsNullOrWhiteSpace(response.error.message))
                        {
                            return DeepSeekClientResult.Failure($"DeepSeek error: {response.error.message}");
                        }

                        string content = ExtractChoiceContent(response, responseBody);
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            return DeepSeekClientResult.Success(content);
                        }

                        string finishReason = GetFinishReason(response);
                        string compactBody = responseBody.Length > 240 ? responseBody.Substring(0, 240) + "..." : responseBody;
                        Debug.LogWarning($"[DeepSeekResponse] attempt={attempt}, empty content, finish_reason={finishReason}, body={compactBody}");

                        if (attempt < MaxAttempts)
                        {
                            Debug.LogWarning("[DeepSeekResponse] Retrying with extra JSON-only instruction.");
                            continue;
                        }

                        string reasonText = string.IsNullOrWhiteSpace(finishReason) ? "unknown" : finishReason;
                        return DeepSeekClientResult.Failure($"DeepSeek returned blank message content after {MaxAttempts} attempts (finish_reason={reasonText}).");
                    }
                    catch (Exception ex)
                    {
                        return DeepSeekClientResult.Failure($"Unable to parse DeepSeek payload: {ex.Message}");
                    }
                }
            }

            return DeepSeekClientResult.Failure("DeepSeek returned empty message content.");
        }
    }
}
