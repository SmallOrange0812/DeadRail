# AGENTS.md — DeadRail Project Instructions

## 1. Project Overview

- Project name: DeadRail
- Project type: Unity 3D multiplayer game
- Main engine: Unity
- Programming language: C#
- Networking: Photon Fusion
- Animation system: Unity Animator, Network Mecanim Animator
- Additional systems: Unity Animation Rigging, Burst Compiler

Unity project directory:

`DeadRailGame/`

Repository root:

`C:\Projects\DeadRail\`

Project handover document:

`HANDOVER.md`

Before starting any substantial task, read `HANDOVER.md` and inspect the actual project state.

Do not assume that the handover document is always up to date. Verify important details against the actual project files and Git state.

## 2. Critical Project Status

The project was previously rolled back to a clean animation configuration after encountering Animation Rigging and Burst Compiler errors.

The following information is important:

- Basic Network Mecanim Animator synchronization was reported as working.
- Rig Builder and the previous rigging configuration were removed during rollback.
- Upper-body and lower-body animation separation has not yet been rebuilt.
- Weapon aiming IK has not yet been rebuilt.
- The previous `PropertyStreamHandle` Burst error must not be reintroduced.
- Developer A's latest work must be reviewed and integrated before rebuilding the rigging system.

Treat these as handover information, not as verified current runtime results. Confirm the actual state before making changes.

## 3. Critical Files

### Player_A.prefab

`Player_A.prefab` is a critical network player prefab.

Rules:

- Inspect its current structure before modifying it.
- Do not replace it with an older version without explicit approval.
- Do not delete or recreate it as a troubleshooting shortcut.
- Preserve Photon Fusion networking components and existing references.
- Check Animator, Network Mecanim Animator, Rigidbody, Collider, and related components before changing anything.
- Avoid unnecessary prefab hierarchy changes.
- Do not modify scene instances when the task requires editing the original prefab.
- When prefab changes are necessary, inspect overrides and serialized references carefully.

### EquipmentPivotController.cs

This script controls equipment pivots and bone references.

Rules:

- Inspect the complete implementation before changing it.
- Pay particular attention to runtime initialization and bone reference assignment.
- Check Unity lifecycle methods such as Awake, Start, OnEnable, and network Spawned.
- Do not re-enable previously disabled or commented-out setup logic without understanding why it was disabled.
- Preserve existing networking and equipment behavior.

### Animation Assets

Important assets include:

- `PlayerAnim.controller`
- `UpperBodyMask.mask`
- `X Bot.fbx`
- `X BotAvatar`

Before rebuilding Animation Rigging, inspect the actual asset configuration.

## 4. Animation Rigging Rules

The following decisions were established in the handover document.

### FBX Import Settings

For `X Bot.fbx`:

- `Optimize Game Objects` must be disabled when rebuilding the rigging system.

Do not change other import settings unnecessarily.

### Two Bone IK

The intended bone connection order is:

`Arm → ForeArm → Hand`

Do not reverse or skip the bone hierarchy.

### Rig Hierarchy

- Rig Builder belongs on the top-level object containing the Animator.
- IK Target objects should be children of the Animator hierarchy.
- Verify all Transform references before enabling the rig.
- Check Avatar and bone hierarchy compatibility.

### Burst Compiler Safety

- Do not assume that deleting cached data is a safe general solution.
- Investigate missing bones, invalid Transform references, rig hierarchy, and FBX optimization first.
- Do not repeatedly modify rigging components without understanding the root cause.
- After each significant rigging change, check for `PropertyStreamHandle` errors.

Do not rebuild the rigging system until Developer A's work has been reviewed and integrated with explicit approval.

## 5. Git Safety Rules

Before making any changes:

1. Inspect the current Git branch.
2. Inspect `git status`.
3. Inspect recent commits.
4. Identify uncommitted changes.
5. Identify available remote branches.
6. Confirm which branch contains Developer A's latest work.
7. Identify files likely to conflict.

Never assume that the working tree is clean.

### Forbidden without explicit user approval

- Git merge
- Git rebase
- Git reset
- Git checkout that changes branches or discards work
- Git switch
- Git clean
- Git restore that discards modifications
- Force push
- Branch deletion
- Commit creation
- Push operations
- History rewriting

Never overwrite or discard existing user changes.

Do not resolve conflicts automatically without reviewing the affected files and explaining the consequences.

Before any merge, provide a merge plan and wait for explicit approval.

## 6. Development Workflow

Follow this order unless the user explicitly changes the plan.

1. Read the handover document.
2. Inspect the actual repository and Unity project structure.
3. Review Git status and recent history.
4. Identify Developer A's latest branch or commits.
5. Review merge risks, especially `Player_A.prefab`.
6. Present a merge plan and wait for approval.
7. Integrate Developer A's work only after approval.
8. Verify basic multiplayer spawning and animation behavior.
9. Rebuild Animation Rigging incrementally.
10. Reconnect and validate `EquipmentPivotController`.
11. Test multiplayer animation synchronization.

Do not skip steps without explaining why.

## 7. Coding Rules

- Understand existing code before editing.
- Follow the project's existing architecture and naming conventions.
- Prefer minimal, focused changes.
- Avoid unrelated refactoring.
- Do not introduce unnecessary dependencies.
- Do not install or update Unity packages without approval.
- Preserve existing Photon Fusion behavior.
- Check references before renaming or moving classes and assets.
- Avoid changing serialized Unity asset data unnecessarily.
- Do not generate replacement files when a targeted modification is sufficient.

When a bug is reported, identify its root cause before proposing a fix.

## 8. Unity Editor Limitations

Some tasks require direct Unity Editor interaction.

Examples:

- Prefab Mode inspection and editing
- Animator Controller configuration
- Avatar Mask configuration
- FBX Import Settings
- Rig Builder and IK hierarchy configuration
- Play Mode multiplayer testing
- Inspector reference assignment

If a task requires Unity Editor interaction, explain which steps the user must perform manually.

Do not claim that an Editor operation or runtime test succeeded unless it was actually performed and verified.

## 9. Testing and Verification

After code changes:

- Run appropriate available checks.
- Check Unity compilation errors where possible.
- Inspect relevant Console errors if available.
- Verify serialized references and asset paths.
- Check Photon Fusion network behavior when the task affects networking.
- Check for Burst and `PropertyStreamHandle` errors when modifying Animation Rigging.

Distinguish clearly between:

- Verified results
- Static code analysis
- Expected behavior
- Tests that could not be performed

Never report an unperformed test as successful.

## 10. Communication Rules

Before a substantial modification:

1. Explain the problem.
2. Explain the proposed solution.
3. List the files that will be changed.
4. Explain possible side effects.
5. Wait for approval when the change is risky or affects critical assets.

After completing a task, report:

- Files inspected
- Files modified
- Changes made
- Tests performed
- Test results
- Remaining risks
- Recommended next step

If the actual project differs from `HANDOVER.md`, report the discrepancy instead of silently assuming either source is correct.

## 11. Current Priority

The immediate priority is:

**Review Developer A's latest work and prepare a safe merge plan before any Animation Rigging reconstruction.**

Do not begin IK reconstruction until the merge has been reviewed, approved, completed, and basic animation behavior has been verified.

Protect the existing project state at all times.