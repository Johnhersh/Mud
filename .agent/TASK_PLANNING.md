---
name: task-planning
description: Guidelines for the two-phase planning process (Product Definition and Technical Specification) for Infinite Qud.
---

# Infinite Qud: Project Overview and Task Planning

## IMPORTANT: Planning Process Guidelines

**Phase 1 (Product Definition) focuses on understanding user needs and gameplay goals without diving into code details.** Phase 2 (Technical Specification) includes thorough codebase exploration to understand implementation details, edge cases, and technical constraints.

**CRITICAL RULE:** Never create task specification documents without explicit user approval. Always complete Phase 1 (Product Definition) and get user confirmation before proceeding to Phase 2 (Technical Specification).

## Project Overview

"Infinite Qud" is a web-based MMO RPG featuring a retro-futuristic ASCII visual style, infinite procedural generation, and real-time multiplayer synchronization.

### Architecture Overview
- **Backend**: ASP.NET Core 8 (C#) running a persistent `BackgroundService` game loop.
- **Networking**: SignalR with MessagePack binary protocol for low-latency updates.
- **Frontend**: Blazor WebAssembly (C#) for UI and logic.
- **Rendering**: PixiJS (JavaScript) via Interop for high-performance WebGL ASCII grid.
- **Database**: SQL Server/PostgreSQL with EF Core for persistent chunks and entities.

### Key Technical Patterns
- **Authoritative Server**: All logic (movement, combat) happens on the server.
- **Snapshot Interpolation**: Server broadcasts world state deltas to clients.
- **Deterministic Generation**: Infinite world consistency via math-based generation.
- **Hybrid Rendering**: ASCII characters rendered as sprites from a bitmap font atlas.

## Task Planning Process

When the user asks "what should I work on next":

### Phase 1: Product Definition (No Code Exploration)
1. **Review Project Context** - Read this planning document's architecture overview.
2. **Understand User Request** - Focus on the gameplay or technical goal.
3. **Interactive Discovery** - Ask ONE question at a time to clarify:
   - Gameplay mechanics (movement, combat, interaction)
   - Visual requirements (ASCII characters, colors, animations)
   - Scaling concerns (player count, world size)
   - **DO NOT explore code during this phase** - focus on product goals.
4. **Product Definition** - Define what the feature should do:
   - Clarify the player experience.
   - Establish feature boundaries and scope.
   - Think of edge cases and places where the design might fail.
5. **Review Product Scope** - Confirm understanding:
   - **MANDATORY:** Ask user: "Are you ready to proceed with creating the technical specification document?"
   - **STOP:** Wait for user confirmation before proceeding to Phase 2.

### Phase 2: Technical Specification (REQUIRES USER APPROVAL)
**CRITICAL:** Only proceed after explicit user confirmation from Phase 1.

6. **Codebase Exploration**:
   - Search for relevant C# classes (Server/Shared) and JS rendering logic.
   - Understand existing SignalR hub methods and MessagePack models.
7. **Draft Initial Task Spec**:
   - Define necessary changes to `Mud.Server`, `Mud.Client`, and `Mud.Shared`.
   - Outline the SignalR message flow and rendering updates.

**CRITICAL CHECKPOINT:** Perform edge case analysis (e.g., race conditions in movement, chunk loading failures).

8. **Edge Case Analysis**:
   - Examine existing implementations for integration challenges.
   - Ask user about specific technical concerns based on code findings.
9. **Finalize Task Document**: Create a `.md` file in the `.agent/tasks/` folder.

**IMPORTANT:** Planning ends with the creation of the task specification. Do NOT begin implementation during planning.

## Implementation Handoff

1. **Task Document Created**: Complete specification in `.agent/tasks/`.
2. **ASK USER FOR NEXT STEPS**: NEVER automatically begin implementation.
3. **Implementation Process**:
   - Follow C# coding standards and SignalR patterns.
   - Update `Task.md` as progress is made.
   - Run tests/builds to ensure no regressions.

## Task Document Format

Each task should be a standalone document in `.agent/tasks/` containing:
- **Objective**: Clear, single-sentence goal.
- **Problem Statement**: What is missing or needs improvement.
- **Success Criteria**: How we know it's complete.
- **Technical Approach**: Specific changes to Server, Client, and Shared projects.
- **Implementation Considerations**: Performance, latency, and synchronization notes.
