# Patch notes

Write the notes for the next release in `ru.json` and `en.json` here, in the same promotion to `stable` as the changes. A file ships only when it changed since the previous release. Without a changed file the release has no notes. See [releasing](../docs/RELEASING.md#patch-notes).

```jsonc
{
  // Changes that belong to no hero, item, or enemy.
  "general": [
    { "text": "Chest prices in co-op now grow per player", "change": "fix" }
  ],
  "sections": [
    {
      "kind": "hero",            // hero | item | enemy | other
      "id": "vampire",           // content id: the launcher shows the localized name and, for heroes and enemies, the sprite
      "changes": [
        { "text": "Base damage reduced by 1", "change": "nerf", "details": ["Level 1 damage from 10 to 9"] }
      ],
      "groups": [
        {
          "title": "Abilities",
          "entries": [
            { "name": "Blood pact", "icon": "companion_leech_r", "changes": [{ "text": "Cooldown from 18 to 19 seconds", "change": "nerf" }] }
          ]
        }
      ]
    },
    { "kind": "item", "id": "sneakers", "icon": "companion_dog_r", "changes": [{ "text": "Move speed from 6% to 8%", "change": "buff" }] },
    { "kind": "other", "title": "Co-op", "changes": [{ "text": "Rooms show the host's ping", "change": "new" }] }
  ]
}
```

- `change` marks a line as `buff` ▲, `nerf` ▼, `rework` ◆, `new` ★, or `fix` ✔. Omit it for a plain bullet.
- `details` are indented sub-points of a change.
- `icon` is an atlas sprite id. It is optional and overrides the default hero or enemy sprite. Items have no default icon.
- `title` overrides the localized name. A section of kind `other` needs a title.
- Each text is one line of at most 300 characters. Unknown fields, kinds, or change tags fail the release.
