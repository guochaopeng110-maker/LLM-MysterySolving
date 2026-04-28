# LLM-MysterySolving

一款基于 Unity + 大语言模型（DeepSeek API）的 2D 古风审讯解谜 Demo。玩家通过与嫌疑人多轮对话、追问并出示证物，推动嫌疑人压力值变化，最终逼近真相。

## 演示视频

- 视频文件：`docs/视频演示.mp4`
- 建议直接在本地播放器打开查看完整交互流程（约 2 分 45 秒）

## 核心玩法

- 多轮审讯对话：玩家输入问题，嫌疑人实时生成回答。
- 压力值系统：嫌疑人每次回复会返回 `stress_delta` 与 `current_stress`，并同步到 UI。
- 证物出示机制：点击“出示证物”后，下一轮提问附带强制追问提示，更容易触发“破防”口供。
- 本地线索检索（轻量 RAG）：根据用户提问关键词，从 `game_config.json` 的 `rag_clues` 中检索相关线索注入系统提示。
- 剧本热编辑：运行时可在“剧本设置”面板修改场景背景、NPC 名称/性格/秘密/背景并保存。
- API 设置面板：运行时可配置 Base URL / API Key / Model。

## 演示流程（对应 `docs/视频演示.mp4`）

1. 启动场景后进入古风审讯界面，左侧为嫌疑人立绘，右侧为对话区。
2. 初始压力值为 0，玩家输入第一轮问题后，嫌疑人给出回避式回答。
3. 随着追问推进，嫌疑人会继续辩解，压力值逐步上升。
4. 打开“剧本设置”可编辑场景与 NPC 设定，保存后立即影响后续对话提示词。
5. 点击“出示证物”后继续提问，模型更容易交代关键矛盾（如篡改时间线），并出现明显压力值跃升。

## 技术栈

- Unity `2021.3.2f1c1`
- C#
- Newtonsoft.Json
- DeepSeek Chat Completions API

## 目录说明

- `Assets/Scenes/MysterySolving/ChatBot.cs`：主交互逻辑（UI、对话、压力值、证物机制、配置面板）
- `Assets/Scenes/MysterySolving/DeepSeekClient.cs`：DeepSeek 请求封装与重试逻辑
- `Assets/Scenes/MysterySolving/DeepSeekDtos.cs`：请求/响应 DTO
- `Assets/Scenes/MysterySolving/GameConfigDtos.cs`：剧本配置 DTO
- `Assets/StreamingAssets/game_config.json`：运行时剧本与 API 默认配置
- `docs/视频演示.mp4`：项目演示视频

## 快速开始

1. 使用 Unity Hub 安装并打开 `2021.3.2f1c1` 版本编辑器。
2. 打开工程目录：`llm-mysterysolving`。
3. 打开场景：`Assets/Scenes/MysterySolving/LLM-MysterySolving.unity`。
4. 点击 Play 运行。
5. 在运行时通过“API设置”填写：
   - `Base URL`（默认 `https://api.deepseek.com/chat/completions`）
   - `API Key`
   - `Model`（默认 `deepseek-chat`）
6. 输入审讯问题开始体验。

## 配置说明（`Assets/StreamingAssets/game_config.json`）

示例字段：

```json
{
  "api": {
    "base_url": "https://api.deepseek.com/chat/completions",
    "api_key": "",
    "model_name": "deepseek-chat"
  },
  "scenario_title": "王掌柜审讯",
  "scenario_background": "唐代长安夜市命案，需通过审讯锁定嫌疑人与动机。",
  "npcs": [
    {
      "name": "王掌柜",
      "personality": "圆滑谨慎，擅长转移话题",
      "secret": "案发当夜为掩护账册问题篡改了时间线",
      "background": "长安西市酒肆掌柜，常与商旅往来"
    }
  ],
  "rag_clues": [
    {
      "id": "ledger",
      "title": "账册缺页",
      "content": "案发当夜账册有两页被撕下，缺失时段集中在亥时前后。",
      "keywords": ["账册", "缺页", "亥时", "账目"]
    }
  ]
}
```

## 已知说明

- 若 API Key 为空或网络不可用，请求会失败并在聊天气泡中显示错误信息。
- 若模型返回空内容，客户端会进行一次附加强约束提示的重试。
- 当前默认单 NPC 审讯流程，适合 Demo 与教学扩展。

## 后续可扩展方向

- 多 NPC 并行审讯与时间线比对
- 证物卡片系统与可视化线索板
- 对话存档、复盘与案件评分
- 接入本地/私有化模型推理服务
