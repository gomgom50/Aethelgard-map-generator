---
trigger: always_on
---

---
trigger: always_on
---

Style Guide & Operational Directives

**Technology Stack:** raylib, imgui, C# 9.0
**Core Philosophy:** Solid Foundation over Quick Fixes. Architecture over Hacky Solutions.

## 1. The Prime Directive: The Proposal Protocol
**Constraint:** Do not write implementation code immediately upon receiving a complex task.

**Definition of "Complex Task":**
A task is complex if it:

Touches 2+ existing systems
Introduces a new subsystem or manager
Changes initialization/lifecycle flow
Involves async, networking, or persistence

Simple bug fixes, UI tweaks, and single-class changes do not require proposals.

**Procedure:**
Before generating code for any new system or significant refactor, you **MUST** present 2-5 distinct implementation strategies.
For each strategy, provide:
1.  **Description:** A high-level summary of the pattern/approach.
2.  **Pros:** Why this fits our architecture (Performance, decoupling, scalability).
3.  **Cons:** The trade-offs (Complexity, memory usage, setup time).
4.  **Recommendation:** Your preferred choice and *why* it is superior for this specific context.

**Wait for my approval on a strategy before writing the code.**

---

## 2. Architectural Patterns (The "No-DI" Framework)
We explicitly **reject** Dependency Injection frameworks (Zenject/VContainer). We prefer explicit control and readability.

### Preferred Patterns
*   **Singleton Managers:** For core global systems (`NetworkSimulator`, `TimeManager`, `GameManager`). Use a thread-safe, lazy-loaded implementation or a standardized `Singleton<T>` base class.
*   **Service Locator / Factory:** Use static factory classes (`ServiceFactory`) to instantiate logical objects (like `NetworkServices`) based on configuration/Enums.
*   **Initialization Injection:** Pass dependencies manually via `Initialize()` methods or Constructors (for POCOs), not via "Magic" container injection.
*   **Command Pattern:**
    *   *Why:* Encapsulates a request as a standalone object. This decouples the invoker from the receiver and allows for "meta" handling of actions.
    *   *When:* Use when actions need to be queued, logged, serialized (sent over a network), or require Undo/Redo functionality.
*   **Chain of Responsibility:**
    *   *Why:* Avoids coupling the sender of a request to its receiver by giving more than one object a chance to handle the request. Creates a processing pipeline.
    *   *When:* Use when a request must pass through a sequence of filters or handlers, where each handler decides whether to process the request, block it, or pass it to the next handler.
*   **Facade Pattern:**
    *   *Why:* Provides a unified, simplified interface to a set of interfaces in a subsystem. Reduces coupling between client code and complex system internals.
    *   *When:* Use when interacting with a complex system (like a simulation engine or hardware layer) to provide a clean API for the rest of the codebase.
*   **Composite Pattern:**
    *   *Why:* Allows clients to treat individual objects and compositions of objects uniformly. Perfect for recursive tree structures.
    *   *When:* Use when you need to represent part-whole hierarchies (like UI trees, organization charts, or file structures).
*   **Strategy Pattern:**
    *   *Why:* Defines a family of algorithms, encapsulates each one, and makes them interchangeable.
    *   *When:* Use when a class has a specific behavior that needs to change at runtime (e.g., different sorting algos, different AI behaviors, or different security checks) without modifying the class itself.
*   **Reactive Property (`Observer<T>`):**
    *   *Why:* Wraps a value and automatically notifies listeners when that value changes. Eliminates manual "check if changed" polling and keeps UI/state synchronized.
    *   *When:* Use when a specific value needs to be watched—typically for UI bindings, stat tracking, or any property where dependents need to react to changes. (e.g., `Observer<int> Health` → health bar updates automatically.)


---

## 3. Code Quality & Debugging (The "No-Patch" Policy)

### Root Cause Analysis
*   **Never** apply a "band-aid" (e.g., `if (x != null) return;`) without understanding *why* `x` was null.
*   If a bug is found, trace the execution flow to the origin. Fix the architectural flaw that permitted the invalid state, rather than suppressing the error.

### Solidity over Speed
*   Write code assuming it will need to scale to 10x its current usage.
*   If a solution feels "hacky" or relies on `GameObject.Find`, magic strings, or fragile hierarchy assumptions, **stop**. Propose a better architectural approach.

### Error Handling
*   **Fail Loudly:** In development, use `Debug.Assert` or throw specific Exceptions when internal state is invalid. Do not fail silently.
*   **Logs:** Use descriptive logs.
---

## 4. Modern C# 9

*   **Null Safety:** Use nullable reference types logic mentally. If a property can be null, check it.
*   **Pattern Matching:** Use C# 9 pattern matching (`if (obj is Device d)`) and switch expressions for cleaner logic.
*   **Var:** Use `var` when the type is obvious from the right-hand side (`var list = new List<string>();`). Use explicit types when it aids readability.
*   **Properties:** Use auto-properties (`public int Health { get; private set; }`) over public fields.
*   **Async/Await:** Prefer `Task` and `async/await` for IO, and file handling.
*   **Loops:** Avoid `foreach` in hot paths (`Update`); use `for` loops. Ideally, avoid logic in `Update` entirely—use Event-Driven logic.

### Naming Conventions
*   **Classes/Methods/Properties:** `PascalCase`
*   **Private Fields:** `_camelCase` (e.g., `private int _health;`)
*   **Parameters/Locals:** `camelCase`
*   **Interfaces:** `IPrefix` (e.g., `IBlocker`)

---

## 5. Documentation & Future-Proofing

*   **Interfaces First:** When designing a system, define the `Interface` (API) before the implementation. This forces decoupling.
*   **Stubs:** When referencing a system that doesn't exist yet, create a file for it, define the class/interface, and add `// TODO: Future Implementation` stubs. Do not leave compilation errors.
*   **XML Comments:** All public methods and classes must have XML summary comments explaining *what* it does and *why*.
---
