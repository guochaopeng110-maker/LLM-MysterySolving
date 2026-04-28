using System.Collections.Generic;
using Newtonsoft.Json;

namespace LLMMysterSolving
{
    [System.Serializable]
    public class DeepSeekMessage
    {
        [JsonProperty("role")]
        public string role;

        [JsonProperty("content")]
        public string content;
    }

    [System.Serializable]
    public class DeepSeekResponseFormat
    {
        [JsonProperty("type")]
        public string type = "json_object";
    }

    [System.Serializable]
    public class DeepSeekChatRequest
    {
        [JsonProperty("model")]
        public string model;

        [JsonProperty("messages")]
        public List<DeepSeekMessage> messages = new List<DeepSeekMessage>();

        [JsonProperty("response_format")]
        public DeepSeekResponseFormat response_format = new DeepSeekResponseFormat();
    }

    [System.Serializable]
    public class DeepSeekChatCompletionResponse
    {
        [JsonProperty("choices")]
        public List<DeepSeekChoice> choices;

        [JsonProperty("error")]
        public DeepSeekError error;
    }

    [System.Serializable]
    public class DeepSeekChoice
    {
        [JsonProperty("message")]
        public DeepSeekMessage message;

        [JsonProperty("finish_reason")]
        public string finish_reason;
    }

    [System.Serializable]
    public class DeepSeekError
    {
        [JsonProperty("message")]
        public string message;
    }

    [System.Serializable]
    public class InterrogationResponsePayload
    {
        [JsonProperty("stress_delta")]
        public int? stress_delta;

        [JsonProperty("current_stress")]
        public int? current_stress;

        [JsonProperty("emotion_state")]
        public string emotion_state;

        [JsonProperty("inner_thought")]
        public string inner_thought;

        [JsonProperty("dialogue")]
        public string dialogue;
    }
}
