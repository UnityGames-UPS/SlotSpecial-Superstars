# CLAUDE.md

## Scope of what Claude may edit

**Claude may only create or modify C# source files under `Assets/Scripts/`.** This is the only part of
the project Claude is allowed to touch.

**Claude must never touch Unity-managed files.** These are owned and edited exclusively in the Unity
Editor; modifying them by hand corrupts the project. This includes (non-exhaustive):

- Scenes: `*.unity`
- Prefabs: `*.prefab`
- Assets / ScriptableObjects / fonts / sprites: `*.asset`, `*.mat`, `*.anim`, `*.controller`
- Meta files: `*.meta` (never create, edit, move, or delete these)
- Art / audio / fonts: anything under `Assets/Graphics/`, `Assets/Fonts/`, images, audio
- Project config: `ProjectSettings/`, `Packages/`, `UserSettings/`, `Library/`

If a task seems to require changing a Unity-managed file, stop and tell the user to make that change in
the Unity Editor instead.

## Project context

This project (**SlotSpecial-Superstars**) is bootstrapped from an older slot game, **Diamond Riches**,
used as a template. A large portion of the codebase is therefore still Diamond Riches code being
migrated to Superstars incrementally. When working here, expect legacy Diamond-Riches naming and
features; prefer commenting out obsolete logic (rather than deleting) during migration unless told
otherwise.
