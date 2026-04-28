using System.Collections.Generic;
using Newtonsoft.Json;

namespace LLMMysterSolving
{
    [System.Serializable]
    public class GameConfigRoot
    {
        [JsonProperty("api")]
        public GameApiConfig api = new GameApiConfig();

        [JsonProperty("scenario_title")]
        public string scenario_title = "王掌柜审讯";

        [JsonProperty("scenario_background")]
        public string scenario_background = "唐代长安夜市命案，需通过审讯锁定嫌疑人与动机。";

        [JsonProperty("npcs")]
        public List<GameNpcConfig> npcs = new List<GameNpcConfig>();

        [JsonProperty("rag_clues")]
        public List<GameRagClue> rag_clues = new List<GameRagClue>();
    }

    [System.Serializable]
    public class GameApiConfig
    {
        [JsonProperty("base_url")]
        public string base_url = "https://api.deepseek.com/chat/completions";

        [JsonProperty("api_key")]
        public string api_key = "";

        [JsonProperty("model_name")]
        public string model_name = "deepseek-chat";
    }

    [System.Serializable]
    public class GameNpcConfig
    {
        [JsonProperty("name")]
        public string name;

        [JsonProperty("personality")]
        public string personality;

        [JsonProperty("secret")]
        public string secret;

        [JsonProperty("background")]
        public string background;
    }

    [System.Serializable]
    public class GameRagClue
    {
        [JsonProperty("id")]
        public string id;

        [JsonProperty("title")]
        public string title;

        [JsonProperty("content")]
        public string content;

        [JsonProperty("keywords")]
        public List<string> keywords = new List<string>();
    }
}
