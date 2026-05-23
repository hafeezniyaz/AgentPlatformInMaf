---
name: agent-design
description: Design focused agents with clear responsibilities, tool access, middleware policies, and measurable success criteria.
license: MIT
metadata:
  author: local
  version: "1.0"
  required-tools: clock
---

# Agent Design

Use this skill when the user asks to create, refine, or evaluate an agent.

1. Identify the target user and the job the agent must perform.
2. Keep the agent responsibility narrow enough that its instructions stay focused.
3. Recommend only tools the agent needs for its task.
4. Add middleware for cross-cutting behavior such as logging, safety, or timing.
5. Define success criteria that can be tested with realistic prompts.
