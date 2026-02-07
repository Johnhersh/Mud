---
name: task-planning
description: Guidelines for the two-phase planning process (Product Definition and Technical Specification) for Infinite Qud.
---

# Infinite Qud: Project Overview and Task Planning

## IMPORTANT: Planning Process Guidelines

**Phase 1 (Product Definition) focuses on understanding user needs and gameplay goals without diving into code details.** Phase 2 (Technical Specification) includes thorough codebase exploration to understand implementation details, edge cases, and technical constraints.

**ITERATIVE DOCUMENTATION:** Create the task `.md` file early—as soon as you understand the broad product strokes. Update the document incrementally as questions are answered and clarity is gained. Don't wait until the end to write everything at once. Add or remove content based on decisions made during the conversation.

## Project Overview

"Infinite Qud" is a web-based MMO RPG featuring a retro-futuristic ASCII visual style, infinite procedural generation, and real-time multiplayer synchronization.

### Architecture Overview
- **Backend**: ASP.NET Core 8 (C#) running a persistent `BackgroundService` game loop.
- **Networking**: SignalR with MessagePack binary protocol for low-latency updates.
- **Frontend**: Blazor WebAssembly (C#) for UI and logic.
- **Rendering**: Phaser 4 (JavaScript) via Interop for high-performance WebGL ASCII grid.
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
   - **Prefer multiple choice** - Offer 2-4 concrete options rather than open-ended questions when possible.
   - **Lead with your recommendation** - State which option you'd choose and why.
   - **Present 2-3 approaches** - Before settling on a direction, briefly present alternatives with trade-offs.
4. **Create Initial Task Document** - Once broad strokes are understood:
   - Create a `.md` file in `.agent/tasks/` with the objective and what's known so far.
   - Update this document incrementally as each question is answered.
   - Add or remove content based on decisions—the document should reflect current understanding.
5. **Product Definition** - Define what the feature should do:
   - Clarify the player experience.
   - Establish feature boundaries and scope.
   - Think of edge cases and places where the design might fail.
6. **Review Product Scope** - Confirm understanding:
   - **MANDATORY:** Ask user: "Are you ready to proceed to technical specification?"
   - **STOP:** Wait for user confirmation before proceeding to Phase 2.

### Phase 2: Technical Specification (REQUIRES USER APPROVAL)
**CRITICAL:** Only proceed after explicit user confirmation from Phase 1.

7. **Deep Codebase Exploration**:
   - Search for relevant C# classes (Server/Shared) and JS rendering logic.
   - Understand existing SignalR hub methods and MessagePack models.
   - **Study existing code patterns and style** - Look at how similar features are implemented. Note naming conventions, architectural patterns, and code organization that should be followed.
   - Update the task document with findings.

8. **Implementation Pitfall Analysis**:
   - Think deeply about what could go wrong during implementation.
   - Consider race conditions, state synchronization issues, and edge cases.
   - Identify where the implementation might conflict with existing systems.
   - Look for hidden complexity that isn't obvious from the product requirements.

9. **Interactive Technical Discovery**:
   - **Bring technical questions to the user** - Don't make difficult technical decisions alone.
   - Present trade-offs clearly (e.g., "Option A is simpler but less flexible; Option B handles future cases but adds complexity").
   - Ask about integration approaches (e.g., "Should we use a new SignalR method or extend the existing one?").
   - Update the task document with each decision.

10. **Edge Case & Integration Analysis**:
    - Examine existing implementations for integration challenges.
    - Identify potential bugs (e.g., "What happens if the target dies while a projectile is in flight?").
    - Present found edge cases to the user and ask for preferred handling.
    - Document edge cases and their resolutions in the task file.

11. **Finalize Task Document**:
    - Ensure the `.md` file in `.agent/tasks/` reflects all decisions made.
    - Include code patterns to follow based on codebase exploration.
    - List specific files to modify and the nature of changes.

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
- **Code Patterns to Follow**: Relevant patterns observed in the codebase that should be used.
- **Edge Cases**: Known edge cases and how they should be handled.
- **Technical Decisions**: Key decisions made during planning and their rationale.
- **Implementation Considerations**: Performance, latency, and synchronization notes.
- **Files to Modify**: Explicit list of files and the nature of changes.
- **How to Test**: Acceptance criteria as plain language, plus instructions for automated and visual testing (see below).

### How to Test Section

Every task document should include a "How to Test" section. Testing has three tiers:

1. **Acceptance Criteria** - Plain-language truths the feature must satisfy (these become integration tests)
2. **Code Quality Review** - Run the `code-quality-reviewer` agent to catch issues
3. **Visual/Functional Testing** (optional) - Run `playwright-tester` for UI/rendering features

Template:

```markdown
## How to Test

### Acceptance Criteria

1. [Plain-language truth about expected behavior]
2. [Another truth]
3. `dotnet build` succeeds with no errors
4. `dotnet test` passes all tests

### Step 1: Code Quality Review

\`\`\`
Task(
  subagent_type: "code-quality-reviewer",
  description: "Review [feature name] code",
  prompt: """
  Review the code changes for [feature name].

  **Files changed:**
  - [List of modified files]

  **What to look for:**
  - Any #pragma directives (require user approval)
  - Calls to obsolete/deprecated APIs
  - Dead code or unused variables
  - Proper error handling for unexpected situations
  - Adherence to project coding patterns
  """
)
\`\`\`

### Step 2: Automated Tests

Run `dotnet test` and verify all tests pass.

### Step 3: Visual Testing (if applicable)

\`\`\`
Task(
  subagent_type: "playwright-tester",
  description: "Test [feature name]",
  prompt: """
  **Feature:** [One-line description]

  **Test Steps:**
  1. [Action] → [Expected result]
  ...
  """
)
\`\`\`
```
